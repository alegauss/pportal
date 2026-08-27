using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One thing the session thread asks a holepunch session for.</summary>
/// <param name="Callee">The C function it calls.</param>
/// <param name="Asks">What it wants back, in a phrase.</param>
/// <param name="PortType">
/// Which port, where the call takes one - the only thing that tells the two socket calls apart.
/// </param>
public readonly record struct HolepunchAsk(string Callee, string Asks, string? PortType = null);

/// <summary>
/// PP429, under PP340: the nine things session.c asks a holepunch session for.
///
/// PP340's section says how it became visible: "reading the callers is how that became visible:
/// session.c drives the whole PSN path from C, across nine call sites." That reading was prose, and
/// PP33's blocked-ness rests on it.
///
/// THE NINE ARE AN INTERFACE, NOT A COUNT. Two finis, two sockets - ctrl and data - the registration
/// info, the offer, the punch, the selected address and the ctrl port. That list is what a managed
/// PSN path has to own, so writing it down is the step before the port rather than bookkeeping about
/// it.
///
/// A TENTH WOULD CHANGE PP33 IN SILENCE. PP33's end state is that chiaki-lib stops compiling
/// holepunch.c, and PP340 says what must be true first. A call site added grows that job without
/// either line moving, and PP33's own `remaining` query would not notice - it counts curl and
/// json-c, not this.
///
/// HELD BY NAME AND BY RELATIVE POSITION, never by a line number: this session moved several of
/// these while working on other files. The two <c>chiaki_get_holepunch_sock</c> calls are told apart
/// by their port type alone, and the seam needs that same distinction - a managed side returning one
/// socket for both would compile. What each ASKS for is the phrase beside it, which is prose a
/// planner reads rather than something a check can measure.
///
/// THIS IS NOT THE PORT. What has no counterpart is the websocket, the STUN exchange and the punch
/// itself, exactly as PP340 says. PP340 stays open; what this closes is that nobody can change the
/// shape of that job without the suite saying so.
/// </summary>
public static class HolepunchSeam
{
    /// <summary>Where the callers live.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The nine, in the order they appear IN THE FILE.
    ///
    /// FILE ORDER, NOT THE CONNECT SEQUENCE, and the difference caught this list out: the two finis
    /// are defined in the init and teardown functions, which sit above the session thread, so they
    /// come first here and last in the flow. A check that claimed connect order would be comparing
    /// one thing and asserting another.
    ///
    /// The two finis are one call each on two teardown paths rather than two different asks, and are
    /// listed as two because that is what a reader counting call sites finds.
    /// </summary>
    public static IReadOnlyList<HolepunchAsk> Asks { get; } =
    [
        new("chiaki_holepunch_session_fini", "the session released, on the init failure path"),

        new("chiaki_holepunch_session_fini", "and released again, on the teardown path"),

        new("chiaki_get_holepunch_sock", "the socket the control channel rides on",
            "CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL"),

        new("chiaki_get_regist_info", "the registration info the session request carries"),

        new("holepunch_session_create_offer", "an offer for the data connection"),

        new("chiaki_holepunch_session_punch_hole", "a hole punched for the data connection",
            "CHIAKI_HOLEPUNCH_PORT_TYPE_DATA"),

        new("chiaki_get_holepunch_sock", "the socket the stream rides on",
            "CHIAKI_HOLEPUNCH_PORT_TYPE_DATA"),

        new("chiaki_get_ps_selected_addr", "the address the console was reached at"),

        new("chiaki_get_ps_ctrl_port", "the port the control channel connects to"),
    ];

    /// <summary>How many call sites the seam has. Nine, which is PP340's number.</summary>
    public const int Count = 9;

    /// <summary>
    /// Every holepunch call session.c makes, as the C spells it, in file order.
    ///
    /// Read rather than listed, so the two can be compared: what <see cref="Asks"/> claims and what
    /// the file does are two statements, and this is the second.
    /// </summary>
    public static IReadOnlyList<string> CallsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);
        var calls = new List<(int At, string Callee)>();

        foreach (string callee in Asks.Select(ask => ask.Callee).Distinct(StringComparer.Ordinal))
        {
            for (int at = code.IndexOf(callee + "(", StringComparison.Ordinal);
                 at >= 0;
                 at = code.IndexOf(callee + "(", at + callee.Length, StringComparison.Ordinal))
            {
                calls.Add((at, callee));
            }
        }

        return [.. calls.OrderBy(one => one.At).Select(one => one.Callee)];
    }

    /// <summary>
    /// Whether the file makes exactly the calls the seam names, in the same order.
    ///
    /// ORDER TOO, but FILE order - which is what can be read from a position. The connect sequence
    /// is a different order and a fact about the flow rather than about the text: the ctrl socket is
    /// taken before the session request and the data socket after the punch, while the two finis sit
    /// above both. Claiming the second and comparing the first is the mistake this reader made
    /// first.
    /// </summary>
    public static bool TheCallsAreStillTheseInOrder(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CallsIn(source).SequenceEqual(
            Asks.Select(ask => ask.Callee), StringComparer.Ordinal);
    }

    /// <summary>
    /// And whether the two socket calls still pass the port types that tell them apart.
    ///
    /// The only difference between them. A file that asked for the same port twice would still make
    /// nine calls in the right order.
    /// </summary>
    public static bool TheTwoSocketsStillAskForDifferentPorts(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        IReadOnlyList<string> ports =
            [.. Asks.Where(ask => ask.Callee == "chiaki_get_holepunch_sock")
                .Select(ask => ask.PortType!)];

        // Two, and not the same one.
        if (ports.Count != 2 || ports[0] == ports[1])
            return false;

        return ports.All(port =>
            code.Contains(
                $"chiaki_get_holepunch_sock(session->holepunch_session,{port})",
                StringComparison.Ordinal));
    }
}
