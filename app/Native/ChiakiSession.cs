using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// ChiakiErrorCode, in declaration order because that is what the numbers are. Every value here is
/// what <see cref="ChiakiNative.ErrorString"/> already turns into a sentence; the enum exists so a
/// caller can branch on one without repeating the integer.
/// </summary>
public enum ChiakiError
{
    Success = 0,
    Unknown,
    ParseAddr,
    Thread,
    Memory,
    Overflow,
    Network,
    ConnectionRefused,
    HostDown,
    HostUnreach,
    Disconnected,
    InvalidData,
    BufTooSmall,
    MutexLocked,
    Canceled,
    Timeout,
    InvalidResponse,
    InvalidMac,
    Uninitialized,
    FecFailed,
    VersionMismatch,
    HttpNonOk,
}

/// <summary>The four resolutions libchiaki has a preset for. The values must not change.</summary>
public enum ChiakiVideoResolution { P360 = 1, P540 = 2, P720 = 3, P1080 = 4 }

/// <summary>30 or 60, spelled as the values libchiaki compares against.</summary>
public enum ChiakiVideoFps { Fps30 = 30, Fps60 = 60 }

/// <summary>What a preset resolved to, read back out of C rather than restated here.</summary>
public readonly record struct ChiakiVideoProfile(
    uint Width, uint Height, uint MaxFps, uint Bitrate, int Codec);

/// <summary>
/// PP4: the connect info, built in C and never marshalled.
///
/// ChiakiConnectInfo has sixteen members, two fixed-size byte arrays, a nested video profile and
/// two fields - the holepunch session and the rudp socket - whose own layouts a
/// <c>[StructLayout]</c> would drag in behind it. That attribute would be a standing promise about
/// MinGW's padding on every future libchiaki, kept by nothing and broken silently: the wrong
/// offsets still read as a plausible resolution and a key that merely fails to open a session.
///
/// So this class holds a handle and calls setters. Nothing here knows the struct has sixteen
/// fields, and a libchiaki that grows a seventeenth changes the shim rather than this file.
/// </summary>
public sealed class ChiakiConnectInfo : IDisposable
{
    private IntPtr _handle;

    public ChiakiConnectInfo()
    {
        _handle = ConnectInfoCreate();
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_connect_info_create returned null.");
    }

    internal IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(ChiakiConnectInfo));

    /// <summary>The console's address. Copied into C, so this string need not be kept alive.</summary>
    public string Host
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ConnectInfoSetHost(Handle, value))
                throw new OutOfMemoryException("chiaki_shim_connect_info_set_host failed.");
        }
    }

    /// <summary>PS5 or PS4, which is the whole of what picks the target the session negotiates.</summary>
    public bool Ps5 { set => ConnectInfoSetPs5(Handle, value); }

    /// <summary>settings/packet_loss_max, whose 0.05 default is already declared on this side.</summary>
    public double PacketLossMax { set => ConnectInfoSetPacketLossMax(Handle, value); }

    /// <summary>
    /// The registration key, zero-padded into its 16 bytes. A key that does not fit is refused
    /// here rather than truncated: the field it would overrun is <c>morning</c>, and a session
    /// built on both of them wrong fails at a handshake step that names neither.
    /// </summary>
    public void SetRegistKey(ReadOnlySpan<byte> key)
    {
        if (!ConnectInfoSetRegistKey(Handle, key.ToArray(), key.Length))
            throw new ArgumentException($"a regist key of {key.Length} bytes does not fit in 16.", nameof(key));
    }

    /// <summary>The morning key, which is exactly 16 bytes and is refused at any other length.</summary>
    public void SetMorning(ReadOnlySpan<byte> morning)
    {
        if (!ConnectInfoSetMorning(Handle, morning.ToArray(), morning.Length))
            throw new ArgumentException($"morning is 16 bytes, not {morning.Length}.", nameof(morning));
    }

    /// <summary>
    /// chiaki_connect_video_profile_preset. The bitrate each preset carries is the part not worth
    /// re-deriving: 15000 for 1080p lives in one switch in session.c, and a copy in C# would be a
    /// second number nothing compares against the first.
    /// </summary>
    public void SetVideoPreset(ChiakiVideoResolution resolution, ChiakiVideoFps fps)
        => ConnectInfoSetVideoPreset(Handle, (int)resolution, (int)fps);

    /// <summary>What the preset resolved to, read back out of C.</summary>
    public ChiakiVideoProfile VideoProfile
    {
        get
        {
            ConnectInfoVideoProfile(Handle, out uint w, out uint h, out uint fps, out uint bitrate, out int codec);
            return new ChiakiVideoProfile(w, h, fps, bitrate, codec);
        }
    }

    /// <summary>Set together because chiaki_session_init reads them together.</summary>
    public void SetFlags(bool autoDowngrade, bool keyboard, bool dualSense, bool idrOnFecFailure)
        => ConnectInfoSetFlags(Handle, autoDowngrade, keyboard, dualSense, idrOnFecFailure);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        ConnectInfoFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ConnectInfoCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoFree(IntPtr info);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_host",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ConnectInfoSetHost(
        IntPtr info, [MarshalAs(UnmanagedType.LPUTF8Str)] string host);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_ps5",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetPs5(IntPtr info, [MarshalAs(UnmanagedType.I1)] bool ps5);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_regist_key",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ConnectInfoSetRegistKey(IntPtr info, byte[] key, int len);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_morning",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ConnectInfoSetMorning(IntPtr info, byte[] morning, int len);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_video_preset",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetVideoPreset(IntPtr info, int resolution, int fps);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_video_profile",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoVideoProfile(
        IntPtr info, out uint width, out uint height, out uint maxFps, out uint bitrate, out int codec);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_flags",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetFlags(
        IntPtr info,
        [MarshalAs(UnmanagedType.I1)] bool autoDowngrade,
        [MarshalAs(UnmanagedType.I1)] bool keyboard,
        [MarshalAs(UnmanagedType.I1)] bool dualSense,
        [MarshalAs(UnmanagedType.I1)] bool idrOnFecFailure);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_packet_loss_max",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetPacketLossMax(IntPtr info, double packetLossMax);
}

