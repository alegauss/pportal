using System.Globalization;
using ChiakiNg.Protocol;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>What a managed wait is to the C wait beside it.</summary>
public enum WaitKind
{
    /// <summary>The managed constant follows a C macro: change the macro and the port must move.</summary>
    MirrorsAMacro,

    /// <summary>The managed constant follows a number written at a C call site, where there is no macro.</summary>
    MirrorsALiteral,

    /// <summary>The two differ on purpose, and the departure is carried as a value.</summary>
    DeliberatelyDifferent,

    /// <summary>A C wait in a unit this port has not reached. Nothing managed answers for it yet.</summary>
    NoCounterpartYet,
}

/// <summary>One wait in the C, and what the managed side does about it.</summary>
/// <param name="Name">The macro's name, or the call-site text for a literal.</param>
/// <param name="SourceRelativePath">The C file it is written in.</param>
/// <param name="Kind">Which of the four this is.</param>
/// <param name="CText">The macro's body exactly as the C spells it; empty for a literal or a departure.</param>
/// <param name="Managed">The managed value, or null where nothing answers for it.</param>
/// <param name="Note">Why this row is the kind it is.</param>
public readonly record struct NativeWait(
    string Name,
    string SourceRelativePath,
    WaitKind Kind,
    string CText,
    double? Managed,
    string Note);

/// <summary>
/// PP585: every timing constant the C names, and which of them the managed side is meant to agree with.
///
/// PP577 to PP581 asked one question across the seam - what here is a hand-written promise about
/// something outside the tree? - and closed it for three enums, a struct size, a symbol table and six
/// callback signatures. Timing was left out, and it is the same shape with a quieter failure: a macro
/// changed upstream leaves the port waiting a different length than the C it reproduces, which is not
/// a crash and is why nothing would report it.
///
/// THE ANSWER IS NOT "MAKE THE TWO COUNTS EQUAL". The C names 32 timing constants across thirteen
/// files. Twenty have a managed constant that follows them, twelve are in units this port has not
/// reached, and three more managed waits follow a number the C did NOT give a name to.
///
/// PP718: AND A ROW CLAIMING NO COUNTERPART IS CHECKED FOR ONE. PP714 ported congestion control and
/// this census went on saying congestioncontrol.c was unported, through a green gate - because the
/// groups were asserted by COUNT, and a row moving from one to the other keeps every count valid.
/// <see cref="Unclaimed"/> is the direction that was missing: a file with managed code answering for
/// it has no business in the unported group.
///
/// AND MATCHING BY VALUE WOULD GET FOUR OF THEM WRONG. Each of those four sits in a file that also
/// defines a macro with the SAME NUMBER for a DIFFERENT wait:
///
/// <list type="bullet">
/// <item>ctrl.c connects with an inline 5000 and defines CTRL_EXPECT_TIMEOUT 5000 for its sends.</item>
/// <item>session.c waits 10000 inline for the registration and defines SESSION_EXPECT_CTRL_START_MS
/// 10000 for the ctrl start.</item>
/// <item>rudp.c selects on an inline 1500 and defines RUDP_EXPECT_TIMEOUT_MS 1000 above it.</item>
/// <item>holepunch.c discovers with an inline 2000 and defines UPNP_DISCOVER_TIMEOUT_MS 7000 for the
/// other half of the same UPnP call.</item>
/// </list>
///
/// So a port that had bound its connect timeout to CTRL_EXPECT_TIMEOUT would look right, agree today,
/// and follow the wrong number the first time upstream moved one of them. That is the reason this is
/// a written list and not a comparison of two totals.
/// </summary>
public static class NativeWaits
{
    /// <summary>The file that names nine of them, which is more than any other.</summary>
    public const string Holepunch = @"lib\src\remote\holepunch.c";

    /// <summary>Where the rudp frame's own wait is.</summary>
    public const string Rudp = @"lib\src\remote\rudp.c";

    /// <summary>And its send buffer's two.</summary>
    public const string RudpSend = @"lib\src\remote\rudpsendbuffer.c";

    /// <summary>The one wait declared in a header.</summary>
    public const string Stun = @"lib\src\remote\stun.h";

    /// <summary>Unported, and one macro each or two.</summary>
    public const string CongestionControl = @"lib\src\congestioncontrol.c";

    /// <summary>Where the connect literal and the send macro share a number.</summary>
    public const string Ctrl = @"lib\src\ctrl.c";

    /// <summary>The feedback sender's window.</summary>
    public const string FeedbackSender = @"lib\src\feedbacksender.c";

    /// <summary>Registration's three.</summary>
    public const string Regist = @"lib\src\regist.c";

    /// <summary>Senkusha's three, including the longest wait in the tree.</summary>
    public const string Senkusha = @"lib\src\senkusha.c";

