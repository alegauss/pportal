using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP753: the seam a session thread hands its stream phase across, and takes an outcome back on.
///
/// PP752 decided that one of the session thread's seven steps becomes managed and that the C thread
/// WAITS rather than returns - its steps five to seven still have to happen there. What it did not
/// have was any way for the two sides to meet, and PP696 cannot land without one.
///
/// NOT A MANAGED FUNCTION POINTER. The shim installs every one of libchiaki's twenty-two callbacks
/// as a C trampoline, for a stated reason: an enum's underlying type is the compiler's choice, and a
/// pinned managed object is one GC compaction from a call into freed memory. A run installed that
/// way would hold that bet for the length of a session rather than for one log line - so the C
/// thread blocks on a condition instead, and this signals it.
///
/// TWO WAITS, ONE EACH WAY. <see cref="AwaitStart"/> is how the managed side learns the thread has
/// reached the stream phase, so it begins rather than polls; <see cref="Finish"/> is how the run's
/// outcome gets back, carrying both values the thread needs - the error, and the remote disconnect
/// reason it compares against the shutdown phrase to choose between two quit reasons.
///
/// THE REASON IS COPIED ACROSS by the shim rather than borrowed, because it is a managed string and
/// the session thread reads it after the run is over. A NULL one is a case the C already has:
/// PP371 found both of its reads dereferencing it, so absent and empty stay distinguishable.
/// </summary>
public sealed class StreamHandover : IDisposable
{
    private IntPtr handle;

    /// <summary>Creates one, or throws where the shim could not.</summary>
    public StreamHandover()
    {
        handle = HandoverCreate();

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_shim_stream_handover_create returned null.");
    }

    /// <summary>Whether the seam is still open.</summary>
    public bool IsOpen => handle != IntPtr.Zero;

    /// <summary>
    /// Raised by the C session thread when it reaches the stream phase.
    ///
    /// Here so a test can stand where session.c will: PP696 is the commit that calls it from the C,
    /// and until then this is the only caller.
    /// </summary>
    public ChiakiError Start()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverStart(handle);
    }

    /// <summary>Waits for that. False on timeout, which is a thread that never arrived.</summary>
    public bool AwaitStart(int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return HandoverAwaitStart(handle, timeoutMs);
    }

    /// <summary>Reports what the managed run answered, and the reason where there is one.</summary>
    public ChiakiError Finish(ChiakiError error, string? reason = null)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverFinish(handle, (int)error, reason);
    }

    /// <summary>
    /// What the session thread calls in place of the run: blocks, then answers the run's error.
    ///
    /// A wait that ran out answers <see cref="ChiakiError.Timeout"/> rather than the run's code,
    /// because the thread has to tell a run that failed from one that never reported.
    /// </summary>
    public ChiakiError AwaitFinish(int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverAwaitFinish(handle, timeoutMs);
    }

    /// <summary>The reason the run reported, or null where it reported none.</summary>
    public string? Reason
    {
        get
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

            IntPtr at = HandoverReason(handle);

            return at == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(at);
        }
    }

    /// <summary>
    /// PP696: whether the C session's stop has reached this handover.
    ///
    /// The session thread stops with four wake-ups and this is the fourth - the one that used to be
    /// chiaki_stream_connection_stop. The trampoline's wait loop reads it every slice, so a stop
    /// that arrives mid-session ends the run rather than waiting out the whole thing.
    /// </summary>
    public bool Stopped
    {
        get
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            return HandoverStopped(handle);
        }
    }

    /// <summary>
    /// PP696: install this handover as a session's stream phase, and as what its stop reaches.
    ///
    /// THE TRAMPOLINE IS C. What goes onto the session is a pair of C function pointers over this
    /// handover, and never a managed delegate: the session thread is one the CLR never created, and
    /// a delegate installed here is a pointer the collector may move under it.
    ///
    /// One call for both callbacks, because they take the same handover - a session wired for the
    /// run and not for the stop is one nothing can quit, which is the failure PP338 is about.
    /// </summary>
    public void InstallOn(Native.ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        StreamRunInstall(session.Handle, handle);
    }

    /// <summary>
    /// PP768: end both waits and mark this stopped, so a waiter can be shut down.
    ///
    /// The seam had no way out. A caller holding a thread inside <see cref="AwaitStart"/> could only
    /// free the object that thread was blocked on, which is what PP762's phase did - and a wait on
    /// freed memory fails intermittently rather than reliably, which is how it was found.
    /// </summary>
    public ChiakiError Cancel()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverCancel(handle);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        HandoverFree(handle);
        handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_run_install",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void StreamRunInstall(IntPtr session, IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_cancel",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverCancel(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_stopped",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandoverStopped(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HandoverCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void HandoverFree(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_start",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverStart(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_await_start",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandoverAwaitStart(IntPtr handover, int timeoutMs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_finish",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int HandoverFinish(
        IntPtr handover, int error, [MarshalAs(UnmanagedType.LPUTF8Str)] string? reason);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_await_finish",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverAwaitFinish(IntPtr handover, int timeoutMs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_stream_handover_reason",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HandoverReason(IntPtr handover);
}
