using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One of the nine things session.c asks a holepunch session for.</summary>
public enum HolepunchStep
{
    /// <summary>The socket the control channel rides on.</summary>
    CtrlSocket,

    /// <summary>The registration info the session request carries.</summary>
    RegistInfo,

    /// <summary>An offer for the data connection.</summary>
    CreateOffer,

    /// <summary>A hole punched for the data connection.</summary>
    PunchHole,

    /// <summary>The socket the stream rides on.</summary>
    DataSocket,

    /// <summary>The address the console was reached at.</summary>
    SelectedAddress,

    /// <summary>The port the control channel connects to.</summary>
    CtrlPort,

    /// <summary>The session released - twice, on two teardown paths.</summary>
    Fini,
}

/// <summary>What happens when a step goes wrong.</summary>
public enum HolepunchGuard
{
    /// <summary>Nothing it returns can say it failed, so there is nothing to guard.</summary>
    NoFailureToReport,

    /// <summary>A failure is tested and quits the connect down the ctrl teardown path.</summary>
    QuitsToCtrlTeardown,

    /// <summary>
    /// A failure is not tested here, but the thing built from the result tests itself - so it
    /// surfaces under another name.
    /// </summary>
    CaughtByWhatItFeeds,

    /// <summary>Nothing tests the result at all.</summary>
    Unchecked,
}

/// <summary>
/// PP460, under PP340: the ORDER session.c asks in, and what a failure at each point does.
///
/// PP429 wrote down the nine call sites as an interface and said so: "held by name and by relative
/// position", in FILE order. PP340's section now says what is left after PP452, PP455, PP456 and
/// PP459 gave the four socket classes their I/O - "not I/O but sequence: nothing managed calls those
/// pieces in order, decides what happens when one fails, or holds the state between them." This is the
/// first two of those three.
///
/// FILE ORDER IS NOT EXECUTION ORDER, and the difference is the two finis. They sit at the top of
/// session.c, before every other call site, and they run last - a reader taking PP429's list as a
/// sequence would tear the session down before punching anything. <see cref="ExecutionOrder"/> and
/// <see cref="HolepunchSeam.Asks"/> are therefore two statements about the same nine, and both are
/// asserted.
///
/// ONLY FOUR OF THE NINE CAN REPORT A FAILURE AT ALL. Two return a ChiakiErrorCode and two return a
/// pointer; the other five hand back a struct, an address written into a caller's buffer, or a port,
/// with no value reserved for "it went wrong". So the question "is this checked" is only meaningful
/// four times, and <see cref="GuardFor"/> says which answer each gets.
///
/// AND THE TWO POINTERS ARE NOT GUARDED THE SAME WAY. The ctrl socket is not tested either, but what
/// it feeds is: <c>chiaki_rudp_init</c> is checked for null, and PP339 made that a QUIT after finding
/// it had carried on with rudp NULL and reported the failure as "no address answered". The DATA socket
/// is tested by nothing and feeds nothing that tests itself. PP339 fixed two members of this family -
/// the second, the offer, found by the check written for the first - and this is the third.
/// </summary>
public static class HolepunchFlow
{
    /// <summary>
    /// The seven steps that run in line, in the order they run.
    ///
    /// <see cref="HolepunchStep.Fini"/> is not here: it is teardown, reached from two paths, and
    /// putting it in a sequence is the mistake this list exists to prevent.
    /// </summary>
    public static IReadOnlyList<HolepunchStep> ExecutionOrder { get; } =
    [
        HolepunchStep.CtrlSocket,
        HolepunchStep.RegistInfo,
        HolepunchStep.CreateOffer,
        HolepunchStep.PunchHole,
        HolepunchStep.DataSocket,
        HolepunchStep.SelectedAddress,
        HolepunchStep.CtrlPort,
    ];

    /// <summary>The C function each step calls.</summary>
    public static string CalleeFor(HolepunchStep step) => step switch
    {
        HolepunchStep.CtrlSocket or HolepunchStep.DataSocket => "chiaki_get_holepunch_sock",
        HolepunchStep.RegistInfo => "chiaki_get_regist_info",
        HolepunchStep.CreateOffer => "holepunch_session_create_offer",
        HolepunchStep.PunchHole => "chiaki_holepunch_session_punch_hole",
        HolepunchStep.SelectedAddress => "chiaki_get_ps_selected_addr",
        HolepunchStep.CtrlPort => "chiaki_get_ps_ctrl_port",
        _ => "chiaki_holepunch_session_fini",
    };

    /// <summary>What a failure at this step does.</summary>
    public static HolepunchGuard GuardFor(HolepunchStep step) => step switch
    {
        HolepunchStep.CreateOffer or HolepunchStep.PunchHole => HolepunchGuard.QuitsToCtrlTeardown,
        HolepunchStep.CtrlSocket => HolepunchGuard.CaughtByWhatItFeeds,
        HolepunchStep.DataSocket => HolepunchGuard.Unchecked,
        _ => HolepunchGuard.NoFailureToReport,
    };

    /// <summary>Whether anything this step returns can say it went wrong.</summary>
    public static bool CanReportFailure(HolepunchStep step)
        => GuardFor(step) != HolepunchGuard.NoFailureToReport;

