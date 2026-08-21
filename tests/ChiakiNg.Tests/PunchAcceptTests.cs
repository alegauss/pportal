using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP242: the accept wait, and the branch that names a question nobody asked.
///
/// <see cref="OnlyTheCatchAllNamesAWiderWait"/> carries the task: the two enumerated failures name
/// the wait correctly and the leftover one does not, which is the branch a reader reaches with the
/// least else to go on.
/// </summary>
public class PunchAcceptTests
{
    /// <summary>The wait asks for one action, not two.</summary>
    [Fact]
    public void TheWaitAsksForOneAction()
    {
        Assert.Equal(SessionMessageAction.Accept, PunchAccept.WaitsFor);

        // And OFFER is a different value entirely - not a spelling of the same one.
        Assert.NotEqual(SessionMessageAction.Offer, PunchAccept.WaitsFor);
    }

    /// <summary>
    /// THE MISNAMING. Timeout and cancellation name the wait that was made; the catch-all names a
    /// wider one, so the least-informed branch is also the misleading one.
    /// </summary>
    [Fact]
    public void OnlyTheCatchAllNamesAWiderWait()
    {
        Assert.False(PunchAccept.NamesAWiderWait(AcceptWaitOutcome.TimedOut));
        Assert.False(PunchAccept.NamesAWiderWait(AcceptWaitOutcome.Canceled));

        Assert.True(PunchAccept.NamesAWiderWait(AcceptWaitOutcome.Failed));

        // What it actually says, and what it should have said.
        Assert.Contains(
            "ACCEPT or OFFER",
            PunchAccept.MessageFor(AcceptWaitOutcome.Failed),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "OFFER", PunchAccept.MessageFor(AcceptWaitOutcome.TimedOut), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two cancellations, the same sentence, different cleanup - so the log does not say which one
    /// ran, and only one of the two is holding something to release.
    /// </summary>
    [Fact]
    public void BothCancellationsReadTheSameAndDoNot()
    {
        Assert.Equal(
            PunchAccept.CancelMessageAt(PunchCancelPoint.BeforeTheWait),
            PunchAccept.CancelMessageAt(PunchCancelPoint.AfterTheWait));

        Assert.False(PunchAccept.HoldsAMessage(PunchCancelPoint.BeforeTheWait));
        Assert.True(PunchAccept.HoldsAMessage(PunchCancelPoint.AfterTheWait));
    }

    /// <summary>
    /// The third site building the same acknowledgement - PP231's automatic one, PP240's offer, and
    /// this. Three is what makes one builder worth having.
    /// </summary>
    [Fact]
    public void TheAcknowledgementIsTheThirdOfTheSameMessage()
    {
        Assert.Equal(OfferAck.Message(91), PunchAccept.Acknowledgement(91));
        Assert.Equal(PunchOpening.Acknowledgement(91), PunchAccept.Acknowledgement(91));

        // Carrying the incoming id, so two accepts do not acknowledge alike.
        Assert.NotEqual(PunchAccept.Acknowledgement(91), PunchAccept.Acknowledgement(92));
    }

    /// <summary>
    /// The copy is sized from the DESTINATION, which is safe only because the two fields are equal.
    /// Asserted rather than assumed: a source one byte shorter turns this into a read past its end.
    /// </summary>
    [Fact]
    public void TheAddressCopyIsSizedFromTheDestination()
    {
        byte[] candidate = new byte[PunchAccept.AddressLength];
        "192.168.0.9\0"u8.CopyTo(candidate);

        // Whatever sits past the terminator comes along - the copy is by field, not by string.
        candidate[^1] = 0x7f;

        byte[] held = PunchAccept.Adopt(candidate);

        Assert.Equal(PunchAccept.AddressLength, held.Length);
        Assert.Equal(0x7f, held[^1]);
        Assert.Equal(candidate, held);
    }

    /// <summary>And a source shorter than the destination is what the sizing does not survive.</summary>
    [Fact]
    public void AShorterSourceWouldNotSurviveThatSizing()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => PunchAccept.Adopt(new byte[PunchAccept.AddressLength - 1]));

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheAcceptHalfIsStillTheCores()
    {
        string? file = PunchAcceptSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchAcceptSource.TheWaitStillAsksForAccept(core), "the wait still asks for ACCEPT");
        Assert.True(
            PunchAcceptSource.TheCatchAllStillNamesAWiderWait(core),
            "and the catch-all still names ACCEPT or OFFER");
        Assert.True(
            PunchAcceptSource.TheAcknowledgementIsStillTheSameShape(core),
            "the acknowledgement still that shape");
        Assert.True(
            PunchAcceptSource.TheTwoCancellationsStillReadAlike(core),
            "two cancellations reading alike");
        Assert.True(
            PunchAcceptSource.TheAddressIsStillCopiedByTheDestinationsSize(core),
            "and the address still copied by the destination's size");
    }
}
