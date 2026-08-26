using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP370: no send in the stream connection has its answer discarded where it can be acted on.
///
/// Receiving streaminfo makes the client send three things, in order: the ack, the controller
/// connection, and the microphone enable. Two of them had their result checked. The first did not.
///
/// THE ACK IS THE ONE THAT MATTERS MOST. It is what tells the console the stream setup was accepted.
/// Failing to send it and carrying on reports CONNECTED to the client while the console is still
/// waiting to be told - and the session then dies on a timeout at the far end, for a reason nothing
/// on this side logged. The two sends that were checked are less consequential than the one that
/// was not.
///
/// THE CHECK IS OVER EVERY SEND that returns something, not the one that was wrong. This is the
/// third result-discarded finding in the same family - PP367's decrypt and PP361's log word being
/// the others - and each was one call in a group whose siblings did it right.
///
/// The heartbeat is the deliberate exception. Its failure is logged and the loop carries on, because
/// a heartbeat is a diagnostic and a stream whose heartbeats fail is still a stream until the
/// console says otherwise (PP363). So the check requires the result to be READ, not that failing
/// ends anything.
/// </summary>
public static partial class StreamSendResults
{
    /// <summary>Where the sends live.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The sends that answer something. Named so the list itself is asserted: a fourth added
    /// without being read here is a fourth the check does not cover.
    /// </summary>
    public static IReadOnlyList<string> SendsThatAnswer { get; } =
    [
        "stream_connection_send_big",
        "stream_connection_send_controller_connection",
        "stream_connection_enable_microphone",
        "stream_connection_send_streaminfo_ack",
        "stream_connection_send_disconnect",
        "stream_connection_send_heartbeat",
        "stream_connection_send_corrupt_frame",
        "stream_connection_send_idr_request",
    ];

    /// <summary>
    /// Every call to one of them whose result goes nowhere.
    ///
    /// A discard is a call opening a statement - nothing but whitespace to its left. A definition
    /// begins with its return type and does not match; a call assigned, returned or tested does not.
    /// </summary>
    /// <returns>The call text of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> DiscardedResults(string source)
        => DiscardedCalls(source, SendsThatAnswer);

    /// <summary>
    /// The same reading, over any list of functions that answer something.
    ///
    /// PP379 needed it for senkusha.c and copying it would have been a second reader with the same
    /// trap in it, which is what PP343 exists to stop. One reader, two callers, each bringing its
    /// own list - because WHICH functions answer is a fact about a file and the shape of a discard
    /// is not.
    /// </summary>
    /// <param name="source">The translation unit.</param>
    /// <param name="answering">The functions whose result is worth something.</param>
    /// <returns>The call text of each discard, so a failure names what it found.</returns>
    public static IReadOnlyList<string> DiscardedCalls(string source, IEnumerable<string> answering)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(answering);

        var found = new List<string>();

        foreach (string function in answering)
        {
            foreach (Match call in Regex.Matches(
                         source, @"^[ \t]*" + Regex.Escape(function) + @"\s*\(", RegexOptions.Multiline))
            {
                found.Add(call.Value.Trim());
            }
        }

        return found;
    }

    /// <summary>
    /// PP375: the same rule one level down, over the transport sends themselves.
    ///
    /// PP370's list above is of the wrappers. Inside them are the calls to takion, and BIG is the only
    /// message that makes more than one - it is fragmented, so it makes as many as the MTU requires.
    /// Every fragment assigned `err` and none read it, and the trailing send then overwrote what the
    /// loop had learnt. A fragment that failed in the middle returned success from a function whose
    /// caller checks it, left the console holding a BIG whose continuation never arrived, and sent the
    /// run thread on to wait for a BANG that was never coming.
    ///
    /// The count of discarded answers grew with the message and with a narrower MTU, which is why a
    /// link that measured 1454 and never fragmented hid this completely.
    /// </summary>
    public static IReadOnlyList<string> TakionSendsWhoseResultGoesNowhere(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // A discard opens a statement: nothing but whitespace to its left. Assigned, returned or
        // tested calls do not match, and neither does a declaration, which begins with its type.
        return Regex.Matches(
                source,
                @"^[ \t]*chiaki_takion_send_message_data(_cont)?\s*\(",
                RegexOptions.Multiline)
            .Select(m => m.Value.Trim())
            .ToList();
    }

    /// <summary>
    /// Whether every fragment of a BIG still has its result read.
    ///
    /// Two facts, because the function has two send sites with different obligations. The loop must
    /// test and LEAVE - a partial BIG cannot be completed by sending the rest of it, so carrying on
    /// after a failed fragment sends bytes the console will never assemble. The trailing send must
    /// test, but has nothing left to abandon, so logging is the whole of what it owes.
    /// </summary>
    public static bool EveryBigFragmentResultIsRead(string sendBigBody)
    {
        ArgumentNullException.ThrowIfNull(sendBigBody);

        int loop = sendBigBody.IndexOf("while(first ?", StringComparison.Ordinal);
        int trailing = sendBigBody.IndexOf("if(total_size > 0)", StringComparison.Ordinal);
        if (loop < 0 || trailing < 0 || trailing < loop)
            return false;

        string inLoop = sendBigBody[loop..trailing];
        bool theLoopTestsAndLeaves =
            inLoop.Contains("err != CHIAKI_ERR_SUCCESS", StringComparison.Ordinal)
            && inLoop.Contains("return err;", StringComparison.Ordinal);

        bool theTrailingSendTests = sendBigBody[trailing..]
            .Contains("err != CHIAKI_ERR_SUCCESS", StringComparison.Ordinal);

        return theLoopTestsAndLeaves && theTrailingSendTests;
    }

    /// <summary>
    /// The order the three sends triggered by streaminfo go out in.
    ///
    /// One arrival, three departures - the same shape as PP342's session-id burst in ctrl.c, and the
    /// same reason a pair table would miss it.
    /// </summary>
    public static IReadOnlyList<string> StreaminfoBurst { get; } =
    [
        "stream_connection_send_streaminfo_ack",
        "stream_connection_send_controller_connection",
        "stream_connection_enable_microphone",
    ];

    /// <summary>Whether the burst still goes out in that order.</summary>
    public static bool TheBurstIsStillInOrder(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        var at = 0;
        foreach (string send in StreaminfoBurst)
        {
            int found = handlerBody.IndexOf(send, at, StringComparison.Ordinal);
            if (found < 0)
                return false;

            at = found;
        }

        return true;
    }
}
