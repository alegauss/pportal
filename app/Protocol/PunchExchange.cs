using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the answering loop does with one thing that arrived, or failed to.</summary>
public enum PunchStep
{
    /// <summary>A request: answer it and go back to waiting.</summary>
    Answer,

    /// <summary>An extra response where a request was expected. Ordinary; wait past it.</summary>
    Ignore,

    /// <summary>Nothing arrived in time, and something had been answered already: done.</summary>
    Done,

    /// <summary>Nothing arrived in time and nothing ever had.</summary>
    TimedOut,

    /// <summary>
    /// Go round again with nothing gained. Logged, and waited on again with the WHOLE timeout - so no
    /// STEP leaves the loop for this, and PP457's bound above the loop is what does.
    /// </summary>
    /// <remarks>
    /// PP458: two producers reach it, and they are not the same event. <see cref="PunchExchange.Next"/>
    /// returns it only for a receive that FAILED; <see cref="ProbeExchange.Judge"/> also returns it for
    /// a datagram that arrived intact and was not an answer this probe accepts. What the arm means is
    /// the loop continuing without progress, which is true of both - PP256 called it Retry and PP238
    /// WaitAgain, and merging the two enums is what made the wider use visible.
    /// </remarks>
    WaitAgain,

    /// <summary>A datagram of the wrong size, or of a type this does not know.</summary>
    Fatal,
}

/// <summary>
/// PP238, PP256, PP458: the loop that answers a console's punch requests, which succeeds by FALLING
/// QUIET.
///
/// PP458 MERGED TWO MODELS OF THIS ONE FUNCTION. PP238 ported
/// <c>receive_request_send_response_ps</c> as "the loop answering punch requests" and PP256 ported it
/// again as "the last thing a successful punch does". Neither knew the other had: there were two
/// six-arm enums meaning the same six things, two decision functions taking four and five parameters
/// for the same choice, and two readers cutting the same body out of holepunch.c with their own
/// predicates. Both agreed, so nothing compared them - the shape PP454 found one level down in the
/// probe packet's offsets.
///
/// What that cost is on the record: PP457 bounded the discard at the top of the loop, and PP256's
/// <c>AFailedReceiveStillContinues</c> stayed green while no longer describing anything, because the
/// block it read was untouched. One model would have gone red once.
///
/// THERE IS NO PATH WHERE RECEIVING SOMETHING RETURNS SUCCESS. Every request that arrives is answered
/// and the wait re-entered, and the only way out with success is a timeout AFTER at least one was
/// answered. A timeout with nothing answered is the timeout error, which is right - but the caller is
/// told "done" by an absence of traffic rather than by a result. <see cref="PunchAnsweringLoop"/> is
/// where that runs; this is the decision it is made of.
///
/// A RECEIVE THAT FAILS COSTS NOTHING, AND PP457 BOUNDED HOW MANY MAY. It logs and continues, and
/// continuing re-enters the wait with the full timeout again - so the timeout bounds SILENCE rather
/// than the call, which is the shape PP212 measured in the notification wait. No step ends the loop
/// for it: <see cref="APersistentFailureEnds"/> still answers false, because the bound is a count
/// above the loop and not a step, which is deliberately a fifth way out that no step names.
///
/// AND A BAD DATAGRAM GETS THREE TREATMENTS: the wrong SIZE is fatal, a response where a request was
/// expected is ignored and waited past because an extra one is ordinary, and any other type is fatal
/// and hexdumped.
/// </summary>
public static class PunchExchange
{
    /// <summary>The size a request has to be, which is the reply's size too.</summary>
    public const int RequestLength = PunchResponse.Length;

    /// <summary>How long one wait is given, in seconds.</summary>
    public const int TimeoutSeconds = 1;

    /// <summary>
    /// What to do next.
    /// </summary>
    /// <param name="timedOut">Whether the wait ended without anything arriving.</param>
    /// <param name="answeredAny">Whether a request has been answered at some point.</param>
    /// <param name="received">Bytes the receive returned, negative where it failed.</param>
    /// <param name="messageType">The type word, meaningful only when a whole datagram arrived.</param>
    public static PunchStep Next(bool timedOut, bool answeredAny, int received, uint messageType)
    {
        // The only success, and it is an absence.
        if (timedOut)
            return answeredAny ? PunchStep.Done : PunchStep.TimedOut;

        // Costs nothing: the next wait gets the whole timeout over again.
        if (received < 0)
            return PunchStep.WaitAgain;

        if (received != RequestLength)
            return PunchStep.Fatal;

        if (messageType == PunchResponse.ResponseType)
            return PunchStep.Ignore;

        return messageType == PunchResponse.RequestType ? PunchStep.Answer : PunchStep.Fatal;
    }

