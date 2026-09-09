using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// One SDL event, as much of it as the device slice reads: the type, when SDL stamped it, and the
/// index or instance id it is about.
/// </summary>
/// <param name="Type">One of <see cref="Gamepads.EventType"/>'s values.</param>
/// <param name="Timestamp">SDL_GetTicks at the time SDL queued it.</param>
/// <param name="Which">
/// A device INDEX on an added event and an INSTANCE ID on a removed one. SDL documents that
/// asymmetry and it is not a mistake to be tidied away here: the two numbering schemes are
/// different, and a port that treated them alike would close the wrong controller.
///
/// On a joystick or controller event this is always SDL's own number and never a caller's. SDL
/// resolves the field against its device tables on the way through the queue, which is measured
/// rather than assumed - a pushed event carrying 0x5afe comes back carrying -1.
/// </param>
/// <param name="Index">
/// PP126: the axis, button or hat index, at offset 12 in all three joystick events - SDL_JoyAxis
/// Event.axis, SDL_JoyButtonEvent.button and SDL_JoyHatEvent.hat share that position. Zero for
/// every other event type, where the byte is padding or something else entirely, so a caller
/// reads it only after checking Type.
/// </param>
/// <param name="Value">
/// The hat's position, at offset 13 - the one field that is not shared: a button event has its
/// state there and an axis event a padding byte. Meaningful only for SDL_JOYHATMOTION.
/// </param>
/// <param name="AxisValue">
/// PP220: SDL_JoyAxisEvent.value, at offset 16 and sixteen bits SIGNED, across the full
/// -32768..32767 the header names. Meaningful only for SDL_JOYAXISMOTION.
///
/// Not read by the capture, which binds an axis on any motion whatever its value - so this exists
/// for the one question that cannot be answered without it: SDL raises an axis event whenever the
/// value CHANGES, including by one, so a stream of them says the stick is not still and says
/// nothing at all about whether it is off centre. Noise and drift look identical until something
/// prints the number.
/// </param>
/// <param name="Wide">
/// PP845: the Sint32 at offset 12, which is the touchpad index on a touchpad event and the sensor
/// kind on a sensor one. It shares those four bytes with <paramref name="Index"/> and
/// <paramref name="Value"/>, so it is meaningful only for the four events PP845 added and reading
/// it otherwise reads a byte of something else widened by three.
/// </param>
/// <param name="Finger">SDL_ControllerTouchpadEvent.finger, at 16. Touchpad events only.</param>
/// <param name="Data0">
/// The float at 16: a sensor's first reading, and the same bytes as <paramref name="Finger"/> and
/// <paramref name="AxisValue"/>. Sensor events only.
/// </param>
/// <param name="Data1">The float at 20: a sensor's second reading, or a touch's x.</param>
/// <param name="Data2">The float at 24: a sensor's third reading, or a touch's y.</param>
public readonly record struct SdlEvent(
    uint Type, uint Timestamp, int Which, byte Index, byte Value, short AxisValue = 0,
    int Wide = 0, int Finger = 0, float Data0 = 0, float Data1 = 0, float Data2 = 0);

/// <summary>
/// PP845: one touch, with the members SDL_ControllerTouchpadEvent actually declares.
///
/// A record rather than five arguments off <see cref="SdlEvent"/>, because the point is that x and
/// y come from the fields a SENSOR event calls its second and third readings - so a caller that
/// took them off the event directly would be one type check away from scaling a gyroscope into a
/// finger position.
/// </summary>
/// <param name="Touchpad">The pad on the device. A DualSense has one; the pair is what keys a touch.</param>
/// <param name="Finger">The finger on that pad.</param>
/// <param name="X">Normalised 0..1, left to right.</param>
/// <param name="Y">Normalised 0..1, top to bottom.</param>
public readonly record struct SdlTouch(int Touchpad, int Finger, float X, float Y);

