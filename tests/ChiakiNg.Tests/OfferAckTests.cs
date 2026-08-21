using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP231: the acknowledgement the websocket thread sends on its own, and the one it loses.
///
/// The asymmetry is the point, and the last two tests are what state it: the same malformed message
/// is DROPPED inside the auto-ACK window and KEPT FOREVER outside it, decided by nothing about the
/// message and everything about which port has been established.
/// </summary>
public class OfferAckTests
{
    /// <summary>An offer nobody is waiting for is acknowledged, and still queued.</summary>
    [Fact]
    public void AnOfferInsideTheWindowIsAcknowledged()
    {
        OfferAckOutcome outcome = OfferAck.Consider(
            inWindow: true, isSessionMessage: true, SessionMessageAction.Offer);

        Assert.Equal(OfferAckOutcome.Acknowledge, outcome);
        Assert.True(OfferAck.Queues(outcome));
    }

    /// <summary>
    /// Anything else that parsed is left alone. The window is about offers nobody is waiting for,
    /// not about traffic in general.
    /// </summary>
    [Theory]
    [InlineData(SessionMessageAction.Result)]
    [InlineData(SessionMessageAction.Accept)]
    [InlineData(SessionMessageAction.Terminate)]
    [InlineData(SessionMessageAction.Unknown)]
    public void AnythingElseThatParsedIsOnlyQueued(SessionMessageAction action)
    {
        OfferAckOutcome outcome = OfferAck.Consider(inWindow: true, isSessionMessage: true, action);

        Assert.Equal(OfferAckOutcome.QueueOnly, outcome);
        Assert.True(OfferAck.Queues(outcome));
    }

    /// <summary>
    /// A notification that is not a session message never had a payload to parse, so the branch
    /// tests that as well as the window.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotASessionMessageIsOnlyQueued()
        => Assert.Equal(
            OfferAckOutcome.QueueOnly,
            OfferAck.Consider(inWindow: true, isSessionMessage: false, action: null));

    /// <summary>
    /// The defect. Inside the window, a payload that will not parse takes the notification with it:
    /// the enqueue is below that branch and the branch leaves.
    /// </summary>
    [Fact]
    public void AParseFailureInsideTheWindowLosesTheNotification()
    {
        OfferAckOutcome outcome = OfferAck.Consider(
            inWindow: true, isSessionMessage: true, action: null);

        Assert.Equal(OfferAckOutcome.Drop, outcome);
        Assert.False(OfferAck.Queues(outcome));
    }

    /// <summary>
    /// And the same message OUTSIDE the window is queued, because nothing parses it here at all -
    /// after which PP213's wait finds it, fails on it, and leaves it there for the next wait to
    /// fail on again. One message, two opposite fates.
    /// </summary>
    [Fact]
    public void TheSameMessageOutsideTheWindowIsKept()
    {
        OfferAckOutcome inside = OfferAck.Consider(true, isSessionMessage: true, action: null);
        OfferAckOutcome outside = OfferAck.Consider(false, isSessionMessage: true, action: null);

        Assert.False(OfferAck.Queues(inside));
        Assert.True(OfferAck.Queues(outside));

        // And what the other side does with it, which is the half PP213 measured.
        Assert.Equal(
            SessionMessageDisposition.Unparseable,
            SessionMessageWait.Consider(null, SessionMessageAction.Offer));

        Assert.False(SessionMessageWait.Clears(SessionMessageDisposition.Unparseable));
    }

    /// <summary>
    /// The reply carries the OFFER's request id. A new one answers a question nobody asked, and
    /// the console is waiting on the one it sent.
    /// </summary>
    [Fact]
    public void TheReplyCarriesTheOffersOwnRequestId()
    {
        string message = OfferAck.Message(offerRequestId: 4321);

        Assert.Equal(
            SessionMessageWriter.ShortMessage(SessionMessageAction.Result, 4321, OfferAck.NoError),
            message);

        Assert.Contains("4321", message, StringComparison.Ordinal);
        Assert.Equal(0, OfferAck.NoError);
    }

    /// <summary>
    /// The window itself is PP207's, unchanged - asserted here so this task's rules and the mask
    /// they hang on cannot drift apart.
    /// </summary>
    [Fact]
    public void TheWindowIsTheOnePP207Read()
    {
        var state = new HolepunchSessionState();

        Assert.False(state.ShouldAckOffers);

        state.Enter(SessionStateFlags.CtrlOfferReceived);
        Assert.True(state.ShouldAckOffers);

        state.Enter(SessionStateFlags.CtrlEstablished);
        Assert.False(state.ShouldAckOffers);

        state.Enter(SessionStateFlags.DataOfferReceived);
        Assert.True(state.ShouldAckOffers);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheAutoAckIsStillTheCores()
    {
        string? file = OfferAckSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(OfferAckSource.TheWindowIsStillTheStateMask(core), "the window");
        Assert.True(OfferAckSource.OnlyAnOfferIsStillAcknowledged(core), "only an offer");
        Assert.True(OfferAckSource.TheReplyIsStillTheOffersOwnId(core), "its own id, short, no error");
        Assert.True(
            OfferAckSource.AParseFailureStillSkipsTheEnqueue(core),
            "and a parse failure still leaves before the queue");
    }
}
