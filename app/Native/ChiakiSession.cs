using System.Runtime.CompilerServices;
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

    /// <summary>
    /// The bitrate override Settings applies on top of the preset when the stored one is not zero.
    /// </summary>
    public uint Bitrate { set => ConnectInfoSetBitrate(Handle, value); }

    /// <summary>
    /// The codec, which is not optional: chiaki_connect_video_profile_preset writes H264 into
    /// every preset it fills, so a caller that stopped at the preset streams H264 on a PS5 whose
    /// default is H265 - a working stream at the wrong codec that nothing reports.
    /// </summary>
    public int Codec { set => ConnectInfoSetCodec(Handle, value); }

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

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_bitrate",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetBitrate(IntPtr info, uint bitrate);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_connect_info_set_codec",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectInfoSetCodec(IntPtr info, int codec);

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
/// ChiakiEventType, in declaration order. Only <see cref="Quit"/> carries a decoded payload so
/// far; the rest arrive as a type until the screen that reads their payload exists.
/// </summary>
public enum ChiakiEventType
{
    Connected = 0,
    LoginPinRequest,
    Holepunch,
    Regist,
    NicknameReceived,
    KeyboardOpen,
    KeyboardTextChange,
    KeyboardRemoteClose,
    Rumble,
    Quit,
    TriggerEffects,
    MotionReset,
    LedColor,
    PlayerIndex,
    HapticIntensity,
    TriggerIntensity,
    VideoFecFailure,
}

/// <summary>
/// One event off the session thread.
///
/// <c>QuitReasonString</c> is not the message to show. libchiaki fills it only from a disconnect
/// reason the console itself sent, so it is null on every failure that never reached one - which
/// includes the commonest of them all, a console that is switched off. The sentence a screen
/// shows is <see cref="ChiakiSession.QuitReasonString(int)"/> of the reason, with this appended
/// when it is there; that is what qmlbackend.cpp's own dialog does. It is copied here because
/// libchiaki's pointer dies when the callback returns.
/// </summary>
public readonly record struct ChiakiSessionEvent(
    ChiakiEventType Type, ChiakiQuitReason QuitReason, string? QuitReasonString);

/// <summary>ChiakiQuitReason, in declaration order.</summary>
public enum ChiakiQuitReason
{
    None = 0,
    Stopped,
    SessionRequestUnknown,
    SessionRequestConnectionRefused,
    SessionRequestRpInUse,
    SessionRequestRpCrash,
    SessionRequestRpVersionMismatch,
    CtrlUnknown,
    CtrlConnectFailed,
    CtrlConnectionRefused,
    StreamConnectionUnknown,
    StreamConnectionRemoteDisconnected,
    StreamConnectionRemoteShutdown,
    PsnRegistFailed,

    /// <summary>
    /// PP345: a Login PIN that could not be handed to ctrl. Last, because these marshal by ordinal
    /// and libchiaki appends for the same reason.
    /// </summary>
    CtrlMemory,
}

/// <summary>
/// PP4: the session lifecycle, as far as it goes without a console answering.
///
/// chiaki_session_init needs nothing on the network - it resolves the host, builds the ctrl and
/// the stream connection, and starts no thread - and chiaki_session_start needs only a thread. So
/// the whole shape is exercisable on a build machine: start against an address nothing answers on
/// and the session reports its own failure through the event callback, which is the same path a
/// real disconnect takes.
///
/// The event callback is the second of libchiaki's 22 and the one every screen above the stream
/// is driven by. It differs from the log's in one way that matters: it arrives on the session
/// thread, which is a thread the CLR never created. The runtime attaches such a thread on the way
/// in, so the [UnmanagedCallersOnly] thunk and the GCHandle work there unchanged - asserted rather
/// than assumed, because "it worked from the calling thread" says nothing about this one.
///
/// The log is not owned here. libchiaki keeps the pointer for the session's whole life, so a
/// <see cref="ChiakiLog"/> disposed first leaves the session logging into freed memory - the one
/// ownership rule at this seam that the managed side cannot check for itself, and therefore the
/// one worth stating twice.
/// </summary>
public sealed unsafe class ChiakiSession : IDisposable
{
    private IntPtr _handle;
    private GCHandle _self;
    private Action<ChiakiSessionEvent>? _handler;
    private bool _started;

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

    /// <summary>
    /// Where the session reports everything it does. Set it before <see cref="Start"/>: a handler
    /// installed afterwards misses whatever already happened, and on a console that answers fast
    /// that is CONNECTED.
    /// </summary>
    public void SetEventHandler(Action<ChiakiSessionEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        _handler = handler;
        if (!_self.IsAllocated)
            _self = GCHandle.Alloc(this);

        SessionSetEventCb(_handle, &Dispatch, GCHandle.ToIntPtr(_self));
    }

