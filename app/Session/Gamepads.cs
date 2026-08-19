using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

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
    /// PP117: inside this host SDL_Init of the controller subsystem does not return, on the
    /// dispatcher thread or on a dedicated one. Until that is understood, calling this is how a
    /// caller hangs, so it is not called by the selftest.
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
