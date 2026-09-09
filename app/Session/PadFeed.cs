using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP756: where a pad's state goes, as an interface rather than as the C session.
///
/// <see cref="PadFeed"/> pushed at ChiakiSession directly, and chiaki_session_set_controller_state
/// hands what it takes to stream_connection.feedback_sender - the struct PP696 deletes. So the one
/// live caller of the pad path was wired to the half of the session that is going away, with no way
/// to be pointed at the port's own sender instead.
///
/// TWO THINGS SATISFY IT, which is what keeps it from being a rename: the C session today, and
/// ManagedControllerStateSink, which is the same contract over the port's own feedback sender.
/// </summary>
public interface IControllerStateSink
{
    /// <summary>
    /// chiaki_session_set_controller_state: keep the state, and pass it on if a sender is armed.
    /// </summary>
    /// <returns>Success, or what taking the sender's lock failed with - never the sender's own answer.</returns>
    ChiakiError SetControllerState(ChiakiControllerState state);
}

/// <summary>
/// PP701: a real pad opened, translated and pushed at the session, for as long as one is held.
///
/// <see cref="PadTranslation"/> is the mapping and this is the wiring: SDL on its own thread, the
/// first game controller it enumerates, and a push on every change. What was missing before was
/// only ever this - the state, the shim's setter and the session's lock all existed and nothing
/// called them outside the self-test.
///
/// ONE PAD, THE FIRST ONE. The client folds every attached controller into a union with
/// chiaki_controller_state_or, and that is the right shape for a client somebody plays on. This is
/// the smallest thing that lets a person play, which is what PP76's reading needs; a second pad is
/// a feature and not a correction, and the union it needs is already exported and already tested.
///
/// PUSHED ON CHANGE. libchiaki's feedback sender reads whatever state it was last handed, on its
/// own timer, so pushing every event would push the same state repeatedly and pushing none would
/// leave the console holding a stale one. The change is the moment there is something new to say -
/// which is also controllermanager.cpp's rule, and the reason its handlers return a bool.
/// </summary>
public sealed class PadFeed : IDisposable
{
    private readonly PadTranslation translation = new();
    private readonly ChiakiControllerState state = new();
    private readonly TouchpadTracker touches = new();
    private readonly MotionTracker motion = new();
    private readonly IControllerStateSink sink;
    private readonly TouchpadExtents extents;
    private readonly object gate = new();

    private SdlThread? sdl;
    private IntPtr controller;
    private long pushed;
    private long adopted;
    private long touched;
    private long moved;