    /// <summary>Where the regist literal and the ctrl-start macro share a number.</summary>
    public const string SessionSource = @"lib\src\session.c";

    /// <summary>The idle loop's two.</summary>
    public const string StreamConnection = @"lib\src\streamconnection.c";

    /// <summary>Takion's two.</summary>
    public const string Takion = @"lib\src\takion.c";

    /// <summary>And its send buffer's two.</summary>
    public const string TakionSend = @"lib\src\takionsendbuffer.c";

    /// <summary>Every C file that names a wait.</summary>
    public static IReadOnlyList<string> Sources { get; } =
    [
        Holepunch, Rudp, RudpSend, Stun, CongestionControl, Ctrl, FeedbackSender,
        Regist, Senkusha, SessionSource, StreamConnection, Takion, TakionSend,
    ];

    /// <summary>The twenty macros a managed constant follows.</summary>
    public static IReadOnlyList<NativeWait> Mirrored { get; } =
    [
        new("SECOND_US", Holepunch, WaitKind.MirrorsAMacro, "1000000L",
            CandidateWait.SecondUs, "the multiplier the candidate wait spells out"),
        new("MILLISECONDS_US", Holepunch, WaitKind.MirrorsAMacro, "1000L",
            NotificationWait.MicrosecondsPerMillisecond, "the other unit, where the notification wait converts"),
        new("WEBSOCKET_PING_INTERVAL_SEC", Holepunch, WaitKind.MirrorsAMacro, "5",
            PushSocketLoop.PingIntervalSeconds, "how often the push socket pings"),
        new("SESSION_CREATION_TIMEOUT_SEC", Holepunch, WaitKind.MirrorsAMacro, "30",
            SessionCreate.TimeoutSeconds, "and it is not shared with the start - SharesOneTimeout is false"),
        new("SESSION_START_TIMEOUT_SEC", Holepunch, WaitKind.MirrorsAMacro, "30",
            SessionStart.TimeoutSeconds, "the same number as the create and a separate timeout"),
        new("SELECT_CANDIDATE_TIMEOUT_SEC", Holepunch, WaitKind.MirrorsAMacro, "0.5F",
            CandidateRace.SelectTimeoutSeconds, "a float, and the port keeps it one"),
        new("SELECT_CANDIDATE_CONNECTION_SEC", Holepunch, WaitKind.MirrorsAMacro, "5",
            CandidateRace.SelectConnectionSeconds, "the long window beside the short one"),
        new("WAIT_RESPONSE_TIMEOUT_SEC", Holepunch, WaitKind.MirrorsAMacro, "1",
            PunchExchange.TimeoutSeconds, "one second per punch exchange attempt"),
        new("UPNP_DISCOVER_TIMEOUT_MS", Holepunch, WaitKind.MirrorsAMacro, "7000",
            GatewayDiscovery.TimeoutMs, "the gateway lookup, NOT the device search below it"),
        new("RUDP_EXPECT_TIMEOUT_MS", Rudp, WaitKind.MirrorsAMacro, "1000",
            RudpFrame.ExpectTimeoutMs, "the frame expectation, not the select at rudp.c:490"),
        new("RUDP_DATA_RESEND_TIMEOUT_MS", RudpSend, WaitKind.MirrorsAMacro, "400",
            RudpSendBuffer.ResendTimeoutMs, "the resend clock"),
        new("RUDP_DATA_RESEND_WAKEUP_TIMEOUT_MS", RudpSend, WaitKind.MirrorsAMacro,
            "(RUDP_DATA_RESEND_TIMEOUT_MS/2)", RudpSendBuffer.ResendWakeupTimeoutMs,
            "derived from the row above, and derived on the managed side too"),
        new("STUN_REPLY_TIMEOUT_SEC", Stun, WaitKind.MirrorsAMacro, "5",
            StunServers.ReplyTimeoutSeconds, "the only wait declared in a header rather than a source"),
        new("EXPECT_TIMEOUT_MS", StreamConnection, WaitKind.MirrorsAMacro, "5000",
            StreamIdleLoop.ExpectTimeoutMs, "one of two macros with this name - senkusha.c has the other"),
        new("HEARTBEAT_INTERVAL_MS", StreamConnection, WaitKind.MirrorsAMacro, "1000",
            StreamIdleLoop.HeartbeatIntervalMs, "the idle loop's own beat"),
        new("TAKION_AV_REORDER_TIMEOUT_US", Takion, WaitKind.MirrorsAMacro, "16000",
            AvReorderTimeout.TimeoutUs, "microseconds, and the only wait in that unit"),
        new("TAKION_EXPECT_TIMEOUT_MS", Takion, WaitKind.MirrorsAMacro, "5000",
            TakionHandshake.ExpectTimeoutMs, "the handshake expectation"),
        new("TAKION_DATA_RESEND_TIMEOUT_MS", TakionSend, WaitKind.MirrorsAMacro, "200",
            TakionResendLoop.ResendTimeoutMs, "takion's resend clock, half rudp's"),
        new("TAKION_DATA_RESEND_WAKEUP_TIMEOUT_MS", TakionSend, WaitKind.MirrorsAMacro,
            "(TAKION_DATA_RESEND_TIMEOUT_MS/2)", TakionResendLoop.WakeupTimeoutMs,
            "the same halving as rudp's, and the same shape on the managed side"),
        new("CONGESTION_CONTROL_INTERVAL_MS", CongestionControl, WaitKind.MirrorsAMacro, "200",
            ManagedCongestionControl.IntervalMs,
            "PP714 moved it out of the unported group by writing the thread that waits it"),
    ];

