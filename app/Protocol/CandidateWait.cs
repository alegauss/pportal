namespace ChiakiNg.Protocol;

/// <summary>What the wait loop does after a window elapses with nothing to read.</summary>
public enum WaitStep
{
    /// <summary>Send the probe at every candidate again and wait another short window.</summary>
    Retry,

    /// <summary>Something has answered - stop retrying and wait one long window instead.</summary>
    Connect,

    /// <summary>Out of both. The console is unreachable.</summary>
    Unreachable,

    /// <summary>A candidate was already chosen, so the loop leaves.</summary>
    Done,
}

/// <summary>Which socket the wait handed back, once the ladder has classified it.</summary>
public enum ReadySocket
{
    /// <summary>The IPv4 socket.</summary>
    Ipv4,

    /// <summary>The IPv6 one.</summary>
    Ipv6,

    /// <summary>One of the guessed-port sockets.</summary>
    Guessed,

    /// <summary>
    /// None of them - which the ladder only NOTICES when port guessing is on.
    /// </summary>
    Unrecognised,
}

/// <summary>
/// PP245: waiting for a candidate to answer.
///
/// TWO TIMEOUT SHAPES, NEVER BOTH. Before anything has answered the window is half a second, put
/// wholly into the microsecond field with the second field zeroed; after something has, it is five
/// seconds put wholly into the second field. <see cref="Window"/> returns the pair so a port cannot
/// quietly normalise one into the other, because the core never carries a value in both.
///
/// The short one is a float times a long, cast to int. Half a second is exactly representable in a
/// float and the multiplication lands on 500000 with nothing to truncate. Most values in that place
/// would not - 0.1F times a million is 100000.0 only because the compiler rounds the way it does,
/// and a cast toward zero has no margin. Correct by its constant, not by its shape, which is why
/// <see cref="ShortWindowIsExact"/> asserts the arithmetic rather than the answer.
///
/// AND ONE MESSAGE SAYS THE OPPOSITE OF WHAT HAPPENED. A ready socket that is none of the ones the
/// ladder knows gets its own handle invalidated, and the next guard logs that the waiting loop
/// returned with no socket having data. Something had data. It was not a socket this ladder
/// recognises - and the ladder only looks for the third kind when port guessing is on, so with it
/// off an unrecognised socket is carried forward rather than caught.
/// </summary>
public static class CandidateWait
{
    /// <summary>How many short windows are spent before anything answers.</summary>
    public const int Tries = 20;

    /// <summary>The short window, in microseconds.</summary>
    public const int ShortWindowUs = 500_000;

    /// <summary>And the long one, in whole seconds.</summary>
    public const int LongWindowSec = 5;

    /// <summary>A second, in microseconds - the multiplier the core spells out.</summary>
    public const long SecondUs = 1_000_000L;

    /// <summary>The float the core writes, before the multiplication.</summary>
    public const float ShortWindowSec = 0.5F;

    /// <summary>
    /// Whether the float-times-long-cast-to-int lands exactly on the window, with nothing lost.
    ///
    /// It does, for this constant. The assertion is on the arithmetic so that changing the constant
    /// to something unrepresentable fails here rather than in a timeout nobody measures.
    /// </summary>
    public static bool ShortWindowIsExact()
    {
        float product = ShortWindowSec * SecondUs;

        // Exact means the cast loses nothing: truncating and rounding up reach the same integer, so
        // there is no fraction for the cast toward zero to eat. Compared as ints, because comparing
        // the float to a literal is the thing this is testing FOR.
        return (int)product == ShortWindowUs && (int)MathF.Ceiling(product) == (int)product;
    }

    /// <summary>
    /// The window for one turn: seconds and microseconds, exactly as the core fills them.
    /// </summary>
    /// <param name="connecting">Whether something has already answered.</param>
    public static (int Seconds, int Microseconds) Window(bool connecting)
        => connecting ? (LongWindowSec, 0) : (0, ShortWindowUs);

    /// <summary>
    /// What happens when a window elapses with nothing read.
    /// </summary>
    /// <param name="candidateChosen">Whether a candidate has already been settled on.</param>
    /// <param name="retries">How many retry rounds have been spent.</param>
    /// <param name="anyAnswer">Whether anything has answered at any point.</param>
    /// <param name="connecting">Whether the long window is already in use.</param>
    public static WaitStep Next(bool candidateChosen, int retries, bool anyAnswer, bool connecting)
    {
        if (candidateChosen)
            return WaitStep.Done;

        // Retrying stops the moment anything answers, whether or not the retries are spent.
        if (retries < Tries && !anyAnswer)
            return WaitStep.Retry;

        if (anyAnswer && !connecting)
            return WaitStep.Connect;

        return WaitStep.Unreachable;
    }

    /// <summary>
    /// The whole budget, in microseconds, before the console is called unreachable.
    /// </summary>
    public static long Budget()
        => ((long)Tries * ShortWindowUs) + ((long)LongWindowSec * SecondUs);

    /// <summary>
    /// Whether a ready socket of this kind is turned into the "no socket has data" error.
    ///
    /// Only the unrecognised one, and only with port guessing on - which is the whole of the
    /// asymmetry.
    /// </summary>
    public static bool BecomesNoSocketHasData(ReadySocket ready, bool portGuessing)
        => portGuessing && ready == ReadySocket.Unrecognised;