/// <summary>
/// PP845: one sensor reading, with the members SDL_ControllerSensorEvent declares.
/// </summary>
/// <param name="Kind">One of <see cref="Gamepads.SensorKind"/>'s values.</param>
/// <param name="X">data[0].</param>
/// <param name="Y">data[1].</param>
/// <param name="Z">data[2].</param>
/// <param name="TimestampMs">
/// SDL's own millisecond stamp, which the tracker wants in microseconds. Carried as SDL gives it
/// and converted by <see cref="MotionTracker.ToMicroseconds"/>, which is where PP130 put the
/// factor of a thousand rather than at the call site.
/// </param>
public readonly record struct SdlSensor(int Kind, float X, float Y, float Z, uint TimestampMs);

/// <summary>
/// PP218: one attached pad, as much of it as the mapping screen needs.
/// </summary>
/// <param name="Index">
/// SDL's device INDEX, which is a position in a list that shifts when anything is unplugged. Good
/// for opening the device now and worthless for remembering it - PP128's roster keys on the
/// instance id for that reason, and this record is not a roster.
/// </param>
/// <param name="Name">What the pad calls itself. The screen's title, and the middle of its prompt.</param>
/// <param name="Mapping">
/// The SDL mapping string. Not a description of the document the screen edits - it IS that
/// document, the same text <see cref="ControllerMappingDocument.Parse"/> takes.
/// </param>
public readonly record struct SdlPad(int Index, string Name, string Mapping);

/// <summary>
/// PP8: SDL's game controller subsystem, driven from managed code.
///
/// SDL is not Qt and does not have to move - so it does not, and the port calls it directly rather
/// than through the shim. What has to move is how the events arrive: SDL's own loop against a WPF
/// dispatcher, on a thread that does not stall on rendering.
///
/// This is the part of that which can be settled before a pad is plugged in, and it is not the
/// small part. Four hints are set before SDL_Init, and each is a decision a rewrite drops by
/// omission rather than by disagreement:
///
///   - the two HIDAPI rumble hints, without which a DualSense connected over USB does not rumble;
///   - background events, without which the pad stops working the moment the window loses focus -
///     which for a client people play full-screen is most of the time it is used;
///   - the Steam Deck hint, which stops SDL claiming a Deck's own controls as a second pad.
///
/// None of those fails loudly. A port that misses one has a gamepad that is subtly less useful and
/// nothing anywhere saying why, so they are asserted by reading them back and held against the Qt
/// client's own copy.
/// </summary>
public static class Gamepads
{
    /// <summary>SDL_INIT_GAMECONTROLLER.</summary>
    public const uint InitGameController = 0x00002000;

    /// <summary>The hints the Qt client sets before SDL_Init, with the values it sets them to.</summary>
    public static IReadOnlyList<(string Name, string Value)> Hints { get; } =
    [
        ("SDL_JOYSTICK_HIDAPI_PS4_RUMBLE", "1"),
        ("SDL_JOYSTICK_HIDAPI_PS5_RUMBLE", "1"),
        ("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1"),
        ("SDL_JOYSTICK_HIDAPI_STEAMDECK", "0"),
    ];

    /// <summary>
    /// SDL_GAMECONTROLLER_USE_BUTTON_LABELS, set to 0 when the user asked for buttons by position.
    /// It is separate because it is a setting rather than a start-up decision.
    /// </summary>
    public const string ButtonLabelsHint = "SDL_GAMECONTROLLER_USE_BUTTON_LABELS";

    /// <summary>Sets one hint. They have to be set before <see cref="Start"/> reads past them.</summary>
    public static bool SetHint(string name, string value) => SdlSetHint(name, value);

    /// <summary>
    /// Sets the hints and starts the subsystem, in that order - a hint set afterwards is a hint
    /// SDL has already read past.
    ///
    /// Called from <see cref="SdlThread"/> and not directly. SDL's Windows joystick backend hangs
    /// its device-arrival notifications off a message-only window belonging to whichever thread
    /// called SDL_Init, so this decides which thread hotplug works on.
    /// </summary>
    public static bool Start()
    {
        // SDL_SetMainReady, because this process did not come in through SDL's own main.
        SdlSetMainReady();

        foreach ((string name, string value) in Hints)
            SdlSetHint(name, value);

        return SdlInit(InitGameController) == 0;
    }

