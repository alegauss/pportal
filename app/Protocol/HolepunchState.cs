using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>How long one piece of the PSN flow's state lives.</summary>
public enum StateLifetime
{
    /// <summary>On the session, so it outlives the connect entirely.</summary>
    Session,

    /// <summary>A local of the connect function, spanning most of it.</summary>
    Function,

    /// <summary>A local of the PSN block, dead as soon as that block ends.</summary>
    Block,
}

/// <summary>One thing the flow carries between the nine holepunch calls.</summary>
/// <param name="Name">As the C names it.</param>
/// <param name="Lifetime">Where it lives.</param>
/// <param name="FromStep">The call that produces it, or null where nothing does.</param>
public readonly record struct FlowState(string Name, StateLifetime Lifetime, HolepunchStep? FromStep);

/// <summary>
/// PP478, under PP340: what the PSN flow HOLDS between the nine calls, and for how long.
///
/// §PP340 names three things missing: nothing managed "calls those pieces in order, decides what
/// happens when one fails, or holds the state between them". PP460 did the first two - the execution
/// order and each step's guard. This is the third.
///
/// FIVE PIECES, THREE LIFETIMES. The rudp is on the session and outlives everything; the data socket
/// and the ctrl port are locals of the connect function; and the registration info is a local of the
/// PSN block, dead the moment that block ends. A managed flow object would naturally give all five the
/// same lifetime, and one of them cannot have it.
///
/// THE REGISTRATION INFO IS A POINTER TO A STACK LOCAL, AND IT IS SAFE BY SCOPE ALONE.
/// `info.holepunch_info = &amp;hinfo` stores the address of a block local, and `info` is handed to
/// `chiaki_regist_start`, which starts a thread. That is only sound because the four calls that use it
/// - start, the timed wait, stop and fini - all complete inside the block that owns `hinfo`. Move any
/// of the four out and the regist thread reads a dead frame.
///
/// Nothing says so, which is what this records. It is the same shape as PP467's locking: correct today,
/// correct for a reason no comment carries, and the reason is the thing a port has to preserve rather
/// than the code.
///
/// AND THE DATA SOCKET'S NULL IS A VALUE, NOT AN ABSENCE. It is declared before the PSN block and stays
/// null for a local session, which is what tells senkusha and the stream connection to use the ordinary
/// socket. PP461 traced that: a managed flow treating null as "not yet set" would break local play.
/// </summary>
public static class HolepunchState
{
    /// <summary>Everything the flow carries between the nine calls.</summary>
    public static IReadOnlyList<FlowState> Carried { get; } =
    [
        new("session->rudp", StateLifetime.Session, HolepunchStep.CtrlSocket),
        new("hinfo", StateLifetime.Block, HolepunchStep.RegistInfo),
        new("data_sock", StateLifetime.Function, HolepunchStep.DataSocket),
        new("session->connect_info.hostname", StateLifetime.Session, HolepunchStep.SelectedAddress),
        new("port", StateLifetime.Function, HolepunchStep.CtrlPort),
    ];

    /// <summary>The one piece that dies before the connect does.</summary>
    public static FlowState TheShortestLived
        => Carried.Single(s => s.Lifetime == StateLifetime.Block);

    /// <summary>
    /// The four calls that must stay inside the block owning the registration info, because one of them
    /// starts a thread that reads it.
    /// </summary>
    public static IReadOnlyList<string> MustStayWithTheRegistInfo { get; } =
    [
        "chiaki_regist_start",
        "session_check_state_pred_regist",
        "chiaki_regist_stop",
        "chiaki_regist_fini",
    ];

    /// <summary>
    /// What a null data socket means: a local session, not an unset field.
    ///
    /// PP461's trace, carried here because this is where a managed flow would get it wrong.
    /// </summary>
    public const string NullDataSocketMeans = "a local session, using the ordinary socket";

    /// <summary>session.c.</summary>
    public static string? Locate() => HolepunchFlow.Locate();

    /// <summary>
    /// Whether all four regist calls are still inside the block that owns the registration info.
    ///
    /// Read as a span: the info is taken, the four calls follow, and the block closes after the last of
    /// them. A call moved out would leave the regist thread reading a dead frame.
    /// </summary>
    public static bool TheRegistCallsStayWithTheInfo(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int taken = text.IndexOf(
            "ChiakiHolepunchRegistInfo hinfo = chiaki_get_regist_info(session->holepunch_session);",
            StringComparison.Ordinal);
        if (taken < 0)
            return false;

        int pointed = text.IndexOf("info.holepunch_info = &hinfo;", taken, StringComparison.Ordinal);
        if (pointed < 0)
            return false;

        // The block ends at the first line that closes it at this indent.
        int closes = text.IndexOf("\n\t}", pointed, StringComparison.Ordinal);
        if (closes < 0)
            return false;

        string block = text[pointed..closes];

        return MustStayWithTheRegistInfo.All(
            call => block.Contains(call, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the data socket is still declared before the PSN block, which is what makes its null a
    /// value rather than an absence.
    /// </summary>
    public static bool TheDataSocketIsDeclaredOutsideThePsnBlock(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int declared = text.IndexOf("chiaki_socket_t *data_sock = NULL;", StringComparison.Ordinal);
        if (declared < 0)
            return false;

        int block = text.IndexOf("if(session->rudp)", declared, StringComparison.Ordinal);
        int assigned = text.IndexOf("data_sock = chiaki_get_holepunch_sock(", declared, StringComparison.Ordinal);

        return block > declared && assigned > block;
    }

    /// <summary>
    /// Whether the selected address is still written straight into the session's own hostname buffer,
    /// rather than returned.
    ///
    /// So a managed flow holding an address of its own would have two places to keep in step.
    /// </summary>
    public static bool TheAddressIsStillWrittenInPlace(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return sessionSource.Contains(
            "chiaki_get_ps_selected_addr(session->holepunch_session, session->connect_info.hostname)",
            StringComparison.Ordinal);
    }
}
