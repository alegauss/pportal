using System.Diagnostics;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP550: the adapter behind PP547's interface, over the pieces that run.
///
/// WHAT IS NOT ASSERTED, as in PP548 and PP549: the sends need PSN and the race needs a console
/// answering datagrams. What can be checked offline is the mapping from step name to wire action -
/// which is where a punch would silently wait for the wrong message - and the guard.
/// </summary>
public class LiveHolepunchPunchStepsTests
{
    private static LiveHolepunchPunchSteps Steps()
        => new("Authorization: Bearer t", "sid", new NotificationQueue(), new CandidateRaceRun());

    /// <summary>A push frame as PushChannel stores it: the prefix, the body marker, then the JSON.</summary>
    private static string Message(string action)
        => "gwid=1:body={\"action\":\"" + action + "\"}";

    /// <summary>
    /// THE MAPPING, which is the reason this class has one. Eleven steps use four actions: both
    /// offers are OFFER, both acknowledgements and the offer-ack wait are RESULT, and only the
    /// accept pair is ACCEPT.
    ///
    /// A wait keyed on the step name would have WaitForOfferAck and WaitForAccept looking for
    /// different things and finding the same message, which is a punch that hangs rather than one
    /// that fails.
    /// </summary>
    [Theory]
    [InlineData(nameof(HolepunchPunchStep.WaitForOffer), SessionMessageAction.Offer)]
    [InlineData(nameof(HolepunchPunchStep.SendOffer), SessionMessageAction.Offer)]
    [InlineData(nameof(HolepunchPunchStep.SendAccept), SessionMessageAction.Accept)]
    [InlineData(nameof(HolepunchPunchStep.WaitForAccept), SessionMessageAction.Accept)]
    [InlineData(nameof(HolepunchPunchStep.AckOffer), SessionMessageAction.Result)]
    [InlineData(nameof(HolepunchPunchStep.AckAccept), SessionMessageAction.Result)]
    [InlineData(nameof(HolepunchPunchStep.WaitForOfferAck), SessionMessageAction.Result)]
    public void EachStepIsAboutTheActionTheWireUses(string step, SessionMessageAction action)
        => Assert.Equal(action, LiveHolepunchPunchSteps.ActionFor(step));

    /// <summary>
    /// And every step PP547 sends or waits on has one. Driven off the sequence's own order rather
    /// than a list here, so a twelfth step arrives as a failure instead of an unmapped send.
    /// </summary>
    [Fact]
    public void EveryMessagingStepHasAnAction()
    {
        HolepunchPunchStep[] notMessages =
        [
            HolepunchPunchStep.Preconditions,
            HolepunchPunchStep.ChooseCandidate,
            HolepunchPunchStep.MarkEstablished,
            HolepunchPunchStep.ReceiveRequestSendResponse,
        ];

        foreach (HolepunchPunchStep step in HolepunchPunch.ExecutionOrder.Except(notMessages))
        {
            Assert.NotEqual(
                SessionMessageAction.Unknown, LiveHolepunchPunchSteps.ActionFor(step.ToString()));
        }
    }

    /// <summary>The steps that are not messages have no action, so a send cannot be invented for one.</summary>
    [Fact]
    public void TheStepsThatAreNotMessagesHaveNone()
    {
        Assert.Equal(
            SessionMessageAction.Unknown,
            LiveHolepunchPunchSteps.ActionFor(nameof(HolepunchPunchStep.ChooseCandidate)));

        Assert.Equal(SessionMessageAction.Unknown, LiveHolepunchPunchSteps.ActionFor("Nonsense"));
    }

