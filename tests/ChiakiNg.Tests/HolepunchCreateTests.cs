using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP545: the create sequence run, not described.
/// </summary>
public class HolepunchCreateTests
{
    /// <summary>Each step answers as told, and records that it was reached.</summary>
    private sealed class Steps : IHolepunchCreateSteps
    {
        public List<HolepunchCreateStep> Reached { get; } = [];

        public HolepunchCreateStep? FailAt { get; init; }

        public TimeSpan? OpenTimeoutSeen { get; private set; }

        public TimeSpan? CreatedTimeoutSeen { get; private set; }

        public Task<bool> LookUpFqdnAsync(CancellationToken ct) => Ran(HolepunchCreateStep.WebSocketFqdn);

        public Task<bool> OpenWebSocketAsync(CancellationToken ct) => Ran(HolepunchCreateStep.OpenWebSocket);

        public Task<bool> WaitForOpenAsync(TimeSpan timeout, CancellationToken ct)
        {
            OpenTimeoutSeen = timeout;
            return Ran(HolepunchCreateStep.WaitForOpen);
        }

        public Task<bool> CreateSessionAsync(CancellationToken ct) => Ran(HolepunchCreateStep.CreateSession);

        public Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken ct)
        {
            CreatedTimeoutSeen = timeout;
            return Ran(HolepunchCreateStep.WaitForCreated);
        }

        private Task<bool> Ran(HolepunchCreateStep step)
        {
            Reached.Add(step);
            return Task.FromResult(FailAt != step);
        }
    }

    /// <summary>The five run in the C's order and the create succeeds.</summary>
    [Fact]
    public async Task EveryStepRunsInOrder()
    {
        var steps = new Steps();

        HolepunchCreateResult result = await new HolepunchCreate(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(HolepunchCreateOutcome.Created, result.Outcome);
        Assert.Null(result.StoppedAt);
        Assert.Equal(HolepunchCreate.ExecutionOrder, steps.Reached);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A cancel is delivered once, at the first checkpoint that reaches it,
    /// and the steps after it do not run.
    ///
    /// PP538's one-shot is what makes this true: a plain bool would stop here too, and would also
    /// stop everything else that ever checked, which is not what the C does.
    /// </summary>
    [Fact]
    public async Task ACancelStopsTheSequenceAtItsFirstCheckpoint()
    {
        var steps = new Steps();
        var stop = new HolepunchStop();
        stop.Cancel(stopWebsocketThread: false);

        HolepunchCreateResult result = await new HolepunchCreate(steps, stop).RunAsync();

        Assert.Equal(HolepunchCreateOutcome.Cancelled, result.Outcome);
        Assert.Equal(HolepunchCreateStep.OpenWebSocket, result.StoppedAt);

        // The lookup ran, because the C's first checkpoint is after it.
        Assert.Equal([HolepunchCreateStep.WebSocketFqdn], steps.Reached);

        // And the one-shot was consumed, so a later checkpoint would see nothing.
        Assert.False(stop.CheckAndConsume());
    }

    /// <summary>
    /// The checkpoints are where the C has them, and there are as many as PP539 counted in
    /// session_create. Asserted against that count rather than against a number chosen here.
    /// </summary>
    [Fact]
    public void TheCheckpointsMatchWhatWasCountedInTheC()
    {
        int before = HolepunchCreate.ExecutionOrder.Count(HolepunchCreate.ChecksBefore);

        // Three before a step, plus the one after the last, is session_create's four.
        Assert.Equal(3, before);
        Assert.Equal(4, HolepunchCreate.CancelChecks);
        Assert.Equal(HolepunchCreate.CancelChecks, before + 1);
    }

    /// <summary>A step that fails stops there and is not a timeout.</summary>
    [Fact]
    public async Task AFailedStepStopsThereAndIsNotATimeout()
    {
        var steps = new Steps { FailAt = HolepunchCreateStep.CreateSession };

        HolepunchCreateResult result = await new HolepunchCreate(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(HolepunchCreateOutcome.Failed, result.Outcome);
        Assert.Equal(HolepunchCreateStep.CreateSession, result.StoppedAt);
        Assert.DoesNotContain(HolepunchCreateStep.WaitForCreated, steps.Reached);
    }

    /// <summary>
    /// THE DEPARTURE. A websocket that never opens is a timeout here and is a hang in the C.
    ///
    /// PP258 found that wait: no deadline, no cancellation inside it, and a thread whose failure
    /// path sets nothing the waiter tests. Reproducing it would ship a known hang on a failure an
    /// expired token is enough to cause, so this is the one place the port does not reproduce
    /// behaviour - and it says so, here and on the type.
    /// </summary>
    [Fact]
    public async Task AWebSocketThatNeverOpensTimesOutInsteadOfHanging()
    {
        var steps = new Steps { FailAt = HolepunchCreateStep.WaitForOpen };

        HolepunchCreateResult result = await new HolepunchCreate(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(HolepunchCreateOutcome.TimedOut, result.Outcome);
        Assert.Equal(HolepunchCreateStep.WaitForOpen, result.StoppedAt);
        Assert.True(HolepunchCreate.BoundsTheWaitTheCDoesNot);
        Assert.Equal(TimeSpan.FromSeconds(10), steps.OpenTimeoutSeen);
    }

    /// <summary>
    /// And the notification wait keeps the C's own thirty seconds, because that one the C bounds.
    /// The two together are what makes the departure exactly one deadline rather than a policy.
    /// </summary>
    [Fact]
    public async Task TheNotificationWaitKeepsTheCsOwnTimeout()
    {
        var steps = new Steps();

        await new HolepunchCreate(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(TimeSpan.FromSeconds(30), steps.CreatedTimeoutSeen);
    }
}
