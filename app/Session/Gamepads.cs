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
public readonly record struct SdlEvent(uint Type, uint Timestamp, int Which);

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

        ev = new SdlEvent(raw.Type, raw.Timestamp, raw.Which);
        return true;
    }

    /// <summary>
    /// Pushes an event onto SDL's own queue, which is how the pump is exercised on a machine with
    /// no controller plugged in. It is not a stub: the event goes through SDL and comes back out
    /// of SDL, so what it proves is the marshalling and the constants below, against the binary
    /// actually loaded rather than against the header this was written from.
    /// </summary>
    public static bool PushEvent(uint type, int which)
    {
        var raw = new SdlEventRaw { Type = type, Timestamp = 0, Which = which };
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

        public const uint JoyDeviceAdded = JoystickBase + 5;
        public const uint JoyDeviceRemoved = JoystickBase + 6;
        public const uint ControllerAxisMotion = ControllerBase + 0;
        public const uint ControllerButtonDown = ControllerBase + 1;
        public const uint ControllerButtonUp = ControllerBase + 2;
        public const uint ControllerDeviceAdded = ControllerBase + 3;
        public const uint ControllerDeviceRemoved = ControllerBase + 4;
        public const uint ControllerDeviceRemapped = ControllerBase + 5;
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
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct SdlEventRaw
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Timestamp;
        [FieldOffset(8)] public int Which;
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

    [DllImport(ChiakiNative.Sdl, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlGetError();
}
