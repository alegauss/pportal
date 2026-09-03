using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP649, which is PP28's second join: the switch to the stream connection, and the wait that
/// reports three different endings as one.
///
/// Both ids, and PP649 exists because of the second. PP28 carries the three joins as criteria and
/// roadkeep holds one partial entry per id, so a step delivered after the first gets a line of its
/// own - which is a better record anyway, since this one turned out to be a defect rather than a
/// translation.
///
/// CtrlOnceOnly models who SETS the flag. This is the side that waits for it, which is the session
/// thread's and therefore PP28's.
/// </summary>
public class StreamConnectionSwitchTests
{
    private static string? Source()
        => StreamConnectionSwitch.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// Three of the five wakes end the session saying the ack did not arrive.
    ///
    /// The predicate wakes on a stop, on ctrl dying and on the flag; the arm after it reads the flag
    /// alone; and the timeout arrives with the flag false as well. So a user who pressed stop, a
    /// session whose ctrl thread died, and a console that genuinely did not answer are one outcome
    /// with one log line between them.
    /// </summary>
    [Theory]
    [InlineData(SwitchWake.AckReceived, SwitchOutcome.Proceed)]
    [InlineData(SwitchWake.StoppedAfterAck, SwitchOutcome.Stopped)]
    [InlineData(SwitchWake.Stopped, SwitchOutcome.ReportedAsMissingAck)]
    [InlineData(SwitchWake.CtrlFailed, SwitchOutcome.ReportedAsMissingAck)]
    [InlineData(SwitchWake.TimedOut, SwitchOutcome.ReportedAsMissingAck)]
    public void TheFlagDecidesTheArmAndNotWhatHappened(SwitchWake wake, SwitchOutcome outcome)
        => Assert.Equal(outcome, StreamConnectionSwitch.After(wake));

    /// <summary>
    /// And the three that are misreported are named, so a later decision has them listed.
    ///
    /// Asserted against <see cref="StreamConnectionSwitch.After"/> rather than written out twice:
    /// a list that drifted from the mapping would be a record of a defect that had moved.
    /// </summary>
    [Fact]
    public void TheMisreportedWakesAreTheOnesThatSayMissingAck()
    {
        foreach (SwitchWake wake in StreamConnectionSwitch.Misreported)
            Assert.Equal(SwitchOutcome.ReportedAsMissingAck, StreamConnectionSwitch.After(wake));

        Assert.Equal(3, StreamConnectionSwitch.Misreported.Count);
    }

    /// <summary>
    /// A plain stop here is never recorded as a stop, which is the second half of the defect.
    ///
    /// CHECK_STOP is what writes CHIAKI_QUIT_REASON_STOPPED and it sits after the arm that quits.
    /// So the only wake that reaches it is the race - the ack arrived and a stop is pending - and a
    /// user who pressed stop during the five seconds gets a session that never says so.
    /// </summary>
    [Fact]
    public void OnlyAStopThatRacedTheAckIsRecordedAsAStop()
    {
        Assert.True(StreamConnectionSwitch.RecordsAStop(SwitchWake.StoppedAfterAck));

        foreach (SwitchWake wake in (SwitchWake[])
            [SwitchWake.Stopped, SwitchWake.CtrlFailed, SwitchWake.TimedOut, SwitchWake.AckReceived])
        {
            Assert.False(
                StreamConnectionSwitch.RecordsAStop(wake),
                $"{wake} would reach CHECK_STOP, which only the ack arm falls through to");
        }
    }

    /// <summary>The whole step is the rudp path's, and a direct session never takes it.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ADirectSessionSkipsTheSwitchEntirely(bool rudp, bool happens)
        => Assert.Equal(happens, StreamConnectionSwitch.Happens(rudp));

    /// <summary>The guard in the C is still what decides that.</summary>
    [Fact]
    public void TheGuardIsStillTheRudpOne()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            StreamConnectionSwitchSource.OnlyOnTheRudpPath(source),
            "the switch message is no longer inside if(session->rudp), so a direct session now "
                + "takes a step this model says it skips");
    }

    /// <summary>
    /// The predicate still reads three fields, which is what makes the arm after it inexact.
    ///
    /// If it is ever narrowed to the flag alone, the three wakes stop being one outcome and this
    /// whole model is describing something that no longer happens.
    /// </summary>
    [Fact]
    public void ThePredicateStillWakesOnThreeThings()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            StreamConnectionSwitchSource.ThePredicateReadsThreeFields(source),
            "the switch predicate no longer reads should_stop, ctrl_failed and the flag together");
    }

    /// <summary>And the arm after it still tests the flag rather than the wait's return code.</summary>
    [Fact]
    public void TheArmAfterTheWaitReadsTheFlag()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            StreamConnectionSwitchSource.TheArmTestsTheFlagAndNotTheError(source),
            "the wait is no longer followed by the flag test, so the error code it returns may now "
                + "be doing work this model says is discarded");
    }

    /// <summary>
    /// And the stop check still comes after it, which is what loses the quit reason.
    ///
    /// The order IS the defect. A commit that moves CHECK_STOP above the arm has fixed it, and this
    /// going red is the right way for that to arrive - the model then describes the old behaviour
    /// and has to be re-read rather than quietly kept.
    /// </summary>
    [Fact]
    public void TheStopCheckIsStillTooLateToRecordAStop()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            StreamConnectionSwitchSource.TheStopCheckComesAfterTheAckArm(source),
            "CHECK_STOP no longer sits after the missing-ack arm, so a stop during the wait may now "
                + "be recorded and this model's second half is describing the old code");
    }

    /// <summary>Five seconds, and the C still says so.</summary>
    [Fact]
    public void TheWaitIsStillFiveSeconds()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            StreamConnectionSwitchSource.TheTimeoutIsUnchanged(source),
            $"SESSION_EXPECT_TIMEOUT_MS is no longer {StreamConnectionSwitch.TimeoutMilliseconds}");
    }
}
