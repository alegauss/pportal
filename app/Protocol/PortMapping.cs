using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the four calls to the router is being made.</summary>
public enum UpnpCall
{
    /// <summary>Looking for UPnP devices on the network.</summary>
    Discover,

    /// <summary>Validating that one of them is a connected gateway.</summary>
    Validate,

    /// <summary>Asking it for the external address.</summary>
    ExternalAddress,

    /// <summary>Adding a forwarding.</summary>
    AddMapping,

    /// <summary>And removing one.</summary>
    DeleteMapping,
}

/// <summary>What removes a forwarding, if anything does.</summary>
public enum MappingLifetime
{
    /// <summary>The router expires it after the lease.</summary>
    ExpiresOnTheRouter,

    /// <summary>Only the session teardown, if it runs.</summary>
    TeardownOnly,
}

/// <summary>
/// PP260: asking the router to forward a port.
///
/// THE MAPPING NEVER EXPIRES. The lease argument is the string zero, which UPnP reads as no expiry
/// at all. Nothing on the router will remove it; only the session teardown does, deleting both
/// mappings if it runs. A process that stops without running it - a crash, a kill - leaves two
/// permanent forwardings behind, and the next run adds two more. See <see cref="Lifetime"/>, which
/// is a value rather than a comment because it is the whole of the finding.
///
/// The delete's own failure is logged and stepped over, so a teardown that tried and could not is
/// indistinguishable downstream from one that did.
///
/// THE GATEWAY PATH CANNOT PRODUCE AN IPv6 ADDRESS. The call that asks the router for the external
/// address writes into a buffer its own documentation puts at sixteen bytes, which is a dotted quad
/// and a terminator. PP252 measured that a gateway found is PREFERRED over STUN, so on that path the
/// external address is necessarily v4 - a property of the call, not a decision anywhere. See
/// <see cref="CanReturnIpv6"/>.
///
/// The internal and external ports are the same value, so the mapping is one to one and the delete
/// finds it by the port the offer already recorded.
/// </summary>
public static class PortMapping
{
    /// <summary>The lease this client asks for, as the core spells it.</summary>
    public const string Lease = "0";

    /// <summary>What that lease means.</summary>
    public static MappingLifetime Lifetime =>
        string.Equals(Lease, "0", StringComparison.Ordinal)
            ? MappingLifetime.TeardownOnly
            : MappingLifetime.ExpiresOnTheRouter;

    /// <summary>The description the forwarding carries on the router.</summary>
    public const string Description = "Chiaki Streaming";

    /// <summary>And the protocol.</summary>
    public const string Protocol = "UDP";

    /// <summary>How long the device search is given, in milliseconds.</summary>
    public const int DiscoverMs = 2000;

    /// <summary>The buffer each port is printed into - five digits and a terminator.</summary>
    public const int PortBuffer = ProbeSend.PortBuffer;

    /// <summary>
    /// How many bytes the external address is written into, per the call's own documentation.
    /// </summary>
    public const int ExternalAddressBuffer = 16;

    /// <summary>Whether a call can hand back an IPv6 address.</summary>
    public static bool CanReturnIpv6(UpnpCall call)
        => call != UpnpCall.ExternalAddress
            || ExternalAddressBuffer >= PunchAccept.AddressLength;

    /// <summary>Whether this call is bounded by a timeout of its own.</summary>
    public static bool IsBounded(UpnpCall call) => call == UpnpCall.Discover;

    /// <summary>
    /// The external port a forwarding uses, from the internal one - the same value.
    /// </summary>
    public static ushort ExternalFor(ushort internalPort) => internalPort;

    /// <summary>Whether a failed delete stops the teardown. It does not.</summary>
    public const bool AFailedDeleteStopsTeardown = false;

    /// <summary>
    /// How many mappings a run that never reached its teardown leaves behind.
    /// </summary>
    /// <param name="controlPortUsed">Whether a control port was mapped.</param>
    /// <param name="dataPortUsed">Whether a data port was.</param>
    public static int LeftBehind(bool controlPortUsed, bool dataPortUsed)
        => (controlPortUsed ? 1 : 0) + (dataPortUsed ? 1 : 0);
}

/// <summary>
/// PP260: the mappings where the core writes them.
/// </summary>
public static class PortMappingSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>Whether the lease is still asked for as never expiring.</summary>
    public static bool TheLeaseIsStillZero(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
            $"gw_info->lan_ip, \"{PortMapping.Description}\", \"{PortMapping.Protocol}\", NULL, \"{PortMapping.Lease}\");",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the only place that deletes them is still the teardown, guarded by a recorded port.
    /// </summary>
    public static bool OnlyTheTeardownStillDeletesThem(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Two call sites, and both inside the teardown's gateway block.
        int calls = text.Split(
            "upnp_delete_udp_port_mapping(session->log, &session->gw,", StringSplitOptions.None).Length - 1;

        // PP388: the block anchor, the free and the slice between them in one space.
        string compact = CCall.Compact(text);

        int block = CCall.Mark(compact, "if(session->gw.data)");
        if (block < 0 || calls != 2)
            return false;

        int frees = CCall.At(compact, "free(session->gw.urls)", block);
        if (frees < 0)
            return false;

        string teardown = compact[block..frees];

        // PP388: the needle is compacted too, because the slice it is counted in is - splitting
        // compacted text on a raw literal finds nothing and reads as "no calls in the teardown".
        return CCall.Mark(teardown, "if(session->local_port_ctrl != 0)") >= 0
            && CCall.Mark(teardown, "if(session->local_port_data != 0)") >= 0
            && CCall.Count(teardown, "upnp_delete_udp_port_mapping(session->log, &session->gw,") == 2;
    }

    /// <summary>And whether a failed delete is still logged without stopping anything.</summary>
    public static bool AFailedDeleteStillDoesNotStop(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("Couldn't delete UPNP local port ctrl mapping", StringComparison.Ordinal)
            && text.Contains("Couldn't delete UPNP local port data mapping", StringComparison.Ordinal);
    }

    /// <summary>Whether the mapping is still one to one.</summary>
    public static bool TheMappingIsStillOneToOne(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            "upnp_add_udp_port_mapping(session->log, &session->gw, local_port, local_port)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the external-address buffer is still documented at sixteen bytes.</summary>
    public static bool TheAddressBufferIsStillSixteen(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            $"needs to be at least {PortMapping.ExternalAddressBuffer} bytes long",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the discovery is still bounded and the validation after it is not.</summary>
    public static bool DiscoveryIsStillBoundedAndValidationIsNot(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int discovers = text.IndexOf(
            $"        {PortMapping.DiscoverMs} /** ms, delay*/, NULL, NULL, 0, 0, 2, &success);",
            StringComparison.Ordinal);
        if (discovers < 0)
            return false;

        int validates = text.IndexOf("int igd_ret = UPNP_GetValidIGD(", discovers, StringComparison.Ordinal);

        return validates > discovers
            && !text[discovers..validates].Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And whether the validation still accepts only its one success value - checked, not a defect.
    /// </summary>
    public static bool TheValidationStillDemandsAConnectedGateway(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            "if (igd_ret != 1) {", StringComparison.Ordinal);
    }

    /// <summary>Whether both port buffers still hold five digits and a terminator.</summary>
    public static bool ThePortBuffersAreStillExact(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains($"char port_internal_str[{PortMapping.PortBuffer}];", StringComparison.Ordinal)
            && text.Contains($"char port_external_str[{PortMapping.PortBuffer}];", StringComparison.Ordinal);
    }
}