    public static void Stop() => SdlQuit();

    /// <summary>Which subsystems are up, masked with what was asked for.</summary>
    public static uint WasInit(uint flags) => SdlWasInit(flags);

    /// <summary>What a hint currently reads as, or null when it has never been set.</summary>
    public static string? GetHint(string name) => Marshal.PtrToStringUTF8(SdlGetHint(name));

    /// <summary>How many joysticks SDL can see. Zero is an ordinary answer on a machine with none.</summary>
    public static int NumJoysticks() => SdlNumJoysticks();

    /// <summary>Whether SDL has a game controller mapping for the device at this index.</summary>
    public static bool IsGameController(int index) => SdlIsGameController(index) != 0;

    /// <summary>
    /// PP219: opens the device, WITHOUT WHICH NO BUTTON EVER ARRIVES.
    ///
    /// Measured, on a DualSense over USB: with the pump running and the device enumerable, pressing
    /// buttons for twenty seconds produced nothing at all. SDL queues the arrival of a device
    /// whether or not anyone opened it, and queues that device's INPUT only once somebody has - so
    /// a port with the thread, the pump and the device list still has a pad that does nothing, and
    /// no error anywhere saying why. controllermanager.cpp opens on construction, which is why the
    /// Qt client never meets this.
    ///
    /// The handle is kept for as long as the events are wanted: closing it stops them again.
    /// </summary>
    /// <returns>The controller handle, or <see cref="IntPtr.Zero"/> where SDL refused.</returns>
    public static IntPtr OpenController(int index) => SdlControllerOpen(index);

    /// <summary>Closes a handle from <see cref="OpenController"/>, and with it the event flow.</summary>
    public static void CloseController(IntPtr controller)
    {
        if (controller != IntPtr.Zero)
            SdlControllerClose(controller);
    }

    /// <summary>
    /// PP838: whether this event is a controller ARRIVING, which is not a button.
    ///
    /// Asked here rather than by exposing the type numbers, because the numbers are SDL's and the
    /// question is the caller's - and the two device events are the only ones anything outside this
    /// file has ever needed to tell apart from input.
    ///
    /// <c>Which</c> is a DEVICE INDEX on this one, which is what <see cref="OpenController"/>
    /// takes. On the removal below it is an instance id instead, which is why that one is answered
    /// by asking the handle rather than by comparing numbers.
    /// </summary>
    public static bool IsControllerArrival(SdlEvent ev) => ev.Type == EventType.ControllerDeviceAdded;

    /// <summary>Whether it is a controller LEAVING.</summary>
    public static bool IsControllerDeparture(SdlEvent ev)
        => ev.Type == EventType.ControllerDeviceRemoved;

    /// <summary>
    /// Whether an opened handle still has a device behind it.
    ///
    /// SDL_CONTROLLERDEVICEREMOVED carries the instance id of whatever left, and a port holding one
    /// handle has no instance id to compare it against without two more calls. Asking the handle is
    /// the same answer in one call, and it stays right when the removal is of some other pad.
    /// </summary>
    public static bool ControllerIsAttached(IntPtr controller)
        => controller != IntPtr.Zero && SdlControllerGetAttached(controller);

    /// <summary>
    /// PP221: where an axis is NOW, with nothing having to move first.
    ///
    /// The question no event can answer. SDL raises an axis event when the value CHANGES, so a
    /// stick resting off centre and still emits once on arrival and then nothing at all - and a
    /// port watching the stream cannot tell that from a stick sitting dead centre. This asks.
    ///
    /// TWO RANGES, and the header is explicit about it. A thumbstick reads across the full signed
    /// span and is documented as centred "within ~8000 of zero" - a quarter of full scale - with
    /// the dead zone left to the application, so a resting stick is not expected to read zero. A
    /// TRIGGER read through here runs 0 to maximum instead, which is NOT the range the joystick
    /// layer reports for the same trigger, and the joystick layer is the one
    /// <see cref="SdlEvent.AxisValue"/> carries. The same trigger has two numbers depending on
    /// which call asked.
    /// </summary>
    /// <param name="controller">A handle from <see cref="OpenController"/>.</param>
    /// <param name="axis">One of <see cref="ControllerAxis"/>'s values.</param>
    public static short AxisNow(IntPtr controller, int axis)
        => controller == IntPtr.Zero ? (short)0 : SdlControllerGetAxis(controller, axis);