    /// <summary>
    /// The four that follow a number the C never named - and the macro each would be joined to by a
    /// reader matching values, which is why this list exists.
    /// </summary>
    public static IReadOnlyList<NativeWait> Literals { get; } =
    [
        new("chiaki_stop_pipe_connect(&ctrl->stop_pipe, sock, sa, addr->ai_addrlen, 5000)",
            Ctrl, WaitKind.MirrorsALiteral, "", CtrlTcpConnect.ConnectTimeoutMs,
            "CTRL_EXPECT_TIMEOUT is also 5000 and is the SEND timeout, not this"),
        // PP632: session.c's registration wait stood here and went with the PSN block. It was
        // reached only through the holepunch handle - the registration info was read out of it - so
        // it has no entry point left. Kept OUT of the list rather than kept in it: this list is
        // what a reader matching values against macros would join wrongly, and a wait that is not
        // in the file cannot be joined to anything.
        //
        // What it said is worth keeping and is why it was here: SESSION_EXPECT_CTRL_START_MS is
        // also 10000 and waits on the ctrl start, which is a different wait with the same number.

        new("chiaki_rudp_select_recv(rudp, 1500, message)",
            Rudp, WaitKind.MirrorsALiteral, "", RudpExchange.SelectTimeoutMs,
            "the same file's RUDP_EXPECT_TIMEOUT_MS is 1000 and is a different wait"),
        new("2000 /** ms, delay*/",
            Holepunch, WaitKind.MirrorsALiteral, "", PortMapping.DiscoverMs,
            "the device search inside the call UPNP_DISCOVER_TIMEOUT_MS times the gateway half of"),
    ];

    /// <summary>Where the two are meant to differ, with the departure carried as a value.</summary>
    public static IReadOnlyList<NativeWait> Departures { get; } =
    [
        new("the create's websocket wait", Holepunch, WaitKind.DeliberatelyDifferent,
            "", null,
            "PP545: the C waits with no bound at all, the managed one is bounded, and "
            + nameof(HolepunchCreate) + " carries that as a value rather than as a comment"),
    ];

    /// <summary>The twelve macros in units this port has not reached.</summary>
    public static IReadOnlyList<NativeWait> Unported { get; } =
    [
        new("SESSION_DELETION_TIMEOUT_SEC", Holepunch, WaitKind.NoCounterpartYet, "3",
            null, "the wait for the deletion notification; SessionDelete's 10 is the HTTP timeout beside it"),
        new("FEEDBACK_STATE_TIMEOUT_MIN_MS", FeedbackSender, WaitKind.NoCounterpartYet, "8",
            null, "PP717 ported the recorder, not the thread; and the C's own TODO says it waits this nowhere"),
        new("FEEDBACK_STATE_TIMEOUT_MAX_MS", FeedbackSender, WaitKind.NoCounterpartYet, "200",
            null, "the other end of the same window, and the one the thread does wait"),
        new("SEARCH_REQUEST_SLEEP_MS", Regist, WaitKind.NoCounterpartYet, "100",
            null, "PP29 modelled regist's shapes, not its clock"),
        new("REGIST_SEARCH_TIMEOUT_MS", Regist, WaitKind.NoCounterpartYet, "3000", null, "as above"),
        new("REGIST_REPONSE_TIMEOUT_MS", Regist, WaitKind.NoCounterpartYet, "3000",
            null, "spelled REPONSE in the core, and reproduced rather than corrected"),
        new("EXPECT_TIMEOUT_MS", Senkusha, WaitKind.NoCounterpartYet, "5000",
            null, "senkusha.c is unported; the name collides with streamconnection.c's"),
        new("CONNECT_TIMEOUT_MS", Senkusha, WaitKind.NoCounterpartYet, "30000",
            null, "the longest wait in the tree"),
        new("EXPECT_PONG_TIMEOUT_MS", Senkusha, WaitKind.NoCounterpartYet, "1000", null, "as above"),
        new("SESSION_EXPECT_TIMEOUT_MS", SessionSource, WaitKind.NoCounterpartYet, "5000",
            null, "session.c's send and recv wait; PP28 is where it lands"),
        new("SESSION_EXPECT_CTRL_START_MS", SessionSource, WaitKind.NoCounterpartYet, "10000",
            null, "the ctrl-start wait, and NOT what SessionRegistFork.RegistWaitMs follows"),
        new("CTRL_EXPECT_TIMEOUT", Ctrl, WaitKind.NoCounterpartYet, "5000",
            null, "ctrl.c's send and recv wait, and NOT what CtrlTcpConnect.ConnectTimeoutMs follows"),
    ];

