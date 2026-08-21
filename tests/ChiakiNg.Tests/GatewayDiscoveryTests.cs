using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP255: the unlock one line too early.
///
/// <see cref="ExactlyOneWriterIsUnlockedWhileAnotherIsAlive"/> carries the task, and
/// <see cref="TheAbandonedThreadIsJoinedEventually"/> is the thing that looked like a leak and is
/// not - checked, because it would have changed what this task claims.
/// </summary>
public class GatewayDiscoveryTests
{
    /// <summary>Seven seconds, and five places that write the field.</summary>
    [Fact]
    public void SevenSecondsAndFiveWriters()
    {
        Assert.Equal(7000, GatewayDiscovery.TimeoutMs);
        Assert.Equal(5, Enum.GetValues<StatusWriter>().Length);
    }

    /// <summary>
    /// THE RACE. Two writers are unlocked, and only one of them has company.
    /// </summary>
    [Fact]
    public void ExactlyOneWriterIsUnlockedWhileAnotherIsAlive()
    {
        // The thread's two are locked.
        Assert.True(GatewayDiscovery.HoldsTheLock(StatusWriter.ThreadFound));
        Assert.True(GatewayDiscovery.HoldsTheLock(StatusWriter.ThreadNotFound));

        // The fallback's two are not, and are safe because nothing else exists.
        Assert.False(GatewayDiscovery.HoldsTheLock(StatusWriter.FallbackFound));
        Assert.False(GatewayDiscovery.AThreadIsAlive(StatusWriter.FallbackFound));
        Assert.False(GatewayDiscovery.Races(StatusWriter.FallbackFound));

        // The timeout is unlocked AND contended.
        Assert.False(GatewayDiscovery.HoldsTheLock(StatusWriter.Timeout));
        Assert.True(GatewayDiscovery.AThreadIsAlive(StatusWriter.Timeout));

        Assert.Equal([StatusWriter.Timeout], GatewayDiscovery.Racing);
    }

    /// <summary>
    /// And the value it writes is the one the thread can still overwrite - which is why the offer's
    /// choice of arm becomes a matter of scheduling.
    /// </summary>
    [Fact]
    public void TheAbandonedOutcomeCanStillBeOverwritten()
    {
        (GatewayStatus written, bool overwritten) =
            GatewayDiscovery.Outcome(DiscoveryEnding.Abandoned, threadWouldFind: true);

        Assert.Equal(GatewayStatus.NotFound, written);
        Assert.True(overwritten);

        // What the two arms do with each answer - PP252's switch.
        Assert.False(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.NotFound));
        Assert.True(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.Found));
    }

    /// <summary>The two settled endings agree with what the work found, and stay put.</summary>
    [Theory]
    [InlineData(DiscoveryEnding.Joined)]
    [InlineData(DiscoveryEnding.RanInline)]
    public void TheSettledEndingsAgreeWithTheWork(DiscoveryEnding ending)
    {
        Assert.Equal((GatewayStatus.Found, false), GatewayDiscovery.Outcome(ending, true));
        Assert.Equal((GatewayStatus.NotFound, false), GatewayDiscovery.Outcome(ending, false));
    }

    /// <summary>
    /// The abandoned thread is joined in the end - by the teardown, not by the wait. It looks like a
    /// leak and is not.
    /// </summary>
    [Fact]
    public void TheAbandonedThreadIsJoinedEventually()
    {
        Assert.True(GatewayDiscovery.IsEventuallyJoined(DiscoveryEnding.Abandoned));
        Assert.False(GatewayDiscovery.JoinedHere(DiscoveryEnding.Abandoned));

        Assert.True(GatewayDiscovery.JoinedHere(DiscoveryEnding.Joined));

        // The inline path never made a thread, so there is nothing to join at all.
        Assert.False(GatewayDiscovery.IsEventuallyJoined(DiscoveryEnding.RanInline));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheDiscoveryIsStillTheCores()
    {
        string? file = GatewayDiscoverySource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(GatewayDiscoverySource.TheWindowIsStillSeven(core), "seven seconds");
        Assert.Equal(5, GatewayDiscoverySource.HowManyWriteTheStatus(core));

        Assert.True(
            GatewayDiscoverySource.TheThreadStillWritesUnderTheLock(core),
            "the thread still writes under the lock");
        Assert.True(
            GatewayDiscoverySource.TheTimeoutStillUnlocksFirst(core),
            "and the timeout still unlocks before it writes");

        Assert.True(
            GatewayDiscoverySource.TheTimeoutStillReturnsWithoutJoining(core),
            "the timeout still returns without joining");
        Assert.True(
            GatewayDiscoverySource.TheTeardownStillJoinsIt(core),
            "while the teardown still joins it, which is what keeps that from being a leak");

        Assert.True(
            GatewayDiscoverySource.TheFallbackStillRepeatsItUnlocked(core),
            "and the fallback still repeats the thread's body without the locking");
    }
}