    /// <summary>
    /// PP221: the axes SDL_GameControllerGetAxis takes, as SDL_gamecontroller.h orders them.
    ///
    /// Spelled out because the enum starts at INVALID = -1 and the six that follow take their
    /// values from that: a port that numbered them from zero as written would read every axis one
    /// place along and report the left stick's Y where its X was asked for.
    /// </summary>
    public static class ControllerAxis
    {
        public const int LeftX = 0;
        public const int LeftY = 1;
        public const int RightX = 2;
        public const int RightY = 3;
        public const int TriggerLeft = 4;
        public const int TriggerRight = 5;

        /// <summary>Every axis, with the name the header gives it.</summary>
        public static IReadOnlyList<(int Axis, string Name)> All { get; } =
        [
            (LeftX, "left stick X"),
            (LeftY, "left stick Y"),
            (RightX, "right stick X"),
            (RightY, "right stick Y"),
            (TriggerLeft, "L2"),
            (TriggerRight, "R2"),
        ];

        /// <summary>Whether an axis is a TRIGGER, whose range through this call starts at zero.</summary>
        public static bool IsTrigger(int axis) => axis is TriggerLeft or TriggerRight;
    }

    /// <summary>
    /// What the device calls itself, or the empty string.
    ///
    /// SDL OWNS this pointer - the header says <c>const char *</c> - so it is read and left alone.
    /// See <see cref="MappingForDeviceIndex"/>, which is the call beside it and the opposite.
    /// </summary>
    public static string NameForIndex(int index)
        => Marshal.PtrToStringUTF8(SdlNameForIndex(index)) ?? "";

    /// <summary>
    /// The device's SDL mapping string - which IS the document the mapping screen edits - or null
    /// where SDL has none for it.
    ///
    /// THE CALLER OWNS THIS ONE. Its own line in SDL_gamecontroller.h says "Must be freed with
    /// SDL_free()", where the name beside it must not be. From managed code the two are
    /// indistinguishable - both are an IntPtr through PtrToStringUTF8 - so the free is here, in a
    /// finally, rather than at whichever call site remembers.
    /// </summary>
    public static string? MappingForDeviceIndex(int index)
    {
        IntPtr owned = SdlMappingForDeviceIndex(index);
        if (owned == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(owned);
        }
        finally
        {
            SdlFree(owned);
        }
    }

    /// <summary>
    /// Every attached device SDL can map, with what the mapping screen needs of it.
    ///
    /// ON THE SDL THREAD, like everything else here: the device tables belong to whichever thread
    /// called SDL_Init, which is the same reason <see cref="Start"/> says so. <see cref="SdlThread"/>
    /// .Invoke is the path from anywhere else.
    ///
    /// A device SDL cannot map is skipped rather than reported without a mapping: the screen has
    /// nothing to draw for one, and an empty list on a machine with no pad is an ordinary answer.
    /// </summary>
    public static IReadOnlyList<SdlPad> Pads()
    {
        var pads = new List<SdlPad>();

        int count = NumJoysticks();
        for (int index = 0; index < count; index++)
        {
            if (!IsGameController(index))
                continue;

            string? mapping = MappingForDeviceIndex(index);
            if (mapping is null)
                continue;

            pads.Add(new SdlPad(index, NameForIndex(index), mapping));
        }

        return pads;
    }

    /// <summary>SDL's own version, so a hint that only exists past a version can be judged.</summary>
    public static Version LinkedVersion()
    {
        SdlGetVersion(out SdlVersion v);
        return new Version(v.Major, v.Minor, v.Patch);
    }

