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
    /// THE FINDING. The session keeps slot zero, and slot zero holds the reported port only when
    /// nothing was guessed.
    /// </summary>
    [Fact]
    public void SlotZeroCarriesAGuessWheneverGuessingRan()
    {
        Assert.Equal(0, OfferLayout.HeldByTheSession.FromSlot);

        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.Plain));
        Assert.True(OfferLayout.SlotZeroHoldsTheStunPort(OfferShape.NoStun));

        foreach (OfferShape shape in Enum.GetValues<OfferShape>().Where(OfferLayout.Guesses))
        {
            Assert.False(
                OfferLayout.SlotZeroHoldsTheStunPort(shape), $"{shape} should carry a guessed port");

            // The address survives on all of them, which is why the comment reads as true.
            Assert.True(OfferLayout.SlotZeroHoldsTheStunAddress(shape));
        }
    }

    /// <summary>
    /// And the first guess is not the reported port - the step is applied before the write, so the
    /// real allocation is the one port the array never carries.
    /// </summary>
    [Fact]
    public void TheReportedPortIsNeverOneOfTheGuesses()
    {
        const int reported = 40000;

        Assert.NotEqual(reported, OfferLayout.FirstGuessedPort(reported, increment: 1));
        Assert.Equal(40001, OfferLayout.FirstGuessedPort(reported, increment: 1));
        Assert.Equal(39999, OfferLayout.FirstGuessedPort(reported, increment: -1));
    }

    /// <summary>The wrapping keeps a guess inside a port, and steps over the well-known range.</summary>
    [Fact]
    public void TheGuessesWrapWithoutLandingBelowTheWellKnownRange()
    {
        // Stepping down out of the range from above wraps to the top instead.
        int wrapped = OfferLayout.FirstGuessedPort(1030, increment: -20);
        Assert.True(wrapped > 1024, $"a guess landed at {wrapped}");

        // Stepping past the top comes back above the well-known range, not to zero.
        int over = OfferLayout.FirstGuessedPort(65530, increment: 20);
        Assert.InRange(over, 1024, ushort.MaxValue);

        // Unless the allocation was already down there, which is left alone.
        Assert.Equal(500, OfferLayout.FirstGuessedPort(499, increment: 1));
    }

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
