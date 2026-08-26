using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP380, under PP295: no wait in senkusha.c reports a success for something that never arrived.
///
/// The rule is over every wait in the file, and that is not a stylistic choice. The task was filed
/// naming three sites; reading the file for the fix found six, and the three it had missed were the
/// worse half - they carry a success out of chiaki_senkusha_run itself. A check on the named three
/// would have left the other three exactly as they were.
/// </summary>
public class SenkushaWaitOutcomesTests(ITestOutputHelper output)
{
    private static string? Core()
    {
        string? path = SenkushaWaitSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE FOURTH CASE. Success from the wait, predicate false, nobody asked to stop.
    /// </summary>
    [Fact]
    public void NothingArrivedIsTheLeftoverCase()
    {
        Assert.Equal(
            SenkushaWake.NothingArrived,
            SenkushaWaitOutcomes.Classify(false, ChiakiError.Success, shouldStop: false));

        // And the three that are not it, in the order the C tests them.
        Assert.Equal(
            SenkushaWake.Finished,
            SenkushaWaitOutcomes.Classify(true, ChiakiError.Success, shouldStop: true));
        Assert.Equal(
            SenkushaWake.TimedOut,
            SenkushaWaitOutcomes.Classify(false, ChiakiError.Timeout, shouldStop: false));
        Assert.Equal(
            SenkushaWake.Stopped,
            SenkushaWaitOutcomes.Classify(false, ChiakiError.Success, shouldStop: true));
    }

    /// <summary>
    /// THE TASK. No site answers a silence with a measurement - a timeout and a silence are the
    /// same thing to whoever called.
    /// </summary>
    [Theory]
    [InlineData(SenkushaWaitSite.Connect)]
    [InlineData(SenkushaWaitSite.ProtocolAck)]
    [InlineData(SenkushaWaitSite.Bang)]
    [InlineData(SenkushaWaitSite.Pong)]
    [InlineData(SenkushaWaitSite.MtuIn)]
    [InlineData(SenkushaWaitSite.MtuOut)]
    [InlineData(SenkushaWaitSite.ClientMtuCommand)]
    [InlineData(SenkushaWaitSite.DataAck)]
    public void NoSiteAnswersASilenceWithAMeasurement(SenkushaWaitSite site)
    {
        SenkushaVerdict silence = SenkushaWaitOutcomes.Answer(site, SenkushaWake.NothingArrived);
        SenkushaVerdict timeout = SenkushaWaitOutcomes.Answer(site, SenkushaWake.TimedOut);

        Assert.NotEqual(SenkushaVerdict.Measured, silence);
        Assert.Equal(timeout, silence);
    }

    /// <summary>
    /// And a site with attempts left spends one, where a site without has to fail. That is the
    /// difference between the RTT loop and the run's three waits, stated once.
    /// </summary>
    [Fact]
    public void AttemptsAreWhatDecideRetryOrFail()
    {
        Assert.Equal(
            SenkushaVerdict.Retry, SenkushaWaitOutcomes.Answer(SenkushaWaitSite.Pong, SenkushaWake.NothingArrived));
        Assert.Equal(
            SenkushaVerdict.Retry, SenkushaWaitOutcomes.Answer(SenkushaWaitSite.MtuIn, SenkushaWake.NothingArrived));

        Assert.Equal(
            SenkushaVerdict.Fail, SenkushaWaitOutcomes.Answer(SenkushaWaitSite.Bang, SenkushaWake.NothingArrived));
        Assert.Equal(
            SenkushaVerdict.Fail, SenkushaWaitOutcomes.Answer(SenkushaWaitSite.DataAck, SenkushaWake.NothingArrived));
    }

    /// <summary>A stop still beats everything, at every site.</summary>
    [Fact]
    public void AStopIsAlwaysCancel()
    {
        foreach (SenkushaWaitSite site in Enum.GetValues<SenkushaWaitSite>())
            Assert.Equal(SenkushaVerdict.Cancel, SenkushaWaitOutcomes.Answer(site, SenkushaWake.Stopped));
    }

    /// <summary>
    /// WHY NOTHING CHANGES TODAY. The case is unreachable while the predicate reads two fields, and
    /// becomes reachable at every site the moment PP365's remedy adds the third.
    /// </summary>
    [Fact]
    public void TheCaseIsUnreachableUntilThePredicateGrowsAThirdField()
    {
        Assert.False(SenkushaWaitOutcomes.IsReachable(predicateReadsStateFailed: false));
        Assert.True(SenkushaWaitOutcomes.IsReachable(predicateReadsStateFailed: true));

        if (Core() is not { } core)
            return;

        Assert.True(
            SenkushaWaitSource.ThePredicateStillReadsTwoFields(core),
            "the predicate now reads state_failed, so PP380's branches are live - which is the "
            + "change this task was written to be safe for");
    }

    /// <summary>
    /// THE RULE, over every wait in the file. None falls out of itself into the success below it.
    /// </summary>
    [Fact]
    public void NoWaitBlockFallsThroughToASuccess()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> blocks = SenkushaWaitSource.WaitBlocksIn(core);

        // PP271: the sweep found the waits, or the rule is about nothing. Seven today.
        Assert.True(blocks.Count >= 7, $"only {blocks.Count} wait blocks were found");
        output.WriteLine($"{blocks.Count} wait blocks");

        IReadOnlyList<string> falling = SenkushaWaitSource.BlocksThatFallThrough(core);

        Assert.True(
            falling.Count == 0,
            $"{falling.Count} wait block(s) decide nothing arrived and then fall through:\n"
            + string.Join("\n---\n", falling));
    }

