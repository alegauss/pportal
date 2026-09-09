using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP845: the pad's other two halves, wired - the touchpad and the sensors reaching a console.
///
/// PP129 mapped SDL's (touchpad, finger) pair onto the console's pool of two ids, PP130 carried the
/// sensor fusion across the seam, PP93 gave the port one answer for how big a touchpad is. All three
/// shipped, all three were reachable, and none was reached: TouchpadTracker and MotionTracker had no
/// caller outside their own tests, and PadTranslation.WriteTo set three fields.
///
/// WHAT WAS MISSING WAS THE WIRE, and this is the wire. The events arrive through fields of SDL's
/// event union that the port had never read, so the offsets are the risky half - and they are
/// checked twice: against SDL's own header for the field order, and by pushing an event through the
/// loaded SDL binary and reading it back.
/// </summary>
public class PadTouchAndMotionTests(ITestOutputHelper output)
{
    private static string? EventsHeader()
        => SdlPadSource.LocateEventsHeader() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The four event types are the four numbers, held against the header's own order.
    ///
    /// SDL_CONTROLLERSTEAMHANDLEUPDATED is why this is an order and not five names: it arrived after
    /// these four, and inserting it above them instead would have shifted every one.
    /// </summary>
    [Fact]
    public void TheFourEventTypesAreWhereTheHeaderPutsThem()
    {
        Assert.Equal(0x656u, Gamepads.EventType.ControllerTouchpadDown);
        Assert.Equal(0x657u, Gamepads.EventType.ControllerTouchpadMotion);
        Assert.Equal(0x658u, Gamepads.EventType.ControllerTouchpadUp);
        Assert.Equal(0x659u, Gamepads.EventType.ControllerSensorUpdate);

        if (EventsHeader() is not { } header)
            return;

        Assert.True(
            SdlPadSource.TheControllerEventOrderIsStill(header),
            "SDL's controller event enum no longer runs remapped, touchpad down/motion/up, sensor");
    }

    /// <summary>
    /// And the two structs still declare their members in the order the overlay reads.
    ///
    /// This is the check that cannot be replaced by the round trip below: a pushed event proves the
    /// bytes travel, and the header proves they mean what this port says they mean.
    /// </summary>
    [Fact]
    public void TheTwoStructsStillHaveTheirFieldOrder()
    {
        if (EventsHeader() is not { } header)
            return;

        Assert.True(
            SdlPadSource.TheTouchpadEventFieldOrderIsStill(header),
            "SDL_ControllerTouchpadEvent's members moved, so the touch overlay reads the wrong bytes");

        Assert.True(
            SdlPadSource.TheSensorEventFieldOrderIsStill(header),
            "SDL_ControllerSensorEvent's members moved, so the sensor overlay reads the wrong bytes");

        // PP271: the reader sees the edit it guards against - a struct it cannot find is not a pass.
        Assert.False(SdlPadSource.TheTouchpadEventFieldOrderIsStill("struct Something { int x; };"));
        Assert.False(SdlPadSource.TheSensorEventFieldOrderIsStill(""));
    }

    /// <summary>
    /// The two sensor kinds are the two numbers, held against an enum with no values written.
    /// </summary>
    [Fact]
    public void TheSensorKindsAreWhereTheHeaderPutsThem()
    {
        Assert.Equal(1, Gamepads.SensorKind.Accelerometer);
        Assert.Equal(2, Gamepads.SensorKind.Gyroscope);

        if (SdlPadSource.LocateSensorHeader() is not { } path)
            return;

        Assert.True(
            SdlPadSource.TheSensorKindOrderIsStill(File.ReadAllText(path)),
            "SDL_SensorType no longer runs invalid, unknown, accel, gyro - so accel is not 1");
    }

