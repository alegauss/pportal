namespace ChiakiNg.Protocol;

/// <summary>
/// PP234: the kinds of adapter the pick looks at, as Windows numbers them.
///
/// Spelled with their own values rather than as an ordinary enumeration, because the walk tests
/// them by equality against IF_TYPE_IEEE80211 and MIB_IF_TYPE_ETHERNET and everything else is
/// skipped. A port that renumbered them would consider a different set of interfaces.
/// </summary>
public enum AdapterKind
{
    /// <summary>MIB_IF_TYPE_ETHERNET.</summary>
    Ethernet = 6,

    /// <summary>IF_TYPE_IEEE80211.</summary>
    Wireless = 71,

    /// <summary>Anything else, which the walk skips whole.</summary>
    Other = 0,
}

/// <summary>One adapter, as much of it as the pick reads.</summary>
/// <param name="Kind">Its type. Only two are considered.</param>
/// <param name="Addresses">Its IPv4 addresses, in the order Windows lists them.</param>
public readonly record struct Adapter(AdapterKind Kind, IReadOnlyList<string> Addresses);

/// <summary>
/// PP234: which address this client offers a console on the same network.
///
/// The rule is not the one a reader expects, and stating it is the point.
///
/// Only wireless and ethernet adapters are looked at; an empty address and 0.0.0.0 are skipped.
/// Then the walk STOPS on the first wireless address it finds and KEEPS GOING after an ethernet
/// one - so wireless wins, and among ethernets the last adapter enumerated wins by overwriting the
/// ones before it. For a stream that is the opposite of the usual preference, and it is exactly the
/// kind of rule a port tidies into "prefer ethernet" without noticing it changed which interface
/// the console is told to reach.
///
/// IPv4 ONLY, because GetAdaptersInfo is. There is no IPv6 local candidate from this path at all.
///
/// AND THE BOUND THE SIGNATURE OFFERS IS DECLINED. The core's function takes a buffer and a length;
/// its caller passes the candidate's own address field and sizeof that field, which is a caller
/// doing exactly the right thing. Neither parameter is read - the copy goes through the struct as a
/// memcpy of strlen plus one. An address from GetAdaptersInfo fits, which is why it has never
/// bitten. Reproduced as a length that is carried and never used, so the port cannot lose it by
/// tidying the parameter away.
/// </summary>
public static class LocalAddress
{
    /// <summary>An address field that means "nothing here".</summary>
    public const string Unset = "";

    /// <summary>And the other one, which is a real string and still not an address.</summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>
    /// The address the core would end up with, or null where it would find none.
    /// </summary>
    /// <param name="adapters">Windows' list, in enumeration order. Order decides the answer.</param>
    public static string? Pick(IEnumerable<Adapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        string? found = null;

        foreach (Adapter adapter in adapters)
        {
            if (adapter.Kind is not (AdapterKind.Ethernet or AdapterKind.Wireless))
                continue;

            string? onThisOne = FirstUsable(adapter.Addresses);
            if (onThisOne is null)
                continue;

            // Overwrites, which is how the LAST ethernet wins.
            found = onThisOne;

            // And the walk ends here only when this was not ethernet - which is how wireless wins.
            if (adapter.Kind != AdapterKind.Ethernet)
                break;
        }

        return found;
    }

    /// <summary>
    /// The first address on one adapter that is neither empty nor the any-address.
    ///
    /// The core breaks out of the address loop on the first it accepts, so a second address on the
    /// same adapter is never reached.
    /// </summary>
    public static string? FirstUsable(IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        foreach (string address in addresses)
        {
            if (address is Unset or AnyAddress)
                continue;

            return address;
        }

        return null;
    }

    /// <summary>
    /// Whether an address would fit the buffer the caller offered.
    ///
    /// Carried rather than used, exactly as the core carries it: the length is a parameter of the
    /// function that writes, and that function never reads it. Kept here so the port has the bound
    /// written down when it comes to do the copy, instead of rediscovering that nobody checked.
    /// </summary>
    public static bool Fits(string address, int bufferLength)
    {
        ArgumentNullException.ThrowIfNull(address);

        // strlen plus one, which is what the memcpy copies.
        return address.Length + 1 <= bufferLength;
    }
}

/// <summary>
/// PP234: the pick where the core writes it.
/// </summary>
public static class LocalAddressSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether only those two adapter kinds are still considered.</summary>
    public static bool OnlyTwoKindsAreStillConsidered(string core)
        => Body(core).Contains(
            "pAdapter->Type != IF_TYPE_IEEE80211 && pAdapter->Type != MIB_IF_TYPE_ETHERNET",
            StringComparison.Ordinal);

    /// <summary>Whether the two useless addresses are still skipped.</summary>
    public static bool TheTwoUselessAddressesAreStillSkipped(string core)
    {
        string body = Body(core);

        return body.Contains($"strcmp(str->IpAddress.String, \"{LocalAddress.Unset}\") == 0", StringComparison.Ordinal)
            && body.Contains($"strcmp(str->IpAddress.String, \"{LocalAddress.AnyAddress}\") == 0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the walk still ends only on a non-ethernet find. This is the whole rule: wireless
    /// wins because it is what stops the search.
    /// </summary>
    public static bool TheWalkStillEndsOnlyOnWireless(string core)
        => Body(core).Contains("if(status && !ethernet)", StringComparison.Ordinal);

    /// <summary>
    /// Whether the buffer and its length are still parameters the body never reads. True means the
    /// bound is still declined, which is what this asserts rather than a fix.
    /// </summary>
    public static bool TheBoundIsStillDeclined(string core)
    {
        string body = Body(core);
        if (body.Length == 0)
            return false;

        // Past the signature line, which is the only place they are allowed to appear.
        int afterSignature = body.IndexOf('\n');
        if (afterSignature < 0)
            return false;

        string rest = body[afterSignature..];

        return !rest.Contains("out_len", StringComparison.Ordinal)
            && rest.Contains("memcpy(local_console_candidate->addr", StringComparison.Ordinal);
    }

    /// <summary>get_client_addr_local's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // LAST, because the forward declaration at the top of the file is a prefix of this - the
        // miss PP213 made and PP233 repeated.
        int start = core.LastIndexOf(
            "static ChiakiErrorCode get_client_addr_local(Session *session", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(
            "static ChiakiErrorCode upnp_get_gateway_info(", start, StringComparison.Ordinal);

        return end < 0 ? core[start..] : core[start..end];
    }
}