    /// <summary>
    /// chiaki_session_start: spawns the session thread and returns at once. Success means a
    /// thread exists, not that a console answered - that arrives as
    /// <see cref="ChiakiEventType.Quit"/> when it does not.
    /// </summary>
    public ChiakiError Start()
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        ChiakiError err = (ChiakiError)SessionStart(_handle);
        if (err == ChiakiError.Success)
            _started = true;
        return err;
    }

    /// <summary>chiaki_session_stop: asks the session thread to wind up, without waiting.</summary>
    public ChiakiError Stop()
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        return (ChiakiError)SessionStop(_handle);
    }

    /// <summary>
    /// chiaki_session_set_controller_state, taken under the lock the feedback sender reads it
    /// through - so it is safe while a stream is running and works before one is.
    /// </summary>
    public ChiakiError SetControllerState(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        return (ChiakiError)SessionSetControllerState(_handle, state.Handle);
    }

    /// <summary>
    /// Whether what the session is holding equals <paramref name="state"/>, by
    /// chiaki_controller_state_equals. The comparison is the library's, so it cannot agree with a
    /// transcription this side also made.
    /// </summary>
    public bool ControllerStateMatches(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        return SessionControllerStateMatches(_handle, state.Handle);
    }

    /// <summary>
    /// PP627: chiaki_session_set_login_pin - the answer to the one event that asks for one.
    ///
    /// <see cref="ChiakiEventType.LoginPinRequest"/> is the only event libchiaki raises that needs
    /// something back, and the session thread waits on it with no timeout at all: a person typing is
    /// not something a network timeout should interrupt. So a caller that never answers is a session
    /// that sits there until ctrl fails.
    ///
    /// An empty PIN is refused by the shim rather than forwarded, and PP345 is why it matters: the C
    /// takes ownership of what it is given and a spent PIN cannot be retried, so an empty one costs
    /// the prompt as well as the attempt.
    /// </summary>
    public ChiakiError SetLoginPin(ReadOnlySpan<byte> pin)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        if (pin.IsEmpty)
            return ChiakiError.InvalidData;

        fixed (byte* first = pin)
            return (ChiakiError)SessionSetLoginPin(_handle, first, (nuint)pin.Length);
    }

    /// <summary>chiaki_session_join: waits for the session thread to end.</summary>
    public ChiakiError Join()
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        ChiakiError err = (ChiakiError)SessionJoin(_handle);
        if (err == ChiakiError.Success)
            _started = false;
        return err;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        // Stop and join before the free, not as a courtesy: chiaki_session_fini tears down the
        // mutex and the stop pipe the session thread is still standing on. A dispose that skipped
        // this would be a use-after-free with a stack in libchiaki and no managed frame in it.
        if (_started)
        {
            SessionStop(_handle);
            SessionJoin(_handle);
            _started = false;
        }

        SessionFree(_handle);
        _handle = IntPtr.Zero;

        // After the free, so the thunk cannot be reached with a handle that is about to go.
        if (_self.IsAllocated)
            _self.Free();
        _handler = null;
    }

    /// <summary>
    /// Called on the session thread. Nothing may escape it - the frame above is C - and the quit
    /// sentence is copied here because libchiaki's pointer dies with the event.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(int type, int quitReason, IntPtr quitReasonStr, IntPtr user)
    {
        try
        {
            if (user == IntPtr.Zero)
                return;
            if (GCHandle.FromIntPtr(user).Target is not ChiakiSession self)
                return;

            self._handler?.Invoke(new ChiakiSessionEvent(
                (ChiakiEventType)type,
                (ChiakiQuitReason)quitReason,
                quitReasonStr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(quitReasonStr)));
        }
        catch
        {
            // Deliberately silent, for the same reason as ChiakiLog.Dispatch: an exception
            // crossing back into C aborts the process with a stack that names neither side.
        }
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

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_set_event_cb",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionSetEventCb(
        IntPtr session, delegate* unmanaged[Cdecl]<int, int, IntPtr, IntPtr, void> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_start",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SessionStart(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_stop",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SessionStop(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_join",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SessionJoin(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_set_login_pin",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SessionSetLoginPin(IntPtr session, byte* pin, nuint pinSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_set_controller_state",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SessionSetControllerState(IntPtr session, IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_controller_state_matches",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionControllerStateMatches(IntPtr session, IntPtr state);
}