    /// <summary>
    /// Whether that message is true when it is printed.
    ///
    /// The loop only reaches the ladder because the event base reported a socket ready, so in the
    /// one case that prints this, a socket HAD data - it was simply not one the ladder knows. The
    /// message is therefore false exactly where it appears.
    /// </summary>
    public static bool MessageIsAccurate(ReadySocket ready, bool portGuessing)
        => !BecomesNoSocketHasData(ready, portGuessing);

    /// <summary>What that branch tells a reader.</summary>
    public const string NoSocketHasData = "Waiting loop returned but no socket has data!";

    /// <summary>How many bytes are read back from whichever socket answered.</summary>
    /// <param name="ready">The socket kind.</param>
    /// <returns>The address length the core expects, which is the v4 size for a guessed socket.</returns>
    public static int AddressLengthFor(ReadySocket ready) => ready switch
    {
        ReadySocket.Ipv4 => 16,
        ReadySocket.Guessed => 16,

        // The default, and the v6 case, are the same value - so an unrecognised socket carried
        // forward is read with the v6 length.
        _ => 28,
    };
}

/// <summary>
/// PP245: the wait where the core writes it.
/// </summary>
public static class CandidateWaitSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the two windows are still filled into one field each.</summary>
    public static bool TheTwoWindowsStillUseOneFieldEach(string core)
    {
        string body = Body(core);

        return body.Contains(
                """
                timeout.tv_sec = SELECT_CANDIDATE_CONNECTION_SEC;
                            timeout.tv_usec = 0;
                """.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal)
            && body.Contains(
                """
                timeout.tv_sec = 0;
                            timeout.tv_usec = (int)(SELECT_CANDIDATE_TIMEOUT_SEC * SECOND_US);
                """.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal);
    }

    /// <summary>And whether the constants behind them are still these.</summary>
    public static bool TheConstantsAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // The float spelled out, NOT formatted from the constant - a machine running in a culture
        // with a decimal comma would otherwise search for "0,5F" and find nothing.
        return core.Contains("#define SELECT_CANDIDATE_TIMEOUT_SEC 0.5F", StringComparison.Ordinal)
            && core.Contains($"#define SELECT_CANDIDATE_TRIES {CandidateWait.Tries}", StringComparison.Ordinal)
            && core.Contains(
                $"#define SELECT_CANDIDATE_CONNECTION_SEC {CandidateWait.LongWindowSec}", StringComparison.Ordinal)
            && core.Contains($"#define SECOND_US {CandidateWait.SecondUs}L", StringComparison.Ordinal);
    }

    /// <summary>Whether retrying still stops at the first answer rather than at the count.</summary>
    public static bool RetryingStillStopsAtTheFirstAnswer(string core)
        => Body(core).Contains(
                "if(retry_counter < SELECT_CANDIDATE_TRIES && !received_response)", StringComparison.Ordinal)
            && Body(core).Contains("else if(received_response && !connecting)", StringComparison.Ordinal);

    /// <summary>Whether the retry round still discards every send failure.</summary>
    public static bool RetriesStillDiscardSendFailures(string core)
    {
        string body = Body(core);

        int resends = body.IndexOf("Resending requests to all candidates TRY", StringComparison.Ordinal);
        if (resends < 0)
            return false;

        int failed = body.IndexOf(
            "check_candidates: Sending request failed for %s:%d with error: ", resends, StringComparison.Ordinal);
        if (failed < 0)
            return false;

        // What follows that log is a bare continue - no error recorded, unlike the first send loop.
        int nextBrace = body.IndexOf('}', failed);
        return body[failed..nextBrace].Contains("continue;", StringComparison.Ordinal)
            && !body[failed..nextBrace].Contains("err =", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an unrecognised socket still becomes the message that says nothing had data.
    /// </summary>
    public static bool TheUnrecognisedSocketStillBecomesThatMessage(string core)
    {
        string body = Body(core);

        int invalidated = body.IndexOf(
            "if (!found)\n                candidate_sock = CHIAKI_INVALID_SOCKET;", StringComparison.Ordinal);
        int says = body.IndexOf(CandidateWait.NoSocketHasData, StringComparison.Ordinal);

        return invalidated >= 0 && says > invalidated;
    }

    /// <summary>
    /// And whether the ladder still looks for the third kind only when port guessing is on.
    /// </summary>
    public static bool TheThirdKindIsStillOnlyLookedForWhenGuessing(string core)
        => Body(core).Contains(
            "else if(session->stun_random_allocation)\n        {\n            bool found = false;",
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the setsockopt failure is still the same sentence as the one above it, minus the
    /// function name.
    /// </summary>
    public static bool TheSameFailureIsStillWrittenTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string sentence = "setsockopt(IP_TTL) failed with error\" CHIAKI_SOCKET_ERROR_FMT";

        int named = text.IndexOf("\"check_candidates: " + sentence, StringComparison.Ordinal);
        int bare = text.IndexOf("\"" + sentence, StringComparison.Ordinal);

        // Two of them, the bare one further down, and nothing else spelling it a third way.
        return named >= 0
            && bare > named
            && text.IndexOf("\"" + sentence, bare + 1, StringComparison.Ordinal) < 0;
    }

    /// <summary>The wait loop, from its head to the point a response is read.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason PP213, PP233, PP234, PP236, PP243 and PP244 each wrote down.
        int function = text.LastIndexOf(
            "static ChiakiErrorCode check_candidates(", StringComparison.Ordinal);
        if (function < 0)
            return "";

        int start = text.IndexOf("    while (!selected_candidate)", function, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(CandidateWait.NoSocketHasData, start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..(end + CandidateWait.NoSocketHasData.Length)];
    }
}
