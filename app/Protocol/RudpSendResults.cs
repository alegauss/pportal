using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP384: the rudp retry loop reads what its sends answered, and says which failure it had.
///
/// chiaki_rudp_send_recv is the loop the whole PSN handshake runs through - init, cookie, ack and
/// session message all reach the console from its switch. Every arm threw its answer away, and the
/// next statement was a receive with a timeout. So a send that failed on the socket was followed by
/// waiting the full timeout for a reply to a message that never left, and the loop reported what it
/// saw: a timeout. The caller was told the console did not answer.
///
/// THE RETRY WAS NEVER THE PROBLEM. A lost datagram is exactly what this loop exists for. What was
/// wrong is that a send which failed LOCALLY is not a lost datagram, the difference was in hand one
/// line before it was needed, and the cost of not using it is tries times the select timeout spent
/// waiting for replies to messages the socket refused.
///
/// SO THE FIX IS A SKIP AND A SENTENCE. A failed send logs and takes the next try immediately
/// rather than waiting, and the summary at the end says how many of the tries never left this host.
/// The return code is deliberately unchanged: nine callers test it only against SUCCESS, and the
/// report is what was missing rather than the branch.
///
/// PP370, PP375, PP379 and PP383 are the same family. Each of those was one call in a group whose
/// siblings were correct; this is the one where all four siblings were wrong together, which is why
/// nothing about it looked odd.
/// </summary>
public static class RudpSendResults
{
    /// <summary>Where the loop lives.</summary>
    public const string RelativePath = @"lib\src\remote\rudp.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The four the switch sends, in the order its arms appear.</summary>
    public static IReadOnlyList<string> TheFourSends { get; } =
    [
        "chiaki_rudp_send_init_message",
        "chiaki_rudp_send_cookie_message",
        "chiaki_rudp_send_ack_message",
        "chiaki_rudp_send_session_message",
    ];

    /// <summary>
    /// The sends in rudp.c that answer something, which is every one of them.
    ///
    /// Named rather than derived, the way PP379's list is, and checked against the file by
    /// <see cref="AnsweringSendsIn"/> so a fifth is not silently outside the rule.
    /// </summary>
    public static IReadOnlyList<string> SendsThatAnswer { get; } =
    [
        .. TheFourSends,
        "chiaki_rudp_send_ctrl_message",
        "chiaki_rudp_send_switch_to_stream_connection_message",
        // chiaki_rudp_send_raw, which the first version of this list left out - and the check
        // against the file is what said so rather than a silent pass over one more send.
        "chiaki_rudp_send_raw",
        "chiaki_rudp_send_recv",
    ];

    /// <summary>
    /// Every call whose result goes nowhere, through the reader PP379 lifted out of PP370.
    /// </summary>
    public static IReadOnlyList<string> DiscardedResults(string source)
        => StreamSendResults.DiscardedCalls(source, SendsThatAnswer);

    /// <summary>Every <c>chiaki_rudp_send_*</c> the file declares as answering a code.</summary>
    public static IReadOnlyList<string> AnsweringSendsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (string line in source.Split('\n'))
        {
            const string Prefix = "CHIAKI_EXPORT ChiakiErrorCode chiaki_rudp_send_";
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            int open = line.IndexOf('(', Prefix.Length);
            if (open < 0)
                continue;

            string name = line["CHIAKI_EXPORT ChiakiErrorCode ".Length..open].Trim();
            if (name.Length > 0 && !found.Contains(name, StringComparer.Ordinal))
                found.Add(name);
        }

        return found;
    }

    /// <summary>
    /// Whether all four arms of the switch still assign what they answered.
    ///
    /// Counted over the four rather than looked for once: they were wrong together, so a check that
    /// found one fixed would pass on three that were not.
    /// </summary>
    public static int ArmsThatDiscardTheirResult(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "ChiakiErrorCode chiaki_rudp_send_recv(");
        if (body is null)
            return -1;

        return TheFourSends.Count(send => !body.Contains($"send_err = {send}(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a failed send still skips the receive rather than waiting a timeout for a reply to
    /// a message that never left.
    ///
    /// The <c>continue</c> is the half that costs time; without it the answer is read and then
    /// ignored in the most expensive way available.
    /// </summary>
    public static bool AFailedSendSkipsTheReceive(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "ChiakiErrorCode chiaki_rudp_send_recv(");
        if (body is null)
            return false;

        int tested = body.IndexOf("if(send_err != CHIAKI_ERR_SUCCESS)", StringComparison.Ordinal);
        if (tested < 0)
            return false;

        int logged = body.IndexOf("CHIAKI_LOGE", tested, StringComparison.Ordinal);
        int skipped = body.IndexOf("continue;", logged < 0 ? tested : logged, StringComparison.Ordinal);
        int received = body.IndexOf("chiaki_rudp_select_recv(", tested, StringComparison.Ordinal);

        return logged > tested && skipped > logged && received > skipped;
    }

    /// <summary>
    /// Whether the summary still distinguishes a failed send from a silent console.
    ///
    /// This is the sentence somebody reads when a remote play session will not start, and it used
    /// to say the console had not answered whatever had happened.
    /// </summary>
    public static bool TheSummarySeparatesTheTwoFailures(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "ChiakiErrorCode chiaki_rudp_send_recv(");
        if (body is null)
            return false;

        return body.Contains("if(send_failures > 0)", StringComparison.Ordinal)
            && body.Contains("never left this host", StringComparison.Ordinal);
    }
}
