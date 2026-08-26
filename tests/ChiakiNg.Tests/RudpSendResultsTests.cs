using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP384, under PP340: the rudp retry loop reads its sends and says which failure it had.
///
/// This is the family's fourth member after PP370, PP375, PP379 and PP383, and the one where all
/// four siblings were wrong together - which is why nothing about it looked odd. So the assertion
/// that matters is <see cref="AllFourArmsReadWhatTheyAnswered"/>: it counts over the four rather
/// than finding one, because a check that saw one fixed would pass on three that were not.
/// </summary>
public class RudpSendResultsTests(ITestOutputHelper output)
{
    private static string? Core() =>
        RudpSendResults.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>THE TASK. Every arm of the switch assigns what its send answered.</summary>
    [Fact]
    public void AllFourArmsReadWhatTheyAnswered()
    {
        if (Core() is not { } core)
            return;

        int discarding = RudpSendResults.ArmsThatDiscardTheirResult(core);

        Assert.True(discarding >= 0, "chiaki_rudp_send_recv could not be found at all");
        Assert.Equal(0, discarding);
    }

    /// <summary>
    /// THE COST. A send that failed takes the next try immediately instead of waiting a timeout
    /// for a reply to a message that never left.
    /// </summary>
    [Fact]
    public void AFailedSendDoesNotWaitForAnAnswer()
    {
        if (Core() is not { } core)
            return;

        Assert.True(
            RudpSendResults.AFailedSendSkipsTheReceive(core),
            "a failed send still falls into the receive, so each try costs a full select timeout");
    }

    /// <summary>
    /// THE SENTENCE. The summary says how many tries never left, rather than reporting every
    /// failure as the console not answering.
    /// </summary>
    [Fact]
    public void TheSummarySaysWhichFailureItWas()
    {
        if (Core() is not { } core)
            return;

        Assert.True(
            RudpSendResults.TheSummarySeparatesTheTwoFailures(core),
            "the summary no longer separates a failed send from a silent console");
    }

    /// <summary>The rule over the file, the way PP370 and PP379 state theirs.</summary>
    [Fact]
    public void NoSendInTheFileIsDiscarded()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> discarded = RudpSendResults.DiscardedResults(core);

        foreach (string call in discarded)
            output.WriteLine(call);

        Assert.True(
            discarded.Count == 0,
            "these are called for their side effect and answer something: "
            + string.Join(", ", discarded));
    }

    /// <summary>
    /// And the list covers the file, so the rule above is about all of it - PP379's check, which
    /// is the one that stops a fifth send from being quietly outside the rule.
    /// </summary>
    [Fact]
    public void EveryAnsweringSendIsListed()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> inTheFile = RudpSendResults.AnsweringSendsIn(core);

        Assert.NotEmpty(inTheFile);

        IEnumerable<string> missing =
            inTheFile.Except(RudpSendResults.SendsThatAnswer, StringComparer.Ordinal);

        Assert.True(
            !missing.Any(),
            "these answer a ChiakiErrorCode and are not in the list the rule reads: "
            + string.Join(", ", missing));
    }

    /// <summary>The four are the four, in the order the switch sends them.</summary>
    [Fact]
    public void TheSwitchStillSendsThoseFour()
    {
        Assert.Equal(
            [
                "chiaki_rudp_send_init_message",
                "chiaki_rudp_send_cookie_message",
                "chiaki_rudp_send_ack_message",
                "chiaki_rudp_send_session_message",
            ],
            RudpSendResults.TheFourSends);
    }

    /// <summary>The readers see the shape they were written for, and read the file (PP272).</summary>
    [Fact]
    public void TheReadersSeeTheShapeTheyGuardAgainst()
    {
        Assert.Equal(-1, RudpSendResults.ArmsThatDiscardTheirResult(""));
        Assert.False(RudpSendResults.AFailedSendSkipsTheReceive(""));
        Assert.False(RudpSendResults.TheSummarySeparatesTheTwoFailures(""));
        Assert.Empty(RudpSendResults.AnsweringSendsIn(""));

        const string AsItWas = """
            ChiakiErrorCode chiaki_rudp_send_recv(RudpInstance *rudp, RudpMessage *message)
            {
                bool success = false;
                for(int i = 0; i < tries; i++)
                {
                    switch(send_type)
                    {
                        case INIT_REQUEST:
                            chiaki_rudp_send_init_message(rudp);
                            break;
                        case COOKIE_REQUEST:
                            chiaki_rudp_send_cookie_message(rudp, buf, buf_size);
                            break;
                        case ACK:
                            chiaki_rudp_send_ack_message(rudp, remote_counter);
                            break;
                        case SESSION_MESSAGE:
                            chiaki_rudp_send_session_message(rudp, remote_counter, buf, buf_size);
                            break;
                    }
                    ChiakiErrorCode err = chiaki_rudp_select_recv(rudp, 1500, message);
                }
            }
            """;

        // All four, which is the count that matters: finding one fixed would say nothing.
        Assert.Equal(4, RudpSendResults.ArmsThatDiscardTheirResult(AsItWas));
        Assert.False(RudpSendResults.AFailedSendSkipsTheReceive(AsItWas));
        Assert.False(RudpSendResults.TheSummarySeparatesTheTwoFailures(AsItWas));

        // And the file-wide reader finds the four bare calls in it.
        Assert.Equal(4, RudpSendResults.DiscardedResults(AsItWas).Count);
    }

    /// <summary>
    /// A half-fix is refused: reading the result without skipping the receive still spends the
    /// timeout, which is the cost the task named.
    /// </summary>
    [Fact]
    public void ReadingWithoutSkippingIsNotEnough()
    {
        const string HalfFixed = """
            ChiakiErrorCode chiaki_rudp_send_recv(RudpInstance *rudp, RudpMessage *message)
            {
                for(int i = 0; i < tries; i++)
                {
                    send_err = chiaki_rudp_send_init_message(rudp);
                    if(send_err != CHIAKI_ERR_SUCCESS)
                        CHIAKI_LOGE(rudp->log, "failed to send");
                    ChiakiErrorCode err = chiaki_rudp_select_recv(rudp, 1500, message);
                }
            }
            """;

        Assert.False(RudpSendResults.AFailedSendSkipsTheReceive(HalfFixed));
    }
}