    /// <summary>The last error SDL recorded, which is empty rather than null when there is none.</summary>
    public static string Error() => Marshal.PtrToStringUTF8(SdlGetError()) ?? "";

    /// <summary>
    /// One event, or false when the queue is empty. Called only from <see cref="SdlThread"/>,
    /// because the queue belongs to the thread that initialised the subsystem.
    /// </summary>
    public static bool PollEvent(out SdlEvent ev)
    {
        if (SdlPollEvent(out SdlEventRaw raw) == 0)
        {
            ev = default;
            return false;
        }

        ev = new SdlEvent(
            raw.Type, raw.Timestamp, raw.Which, raw.Index, raw.Value, raw.AxisValue,
            raw.Wide, raw.Finger, raw.Data0, raw.Data1, raw.Data2);
        return true;
    }

    /// <summary>Whether an event is one of the three a finger on the touchpad arrives as.</summary>
    public static bool IsTouchpadEvent(SdlEvent ev)
        => ev.Type is EventType.ControllerTouchpadDown
            or EventType.ControllerTouchpadMotion
            or EventType.ControllerTouchpadUp;

    /// <summary>
    /// The touch an event carries. Meaningful only where <see cref="IsTouchpadEvent"/> holds.
    ///
    /// x and y are the fields a sensor event calls data[1] and data[2], which is why this exists
    /// rather than the caller reading them off the event.
    /// </summary>
    public static SdlTouch Touch(SdlEvent ev) => new(ev.Wide, ev.Finger, ev.Data1, ev.Data2);

    /// <summary>The sensor reading an event carries, for SDL_CONTROLLERSENSORUPDATE.</summary>
    public static SdlSensor Sensor(SdlEvent ev)
        => new(ev.Wide, ev.Data0, ev.Data1, ev.Data2, ev.Timestamp);

    /// <summary>
    /// Turns on whichever of the two sensors a pad has, and reports which.
    ///
    /// ASKED BEFORE IT IS SET, which is the Qt client's order and not a politeness: enabling a
    /// sensor a device does not have is an SDL error, and a port that ignored the answer would take
    /// an error return on every pad without motion - which is most pads that are not a DualSense.
    ///
    /// Called on the SDL thread, like every other controller call in this file.
    /// </summary>
    /// <returns>The kinds now enabled, which is empty for a pad with neither.</returns>
    public static IReadOnlyList<int> EnableMotionSensors(IntPtr controller)
    {
        if (controller == IntPtr.Zero)
            return [];

        var enabled = new List<int>();

        foreach (int kind in (int[])[SensorKind.Accelerometer, SensorKind.Gyroscope])
        {
            if (!SdlControllerHasSensor(controller, kind))
                continue;

            if (SdlControllerSetSensorEnabled(controller, kind, true) == 0)
                enabled.Add(kind);
        }

        return enabled;
    }

    /// <summary>
    /// Pushes an event onto SDL's own queue, which is how the pump is exercised on a machine with
    /// no controller plugged in. It is not a stub: the event goes through SDL and comes back out
    /// of SDL, so what it proves is the marshalling and the constants below, against the binary
    /// actually loaded rather than against the header this was written from.
    /// </summary>
    public static bool PushEvent(uint type, int which, byte index = 0, byte value = 0)
    {
        var raw = new SdlEventRaw
        {
            Type = type, Timestamp = 0, Which = which, Index = index, Value = value,
        };
        return SdlPushEvent(ref raw) == 1;
    }