    /// <summary>Every row, in the four groups above.</summary>
    public static IReadOnlyList<NativeWait> All { get; } =
        [.. Mirrored, .. Literals, .. Departures, .. Unported];

    /// <summary>The claim an unported row makes about its file, which a ship can falsify.</summary>
    public const string UnportedClaim = "is unported";

    /// <summary>
    /// PP718: unported rows that contradict themselves - a file said to be unported which another
    /// row already follows a macro out of.
    ///
    /// The direction the census was missing. PP714 gave CONGESTION_CONTROL_INTERVAL_MS a managed
    /// counterpart and left the note reading "congestioncontrol.c is unported"; every count still
    /// added up, so the gate stayed green over a row that was now false.
    ///
    /// A half-ported file is not the failure - feedbacksender.c has PP717's recorder and still owes
    /// the thread that waits its window, and its rows say so without claiming the file is untouched.
    /// What this catches is the SENTENCE: a row cannot say a file is unported while a mirrored row
    /// names that same file.
    /// </summary>
    public static IReadOnlyList<NativeWait> Unclaimed { get; } =
    [
        .. Unported.Where(
            one => one.Note.Contains(UnportedClaim, StringComparison.OrdinalIgnoreCase)
                && Mirrored.Any(
                    other => string.Equals(other.SourceRelativePath, one.SourceRelativePath, StringComparison.Ordinal))),
    ];

    /// <summary>One source, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// Every timing macro a file defines, by name.
    ///
    /// The pattern is the names the core actually uses: a wait word, or a unit suffix. It is what
    /// decides whether a macro arriving upstream is one this list has to account for, so it is
    /// deliberately wider than the rows - a name it admits and <see cref="All"/> does not is the
    /// failure this exists to report.
    /// </summary>
    public static IReadOnlyList<string> MacrosIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (string line in source.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith("#define ", StringComparison.Ordinal))
                continue;

            string rest = text["#define ".Length..].TrimStart();
            int end = rest.IndexOfAny([' ', '\t', '(']);
            if (end <= 0)
                continue;

            string name = rest[..end];
            if (IsATimingName(name))
                found.Add(name);
        }

        return found;
    }

    /// <summary>Whether a macro's name says it holds a duration.</summary>
    public static bool IsATimingName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string[] words = ["TIMEOUT", "INTERVAL", "SLEEP", "PERIOD", "HEARTBEAT", "WAKEUP"];
        if (words.Any(w => name.Contains(w, StringComparison.Ordinal)))
            return true;

        string[] units = ["_MS", "_SEC", "_US"];
        return units.Any(u => name.EndsWith(u, StringComparison.Ordinal));
    }

    /// <summary>
    /// A macro's body, with a trailing comment removed, or null where the file does not define it.
    ///
    /// Two files define EXPECT_TIMEOUT_MS, which is why this takes a source and not a tree: the row
    /// names the file, and reading the first match anywhere would let one of the two answer for both.
    /// </summary>
    public static string? BodyOf(string source, string macro)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(macro);

        foreach (string line in source.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith("#define ", StringComparison.Ordinal))
                continue;

            string rest = text["#define ".Length..].TrimStart();
            if (!rest.StartsWith(macro, StringComparison.Ordinal))
                continue;

            string after = rest[macro.Length..];
            if (after.Length > 0 && after[0] is not (' ' or '\t'))
                continue;

            int comment = after.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                after = after[..comment];

            comment = after.IndexOf("/*", StringComparison.Ordinal);
            if (comment >= 0)
                after = after[..comment];

            return after.Trim();
        }

        return null;
    }

    /// <summary>The body as a number, or null where it is an expression rather than a literal.</summary>
    public static double? NumberOf(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string bare = body.TrimEnd('L', 'l', 'F', 'f', 'U', 'u');
        return double.TryParse(bare, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }
}
