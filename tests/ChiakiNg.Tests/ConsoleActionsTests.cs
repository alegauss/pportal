using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP13: what the front door's rows do, and the screen that connects without being asked.
/// </summary>
public class ConsoleActionsTests
{
    private static ConsoleActionState Discovered(bool registered = true)
        => new(Discovered: true, Manual: false, Registered: registered, Duid: null);

    private static ConsoleActionState Manual(bool registered = true)
        => new(Discovered: false, Manual: true, Registered: registered, Duid: null);

    private static ConsoleActionState Psn()
        => new(Discovered: false, Manual: false, Registered: true, Duid: "00112233");

    /// <summary>
    /// The nickname goes with a discovered console and with nothing else. It is what the
    /// wake-then-connect path waits to see come back, so a manual console would wait on a name
    /// that never arrives.
    /// </summary>
    [Fact]
    public void ConnectingSendsTheNicknameOnlyForADiscoveredConsole()
    {
        Assert.True(ConsoleActions.ConnectSendsTheNickname(Discovered()));
        Assert.False(ConsoleActions.ConnectSendsTheNickname(Manual()));
        Assert.False(ConsoleActions.ConnectSendsTheNickname(Psn()));
    }

    /// <summary>
    /// Waking is for the console with no other way in. A discovered one is awake and a PSN one is
    /// reached through the relay, so on both of those the action is not merely useless - it is the
    /// wrong thing to have on screen.
    /// </summary>
    [Fact]
    public void WakingIsOfferedOnlyWhereThereIsNoOtherWayIn()
    {
        Assert.True(ConsoleActions.CanWake(Manual()));
        Assert.False(ConsoleActions.CanWake(Discovered()));
        Assert.False(ConsoleActions.CanWake(Psn()));
    }

    /// <summary>
    /// And the screen's rule is not the backend's. An unregistered manual console offers the
    /// action and nothing is sent, because the packet carries a registration key there is none of.
    /// </summary>
    [Fact]
    public void AnUnregisteredConsoleOffersTheActionAndSendsNothing()
    {
        ConsoleActionState console = Manual(registered: false);

        Assert.True(ConsoleActions.CanWake(console));
        Assert.False(ConsoleActions.WakeWouldBeSent(console));

        Assert.True(ConsoleActions.WakeWouldBeSent(Manual(registered: true)));
    }

    /// <summary>
    /// Removing has three outcomes. The third is the one worth naming: a discovered, registered
    /// console offers neither, and the menu entry does nothing at all. A port that filled that
    /// silence in with a delete loses the user their registration.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, RemoveAction.Delete)]
    [InlineData(true, true, true, RemoveAction.Delete)]
    [InlineData(false, true, false, RemoveAction.Hide)]
    [InlineData(false, true, true, RemoveAction.None)]
    [InlineData(false, false, true, RemoveAction.None)]
    public void RemovingHasThreeOutcomesAndOneOfThemIsSilence(
        bool manual, bool discovered, bool registered, RemoveAction expected)
    {
        var console = new ConsoleActionState(discovered, manual, registered, null);
        Assert.Equal(expected, ConsoleActions.RemovalFor(console));
    }

    /// <summary>
    /// Manual wins over discovered. A console the user typed in that discovery has since found is
    /// still theirs to delete, and hiding it would leave the entry they made behind.
    /// </summary>
    [Fact]
    public void AManualConsoleThatWasAlsoDiscoveredIsStillDeleted()
        => Assert.Equal(
            RemoveAction.Delete,
            ConsoleActions.RemovalFor(new ConsoleActionState(true, true, true, null)));

    /// <summary>Nothing cancels the auto-connect screen for the first second and a half.</summary>
    [Fact]
    public void NothingCancelsTheAutoConnectScreenAtFirst()
    {
        var screen = new AutoConnectScreen();

        Assert.False(screen.AllowClose);
        Assert.False(screen.HintVisible);
        Assert.Equal(AutoConnectScreen.Waiting, screen.Message);

        screen.Cancel();

        Assert.Equal(AutoConnectScreen.Waiting, screen.Message);
        Assert.False(screen.Stopped);

        // A millisecond short of the grace, and still nothing: no hint offering a way out, and
        // nothing a keypress does. The hint appearing early would be worse than useless - it would
        // name a key that is being ignored.
        screen.Advance(TimeSpan.FromMilliseconds(1499));

        Assert.False(screen.AllowClose);
        Assert.False(screen.HintVisible);

        screen.Cancel();
        Assert.Equal(AutoConnectScreen.Waiting, screen.Message);
    }

