using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP251: where the two local candidates come from.
///
/// <see cref="SlotZeroCarriesAGuessWheneverGuessingRan"/> carries the task: PP248 uses that slot for
/// the winner's mapped port, and on three of the five shapes it is not the port STUN reported.
/// </summary>
public class OfferLayoutTests
{
    /// <summary>The count is written while the pair is still parked somewhere else.</summary>
    [Fact]
    public void TheCountIsWrittenBeforeTheLayoutExists()
    {
        Assert.Equal(2, OfferLayout.ProvisionalCount);
        Assert.Equal(4, OfferLayout.InitialSlots);

        // Two declared, and the two real ones at slots one and two - so a message sent as it stands
        // would carry slot zero, which nothing has written yet.
        Assert.Equal(1, OfferLayout.Provisional.Remote);
        Assert.Equal(2, OfferLayout.Provisional.Local);
        Assert.True(OfferLayout.Provisional.Local >= OfferLayout.Provisional.Count);
    }

    /// <summary>Every path corrects it, and no two agree on where the pair ends up.</summary>
    [Fact]
    public void EveryPathCorrectsItDifferently()
    {
        Assert.Equal(new OfferSlots(0, 1, 2), OfferLayout.SlotsFor(OfferShape.Plain));
        Assert.Equal(new OfferSlots(8, 9, 10), OfferLayout.SlotsFor(OfferShape.GuessedEight));
        Assert.Equal(new OfferSlots(5, 6, 7), OfferLayout.SlotsFor(OfferShape.GuessedForced, guesses: 5));
        Assert.Equal(new OfferSlots(1, 2, 3), OfferLayout.SlotsFor(OfferShape.NoStun));

        // And in every one of them the count covers both members of the pair.
        foreach (OfferShape shape in Enum.GetValues<OfferShape>())
        {
            OfferSlots slots = OfferLayout.SlotsFor(shape, guesses: 5);
            Assert.True(slots.Local < slots.Count, $"{shape} leaves the local candidate unsent");
            Assert.True(slots.Remote < slots.Count, $"{shape} leaves the remote candidate unsent");
        }
    }

    /// <summary>
    /// The session keeps slot zero, and what sits there depends on which distribution ran.
    ///
    /// CORRECTED BY PP253. This first asserted that every guessing shape put a guessed port there.
    /// Only the sequential one does - the two symmetric ones open at the reported port. The rule
    /// now lives in <see cref="PortGuessing.FirstGuessIsTheReportedPort"/> and this defers to it.
    /// </summary>
    [Fact]
    public void WhatSitsInSlotZeroDependsOnTheDistribution()
    {
        Assert.Equal(0, OfferLayout.HeldByTheSession.FromSlot);

        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.Plain));
        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.NoStun));

        // The sequential path steps past it.
        Assert.False(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.GuessedEight));

        // The symmetric ones do not.
        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.GuessedForced));
        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.GuessedMeasured));

        // The address survives on every path that had one, which is why the comment reads as true.
        foreach (OfferShape shape in Enum.GetValues<OfferShape>().Where(OfferLayout.Guesses))
            Assert.True(OfferLayout.SlotZeroHoldsTheStunAddress(shape));
    }

    /// <summary>
    /// The two generators, asked directly - which is what PP251 should have done instead of writing
    /// a third one. PP33's own test already named this difference.
    /// </summary>
    [Fact]
    public void TheSequentialPathStepsPastItAndTheSpreadDoesNot()
    {
        Assert.DoesNotContain<ushort>(40000, PortGuessing.Sequential(40000, increment: 1));
        Assert.Equal(40000, PortGuessing.Spread(40000, count: 1)[0]);
    }

    // The wrapping and the two underflow rules were tested here too, which was PP33's
    // PortGuessingTests written a second time. PP253 deleted them; that file has them.

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheLayoutIsStillTheCores()
    {
        string? file = OfferLayoutSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            OfferLayoutSource.TheCountIsStillWrittenFirst(core),
            "the count is still written before the layout is decided");
        Assert.True(OfferLayoutSource.ThePairIsStillParkedThere(core), "the pair is still parked there");
        Assert.True(
            OfferLayoutSource.ThePlainPathStillShiftsDown(core), "the plain path still shifts down");

        Assert.Equal(3, OfferLayoutSource.HowManyPathsPutGuessesFirst(core));

        Assert.True(
            OfferLayoutSource.TheSessionStillKeepsSlotZero(core),
            "the session still keeps the local candidate and slot zero");
        Assert.True(
            OfferLayoutSource.TheGuessesStillStepBeforeWriting(core),
            "and the guesses still step the port before writing it");

        // Checked, not claimed: the growth precedes the writes that outrun the allocation.
        Assert.True(
            OfferLayoutSource.TheArrayIsStillGrownFirst(core),
            "the array is still grown before slot eight is written");
    }
}