    /// <summary>
    /// PP845: the same, for an event whose members are past the head.
    ///
    /// The touchpad's and the sensor's fields are the whole reason this overload exists. The offsets
    /// in <c>SdlEventRaw</c> are a promise about a C union, kept by nothing on this side - and a
    /// wrong one does not fail, it reads a finger index out of a float or a gyroscope reading out of
    /// a touch position. Pushing one through SDL and reading it back out is the only check available
    /// on a machine with no pad, and it is a check against the BINARY rather than the header.
    ///
    /// <paramref name="wide"/> is the Sint32 at 12 - the touchpad index or the sensor kind - and
    /// <paramref name="finger"/> the Sint32 at 16, which shares its bytes with
    /// <paramref name="data0"/>. So a caller sets one or the other and never both, exactly as the
    /// union means.
    /// </summary>
    public static bool PushEvent(
        uint type, int which, int wide, int finger = 0,
        float data0 = 0, float data1 = 0, float data2 = 0)
    {
        var raw = new SdlEventRaw { Type = type, Timestamp = 0, Which = which, Wide = wide };

        if (finger != 0)
            raw.Finger = finger;
        else
            raw.Data0 = data0;

        raw.Data1 = data1;
        raw.Data2 = data2;

        return SdlPushEvent(ref raw) == 1;
    }

    /// <summary>
    /// SDL_USEREVENT. Not an event this port sends - it is the one SDL queues without interpreting,
    /// which makes it the only way to prove the marshalling below without a controller plugged in.
    /// Every joystick and controller event has its `which` resolved against SDL's own device
    /// tables on the way through, so a pushed one carries the port's value nowhere.
    /// </summary>
    public const uint UserEvent = 0x8000;

    /// <summary>
    /// The event types this port acts on, as SDL numbers them. Spelled as their base plus an
    /// offset because that is how SDL_events.h derives them - only the two bases are literals
    /// there, and a value written out flat is one that cannot be checked against the header.
    ///
    /// Which is why <see cref="PushEvent"/> exists: these round-trip through SDL, so a value that
    /// disagrees with the loaded binary is a failed assertion rather than an event silently
    /// filed under the wrong name.
    /// </summary>
    public static class EventType
    {
        private const uint JoystickBase = 0x600;
        private const uint ControllerBase = 0x650;

        public const uint JoyAxisMotion = JoystickBase + 0;
        public const uint JoyHatMotion = JoystickBase + 2;
        public const uint JoyButtonDown = JoystickBase + 3;
        public const uint JoyButtonUp = JoystickBase + 4;
        public const uint JoyDeviceAdded = JoystickBase + 5;
        public const uint JoyDeviceRemoved = JoystickBase + 6;
        public const uint ControllerAxisMotion = ControllerBase + 0;
        public const uint ControllerButtonDown = ControllerBase + 1;
        public const uint ControllerButtonUp = ControllerBase + 2;
        public const uint ControllerDeviceAdded = ControllerBase + 3;
        public const uint ControllerDeviceRemoved = ControllerBase + 4;
        public const uint ControllerDeviceRemapped = ControllerBase + 5;

        // PP845: the four a pad's other two halves arrive as. Derived from the same base for the
        // same reason as the six above, and SDL_CONTROLLERSTEAMHANDLEUPDATED sits after these -
        // so the run is contiguous and a value here is checkable against the header's own order.
        public const uint ControllerTouchpadDown = ControllerBase + 6;
        public const uint ControllerTouchpadMotion = ControllerBase + 7;
        public const uint ControllerTouchpadUp = ControllerBase + 8;
        public const uint ControllerSensorUpdate = ControllerBase + 9;
    }

    /// <summary>
    /// SDL_SensorType, as far as a pad reports. INVALID is -1 and UNKNOWN is 0, so a kind this
    /// port does not handle is any other value rather than a missing case.
    /// </summary>
    public static class SensorKind
    {
        public const int Accelerometer = 1;
        public const int Gyroscope = 2;
    }