    /// <summary>And once the grace has passed, the hint appears and cancelling works.</summary>
    [Fact]
    public void AfterTheGraceTheHintAppearsAndCancellingTakesTwoSeconds()
    {
        var screen = new AutoConnectScreen();
        screen.Advance(AutoConnectScreen.Grace);

        Assert.True(screen.AllowClose);
        Assert.True(screen.HintVisible);

        screen.Cancel();

        // It says what it is doing rather than vanishing.
        Assert.Equal(AutoConnectScreen.Cancelling, screen.Message);
        Assert.False(screen.HintVisible);
        Assert.False(screen.Stopped);

        screen.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(screen.Stopped);

        screen.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(screen.Stopped);
    }

    /// <summary>
    /// A console that never woke reports it whenever it likes, grace period included - the
    /// callback has no guard where the cancel does.
    /// </summary>
    [Fact]
    public void ATimeoutIsHeardDuringTheGracePeriod()
    {
        var screen = new AutoConnectScreen();
        screen.WakeupFailed();

        Assert.Equal(AutoConnectScreen.TimedOut, screen.Message);
        Assert.False(screen.AllowClose);
    }

    /// <summary>
    /// And it gets out - but only because two seconds is longer than a second and a half. The exit
    /// it schedules runs through the stop that IS guarded, so a grace period longer than the exit
    /// delay would leave the user on a black screen with nothing that leaves it.
    /// </summary>
    [Fact]
    public void TheTimeoutEscapesOnlyBecauseTheExitOutlastsTheGrace()
    {
        Assert.True(AutoConnectScreen.FailDelay > AutoConnectScreen.Grace,
            "the exit delay has to outlast the grace period or a timeout cannot leave");

        var screen = new AutoConnectScreen();
        screen.WakeupFailed();
        screen.Advance(AutoConnectScreen.FailDelay);

        Assert.True(screen.Stopped);
    }

    /// <summary>
    /// Which makes the guard on leaving unreachable: every exit is scheduled a fixed two seconds
    /// out, and the earliest one can be scheduled is the moment the screen opens - already past
    /// the grace by the time it runs. Stated so that a later change to either interval is read as
    /// the coupled change it is, rather than as two independent numbers.
    /// </summary>
    [Fact]
    public void EveryScheduledExitLandsAfterTheGrace()
    {
        for (int at = 0; at <= 3000; at += 100)
        {
            var screen = new AutoConnectScreen();
            screen.Advance(TimeSpan.FromMilliseconds(at));
            screen.WakeupFailed();
            screen.Advance(AutoConnectScreen.FailDelay);

            Assert.True(screen.Stopped, $"a timeout at {at}ms did not get out");
        }
    }

    /// <summary>The hint names a button when there is one to name, and two words when there is not.</summary>
    [Theory]
    [InlineData(false, false, "escape or right-click")]
    [InlineData(false, true, "escape or right-click")]
    [InlineData(true, false, "Circle")]
    [InlineData(true, true, "B")]
    public void TheHintNamesWhateverTheUserIsHolding(bool controller, bool deck, string expected)
        => Assert.Equal(expected, AutoConnectScreen.CancelHint(controller, deck));

    /// <summary>And the shapes these were read out of.</summary>
    [Fact]
    public void TheFrontDoorIsStillTheQtClients()
    {
        string? list = FrontDoorSource.Locate(FrontDoorSource.MainViewQml);
        string? auto = FrontDoorSource.Locate(FrontDoorSource.AutoConnectQml);
        if (list is null || auto is null)
            return;

        string mainView = File.ReadAllText(list);
        string autoConnect = File.ReadAllText(auto);

        Assert.True(FrontDoorSource.TheNicknameGoesOnlyWithADiscoveredConsole(mainView),
            "the nickname still goes only with a discovered console");
        Assert.True(FrontDoorSource.WakingNeedsNeitherDiscoveryNorADuid(mainView),
            "waking still needs neither discovery nor a duid");
        Assert.True(FrontDoorSource.RemovingHasThreeOutcomes(mainView),
            "removing still branches manual, then discovered-and-unregistered, then nothing");
        Assert.True(FrontDoorSource.StopIsGuardedAndTheTimeoutIsNot(autoConnect),
            "leaving is still guarded where the timeout that schedules it is not");
    }

    /// <summary>
    /// The two intervals, read rather than remembered - because the relationship between them is
    /// what the timeout's escape depends on.
    /// </summary>
    [Fact]
    public void TheAutoConnectScreenStillDeclaresTheseTwoIntervals()
    {
        string? auto = FrontDoorSource.Locate(FrontDoorSource.AutoConnectQml);
        if (auto is null)
            return;

        IReadOnlyList<int> intervals = FrontDoorSource.Intervals(File.ReadAllText(auto));

        Assert.Equal(new[] { 1500, 2000 }, intervals);
        Assert.Equal(intervals[0], (int)AutoConnectScreen.Grace.TotalMilliseconds);
        Assert.Equal(intervals[1], (int)AutoConnectScreen.FailDelay.TotalMilliseconds);
    }
}
