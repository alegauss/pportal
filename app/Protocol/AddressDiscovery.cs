namespace ChiakiNg.Protocol;

/// <summary>What is known about the router between this machine and the internet.</summary>
public enum GatewayStatus
{
    /// <summary>Not looked for yet.</summary>
    Unknown,

    /// <summary>Looked for and not there.</summary>
    NotFound,

    /// <summary>Found, and its LAN address is known.</summary>
    Found,
}

/// <summary>Where the external address ended up coming from.</summary>
public enum AddressSource
{
    /// <summary>The gateway, over UPnP, after a port mapping succeeded.</summary>
    Upnp,

    /// <summary>A STUN server, which is the fallback.</summary>
    Stun,

    /// <summary>Nowhere - neither produced one.</summary>
    None,
}

/// <summary>What one run of the discovery produced.</summary>
/// <param name="Source">Where the external address came from.</param>
/// <param name="LocalAddressKnown">Whether the local address was actually found.</param>
/// <param name="LocalAdvertised">What is advertised as the local address regardless.</param>
public readonly record struct DiscoveryResult(
    AddressSource Source, bool LocalAddressKnown, string LocalAdvertised);

/// <summary>
/// PP252: how the offer learns the addresses it advertises.
///
/// ONE SWITCH, TWO SUBJECTS. The gateway's status picks the arm, and the two arms are not about the
/// same candidate. With no gateway, a local lookup fills the LOCAL candidate's address and that is
/// all it does. With a gateway, its LAN address goes into that same candidate AND a port mapping is
/// attempted, and only a mapping that succeeded is followed by asking for the EXTERNAL one. So the
/// flag meaning "we have an external address" can only be raised by the second arm - see
/// <see cref="CanProduceAnExternalAddress"/>.
///
/// WHICH MEANS THE LOOKUP'S FAILURE IS NOT READ. The local arm discards its result. A lookup that
/// fails leaves the address as the allocation left it, and the next statement copies that whole
/// field into the session, from where it is advertised. Nothing between the failure and the wire
/// inspects it, which is why <see cref="DiscoveryResult.LocalAddressKnown"/> and
/// <see cref="DiscoveryResult.LocalAdvertised"/> are separate answers.
///
/// STUN IS THE FALLBACK AND RUNS WHENEVER THE FLAG IS STILL DOWN - on the no-gateway arm, always.
/// The static candidate then takes STUN's ADDRESS while keeping the local port, which the file
/// explains: that port is what is used externally, so a second STUN candidate would duplicate the
/// static one.
/// </summary>
public static class AddressDiscovery
{
    /// <summary>What the allocation leaves in an address field that nothing writes.</summary>
    public const string Unwritten = "";

    /// <summary>Whether this arm of the switch can raise the external-address flag.</summary>
    public static bool CanProduceAnExternalAddress(GatewayStatus status)
        => status == GatewayStatus.Found;

    /// <summary>
    /// One run of the discovery.
    /// </summary>
    /// <param name="status">What is known about the gateway.</param>
    /// <param name="localLookup">What the local lookup found, or null where it failed.</param>
    /// <param name="gatewayLanAddress">The gateway's LAN address, for the arm that uses it.</param>
    /// <param name="mappingAdded">Whether the port mapping was accepted.</param>
    /// <param name="upnpExternal">The external address UPnP reported, or null.</param>
    /// <param name="stunExternal">And STUN's, or null.</param>
    public static DiscoveryResult Discover(
        GatewayStatus status,
        string? localLookup,
        string gatewayLanAddress,
        bool mappingAdded,
        string? upnpExternal,
        string? stunExternal)
    {
        ArgumentNullException.ThrowIfNull(gatewayLanAddress);

        string local;
        bool known;
        string? external = null;

        if (status == GatewayStatus.Found)
        {
            local = gatewayLanAddress;
            known = true;

            // The external address is asked for ONLY after a mapping the gateway accepted.
            if (mappingAdded)
                external = upnpExternal;
        }
        else
        {
            // The result is discarded here, so a failure is advertised as whatever was allocated.
            known = localLookup is not null;
            local = localLookup ?? Unwritten;
        }

        if (external is not null)
            return new DiscoveryResult(AddressSource.Upnp, known, local);

        // The flag is still down, so STUN runs.
        return new DiscoveryResult(
            stunExternal is not null ? AddressSource.Stun : AddressSource.None, known, local);
    }

