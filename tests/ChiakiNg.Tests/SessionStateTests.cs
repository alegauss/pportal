using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the session's progress, which is a history rather than a position.
/// </summary>
public class SessionStateTests
{
    private static string? Core()
    {
        string? path = SessionStateSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// IT IS A HISTORY, NOT A POSITION. Every transition adds a bit and nothing anywhere removes
    /// one, so "the session is in state X" always means "has at some point reached X".
    /// </summary>
    [Fact]
    public void NothingIsEverUnmade()
    {
        var state = new HolepunchSessionState();

        state.Enter(SessionStateFlags.Created);
        state.Enter(SessionStateFlags.ClientJoined);
        state.Enter(SessionStateFlags.Deleted);

        // Deleted, and still created and joined.
        Assert.True(state.Has(SessionStateFlags.Deleted));
        Assert.True(state.Has(SessionStateFlags.Created));
        Assert.True(state.Has(SessionStateFlags.ClientJoined));
        Assert.True(state.CreationFinished);
    }

    /// <summary>Which the core states by never writing a clearing operation at all.</summary>
    [Fact]
    public void TheCoreNeverClearsABit()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.True(SessionStateSource.NothingIsEverUnset(core));
    }

    /// <summary>
    /// EIGHT OF THE NINETEEN STATES ARE NEVER ENTERED - and every one of the eight is read
    /// somewhere, so eight branches in the file cannot be taken.
    /// </summary>
    [Fact]
    public void EightStatesAreDeclaredReadAndNeverSet()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.Equal(8, HolepunchSessionState.NeverEntered.Count);
        Assert.Equal(19, Enum.GetValues<SessionStateFlags>().Length - 1);

        foreach (SessionStateFlags flag in HolepunchSessionState.NeverEntered)
        {
            (int set, int read) = SessionStateSource.CountsFor(core, flag);

            Assert.Equal(0, set);
            Assert.True(read > 0, $"{SessionStateSource.NameOf(flag)} is never read either");
        }
    }

    /// <summary>And the other eleven are all set at least once, which is what makes eight the count.</summary>
    [Fact]
    public void TheOtherElevenAreAllReallyUsed()
    {
        string? core = Core();
        if (core is null)
            return;

        IEnumerable<SessionStateFlags> entered = Enum.GetValues<SessionStateFlags>()
            .Where(f => f != SessionStateFlags.None)
            .Except(HolepunchSessionState.NeverEntered);

        Assert.Equal(11, entered.Count());

        foreach (SessionStateFlags flag in entered)
        {
            (int set, _) = SessionStateSource.CountsFor(core, flag);
            Assert.True(set > 0, $"{SessionStateSource.NameOf(flag)} is never set after all");
        }
    }

    /// <summary>
    /// THE "ALREADY STARTED" GUARD CANNOT FIRE. It tests a flag nothing sets, so starting a session
    /// twice is not prevented at all - the check reads like protection and is decoration.
    /// </summary>
    [Fact]
    public void TheAlreadyStartedGuardCanNeverRefuseAnything()
    {
        var state = new HolepunchSessionState();

        // Everything the session really does, in order.
        foreach (SessionStateFlags flag in Enum.GetValues<SessionStateFlags>()
            .Where(f => f != SessionStateFlags.None)
            .Except(HolepunchSessionState.NeverEntered))
        {
            state.Enter(flag);
        }

        Assert.False(state.WouldRefuseAsAlreadyStarted);

        string? core = Core();
        if (core is null)
            return;

        Assert.True(SessionStateSource.TheStartedGuardIsStillDead(core));
    }

    /// <summary>
    /// THE AUTO-ACK WINDOW IS ASYMMETRIC. The control clause closes when the control port comes up;
    /// the data clause never closes, because after the data offer nothing is waiting for one again.
    /// </summary>
    [Fact]
    public void TheAckWindowOpensTwiceAndClosesOnce()
    {
        var state = new HolepunchSessionState();

        // Before any offer, something IS waiting for one.
        Assert.False(state.ShouldAckOffers);

        // The control offer arrives and the race begins: nothing is waiting, so ack automatically.
        state.Enter(SessionStateFlags.CtrlOfferReceived);
        Assert.True(state.ShouldAckOffers);

        // The control port comes up and the data offer is now expected: stop acking.
        state.Enter(SessionStateFlags.CtrlEstablished);
        Assert.False(state.ShouldAckOffers);

        // It arrives, and from here nothing is ever waiting again.
        state.Enter(SessionStateFlags.DataOfferReceived);
        Assert.True(state.ShouldAckOffers);

        state.Enter(SessionStateFlags.DataEstablished);
        Assert.True(state.ShouldAckOffers);
    }

    /// <summary>Creating is finished when PSN has made a session and this end has joined it.</summary>
    [Fact]
    public void CreationWantsBothOfItsTwoBits()
    {
        var state = new HolepunchSessionState();

        state.Enter(SessionStateFlags.Created);
        Assert.False(state.CreationFinished);

        state.Enter(SessionStateFlags.ClientJoined);
        Assert.True(state.CreationFinished);
    }

    /// <summary>And starting when the console has joined and its sixteen bytes have arrived.</summary>
    [Fact]
    public void StartingWantsBothOfItsTwoBits()
    {
        var state = new HolepunchSessionState();

        state.Enter(SessionStateFlags.ConsoleJoined);
        Assert.False(state.StartFinished);

        state.Enter(SessionStateFlags.CustomData1Received);
        Assert.True(state.StartFinished);
    }

    /// <summary>Nineteen distinct bits, none of them sharing a value.</summary>
    [Fact]
    public void TheNineteenBitsAreDistinct()
    {
        int[] values = [.. Enum.GetValues<SessionStateFlags>()
            .Where(f => f != SessionStateFlags.None)
            .Select(f => (int)f)];

        Assert.Equal(19, values.Length);
        Assert.Equal(19, values.Distinct().Count());
        Assert.All(values, v => Assert.Equal(1, System.Numerics.BitOperations.PopCount((uint)v)));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheMachinesRulesAreStillTheQtCores()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.True(SessionStateSource.NothingIsEverUnset(core), "nothing unmade");
        Assert.True(SessionStateSource.TheStartedGuardIsStillDead(core), "a guard that cannot fire");
        Assert.True(SessionStateSource.TheAckWindowIsStillAsymmetric(core), "one clause closes, one does not");
        Assert.True(
            SessionStateSource.TheFinishConditionsStillWantTwoBitsEach(core), "two bits each");
    }
}