    /// <summary>
    /// The reader sees the two shapes it was written for: the RTT loop, which was always right, and
    /// the MTU test, which fell through into a success.
    /// </summary>
    [Fact]
    public void TheReaderTellsTheModelFromTheDefect()
    {
        const string TheOneThatWasRight = """
            			if(err == CHIAKI_ERR_TIMEOUT)
            				CHIAKI_LOGE(senkusha->log, "Senkusha pong receive timeout");

            			if(senkusha->should_stop)
            				return CHIAKI_ERR_CANCELED;
            			else
            				CHIAKI_LOGE(senkusha->log, "Senkusha failed to receive pong");

            			continue;
            """;

        Assert.True(SenkushaWaitSource.ItAnswersRatherThanFallsThrough(TheOneThatWasRight));

        const string TheDefect = """
            				if(err == CHIAKI_ERR_TIMEOUT)
            				{
            					CHIAKI_LOGI(senkusha->log, "Senkusha MTU %u timeout", (unsigned int)cur);
            					continue;
            				}

            				if(senkusha->should_stop)
            					return CHIAKI_ERR_CANCELED;
            				else
            					CHIAKI_LOGE(senkusha->log, "Senkusha failed to receive MTU response");
            """;

        Assert.False(SenkushaWaitSource.ItAnswersRatherThanFallsThrough(TheDefect));

        // And the run's shape, where the answer is a write into err before the jump.
        const string TheRunsShape = """
            		if(senkusha->should_stop)
            			err = CHIAKI_ERR_CANCELED;
            		else
            		{
            			CHIAKI_LOGE(session->log, "Senkusha didn't receive bang");
            			err = CHIAKI_ERR_UNKNOWN;
            		}

            		QUIT(quit_takion);
            """;

        Assert.True(SenkushaWaitSource.ItAnswersRatherThanFallsThrough(TheRunsShape));

        // The same shape without the write, which is what it was.
        const string TheRunsShapeAsItWas = """
            		if(senkusha->should_stop)
            			err = CHIAKI_ERR_CANCELED;
            		else
            			CHIAKI_LOGE(session->log, "Senkusha didn't receive bang");

            		QUIT(quit_takion);
            """;

        // A QUIT is a jump, so this one passes the shape check and would have needed the write to
        // be wrong in a way the shape cannot see - which is why the model above carries it and this
        // note says so rather than pretending the reader caught it.
        Assert.True(SenkushaWaitSource.ItAnswersRatherThanFallsThrough(TheRunsShapeAsItWas));
    }

    /// <summary>And the reader answers no to a file with nothing in it (PP272).</summary>
    [Fact]
    public void TheReaderReadsTheFile()
    {
        Assert.Empty(SenkushaWaitSource.WaitBlocksIn(""));
        Assert.False(SenkushaWaitSource.ItAnswersRatherThanFallsThrough(""));
        Assert.False(SenkushaWaitSource.ThePredicateStillReadsTwoFields(""));
    }

    /// <summary>
    /// The three that carried a success out of the run: each now writes a failure into err before
    /// the jump, so the run answers what happened.
    ///
    /// Read as text rather than through the shape check, because a QUIT is a jump either way -
    /// what changed is the value it carries, which no shape rule can see.
    /// </summary>
    [Fact]
    public void TheRunsThreeWaitsWriteAFailureBeforeTheyJump()
    {
        if (Core() is not { } core)
            return;

        foreach (string log in (string[])[
            "Senkusha Takion connect failed",
            "Senkusha didn't receive protocol request ack",
            "Senkusha didn't receive bang"])
        {
            int at = core.IndexOf(log, StringComparison.Ordinal);
            Assert.True(at > 0, $"the log \"{log}\" is gone, so this assertion has lost its anchor");

            int quit = core.IndexOf("QUIT(", at, StringComparison.Ordinal);
            int written = core.IndexOf("err = CHIAKI_ERR_UNKNOWN;", at, StringComparison.Ordinal);

            Assert.True(
                written > at && written < quit,
                $"\"{log}\" still jumps out carrying whatever the wait left in err");
        }
    }
}
