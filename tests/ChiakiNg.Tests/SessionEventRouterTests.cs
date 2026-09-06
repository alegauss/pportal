using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP747, under PP707: the nine events landing somewhere instead of being counted unheard.
///
/// PP719's seam drops an event where nothing listens and keeps the count, and until this there was
/// nothing in app to listen. These hold what each of the nine leaves behind, and that the seam
/// stops reporting Unheard once a router is attached.
/// </summary>
public class SessionEventRouterTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE SEAM STOPS DROPPING, which is the whole of what this task changes for a run.
    ///
    /// PP719's Send returns false and counts an Unheard where no sink is attached. That count was
    /// the only record any of these events left.
    /// </summary>
    [Fact]
    public void AnAttachedRouterIsWhatStopsTheEventsBeingUnheard()
    {
        var events = new ManagedSessionEvents();

        Assert.False(events.IsHeard);
        Assert.False(events.SendConnected());
        Assert.Equal(1, events.Unheard);
        Assert.Equal(0, events.Sent);

        var router = new SessionEventRouter();
        events.Listen(router);

        Assert.True(events.IsHeard);
        Assert.True(events.SendConnected());

        Assert.Equal(1, events.Sent);
        Assert.Equal(1, events.Unheard);
        Assert.Equal(1, router.Connected);
    }

    /// <summary>And the hook fires for the one event the front door has a state for.</summary>
    [Fact]
    public void ConnectedReachesTheHookTheFrontDoorWouldGive()
    {
        var reached = 0;
        var router = new SessionEventRouter(() => reached++);

        router.Send(ManagedSessionEvents.Connected());

        Assert.Equal(1, reached);
        Assert.Equal(1, router.Connected);
        Assert.Equal(1, router.Received);
    }

    /// <summary>
    /// EVERY ONE OF THE NINE LEAVES SOMETHING BEHIND, which is what "routed" has to mean.
    ///
    /// PP271's shape: a router that held nothing would pass any test asking only that Send returns.
    /// So each kind is sent and the thing it decided is read back.
    /// </summary>
    [Fact]
    public void EachOfTheNineLeavesItsValueBehind()
    {
        var router = new SessionEventRouter();

        router.Send(new SessionEvent(ChiakiEventType.Rumble, Rumble: new RumbleState(1, 0x20, 0x30)));
        router.Send(new SessionEvent(ChiakiEventType.LedColor, Led: new PadLed(9, 8, 7)));
        router.Send(new SessionEvent(ChiakiEventType.PlayerIndex, PlayerIndex: 3));
        router.Send(new SessionEvent(
            ChiakiEventType.HapticIntensity, Intensity: DualSenseEffectIntensity.Strong));
        router.Send(new SessionEvent(
            ChiakiEventType.TriggerIntensity, Intensity: DualSenseEffectIntensity.Weak));
        router.Send(new SessionEvent(ChiakiEventType.MotionReset));
        router.Send(new SessionEvent(
            ChiakiEventType.VideoFecFailure, FecFrameIndex: 41, FecIdrRequestSent: true));
        router.Send(ManagedSessionEvents.Connected());

        output.WriteLine($"{router.Received} event(s) routed");

        Assert.Equal(new RumbleState(1, 0x20, 0x30), router.Rumble);
        Assert.Equal(new PadLed(9, 8, 7), router.Led);
        Assert.Equal((byte)3, router.PlayerIndex);
        Assert.Equal(DualSenseEffectIntensity.Strong, router.HapticIntensity);
        Assert.Equal(DualSenseEffectIntensity.Weak, router.TriggerIntensity);
        Assert.Equal((41, true), router.FecFailure);
        Assert.Equal(1, router.Connected);

        // The eighth is a flag, because MOTION_RESET carries nothing and means "re-take the origin".
        Assert.True(router.MotionResetWanted);
        Assert.True(router.TakeMotionReset());
        Assert.False(router.MotionResetWanted);
        Assert.False(router.TakeMotionReset());
    }

    /// <summary>Before anything arrives, every value is absent rather than a default that reads real.</summary>
    [Fact]
    public void NothingArrivedMeansNothingHeld()
    {
        var router = new SessionEventRouter();

        Assert.Null(router.Rumble);
        Assert.Null(router.TriggerEffects);
        Assert.Null(router.Led);
        Assert.Null(router.PlayerIndex);
        Assert.Null(router.HapticIntensity);
        Assert.Null(router.TriggerIntensity);
        Assert.Null(router.FecFailure);
        Assert.False(router.MotionResetWanted);
        Assert.Equal(0, router.Received);
    }

    /// <summary>
    /// LAST WINS, because each of these carries a value the console has just set.
    ///
    /// PP689: the pad info handler writes the new value onto the stream connection and reads it
    /// back into the event, so the latest IS the state and an older one is only history.
    /// </summary>
    [Fact]
    public void TheLatestValueIsTheState()
    {
        var router = new SessionEventRouter();

        router.Send(new SessionEvent(ChiakiEventType.PlayerIndex, PlayerIndex: 1));
        router.Send(new SessionEvent(ChiakiEventType.PlayerIndex, PlayerIndex: 4));

        Assert.Equal((byte)4, router.PlayerIndex);
        Assert.Equal(2, router.Received);
    }

    /// <summary>
    /// The router routes exactly the frame path's nine, by PP719's own list rather than a copy.
    /// </summary>
    [Fact]
    public void TheRoutedKindsAreTheFramePathsNine()
    {
        Assert.Equal(ManagedSessionEvents.RaisedByTheFramePath, SessionEventRouter.Routed);
        Assert.Equal(9, SessionEventRouter.Routed.Count);
    }

    /// <summary>
    /// An event from outside the frame path is counted and held nowhere, which is the honest answer.
    ///
    /// PP722's census says who raises the other eight, and none of them is this seam's caller. So
    /// one arriving here is not an error to throw on - it is a thing this router has no place for.
    /// </summary>
    [Fact]
    public void AnEventFromOutsideTheFramePathIsCountedAndNotHeld()
    {
        var router = new SessionEventRouter();

        router.Send(new SessionEvent(ChiakiEventType.KeyboardOpen));

        Assert.Equal(1, router.Received);
        Assert.Equal(0, router.Connected);
        Assert.Null(router.Rumble);
        Assert.False(router.MotionResetWanted);
    }

    /// <summary>
    /// PP741: and the seam it fills is off the unreached list, without a new one taking its place.
    /// </summary>
    [Fact]
    public void TheSeamItFillsIsNoLongerUnreached()
    {
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(typeof(SessionEventRouter).Assembly);

        output.WriteLine(string.Join(", ", unreached));

        Assert.DoesNotContain(nameof(ISessionEventSink), unreached);
        Assert.Equal([.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)], unreached);
    }
}