/// <summary>
/// PP4: the session's two ends, which is as far as a lifecycle goes before there is a console.
///
/// chiaki_session_init is reachable with nothing on the network: it resolves the host, builds the
/// ctrl and the stream connection, and starts no thread. So the construction of a session - the
/// part every screen above eventually depends on - is assertable on a build machine, and only
/// <c>Start</c> is not. That boundary is why this class exists before any of it streams.
///
/// The log is not owned here. libchiaki keeps the pointer for the session's whole life, so a
/// <see cref="ChiakiLog"/> disposed first leaves the session logging into freed memory - the one
/// ownership rule at this seam that the managed side cannot check for itself, and therefore the
/// one worth stating twice.
/// </summary>
public sealed class ChiakiSession : IDisposable
{
    private IntPtr _handle;

    private ChiakiSession(IntPtr handle) => _handle = handle;

    /// <summary>
    /// chiaki_lib_init: seeds rand, builds jerasure's Galois field and calls WSAStartup. Nothing
    /// managed had called it, and without it every socket libchiaki opens fails with
    /// WSANOTINITIALISED. Idempotent, so callers need not coordinate.
    /// </summary>
    public static ChiakiError LibInit() => (ChiakiError)LibInitRaw();

    /// <summary>
    /// Builds a session, or returns null with <paramref name="error"/> saying why - most often
    /// <see cref="ChiakiError.ParseAddr"/> for a host that does not resolve.
    /// </summary>
    public static ChiakiSession? TryCreate(ChiakiConnectInfo connectInfo, ChiakiLog? log, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(connectInfo);

        IntPtr handle = SessionCreate(connectInfo.Handle, log?.Handle ?? IntPtr.Zero, out int err);
        error = (ChiakiError)err;
        return handle == IntPtr.Zero ? null : new ChiakiSession(handle);
    }

    /// <summary>The opaque native session, which is what Start and Stop will be handed next.</summary>
    public IntPtr Handle => _handle;

    /// <summary>chiaki_quit_reason_string: the sentence a disconnect screen shows.</summary>
    public static string? QuitReasonString(int reason)
        => Marshal.PtrToStringUTF8(QuitReasonStringPtr(reason));

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        SessionFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_lib_init",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int LibInitRaw();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SessionCreate(IntPtr connectInfo, IntPtr log, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void SessionFree(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_quit_reason_string",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QuitReasonStringPtr(int reason);
}
