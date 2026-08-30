using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP547: the punch sequence run, and its five checkpoints checked against the count PP539 read
/// out of the C rather than against itself.
/// </summary>
public class HolepunchPunchTests
{
    private sealed class Steps : IHolepunchPunchSteps
    {
        public List<HolepunchPunchStep> Reached { get; } = [];

        public List<string> Sent { get; } = [];

        public bool GuardFails { get; init; }

        public string? TimeOutAt { get; init; }

        public bool NoCandidateAnswers { get; init; }

        /// <summary>
        /// Cancel the moment this step runs, so the NEXT checkpoint is the one that sees it. That
        /// is the only way to reach a checkpoint past the first: a fresh run always meets its own
        /// first check before any other.
        /// </summary>
        public HolepunchPunchStep? CancelWhenReached { get; init; }

        /// <summary>The one-shot to cancel on, where a run is cancelling itself mid-flight.</summary>
        public HolepunchStop? Stop { get; init; }

        public HolepunchPortType? Established { get; private set; }

        private void Note(HolepunchPunchStep step)
        {
            Reached.Add(step);
            if (CancelWhenReached == step)
                Stop?.Cancel(stopWebsocketThread: false);
        }

        public bool PreconditionsHold(HolepunchPortType type)
        {
            Note(HolepunchPunchStep.Preconditions);
            return !GuardFails;
        }

        public Task<bool> WaitForMessageAsync(string action, TimeSpan timeout, CancellationToken ct)
        {
            Note(Enum.Parse<HolepunchPunchStep>(action));
            return Task.FromResult(action != TimeOutAt);
        }

        public Task<bool> SendMessageAsync(string action, CancellationToken ct)
        {
            Note(Enum.Parse<HolepunchPunchStep>(action));
            Sent.Add(action);
            return Task.FromResult(true);
        }

        public Task<bool> ChooseCandidateAsync(CancellationToken ct)
        {
            Note(HolepunchPunchStep.ChooseCandidate);
            return Task.FromResult(!NoCandidateAnswers);
        }

        public void MarkEstablished(HolepunchPortType type)
        {
            Note(HolepunchPunchStep.MarkEstablished);
            Established = type;
        }

        public Task<bool> ReceiveRequestSendResponseAsync(TimeSpan timeout, CancellationToken ct)
        {
            Note(HolepunchPunchStep.ReceiveRequestSendResponse);
            return Task.FromResult(true);
        }
    }

    /// <summary>All eleven run in the C's order, and the hole is marked for the port asked for.</summary>
    [Fact]
    public async Task EveryStepRunsInOrder()
    {
        var steps = new Steps();

        HolepunchPunchResult result = await new HolepunchPunch(
            steps, new HolepunchStop(), HolepunchPortType.Data).RunAsync();

        Assert.Equal(HolepunchPunchOutcome.Punched, result.Outcome);
        Assert.Equal(HolepunchPunch.ExecutionOrder, steps.Reached);
        Assert.Equal(HolepunchPortType.Data, steps.Established);
    }

    /// <summary>
    /// THE COUNT, and every one of the five reached.
    ///
    /// PP539 read five out of the C and this sequence declares five; declaring is not checking. So
    /// each is reached in turn by cancelling from inside the step that runs immediately before it,
    /// and each run must stop at exactly that checkpoint. A sequence that had four real checks and
    /// a fifth in the list would fail here, which is the whole point - a fresh run always meets its
    /// FIRST check first, so a test that only cancelled up front proves nothing about the rest.
    /// </summary>
    [Fact]
    public async Task EachOfTheFiveCheckpointsIsReached()
    {
        Assert.Equal(HolepunchPunch.CancelChecks, HolepunchPunch.ChecksBefore.Count);

        // The step that runs immediately before each checkpoint, in the C's order.
        (HolepunchPunchStep CancelAt, HolepunchPunchStep StopsAt)[] cases =
        [
            (HolepunchPunchStep.AckOffer, HolepunchPunchStep.SendOffer),
            (HolepunchPunchStep.ChooseCandidate, HolepunchPunchStep.SendAccept),
            (HolepunchPunchStep.SendAccept, HolepunchPunchStep.WaitForAccept),
            (HolepunchPunchStep.AckAccept, HolepunchPunchStep.MarkEstablished),
            (HolepunchPunchStep.MarkEstablished, HolepunchPunchStep.ReceiveRequestSendResponse),
        ];

        Assert.Equal(HolepunchPunch.ChecksBefore, [.. cases.Select(c => c.StopsAt)]);

        foreach ((HolepunchPunchStep cancelAt, HolepunchPunchStep stopsAt) in cases)
        {
            var stop = new HolepunchStop();
            var steps = new Steps { Stop = stop, CancelWhenReached = cancelAt };

            HolepunchPunchResult result = await new HolepunchPunch(
                steps, stop, HolepunchPortType.Ctrl).RunAsync();

            Assert.Equal(HolepunchPunchOutcome.Cancelled, result.Outcome);
            Assert.Equal(stopsAt, result.StoppedAt);

            // Consumed exactly once, by that checkpoint and no later one.
            Assert.False(stop.CheckAndConsume());
        }
    }

    /// <summary>A cancel stops the punch at its first checkpoint, and is consumed once.</summary>
    [Fact]
    public async Task ACancelStopsBeforeSendingOurOffer()
    {
        var steps = new Steps();
        var stop = new HolepunchStop();
        stop.Cancel(stopWebsocketThread: false);

        HolepunchPunchResult result = await new HolepunchPunch(
            steps, stop, HolepunchPortType.Ctrl).RunAsync();

        Assert.Equal(HolepunchPunchOutcome.Cancelled, result.Outcome);
        Assert.Equal(HolepunchPunchStep.SendOffer, result.StoppedAt);

        // The console's offer was waited for and acknowledged first, which is the C's order.
        Assert.Contains(HolepunchPunchStep.AckOffer, steps.Reached);
        Assert.DoesNotContain(HolepunchPunchStep.ChooseCandidate, steps.Reached);
        Assert.False(stop.CheckAndConsume());
    }

    /// <summary>The guard answers Uninitialised, not Failed - the C's own distinction.</summary>
    [Fact]
    public async Task TheGuardIsNotAFailure()
    {
        HolepunchPunchResult result = await new HolepunchPunch(
            new Steps { GuardFails = true }, new HolepunchStop(), HolepunchPortType.Data).RunAsync();

        Assert.Equal(HolepunchPunchOutcome.Uninitialised, result.Outcome);
        Assert.Equal(HolepunchPunchStep.Preconditions, result.StoppedAt);
    }

    /// <summary>A message that never arrives is a timeout; a race nothing answers is a failure.</summary>
    [Fact]
    public async Task AWaitTimesOutAndAnUnansweredRaceFails()
    {
        HolepunchPunchResult timedOut = await new HolepunchPunch(
            new Steps { TimeOutAt = nameof(HolepunchPunchStep.WaitForAccept) },
            new HolepunchStop(), HolepunchPortType.Ctrl).RunAsync();

        Assert.Equal(HolepunchPunchOutcome.TimedOut, timedOut.Outcome);
        Assert.Equal(HolepunchPunchStep.WaitForAccept, timedOut.StoppedAt);

        HolepunchPunchResult unanswered = await new HolepunchPunch(
            new Steps { NoCandidateAnswers = true }, new HolepunchStop(), HolepunchPortType.Ctrl).RunAsync();

        Assert.Equal(HolepunchPunchOutcome.Failed, unanswered.Outcome);
        Assert.Equal(HolepunchPunchStep.ChooseCandidate, unanswered.StoppedAt);
    }
}
