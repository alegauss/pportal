using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP475, PP27: the resend loop - the last thing PP27's own sentence names.
///
/// PP125 drove the send buffer across the seam; this is the buffer's own thread, which decides a packet
/// was lost. Two assertions are worth the task: the two waits are different, and giving up ACKS the
/// packet rather than dropping it - the one place this code lies to the rest of the transport on
/// purpose.
///
/// PP476 then answered the question PP475 filed: the ack removes the whole prefix, so the step back was
/// wrong at every index and the scan restarts instead.
/// </summary>
public class TakionResendLoopTests
{
    private static string? Source()
    {
        string? path = TakionResendLoop.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// TWO DIFFERENT WAITS: a timeout of half the resend interval with packets, none at all without.
    ///
    /// So an idle stream costs nothing and a busy one is checked twice per resend window. A port
    /// collapsing them into one poll would either spin when idle or be a window late when not.
    /// </summary>
    [Fact]
    public void TheWaitDependsOnWhetherAnythingIsBuffered()
    {
        Assert.Null(TakionResendLoop.WaitFor(0));
        Assert.Equal(TakionResendLoop.WakeupTimeoutMs, TakionResendLoop.WaitFor(1));
        Assert.Equal(TakionResendLoop.WakeupTimeoutMs, TakionResendLoop.WaitFor(TakionResendLoop.BufferSize));

        // Half, so a due packet is never a full interval late.
        Assert.Equal(TakionResendLoop.ResendTimeoutMs / 2, TakionResendLoop.WakeupTimeoutMs);
    }

    /// <summary>A timeout and a success both carry on; anything else leaves, and so does a stop.</summary>
    [Fact]
    public void OnlyAnErrorOrAStopLeaves()
    {
        Assert.False(TakionResendLoop.Leaves(ChiakiError.Timeout, shouldStop: false));
        Assert.False(TakionResendLoop.Leaves(ChiakiError.Success, shouldStop: false));

        Assert.True(TakionResendLoop.Leaves(ChiakiError.Unknown, shouldStop: false));
        Assert.True(TakionResendLoop.Leaves(ChiakiError.Timeout, shouldStop: true));
    }

    /// <summary>The due test is strictly greater, so a packet exactly at the timeout waits one pass.</summary>
    [Theory]
    [InlineData(0, ResendStep.NotDue)]
    [InlineData(200, ResendStep.NotDue)]
    [InlineData(201, ResendStep.Resent)]
    public void APacketIsDueOnlyPastTheTimeout(long sinceMs, ResendStep expected)
    {
        Assert.Equal(expected, TakionResendLoop.Next(sinceMs, tries: 0));
    }

    /// <summary>
    /// GIVING UP ACKS THE PACKET TO ITSELF, at the twenty-fifth try.
    ///
    /// Not a quiet removal: it takes the console's own ack path, so everything waiting on that sequence
    /// number is released as if the acknowledgement had arrived. That is the behaviour a rewrite is
    /// likeliest to replace with a drop.
    /// </summary>
    [Fact]
    public void TheTwentyFifthTryIsWhereItGivesUp()
    {
        Assert.Equal(25, TakionResendLoop.TriesMax);

        Assert.Equal(ResendStep.Resent, TakionResendLoop.Next(500, tries: TakionResendLoop.TriesMax - 1));
        Assert.Equal(ResendStep.GivenUp, TakionResendLoop.Next(500, tries: TakionResendLoop.TriesMax));

        Assert.True(TakionResendLoop.GivingUpAcksLikeTheConsole);
    }

    /// <summary>And a packet not yet due is not given up on, however many tries it has had.</summary>
    [Fact]
    public void TheTimeoutIsTestedBeforeTheTryCount()
    {
        Assert.Equal(
            ResendStep.NotDue,
            TakionResendLoop.Next(sinceLastSendMs: 10, tries: TakionResendLoop.TriesMax + 5));
    }

    /// <summary>Every constant is the C's, read from its defines.</summary>
    [Fact]
    public void TheConstantsAreStillTheCs()
    {
        if (Source() is not { } source)
            return;

        Assert.Equal(
            (long?)TakionResendLoop.ResendTimeoutMs,
            TakionResendLoop.DefineIn(source, "TAKION_DATA_RESEND_TIMEOUT_MS"));
        Assert.Equal(
            (long?)TakionResendLoop.TriesMax,
            TakionResendLoop.DefineIn(source, "TAKION_DATA_RESEND_TRIES_MAX"));
        Assert.Equal(
            (long?)TakionResendLoop.BufferSize,
            TakionResendLoop.DefineIn(source, "TAKION_SEND_BUFFER_SIZE"));
    }

    /// <summary>The two waits are still two, in the C.</summary>
    [Fact]
    public void TheTwoWaitsAreStillDifferentInTheC()
    {
        if (Source() is not { } source || TakionResendLoop.ThreadBody(source) is not { } body)
            return;

        Assert.True(TakionResendLoop.TheTwoWaitsAreStillDifferent(body));
    }

    /// <summary>And giving up still goes through the ack, with the lock dropped around it.</summary>
    [Fact]
    public void GivingUpStillGoesThroughTheAck()
    {
        if (Source() is not { } source || TakionResendLoop.ResendBody(source) is not { } body)
            return;

        Assert.True(
            TakionResendLoop.GivingUpStillAcks(body),
            "giving up no longer acks the packet, so whatever waits on that sequence number is no "
                + "longer released and this model is behind the C");
    }

    /// <summary>
    /// PP476: THE SCAN RESTARTS after a give-up, because the ack removes the whole prefix.
    ///
    /// PP475 filed this as PP464's idiom at a second site. Reading the ack made the answer bigger than
    /// the guard: it removes every packet at or before the sequence number, and the buffer is in send
    /// order, so acking index i removes indices 0..i. A one-step back was wrong at EVERY index, not
    /// only at zero - so PP464's unguarded decrement was never the right repair here.
    /// </summary>
    [Fact]
    public void TheScanRestartsAfterAGiveUp()
    {
        // The arithmetic that makes a one-step back wrong.
        Assert.Equal(1, TakionResendLoop.AckedByGivingUpAt(0));
        Assert.Equal(4, TakionResendLoop.AckedByGivingUpAt(3));

        if (Source() is not { } source || TakionResendLoop.ResendBody(source) is not { } body)
            return;

        Assert.True(
            TakionResendLoop.TheScanRestartsAfterAGiveUp(body),
            "the give-up no longer restarts the scan, or the stepped-back spelling is back beside it");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionResendLoop.ThreadBody(""));
        Assert.Null(TakionResendLoop.ResendBody(""));
        Assert.Null(TakionResendLoop.DefineIn("", "TAKION_DATA_RESEND_TRIES_MAX"));
        Assert.False(TakionResendLoop.TheTwoWaitsAreStillDifferent(""));
        Assert.False(TakionResendLoop.GivingUpStillAcks(""));
        Assert.False(TakionResendLoop.TheScanRestartsAfterAGiveUp(""));
    }
}