    /// <summary>
    /// SDL_Event as far as this port reads it. 56 bytes on x64 - SDL asserts that size against its
    /// own union at compile time, and getting it wrong here is a queue read off by whole events
    /// rather than a compiler error.
    ///
    /// Only the head is named. type and timestamp are common to every member of the union, and
    /// which sits at 8 in both SDL_JoyDeviceEvent and SDL_ControllerDeviceEvent - which is what
    /// makes one struct enough for the device events this slice handles. A member that needs more
    /// than the head gets read through its own overlay, not by widening this.
    /// </summary>
    // PP579: the size is SdlPadSource.EventSize, which a check holds against SDL's own header. It
    // was a literal here, and the note above says what a wrong one costs.
    [StructLayout(LayoutKind.Explicit, Size = SdlPadSource.EventSize)]
    internal struct SdlEventRaw
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Timestamp;
        [FieldOffset(8)] public int Which;

        // PP126. All three joystick events put their index at 12 - SDL_JoyAxisEvent.axis,
        // SDL_JoyButtonEvent.button, SDL_JoyHatEvent.hat - which is what lets one overlay read
        // any of them. 13 is NOT shared: it is the hat's value, a button's state, and an axis's
        // padding, so only SDL_JOYHATMOTION may read it.
        [FieldOffset(12)] public byte Index;
        [FieldOffset(13)] public byte Value;

        // PP220. SDL_JoyAxisEvent.value, and the header is explicit that it is Sint16 over
        // -32768..32767 - so it is read signed. Unsigned here would turn every leftward or
        // downward reading into a number near 65535 and make a centred stick look pinned.
        [FieldOffset(16)] public short AxisValue;

        // PP845: the touchpad's and the sensor's members, which ALIAS the three above and each
        // other. That is what a union is, and the note at the top of this struct says a member
        // needing more than the head gets its own overlay - these are that overlay, in this struct
        // rather than beside it because SDL_PollEvent takes one by reference.
        //
        // WHICH FIELD MEANS WHAT DEPENDS ON THE TYPE, and nothing here can enforce that: 12 is the
        // touchpad index or the sensor kind, and 16 is the finger, an axis value, or the first
        // sensor reading. So the fields are read through Touch and Sensor below, each of which
        // takes only the members its own event declares, and no caller reads this struct.
        //
        //   SDL_ControllerTouchpadEvent: touchpad 12, finger 16, x 20, y 24, pressure 28
        //   SDL_ControllerSensorEvent:   sensor 12, data[3] 16, 20, 24
        //
        // Pressure is deliberately absent: the Qt client's HandleTouchpadEvent ignores it, and a
        // field this port reads and never sends is a promise about a layout for no reason.
        [FieldOffset(12)] public int Wide;
        [FieldOffset(16)] public int Finger;
        [FieldOffset(16)] public float Data0;
        [FieldOffset(20)] public float Data1;
        [FieldOffset(24)] public float Data2;
    }

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_PollEvent", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlPollEvent(out SdlEventRaw ev);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_PushEvent", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlPushEvent(ref SdlEventRaw ev);

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion
    {
        public byte Major;
        public byte Minor;
        public byte Patch;
    }

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_SetMainReady", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlSetMainReady();

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SdlSetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GetHint", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlGetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlInit(uint flags);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_Quit", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlQuit();

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_WasInit", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SdlWasInit(uint flags);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_NumJoysticks", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlNumJoysticks();

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GetVersion", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlGetVersion(out SdlVersion version);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_IsGameController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlIsGameController(int index);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerOpen",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlControllerOpen(int index);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerClose",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlControllerClose(IntPtr controller);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerGetAttached",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SdlControllerGetAttached(IntPtr controller);

    // PP845: SDL_bool and SDL_SensorType, which are an int and an enum over int - so both cross as
    // int rather than through a marshalled bool, and only the return is told it is one byte.
    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerHasSensor",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SdlControllerHasSensor(IntPtr controller, int sensorType);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerSetSensorEnabled",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlControllerSetSensorEnabled(
        IntPtr controller, int sensorType, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerGetAxis",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern short SdlControllerGetAxis(IntPtr controller, int axis);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerNameForIndex",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlNameForIndex(int index);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GameControllerMappingForDeviceIndex",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlMappingForDeviceIndex(int index);

    /// <summary>SDL's own free, which is the only one that may release the mapping string above.</summary>
    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlFree(IntPtr memory);

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlGetError();
}