    /// <summary>Whether a step leaves the loop.</summary>
    public static bool Leaves(PunchStep step)
        => step is PunchStep.Done or PunchStep.TimedOut or PunchStep.Fatal;

    /// <summary>Whether a step is one the caller is told succeeded.</summary>
    public static bool IsSuccess(PunchStep step) => step == PunchStep.Done;

    /// <summary>Every step that goes round again.</summary>
    public static IReadOnlyList<PunchStep> Continues { get; } =
        [.. Enum.GetValues<PunchStep>().Where(s => !Leaves(s))];

    /// <summary>
    /// Whether a condition that persists can end the loop by ITS STEP - which for a failing receive
    /// it cannot, and PP457's bound is why that is no longer the whole story.
    /// </summary>
    public static bool APersistentFailureEnds(PunchStep step) => Leaves(step);

    /// <summary>What the caller is told for each ending.</summary>
    public static string CodeFor(PunchStep step) => step switch
    {
        PunchStep.Done => "CHIAKI_ERR_SUCCESS",
        PunchStep.TimedOut => "CHIAKI_ERR_TIMEOUT",
        PunchStep.Fatal => "CHIAKI_ERR_NETWORK or CHIAKI_ERR_UNKNOWN",
        _ => "",
    };

    /// <summary>
    /// Whether the caller forgives the ending, which PP249 measured - the timeout is forgiven when
    /// the caller itself had already answered a request.
    /// </summary>
    public static bool CallerForgives(PunchStep step, bool callerAlreadyAnswered)
        => step == PunchStep.TimedOut && callerAlreadyAnswered;
}