    /// <summary>A queued session message with the wanted action ends the wait; another does not.</summary>
    [Fact]
    public async Task OnlyTheWantedActionEndsTheWait()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionMessageCreated, Message("OFFER")));

        using var steps = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun());

        Assert.True(await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), TimeSpan.FromSeconds(5), CancellationToken.None));

        // PP558: and it took it, which is what the C does. This asserted the opposite, on the
        // reasoning that the punch runs twice over the same queue - which is exactly why it must:
        // the data punch would otherwise sail through on the ctrl punch's messages.
        Assert.Equal(0, queue.Count);

        Assert.False(await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForAccept), TimeSpan.FromMilliseconds(120), CancellationToken.None));
    }

    /// <summary>
    /// PP558: THE SECOND PUNCH DOES NOT SAIL THROUGH ON THE FIRST ONE'S MESSAGES.
    ///
    /// The punch runs once per port over one queue. Before the clearing, the data punch's wait for
    /// an offer found the ctrl punch's and went straight on without the console having sent
    /// anything - which is a punch that reports success having done nothing.
    /// </summary>
    [Fact]
    public async Task TheSecondPunchDoesNotReuseTheFirstsMessages()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionMessageCreated, Message("OFFER")));

        using var ctrl = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun());
        using var data = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun());

        Assert.True(await ctrl.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.False(await data.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), TimeSpan.FromMilliseconds(150), CancellationToken.None));
    }

    /// <summary>
    /// A session message the wait is not after is taken off too, and counted - which is the C's
    /// "Ignoring holepunch session message with action %d" followed by a clear.
    /// </summary>
    [Fact]
    public async Task AMessageItIsNotWaitingForIsTakenAndCounted()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionMessageCreated, Message("ACCEPT")));

        using var steps = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun());

        Assert.False(await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), TimeSpan.FromMilliseconds(150), CancellationToken.None));

        Assert.Equal(0, queue.Count);
        Assert.Equal(1, steps.Ignored);
    }

    /// <summary>And a notification that is not a session message is left alone.</summary>
    [Fact]
    public async Task ANotificationOfAnotherTypeIsLeftWhereItIs()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.CustomData1Updated, "{}"));

        using var steps = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun());

        await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), TimeSpan.FromMilliseconds(150), CancellationToken.None);

        Assert.Equal(1, queue.Count);
        Assert.Equal(0, steps.Ignored);
    }

    /// <summary>
    /// The action is read from its key, not found in the text - so a RESULT that names the OFFER it
    /// acknowledges is still a result. Finding the first word that appears would take the punch
    /// down the wrong branch.
    /// </summary>
    [Fact]
    public void AResultNamingAnOfferIsStillAResult()
    {
        Assert.Equal(
            "RESULT",
            LiveHolepunchPunchSteps.ActionWordIn("{\"action\":\"RESULT\",\"for\":\"OFFER\"}"));

        // And with no action key at all there is no action, however the text reads.
        Assert.Null(LiveHolepunchPunchSteps.ActionWordIn("{\"note\":\"OFFER\"}"));
    }

    /// <summary>A notification that is not a session message is not one, whatever it carries.</summary>
    [Fact]
    public void ANotificationOfAnotherTypeCarriesNoAction()
    {
        Assert.False(LiveHolepunchPunchSteps.Carries(
            new QueuedNotification(PushNotificationType.MemberCreated, Message("OFFER")),
            SessionMessageAction.Offer));

        Assert.True(LiveHolepunchPunchSteps.Carries(
            new QueuedNotification(PushNotificationType.SessionMessageCreated, Message("OFFER")),
            SessionMessageAction.Offer));
    }

    /// <summary>
    /// PP552: A DEAD SOCKET ENDS THE WAIT. This is where the cost was worst - the punch waits three
    /// times at thirty seconds and runs twice, once per port.
    /// </summary>
    [Fact]
    public async Task ADeadChannelEndsTheWaitEarly()
    {
        using var steps = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", new NotificationQueue(), new CandidateRaceRun())
        {
            ChannelEnded = () => true,
        };

        var clock = Stopwatch.StartNew();
        bool arrived = await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), HolepunchPunch.MessageTimeout, CancellationToken.None);
        clock.Stop();

        Assert.False(arrived);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5),
            $"it served out the deadline: {clock.Elapsed}");
    }

    /// <summary>
    /// And a message already queued is still found, so the check does not run ahead of the queue it
    /// is meant to give up on.
    /// </summary>
    [Fact]
    public async Task ADeadChannelStillDeliversWhatAlreadyArrived()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionMessageCreated, Message("OFFER")));

        using var steps = new LiveHolepunchPunchSteps(
            "Authorization: Bearer t", "sid", queue, new CandidateRaceRun())
        {
            ChannelEnded = () => true,
        };

        Assert.True(await steps.WaitForMessageAsync(
            nameof(HolepunchPunchStep.WaitForOffer), HolepunchPunch.MessageTimeout, CancellationToken.None));
    }

    /// <summary>The guard is a started session and a port not already punched.</summary>
    [Fact]
    public void TheGuardIsStartedAndNotAlreadyPunched()
    {
        using var steps = Steps();

        Assert.False(steps.PreconditionsHold(HolepunchPortType.Ctrl));

        steps.State = SessionStateFlags.Created
            | SessionStateFlags.ConsoleJoined | SessionStateFlags.CustomData1Received;
        Assert.True(steps.PreconditionsHold(HolepunchPortType.Ctrl));

        steps.MarkEstablished(HolepunchPortType.Ctrl);
        Assert.False(steps.PreconditionsHold(HolepunchPortType.Ctrl));

        // The other port is a separate hole, and the C punches both.
        Assert.True(steps.PreconditionsHold(HolepunchPortType.Data));
        Assert.Contains(HolepunchPortType.Ctrl, steps.Established);
    }

    /// <summary>
    /// Nothing runs on what it was not given: a send with no envelope and a race with no candidates
    /// refuse, rather than reaching PSN to be told no.
    /// </summary>
    [Fact]
    public async Task NothingRunsOnWhatItWasNotGiven()
    {
        using var steps = Steps();

        Assert.False(await steps.SendMessageAsync(
            nameof(HolepunchPunchStep.SendOffer), CancellationToken.None));
        Assert.False(await steps.ChooseCandidateAsync(CancellationToken.None));

        // And with no candidate chosen there is no socket to answer the console on.
        Assert.Null(steps.Chosen);
        Assert.False(await steps.ReceiveRequestSendResponseAsync(
            TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    /// <summary>A send for a step that is not a message refuses even with an envelope set for it.</summary>
    [Fact]
    public async Task AStepThatIsNotAMessageCannotBeSent()
    {
        using var steps = Steps();
        steps.Envelopes[nameof(HolepunchPunchStep.ChooseCandidate)] = "{}";

        Assert.False(await steps.SendMessageAsync(
            nameof(HolepunchPunchStep.ChooseCandidate), CancellationToken.None));
    }
}