    /// <summary>
    /// A TOUCH THROUGH SDL AND BACK, which is the offsets checked against the binary.
    ///
    /// The whole hazard in this task is a union: 12 is a touchpad index or a sensor kind, and 16 is
    /// a finger or the first sensor reading. A wrong offset does not fail - it reads a float's bytes
    /// as an integer, which is a finger index in the millions or a touch position at the far corner.
    /// Nothing on this side can catch that, and SDL's own queue can.
    ///
    /// `which` is deliberately not asserted: SDL resolves it against its device tables on the way
    /// through, which the pump's own test measured, so a pushed one carries the port's value nowhere.
    /// </summary>
    [Fact]
    public void ATouchpadEventSurvivesSdlsOwnQueue()
    {
        SdlEvent? seen = RoundTrip(
            Gamepads.EventType.ControllerTouchpadDown,
            () => Gamepads.PushEvent(
                Gamepads.EventType.ControllerTouchpadDown,
                which: 0, wide: 1, finger: 2, data1: 0.25f, data2: 0.75f));

        if (seen is null)
            return;

        SdlTouch touch = Gamepads.Touch(seen.Value);
        output.WriteLine($"touchpad {touch.Touchpad}, finger {touch.Finger}, {touch.X} x {touch.Y}");

        Assert.Equal(1, touch.Touchpad);
        Assert.Equal(2, touch.Finger);
        Assert.Equal(0.25f, touch.X);
        Assert.Equal(0.75f, touch.Y);
    }

    /// <summary>And a sensor reading, whose three floats start where the finger sits.</summary>
    [Fact]
    public void ASensorEventSurvivesSdlsOwnQueue()
    {
        SdlEvent? seen = RoundTrip(
            Gamepads.EventType.ControllerSensorUpdate,
            () => Gamepads.PushEvent(
                Gamepads.EventType.ControllerSensorUpdate,
                which: 0, wide: Gamepads.SensorKind.Gyroscope,
                data0: 1.5f, data1: -2.5f, data2: 3.5f));

        if (seen is null)
            return;

        SdlSensor sensor = Gamepads.Sensor(seen.Value);
        output.WriteLine($"kind {sensor.Kind}, {sensor.X} {sensor.Y} {sensor.Z}");

        Assert.Equal(Gamepads.SensorKind.Gyroscope, sensor.Kind);
        Assert.Equal(1.5f, sensor.X);
        Assert.Equal(-2.5f, sensor.Y);
        Assert.Equal(3.5f, sensor.Z);
    }

