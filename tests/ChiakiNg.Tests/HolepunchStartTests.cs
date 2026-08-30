using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP546: the start sequence run, and the failure the C reports as success.
/// </summary>
public class HolepunchStartTests
{
    private sealed class Steps : IHolepunchStartSteps
    {
        public List<HolepunchStartStep> Reached { get; } = [];

        public bool Created { get; init; } = true;

        public bool AlreadyStarted { get; init; }

        public bool WakeupFails { get; init; }

        public bool StartFails { get; init; }

        public StartFailure MemberFailure { get; init; } = StartFailure.None;

        public TimeSpan? MemberTimeoutSeen { get; private set; }

        public bool PreconditionsHold(out bool created)
        {
            created = Created;
            return Created && !AlreadyStarted;
        }

        public Task<bool> WakeUpPs4Async(CancellationToken ct)
        {
            Reached.Add(HolepunchStartStep.WakeUpPs4);
            return Task.FromResult(!WakeupFails);
        }

        public Task<bool> StartSessionAsync(CancellationToken ct)
        {
            Reached.Add(HolepunchStartStep.StartSession);
            return Task.FromResult(!StartFails);
        }

        public Task<StartFailure> WaitForMemberAsync(TimeSpan timeout, CancellationToken ct)
        {
            MemberTimeoutSeen = timeout;
            Reached.Add(HolepunchStartStep.WaitForMember);
            return Task.FromResult(MemberFailure);
        }
    }

    /// <summary>A PS5 start runs three steps and skips the wakeup entirely.</summary>
    [Fact]
    public async Task APs5StartSkipsTheWakeup()
    {
        var steps = new Steps();

        HolepunchStartResult result = await new HolepunchStart(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(HolepunchStartOutcome.Started, result.Outcome);
        Assert.DoesNotContain(HolepunchStartStep.WakeUpPs4, steps.Reached);
        Assert.Equal(
            [HolepunchStartStep.Preconditions, HolepunchStartStep.StartSession, HolepunchStartStep.WaitForMember],
            result.Ran);
    }

    /// <summary>And a PS4 runs it, in front of the start call.</summary>
    [Fact]
    public async Task APs4StartRunsTheWakeupFirst()
    {
        var steps = new Steps();

        await new HolepunchStart(steps, new HolepunchStop(), isPs4: true).RunAsync();

        Assert.Equal(
            [HolepunchStartStep.WakeUpPs4, HolepunchStartStep.StartSession, HolepunchStartStep.WaitForMember],
            steps.Reached);
    }

    /// <summary>The two state guards answer differently, as the C's two returns do.</summary>
    [Fact]
    public async Task TheTwoGuardsAreToldApart()
    {
        HolepunchStartResult uncreated = await new HolepunchStart(
            new Steps { Created = false }, new HolepunchStop()).RunAsync();
        Assert.Equal(HolepunchStartOutcome.Uninitialised, uncreated.Outcome);

        HolepunchStartResult twice = await new HolepunchStart(
            new Steps { AlreadyStarted = true }, new HolepunchStop()).RunAsync();
        Assert.Equal(HolepunchStartOutcome.AlreadyStarted, twice.Outcome);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A session that joined the wrong console is reported as a failure here,
    /// and as SUCCESS by the C.
    ///
    /// PP257 found the shadow: the inner error variable takes the assignment, the branch breaks, and
    /// the outer one still holds what the wait put there. Reproducing that would mean a port that
    /// streams from a console the user did not ask for and says everything went fine.
    /// </summary>
    [Fact]
    public async Task TheWrongConsoleIsAFailureHereAndSuccessInTheC()
    {
        var steps = new Steps { MemberFailure = StartFailure.WrongConsole };

        HolepunchStartResult result = await new HolepunchStart(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(HolepunchStartOutcome.Failed, result.Outcome);
        Assert.Equal(StartFailure.WrongConsole, result.Failure);

        // And what the C would have said is still available, which is what makes this a departure
        // rather than a disagreement nobody recorded.
        Assert.Equal("CHIAKI_ERR_SUCCESS", SessionStart.Reported(StartFailure.WrongConsole));
        Assert.True(SessionStart.IsLost(StartFailure.WrongConsole));
    }

    /// <summary>
    /// Both of PP257's lost failures are reported, not just the identity one. Driven off
    /// SessionStart.Lost so a third joining that list arrives here without an edit.
    /// </summary>
    [Fact]
    public async Task EveryFailureTheCLosesIsReported()
    {
        Assert.NotEmpty(SessionStart.Lost);

        foreach (StartFailure lost in SessionStart.Lost)
        {
            HolepunchStartResult result = await new HolepunchStart(
                new Steps { MemberFailure = lost }, new HolepunchStop()).RunAsync();

            Assert.Equal(HolepunchStartOutcome.Failed, result.Outcome);
            Assert.Equal(lost, result.Failure);
        }
    }

    /// <summary>A cancel after the start call stops before the wait, and is consumed once.</summary>
    [Fact]
    public async Task ACancelAfterTheStartCallStopsBeforeTheWait()
    {
        var steps = new Steps();
        var stop = new HolepunchStop();
        stop.Cancel(stopWebsocketThread: false);

        HolepunchStartResult result = await new HolepunchStart(steps, stop).RunAsync();

        Assert.Equal(HolepunchStartOutcome.Cancelled, result.Outcome);
        Assert.DoesNotContain(HolepunchStartStep.WaitForMember, steps.Reached);
        Assert.False(stop.CheckAndConsume());
    }

    /// <summary>And the wait keeps the C's own thirty seconds, which this one the C does bound.</summary>
    [Fact]
    public async Task TheMemberWaitKeepsTheCsOwnTimeout()
    {
        var steps = new Steps();

        await new HolepunchStart(steps, new HolepunchStop()).RunAsync();

        Assert.Equal(TimeSpan.FromSeconds(30), steps.MemberTimeoutSeen);
        Assert.Equal(2, HolepunchStart.CancelChecks);
    }
}
