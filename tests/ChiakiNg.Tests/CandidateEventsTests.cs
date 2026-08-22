using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP263: the last one to fire.
///
/// <see cref="TwoSocketsReadyTogetherLeaveTheSecond"/> carries the task, and
/// <see cref="TheArrayCannotFill"/> is the branch that is computed rather than trusted.
/// </summary>
public class CandidateEventsTests
{
    /// <summary>
    /// Two successes that mean different things: armed, and nothing to arm.
    /// </summary>
    [Fact]
    public void SuccessMeansTwoDifferentThings()
    {
        WatchResult nothing = CandidateEvents.Watch(valid: false, room: true, armed: true);
        WatchResult armed = CandidateEvents.Watch(valid: true, room: true, armed: true);

        Assert.Equal(WatchResult.NothingToWatch, nothing);
        Assert.Equal(WatchResult.Watching, armed);

        // Both are reported as success; only one is being watched.
        Assert.True(CandidateEvents.ReportedAsSuccess(nothing));
        Assert.True(CandidateEvents.ReportedAsSuccess(armed));

        Assert.False(CandidateEvents.ActuallyWatching(nothing));
        Assert.True(CandidateEvents.ActuallyWatching(armed));
    }

    /// <summary>And an invalid socket is answered before the room is even looked at.</summary>
    [Fact]
    public void AnInvalidSocketIsAnsweredFirst()
        => Assert.Equal(
            WatchResult.NothingToWatch,
            CandidateEvents.Watch(valid: false, room: false, armed: false));

    /// <summary>A refusal is the only outcome the caller treats as an error.</summary>
    [Fact]
    public void OnlyARefusalIsAnError()
    {
        Assert.Equal(WatchResult.Refused, CandidateEvents.Watch(true, room: false, armed: true));
        Assert.Equal(WatchResult.Refused, CandidateEvents.Watch(true, room: true, armed: false));

        Assert.False(CandidateEvents.ReportedAsSuccess(WatchResult.Refused));
    }

    /// <summary>
    /// THE FINDING. The field holds the last callback to run, not the one that woke the loop.
    /// </summary>
    [Fact]
    public void TwoSocketsReadyTogetherLeaveTheSecond()
    {
        Assert.Equal(7, CandidateEvents.ReadyAfter([4, 7]));
        Assert.Equal(4, CandidateEvents.ReadyAfter([4]));

        // Woken either way.
        Assert.True(CandidateEvents.Triggered([4, 7]));

        // And a round with nothing in it is a timeout.
        Assert.Null(CandidateEvents.ReadyAfter([]));
        Assert.False(CandidateEvents.Triggered([]));
    }

    /// <summary>
    /// The array cannot fill: an invalid socket consumes no slot, so the count is an upper bound.
    /// </summary>
    [Fact]
    public void TheArrayCannotFill()
    {
        // Everything open: capacity and use agree exactly.
        Assert.Equal(
            CandidateEvents.CapacityFor(true, true, portGuessing: true, guessedSockets: 6),
            CandidateEvents.SlotsUsed(true, true, portGuessing: true, guessedStillOpen: 6));

        // Some guessed sockets closed by the send loop: capacity is larger, never smaller.
        Assert.True(
            CandidateEvents.CapacityFor(true, true, true, 6)
            > CandidateEvents.SlotsUsed(true, true, true, 2));

        // And with port guessing off, the guessed ones count for neither.
        Assert.Equal(2, CandidateEvents.CapacityFor(true, true, portGuessing: false, guessedSockets: 6));
        Assert.Equal(0, CandidateEvents.CapacityFor(false, false, false, 0));
    }

    /// <summary>Only an armed watch consumes a slot.</summary>
    [Fact]
    public void OnlyAnArmedWatchConsumesASlot()
    {
        Assert.True(CandidateEvents.ConsumesASlot(WatchResult.Watching));
        Assert.False(CandidateEvents.ConsumesASlot(WatchResult.NothingToWatch));
        Assert.False(CandidateEvents.ConsumesASlot(WatchResult.Refused));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheGlueIsStillTheCores()
    {
        string? file = CandidateEventsSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            CandidateEventsSource.AnInvalidSocketIsStillSuccess(core),
            "an invalid socket is still answered with success");
        Assert.True(
            CandidateEventsSource.ItStillComesBeforeTheCapacityCheck(core),
            "and still before the capacity check");

        Assert.True(
            CandidateEventsSource.TheCallbackStillOverwritesAndDefers(core),
            "the callback still overwrites the field and asks for a deferred exit");

        Assert.True(CandidateEventsSource.TheEventsStillPersist(core), "the events still persist");
        Assert.True(
            CandidateEventsSource.TheCapacityIsStillCountedTheSameWay(core),
            "the capacity is still counted from the same three sources");
        Assert.True(
            CandidateEventsSource.AFailedArmStillReleasesIt(core),
            "and a failed arm still releases what it made");
    }
}