    /// <summary>
    /// One event pushed onto SDL's queue and taken back off the pump, or null where SDL did not
    /// start - which is a machine without it and not a failure, the same answer every other SDL
    /// assertion in this tree gives.
    ///
    /// Read through the PUMP and not by polling inside the push: the pump owns the queue and would
    /// consume the event between the two, which is a race that passes most of the time.
    /// </summary>
    private static SdlEvent? RoundTrip(uint type, Func<bool> push)
    {
        var seen = new List<SdlEvent>();

        using var sdl = new SdlThread(ev =>
        {
            lock (seen)
            {
                seen.Add(ev);
            }
        });

        if (sdl.Start(TimeSpan.FromSeconds(10)) != SdlStart.Started)
            return null;

        bool pushed = false;
        Assert.True(
            sdl.Invoke(() => pushed = push(), TimeSpan.FromSeconds(5)),
            "SDL did not run the push within five seconds");

        Assert.True(pushed, "SDL_PushEvent refused the event");

        SdlEvent? found = null;

        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    lock (seen)
                    {
                        int at = seen.FindIndex(one => one.Type == type);
                        if (at >= 0)
                            found = seen[at];
                    }

                    return found is not null;
                },
                TimeSpan.FromSeconds(5)),
            $"SDL never delivered event 0x{type:x} back to the pump");

        return found;
    }

    /// <summary>
    /// The three touchpad types are touchpad events and nothing else is.
    ///
    /// A button event's index sits in the bytes the touchpad index is read from, so a classifier
    /// that let one through would scale a button number into a finger position.
    /// </summary>
    [Fact]
    public void OnlyTheThreeTouchpadTypesAreTouchpadEvents()
    {
        foreach (uint type in (uint[])
            [
                Gamepads.EventType.ControllerTouchpadDown,
                Gamepads.EventType.ControllerTouchpadMotion,
                Gamepads.EventType.ControllerTouchpadUp,
            ])
        {
            Assert.True(Gamepads.IsTouchpadEvent(new SdlEvent(type, 0, 0, 0, 0)));
        }

        foreach (uint type in (uint[])
            [
                Gamepads.EventType.ControllerSensorUpdate,
                Gamepads.EventType.ControllerButtonDown,
                Gamepads.EventType.ControllerAxisMotion,
                Gamepads.EventType.ControllerDeviceAdded,
                Gamepads.UserEvent,
            ])
        {
            Assert.False(Gamepads.IsTouchpadEvent(new SdlEvent(type, 0, 0, 0, 0)));
        }
    }

    /// <summary>
    /// The touch reaches the console's own pool, scaled by the console's own pad.
    ///
    /// The two halves PP93 is about: the same fraction is a different number of units on a PS4 and a
    /// PS5, and the port has one place that answers which - so this asserts the answer travels
    /// rather than that some number came out.
    /// </summary>
    [Theory]
    [InlineData(false, 1920, 942)]
    [InlineData(true, 1919, 1079)]
    public void AFingerReachesTheStateInTheConsolesOwnUnits(bool ps5, int maxX, int maxY)
    {
        TouchpadExtents extents = TouchpadExtents.For(ps5);

        Assert.Equal(maxX, extents.MaxX);
        Assert.Equal(maxY, extents.MaxY);

        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        Assert.True(tracker.Down(state, touchpad: 0, finger: 0, 1.0f, 1.0f, extents));

        // The far corner, truncated the way the C++ multiplication into a uint16 truncates.
        Assert.Equal(1, tracker.Count);

        ChiakiControllerTouch held = state.Touch(0);
        Assert.Equal((ushort)maxX, held.X);
        Assert.Equal((ushort)maxY, held.Y);
    }

    /// <summary>
    /// PP845's own wiring, read out of PadFeed: the two trackers are CALLED.
    ///
    /// Read from the source because the alternative is a person holding a pad. The counters below
    /// are what a live run reports, and they cannot distinguish "nobody touched the pad" from "the
    /// code that would have noticed is not there" - which is exactly the reading PP838 was filed
    /// about, and §PP845 names it as the shape this task repeats one field along.
    /// </summary>
    [Fact]
    public void PadFeedCallsBothTrackers()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\PadFeed.cs") is not { } path)
            return;

        string code = CCall.Code(File.ReadAllText(path));

        foreach (string call in (string[])
            [
                "touches.Down(", "touches.Motion(", "touches.Up(", "touches.Forget(",
                "motion.Accelerometer(", "motion.Gyroscope(", "motion.ApplyTo(",
                "Gamepads.EnableMotionSensors(",
            ])
        {
            Assert.Contains(call, code, StringComparison.Ordinal);
        }

        // And the extents come from the caller, which is what §PP93 says must not have a default.
        Assert.Contains("TouchpadExtents extents", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TouchpadExtents.QtMacros", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the run tells it which console, off the same bit the connect info was built from.
    ///
    /// The one place a default would have been easiest and would have been wrong: PadFeed has no
    /// session, so a pad that is neither console's is what the Qt client's SDL path settles for.
    /// </summary>
    [Fact]
    public void TheRunTellsTheFeedWhichConsole()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs") is not { } path)
            return;

        string code = CCall.Code(File.ReadAllText(path));

        Assert.Contains("new PadFeed(phase.Pad, TouchpadExtents.For(", code, StringComparison.Ordinal);
    }
}