    /// <summary>Whether STUN runs, given what came before it.</summary>
    public static bool StunRuns(GatewayStatus status, bool mappingAdded, string? upnpExternal)
        => !(status == GatewayStatus.Found && mappingAdded && upnpExternal is not null);

    /// <summary>
    /// Whether the static candidate keeps the local port rather than STUN's.
    ///
    /// It does, and the file gives the reason: the local port is the one used externally, so a
    /// separate STUN candidate would be the static one twice.
    /// </summary>
    public const bool StaticKeepsTheLocalPort = true;

    /// <summary>
    /// The width every address field here shares, which is what makes the copies whole-field.
    /// </summary>
    public const int AddressWidth = PunchAccept.AddressLength;
}

/// <summary>
/// PP252: the discovery where the core writes it.
/// </summary>
public static class AddressDiscoverySource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the two arms still write different things.</summary>
    public static bool TheTwoArmsStillWriteDifferentThings(string core)
    {
        string body = Body(core);

        return body.Contains(
                "get_client_addr_local(session, candidate_local, candidate_local->addr,", StringComparison.Ordinal)
            && body.Contains(
                "memcpy(candidate_local->addr, session->gw.lan_ip, sizeof(session->gw.lan_ip));",
                StringComparison.Ordinal)
            && body.Contains(
                "have_addr = get_client_addr_remote_upnp(session->log, &session->gw, candidate_remote->addr);",
                StringComparison.Ordinal);
    }

    /// <summary>And whether the local lookup's result is still discarded.</summary>
    public static bool TheLocalLookupsResultIsStillDiscarded(string core)
    {
        string body = Body(core);

        // The call stands alone as a statement - nothing takes what it returns.
        return body.Contains(
            "\n            get_client_addr_local(session, candidate_local,", StringComparison.Ordinal);
    }

    /// <summary>Whether the external address is still asked for only after a mapping succeeded.</summary>
    public static bool TheExternalIsStillAskedOnlyAfterAMapping(string core)
    {
        string body = Body(core);

        int mapped = body.IndexOf(
            "if(upnp_add_udp_port_mapping(session->log, &session->gw, local_port, local_port))",
            StringComparison.Ordinal);
        int asked = body.IndexOf("have_addr = get_client_addr_remote_upnp(", StringComparison.Ordinal);

        return mapped >= 0 && asked > mapped;
    }

    /// <summary>Whether STUN still runs on the flag alone.</summary>
    public static bool StunStillRunsOnTheFlagAlone(string core)
    {
        string body = Body(core);

        int guard = body.IndexOf("    if (!have_addr)\n", StringComparison.Ordinal);
        int stun = body.IndexOf("have_addr = get_client_addr_remote_stun(", StringComparison.Ordinal);

        return guard >= 0 && stun > guard;
    }

    /// <summary>
    /// Whether the local address is still copied whole into the session, straight after the switch.
    /// </summary>
    public static bool TheLocalAddressIsStillCopiedWhole(string core)
        => Body(core).Contains(
            "memcpy(session->client_local_ip, candidate_local->addr, sizeof(candidate_local->addr));",
            StringComparison.Ordinal);

    /// <summary>
    /// And whether the three fields are still the same width, which is what makes those copies safe.
    /// </summary>
    public static bool TheThreeFieldsAreStillTheSameWidth(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("char lan_ip[INET6_ADDRSTRLEN];", StringComparison.Ordinal)
            && text.Contains("char addr[INET6_ADDRSTRLEN];", StringComparison.Ordinal)
            && text.Contains("char client_local_ip[INET6_ADDRSTRLEN];", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the comment introducing the STUN block still describes a move that happens elsewhere.
    /// </summary>
    public static bool TheCommentStillDescribesALaterMove(string core)
    {
        string body = Body(core);

        int says = body.IndexOf(
            "// Move current candidates behind STUN candidates", StringComparison.Ordinal);
        if (says < 0)
            return false;

        int moves = body.IndexOf(
            "memcpy(&msg.conn_request->candidates[0], &msg.conn_request->candidates[1],",
            StringComparison.Ordinal);

        // The move is further down, past the whole STUN block the comment sits at the head of.
        return moves > says && moves - says > 2000;
    }

    /// <summary>The stretch from the gateway switch to the end of the STUN fallback's opening.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("    switch(session->gw_status)", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "\n    memcpy(candidate_remote->addr_mapped,", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
