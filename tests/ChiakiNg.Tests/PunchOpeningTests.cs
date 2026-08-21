using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP240: the entry to opening a hole, and the ordering that decides who answers the next offer.
///
/// <see cref="TheWindowOpensBeforeThisPathAnswers"/> is the one that carries the task: it puts the
/// state where the core puts it and asks whether a second offer has an owner, which is the whole
/// reason the flag is set where it is.
/// </summary>
public class PunchOpeningTests
{
    /// <summary>
    /// Two ports, two conditions, and neither is a check on the other's - so one shared guard
    /// would let one of them start too early.
    /// </summary>
    [Fact]
    public void TheTwoPortsNeedDifferentThings()
    {
        var fresh = new HolepunchSessionState();

        Assert.Equal(PunchReadiness.NoCustomData, PunchOpening.Readiness(PunchPort.Control, fresh));
        Assert.Equal(PunchReadiness.ControlNotOpen, PunchOpening.Readiness(PunchPort.Data, fresh));

        var withData = new HolepunchSessionState();
        withData.Enter(SessionStateFlags.CustomData1Received);

        // What lets the control port start does nothing for the data port.
        Assert.Equal(PunchReadiness.Ready, PunchOpening.Readiness(PunchPort.Control, withData));
        Assert.Equal(PunchReadiness.ControlNotOpen, PunchOpening.Readiness(PunchPort.Data, withData));
    }

    /// <summary>And what lets the data port start says nothing about the control port's condition.</summary>
    [Fact]
    public void TheDataPortNeedsTheControlOneEstablished()
    {
        var state = new HolepunchSessionState();
        state.Enter(SessionStateFlags.CtrlEstablished);

        Assert.Equal(PunchReadiness.Ready, PunchOpening.Readiness(PunchPort.Data, state));
        Assert.Equal(PunchReadiness.NoCustomData, PunchOpening.Readiness(PunchPort.Control, state));
    }

    /// <summary>Each port marks its own arrival.</summary>
    [Fact]
    public void EachPortMarksItsOwnOffer()
    {
        Assert.Equal(SessionStateFlags.CtrlOfferReceived, PunchOpening.OfferReceivedFor(PunchPort.Control));
        Assert.Equal(SessionStateFlags.DataOfferReceived, PunchOpening.OfferReceivedFor(PunchPort.Data));
    }

    /// <summary>
    /// THE ORDERING. The flag is set before the acknowledgement, so the automatic window is already
    /// open while this path is still handling the first offer - which is what gives a second one an
    /// owner. Before the flag, nobody would answer it.
    /// </summary>
    [Fact]
    public void TheWindowOpensBeforeThisPathAnswers()
    {
        var state = new HolepunchSessionState();

        // An offer arriving now would be answered by nobody: this path is waiting for one, and the
        // automatic window is shut.
        Assert.False(PunchOpening.ASecondOfferWouldBeAnswered(state));

        // The core sets this, and only then sends its own acknowledgement.
        state.Enter(PunchOpening.OfferReceivedFor(PunchPort.Control));

        Assert.True(PunchOpening.ASecondOfferWouldBeAnswered(state));
    }

    /// <summary>
    /// And the window shuts again once the control port is established, which is where PP207's
    /// asymmetry comes from - the data port's clause never closes.
    /// </summary>
    [Fact]
    public void TheWindowShutsWhenTheControlPortIsEstablished()
    {
        var state = new HolepunchSessionState();
        state.Enter(SessionStateFlags.CtrlOfferReceived);
        Assert.True(PunchOpening.ASecondOfferWouldBeAnswered(state));

        state.Enter(SessionStateFlags.CtrlEstablished);
        Assert.False(PunchOpening.ASecondOfferWouldBeAnswered(state));

        state.Enter(SessionStateFlags.DataOfferReceived);
        Assert.True(PunchOpening.ASecondOfferWouldBeAnswered(state));
    }

    /// <summary>
    /// The acknowledgement this path sends by hand is the automatic one's message, which is why one
    /// builder serves both.
    /// </summary>
    [Fact]
    public void ThisPathSendsTheSameMessageTheThreadWould()
        => Assert.Equal(OfferAck.Message(77), PunchOpening.Acknowledgement(77));

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheOpeningIsStillTheCores()
    {
        string? file = PunchOpeningSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(
            PunchOpeningSource.TheTwoPortsStillDifferInWhatTheyNeed(core),
            "two ports, two conditions");
        Assert.True(
            PunchOpeningSource.TheConsolesIdentityStillComesFromTheOffer(core),
            "the console's identity from the offer");
        Assert.True(
            PunchOpeningSource.TheFlagIsStillSetBeforeTheAnswer(core),
            "and the flag still set before the answer");
        Assert.True(
            PunchOpeningSource.TheAnswerIsStillTheSameMessage(core),
            "which is the automatic one's message");
    }
}