    /// <summary>
    /// The step whose failure nothing notices. One, and naming it as a value rather than as prose is
    /// what lets a test assert there is exactly one.
    /// </summary>
    public static IReadOnlyList<HolepunchStep> Unguarded { get; } =
        [.. Enum.GetValues<HolepunchStep>().Where(s => GuardFor(s) == HolepunchGuard.Unchecked)];

    /// <summary>session.c, where the sequence lives.</summary>
    public static string? Locate() => HolepunchSeam.Locate();

    /// <summary>
    /// Every step in the order its call appears in the file, which is what
    /// <see cref="ExecutionOrder"/> is compared against.
    ///
    /// The two finis are dropped: they appear first and run last, and a reader comparing the two lists
    /// has to be told which difference is expected.
    /// </summary>
    public static IReadOnlyList<HolepunchStep> InFileOrder(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<(int At, HolepunchStep Step)>();

        foreach (HolepunchStep step in Enum.GetValues<HolepunchStep>())
        {
            if (step == HolepunchStep.Fini)
                continue;

            string callee = CalleeFor(step);
            string needle = step switch
            {
                HolepunchStep.CtrlSocket => $"{callee}(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL)",
                HolepunchStep.DataSocket => $"{callee}(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)",
                _ => $"{callee}(session->holepunch_session",
            };

            int at = source.IndexOf(needle, StringComparison.Ordinal);
            if (at >= 0)
                found.Add((at, step));
        }

        return [.. found.OrderBy(f => f.At).Select(f => f.Step)];
    }

    /// <summary>
    /// Whether the two finis still come BEFORE every other call site in the file, which is the
    /// difference between the two orders.
    /// </summary>
    public static bool TheFinisStillComeFirstInTheFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int lastFini = source.LastIndexOf(
            "chiaki_holepunch_session_fini(session->holepunch_session)", StringComparison.Ordinal);
        int firstOther = source.IndexOf(
            "chiaki_get_holepunch_sock(session->holepunch_session", StringComparison.Ordinal);

        return lastFini >= 0 && firstOther > lastFini;
    }

    /// <summary>
    /// Whether the two error-returning steps still QUIT on failure - which is PP339's fix, and the
    /// thing the data socket has no equivalent of.
    /// </summary>
    public static bool BothErrorStepsStillQuit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return QuitsAfter(source, "holepunch_session_create_offer(session->holepunch_session)")
            && QuitsAfter(
                source,
                "chiaki_holepunch_session_punch_hole(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)");
    }

    /// <summary>Whether the ctrl socket's failure is still caught by the rudp init it feeds.</summary>
    public static bool TheCtrlSocketIsStillCaughtByRudpInit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int socket = source.IndexOf(
            "chiaki_get_holepunch_sock(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL)",
            StringComparison.Ordinal);
        if (socket < 0)
            return false;

        string after = source[socket..];
        int init = after.IndexOf("chiaki_rudp_init(rudp_sock", StringComparison.Ordinal);
        int guard = after.IndexOf("if(!session->rudp)", StringComparison.Ordinal);
        int quits = after.IndexOf("QUIT(quit);", StringComparison.Ordinal);

        return init >= 0 && guard > init && quits > guard;
    }

    /// <summary>
    /// THE FINDING. Whether the data socket's result is still tested by nothing.
    ///
    /// True means the gap is present, which is what this asserts rather than a fix - the repair is
    /// filed separately, because it is a change to lib/ and not a reading of it.
    /// </summary>
    public static bool TheDataSocketIsStillUnchecked(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf(
            "data_sock = chiaki_get_holepunch_sock(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)",
            StringComparison.Ordinal);
        if (at < 0)
            return false;

        // Everything between the call and the wait that ends the block. Nothing in it may mention the
        // variable again, which is what "tested by nothing" means here.
        int ends = source.IndexOf("chiaki_cond_timedwait_pred(", at, StringComparison.Ordinal);
        if (ends < 0)
            return false;

        return !source[(at + 1)..ends].Contains("data_sock", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a failed punch still sends the holepunch STARTED event and never the finished one.
    ///
    /// The pair brackets the punch: finished=false goes out before it, finished=true after the data
    /// socket is in hand. A failure between them quits, so the second never goes - and a listener that
    /// pairs them sees a start with no end.
    /// </summary>
    public static bool AFailedPunchSendsNoFinishedEvent(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int starts = source.IndexOf("event_start.data_holepunch.finished = false;", StringComparison.Ordinal);
        int finishes = source.IndexOf("event_finish.data_holepunch.finished = true;", StringComparison.Ordinal);
        if (starts < 0 || finishes < starts)
            return false;

        // The quit that skips the second one sits between them.
        return source[starts..finishes].Contains("QUIT(quit_ctrl);", StringComparison.Ordinal);
    }

    private static bool QuitsAfter(string source, string call)
    {
        int at = source.IndexOf(call, StringComparison.Ordinal);
        if (at < 0)
            return false;

        int tested = source.IndexOf("if (err != CHIAKI_ERR_SUCCESS)", at, StringComparison.Ordinal);
        int quits = source.IndexOf("QUIT(quit_ctrl);", at, StringComparison.Ordinal);

        return tested > at && quits > tested;
    }
}