    /// <param name="sink">Where the state goes. Not owned, and outlives this.</param>
    /// <param name="extents">
    /// PP845: which console's touchpad SDL's fractions are a fraction OF, and there is no default.
    ///
    /// §PP93's finding is that the Qt client answers this three times and its SDL path takes the
    /// pair that is neither console - because that layer has no session to ask. This layer has none
    /// either; what it has is a caller obliged to say, which is the whole of the difference.
    /// </param>
    public PadFeed(IControllerStateSink sink, TouchpadExtents extents)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
        this.extents = extents;
    }

    /// <summary>How many states have been pushed. Zero after a session is a pad that never reached it.</summary>
    public long Pushed => Interlocked.Read(ref pushed);

    /// <summary>The name of the pad that is open now, or null where none is.</summary>
    public string? PadName { get; private set; }

    /// <summary>
    /// PP838: whether a pad is feeding RIGHT NOW, which <see cref="Start"/>'s answer cannot say.
    ///
    /// Start reports what was there when it ran, and a run that reported no pad can now have one -
    /// which is the whole change. A caller printing Start's answer at the end of a session would
    /// say "no pad" about a session somebody played.
    /// </summary>
    public bool HasPad => controller != IntPtr.Zero;

    /// <summary>
    /// How many pads were adopted after the start, which is the number this line is about.
    ///
    /// Separate from <see cref="Pushed"/> because they answer different questions: zero pushed with
    /// one adopted is a pad connected and not touched, and zero of both is a pad that never
    /// arrived. Before this those two were the same reading.
    /// </summary>
    public long Adopted => Interlocked.Read(ref adopted);

    /// <summary>
    /// PP845: how many touch events reached the console, and how many sensor readings did.
    ///
    /// Counted for the reason <see cref="Adopted"/> is, and §PP845 names it: before this a live run
    /// said "635 states pushed" whether or not any of them carried a finger, so the two halves this
    /// task wires were unobservable from outside a person holding a pad. Zero touched with a
    /// non-zero Pushed is now a distinct reading - a pad played with the touchpad untouched - and
    /// zero moved is a pad with no sensors or sensors that failed to enable.
    /// </summary>
    public long Touched => Interlocked.Read(ref touched);

    /// <summary>Sensor readings folded into the orientation, which is the other half.</summary>
    public long Moved => Interlocked.Read(ref moved);

    /// <summary>
    /// Which sensor kinds the open pad has turned on, empty for a pad with neither.
    ///
    /// Reported rather than assumed: a pad without motion is ordinary, and it is the one case where
    /// <see cref="Moved"/> reading zero is correct rather than a defect.
    /// </summary>
    public IReadOnlyList<int> Sensors { get; private set; } = [];

    /// <summary>
    /// Opens SDL and the first game controller on it, and reports whether a pad is now feeding.
    ///
    /// FALSE IS NOT A FAILURE OF THE SESSION. A machine with no pad plugged in still streams, and
    /// this returning false is how a caller says so rather than refusing to run - the picture is
    /// worth having without a controller, which is the state everything before this shipped in.
    /// </summary>
    public bool Start()
    {
        var thread = new SdlThread(Offer);

        if (thread.Start(TimeSpan.FromSeconds(10)) != SdlStart.Started)
        {
            thread.Dispose();
            return false;
        }

        sdl = thread;

        // Opened ON the SDL thread. Gamepads' own note: SDL's controller API is not documented as
        // thread-safe against its own pump, and this runs between two polls.
        thread.Invoke(
            () =>
            {
                foreach (SdlPad pad in Gamepads.Pads())
                {
                    IntPtr opened = Gamepads.OpenController(pad.Index);
                    if (opened == IntPtr.Zero)
                        continue;

                    controller = opened;
                    PadName = pad.Name;

                    // PP845: and its sensors, which produce no events until they are asked to.
                    // The Qt client does this at the same moment for the same reason.
                    Sensors = Gamepads.EnableMotionSensors(opened);
                    break;
                }
            },
            TimeSpan.FromSeconds(5));

        return controller != IntPtr.Zero;
    }

    /// <summary>
    /// One event, folded in and pushed if it changed anything.
    ///
    /// Runs on the SDL thread, which is why it is short: SdlThread's own note says a callback
    /// happens between two polls and must not block. The push takes libchiaki's state lock and
    /// returns - it does not wait for the console.
    /// </summary>
    private void Offer(SdlEvent ev)
    {
        // PP838: A DEVICE ARRIVING IS NOT A BUTTON, and this used to fall through to a translation
        // that has no arm for it. Start enumerated once, so a pad plugged in after the picture
        // appeared was never opened - the run reported `armed True, 0 state(s) pushed`, which reads
        // as a pad nobody touched and was a pad that was never there.
        //
        // ON THIS THREAD BECAUSE IT IS ALREADY THIS THREAD. SdlThread's own note is that SDL's
        // Windows joystick backend hears about arrival on whichever thread called SDL_Init, and
        // this callback runs between two of that thread's polls - which is exactly where Start's
        // own open is posted to. So the arm costs no thread and no polling.
        if (Gamepads.IsControllerArrival(ev))
        {
            Adopt(ev.Which);
            return;
        }

        if (Gamepads.IsControllerDeparture(ev))
        {
            Release();
            return;
        }

        // PP845: THE TOUCHPAD AND THE SENSORS, which were ported and never called.
        //
        // Handled before the translation and not inside it, because these two write into the STATE
        // rather than into a scalar the translation holds. The console hands back a touch id when a
        // touch starts and has room for two, so the pool lives in the state object - there is
        // nothing for a WriteTo to defer, and deferring it would mean holding a shadow copy of a
        // pool whose ids the console issued.
        if (Gamepads.IsTouchpadEvent(ev))
        {
            if (!OfferTouch(ev))
                return;

            Interlocked.Increment(ref touched);
            Interlocked.Increment(ref pushed);
            return;
        }

        if (ev.Type == Gamepads.EventType.ControllerSensorUpdate)
        {
            if (!OfferSensor(Gamepads.Sensor(ev)))
                return;

            Interlocked.Increment(ref moved);
            Interlocked.Increment(ref pushed);
            return;
        }

        lock (gate)
        {
            if (!translation.Offer(ev))
                return;

            translation.WriteTo(state);
            sink.SetControllerState(state);
        }

        Interlocked.Increment(ref pushed);
    }

    /// <summary>
    /// One finger, through PP129's mapping of SDL's pair onto the console's two slots.
    ///
    /// Every arm can answer false and each means something: no room in the pool, a motion for a
    /// finger that never went down, a stray up. In all three the console is told nothing and
    /// nothing is pushed - which is controllermanager.cpp's own answer, and the reason its handler
    /// returns a bool.
    /// </summary>
    private bool OfferTouch(SdlEvent ev)
    {
        SdlTouch touch = Gamepads.Touch(ev);

        lock (gate)
        {
            bool changed = ev.Type switch
            {
                Gamepads.EventType.ControllerTouchpadDown =>
                    touches.Down(state, touch.Touchpad, touch.Finger, touch.X, touch.Y, extents),
                Gamepads.EventType.ControllerTouchpadMotion =>
                    touches.Motion(state, touch.Touchpad, touch.Finger, touch.X, touch.Y, extents),
                Gamepads.EventType.ControllerTouchpadUp =>
                    touches.Up(state, touch.Touchpad, touch.Finger),
                _ => false,
            };

            if (!changed)
                return false;

            // The buttons, triggers and sticks go with it. The state is one struct and the sink
            // takes the whole of it, so pushing a touch without them would send a frame in which
            // every button is whatever the state held before the last translation wrote it.
            translation.WriteTo(state);
            sink.SetControllerState(state);
        }

        return true;
    }

    /// <summary>
    /// One sensor reading, through PP130's fusion.
    ///
    /// A kind this port does not handle is dropped rather than fused - SDL_SENSOR_UNKNOWN is 0 and
    /// a pad may report others - and the orientation is applied to the state on every accepted
    /// reading, which is what the Qt client does and is why the filter's state is not read out here.
    /// </summary>
    private bool OfferSensor(SdlSensor sensor)
    {
        lock (gate)
        {
            switch (sensor.Kind)
            {
                case Gamepads.SensorKind.Accelerometer:
                    motion.Accelerometer(sensor.X, sensor.Y, sensor.Z, sensor.TimestampMs);
                    break;

                case Gamepads.SensorKind.Gyroscope:
                    motion.Gyroscope(sensor.X, sensor.Y, sensor.Z, sensor.TimestampMs);
                    break;

                default:
                    return false;
            }

            motion.ApplyTo(state);
            translation.WriteTo(state);
            sink.SetControllerState(state);
        }

        return true;
    }

    /// <summary>
    /// One arriving controller, taken if none is held.
    ///
    /// ONE PAD STAYS ONE PAD. PP701 chose the first controller deliberately and this does not
    /// reopen that: what changes is WHEN the first one is chosen. A second arriving while one is
    /// held is ignored, which is the same answer <see cref="Start"/>'s own break gives.
    /// </summary>
    /// <param name="index">The device index the arrival carries, which is what an open takes.</param>
    private void Adopt(int index)
    {
        if (controller != IntPtr.Zero)
            return;

        IntPtr opened = Gamepads.OpenController(index);
        if (opened == IntPtr.Zero)
            return;

        controller = opened;
        PadName = Gamepads.Pads().FirstOrDefault(one => one.Index == index).Name;

        // PP845: and its sensors, exactly as Start does. A pad plugged in mid-session is the case
        // PP838 was filed about, and a hotplug path that opened the pad but not its sensors would
        // be that finding one field along - PadFeed's own note calls that shape out.
        Sensors = Gamepads.EnableMotionSensors(opened);

        Interlocked.Increment(ref adopted);
    }

    /// <summary>
    /// A departure, which releases the handle only when it is OURS that went.
    ///
    /// The event carries the instance id of whatever left and this holds a handle, so the handle is
    /// asked instead: a second pad being unplugged must not close the one somebody is playing on.
    /// Released rather than left, because a handle to a device that is gone keeps
    /// <see cref="Adopt"/> from taking the pad that replaces it.
    /// </summary>
    private void Release()
    {
        if (controller == IntPtr.Zero || Gamepads.ControllerIsAttached(controller))
            return;

        Gamepads.CloseController(controller);
        controller = IntPtr.Zero;
        PadName = null;
        Sensors = [];

        // PP845: and the touches are FORGOTTEN rather than lifted. TouchpadTracker.Forget exists
        // for this exact case and says why: the state it would be stopping touches in belongs to a
        // pad that is no longer there, so what needs clearing is this side's memory of the pairs.
        // Without it the pool of two stays full and the pad that replaces this one has no slots.
        lock (gate)
        {
            touches.Forget();
        }
    }

    public void Dispose()
    {
        // The pad is closed on the thread that opened it, and BEFORE the pump stops - after it,
        // Invoke has nothing left to run on and the handle would leak with SDL still holding it.
        if (sdl is { Running: true } thread && controller != IntPtr.Zero)
        {
            thread.Invoke(
                () =>
                {
                    Gamepads.CloseController(controller);
                    controller = IntPtr.Zero;
                },
                TimeSpan.FromSeconds(5));
        }

        sdl?.Dispose();
        sdl = null;

        // PP845: the tracker holds two native allocations - the filter and the accelerometer zero -
        // and it is freed BEFORE the state, because nothing in the tracker points at the state:
        // ApplyTo takes it per call. The other order would work and this one reads as the reverse
        // of construction.
        motion.Dispose();
        state.Dispose();
    }
}