/// <summary>
/// PP238, PP256, PP458: the loop where the core writes it - read in ONE place.
///
/// The union of what two source readers used to check, with one <see cref="Body"/>. Three of the
/// predicates were the same claim twice: the timeout-success line, the failed receive's continue, and
/// the wrong-size branch. Where the two versions differed in strength the stronger one survived, which
/// is stated at each.
/// </summary>
public static class PunchExchangeSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>The C function both tasks ported.</summary>
    public const string FunctionName = "receive_request_send_response_ps";

    /// <summary>
    /// How a reader finds the definition, which is what the guard below looks for.
    ///
    /// Spelled as one literal rather than interpolated from <see cref="FunctionName"/>, because the
    /// guard searches source text: an interpolated form would not appear in any file, including this
    /// one, and the check would pass by finding nothing.
    /// </summary>
    public const string Definition = "static ChiakiErrorCode receive_request_send_response_ps(Session *session";

    /// <summary>Whether the loop still has no condition of its own.</summary>
    public static bool TheLoopIsStillUnconditional(string core)
        => Body(core).Contains("    while (true)\n", StringComparison.Ordinal);

    /// <summary>
    /// Whether the only success is still a timeout with something already answered.
    ///
    /// PP238's version rather than PP256's: both matched the line, and only this one also asserts
    /// there is no OTHER success return, which is what makes the first one the only one.
    /// </summary>
    public static bool SuccessIsStillATimeout(string core)
    {
        string body = Body(core);

        return body.Contains("if(err == CHIAKI_ERR_TIMEOUT && received)", StringComparison.Ordinal)
            && body.Contains("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)
            && body.IndexOf("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)
                == body.LastIndexOf("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a failed receive still continues, counts its discard, and is bounded above the loop.
    ///
    /// The union of both versions plus PP457's half: PP238 checked that the continue comes before the
    /// size test, PP256 that the block declines to return, and neither would have noticed the bound
    /// arriving or leaving.
    /// </summary>
    public static bool AFailedReceiveContinuesUnderABound(string core)
    {
        string body = Body(core);

        int fails = body.IndexOf(
            "check_candidates: Receiving response from %s:%d failed with error: ", StringComparison.Ordinal);
        if (fails < 0)
            return false;

        int closes = body.IndexOf("\n        }", fails, StringComparison.Ordinal);
        int sized = body.IndexOf("if (len != sizeof(req))", StringComparison.Ordinal);
        if (closes < 0 || sized < closes)
            return false;

        string block = body[fails..closes];

        return block.Contains("continue;", StringComparison.Ordinal)
            && !block.Contains("return", StringComparison.Ordinal)
            && block.Contains("discarded++;", StringComparison.Ordinal)
            && body.Contains("if(discarded > MAX_CONSECUTIVE_DISCARDS)", StringComparison.Ordinal);
    }

    /// <summary>And whether the wrong-size packet beside it still ends the punch.</summary>
    public static bool TheWrongSizeStillEndsIt(string core)
    {
        string body = Body(core);

        int wrong = body.IndexOf(
            "check_candidates: Received request of unexpected size", StringComparison.Ordinal);
        if (wrong < 0)
            return false;

        int closes = body.IndexOf("\n        }", wrong, StringComparison.Ordinal);
        return closes > wrong
            && body[wrong..closes].Contains("return CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);
    }

    /// <summary>Whether a bad datagram still gets those three different treatments.</summary>
    public static bool ThreeTreatmentsForABadDatagram(string core)
    {
        string body = Body(core);

        return body.Contains("Received request of unexpected size", StringComparison.Ordinal)
            && body.Contains("Received an extra response, ignoring", StringComparison.Ordinal)
            && body.Contains("Received response of unexpected type", StringComparison.Ordinal)
            && body.Contains("chiaki_log_hexdump(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether four lines still name the OPERATION and one still names nothing.
    ///
    /// The four are defensible - PP238 settled that - so what this asserts is the split, not a count
    /// of wrongs. The fifth is the one with nothing on it.
    ///
    /// It was three of four until PP457 added the discard-limit line, which names the operation like
    /// its neighbours. The numbers move with the file rather than the check being loosened, because
    /// the split is the thing worth holding: a line appearing with no name at all, or with its own
    /// function's, is a change somebody decided and should have to say so.
    /// </summary>
    public static bool FourNameTheOperationAndOneNamesNothing(string core)
    {
        string body = Body(core);

        // Counted by the message, not by the macro: a hexdump takes CHIAKI_LOG_ERROR as an argument
        // and would be counted as another line by anything matching the prefix alone.
        int lines = body.Split("session->log, \"", StringSplitOptions.None).Length - 1;
        int operation = body.Split("\"check_candidates: ", StringSplitOptions.None).Length - 1;

        return lines == 5 && operation == 4;
    }

    /// <summary>The one line carrying no name of any kind.</summary>
    public const string TheLineThatNamesNothing = "Received an extra response, ignoring....";

    /// <summary>Whether it is still there, unqualified.</summary>
    public static bool TheUnnamedLineIsStillThere(string core)
        => Body(core).Contains($"\"{TheLineThatNamesNothing}\"", StringComparison.Ordinal);

    /// <summary>And whether the request it reads is still the probe's size.</summary>
    public static bool TheRequestIsStillTheProbesSize(string core)
        => Body(core).Contains(
            $"uint8_t req[{PunchExchange.RequestLength}] = {{0}};", StringComparison.Ordinal);

    /// <summary>
    /// PP458's guard: every file under app/Protocol that goes looking for this function's DEFINITION.
    ///
    /// One is the answer, and it is this file. A second is a third model of the same loop starting up,
    /// which is what PP238 and PP256 each did without the other noticing - and the only reason it was
    /// ever found is that PP457's fix happened to touch both.
    ///
    /// It looks for <see cref="Definition"/> and not for <see cref="FunctionName"/>: five files mention
    /// the name in prose or in a list of decided log prefixes, and none of those is a duplicate. What
    /// makes one is cutting the body out of the C, and to do that a reader has to name the definition.
    /// </summary>
    public static IReadOnlyList<string> FilesReadingTheLoop()
    {
        if (ProbeGeometry.LocateDirectory() is not { } directory)
            return [];

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(directory, "*.cs"))
        {
            if (File.ReadAllText(path).Contains(Definition, StringComparison.Ordinal))
                found.Add(Path.GetFileName(path));
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    /// receive_request_send_response_ps's body.
    ///
    /// LAST, for the reason ten earlier tasks each wrote down - and CRLF-normalised, because several
    /// predicates above match on a newline and an indent.
    /// </summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(Definition, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic void log_session_state(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
