using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What a senkusha run measured, and whether it measured anything at all.</summary>
/// <param name="MtuIn">The inbound MTU the launch spec carries.</param>
/// <param name="MtuOut">The outbound one, which senkusha measures separately and need not agree.</param>
/// <param name="RoundTripMicroseconds">The round trip, in the unit the C keeps it in.</param>
public readonly record struct SenkushaMeasurementsReading(
    uint MtuIn, uint MtuOut, ulong RoundTripMicroseconds);

/// <summary>
/// PP792, under PP784: the seam a session thread hands its senkusha phase across.
///
/// PP753 built this for the stream phase and senkusha was left without one - session.c calls
/// chiaki_senkusha_run directly, so PP788 to PP791's managed run was a run no session could reach
/// however complete it was. This is the meeting point, and it is not a copy of the stream's: what
/// comes back is three measurements and an error, rather than an error and a disconnect reason.
///
/// PP28's PLACEMENT IS WHAT SPENDS THEM, which is why the outcome has to cross with the numbers.
/// SUCCESS keeps what was measured, CANCELED ends the session, and every other code throws the
/// measurement away for 1454, 1454, a millisecond and the don't-fragment bit cleared. A seam
/// answering only an error would carry the half that decides and lose the half that is decided.
///
/// NOT A MANAGED FUNCTION POINTER, for <see cref="StreamHandover"/>'s reason: the session thread is
/// one the CLR never created, so what goes onto the session is a C trampoline over this handover and
/// the C thread blocks on a condition while this signals it.
///
/// AND NOT INSTALLING ONE IS A SUPPORTED STATE, which is where the two seams differ. The stream
/// phase has no C left to fall back to and a session with no phase quits; senkusha.c is still in the
/// build and still measures the link, so a session that installs nothing here runs the C's - which
/// is PP763's lesson kept rather than restated.
/// </summary>
public sealed class SenkushaHandover : IDisposable
{
    private IntPtr handle;

    /// <summary>Creates one, or throws where the shim could not.</summary>
    public SenkushaHandover()
    {
        handle = HandoverCreate();

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_shim_senkusha_handover_create returned null.");
    }

    /// <summary>Whether the seam is still open.</summary>
    public bool IsOpen => handle != IntPtr.Zero;

    /// <summary>
    /// Raised by the C session thread when it reaches the senkusha phase.
    ///
    /// Here so a test can stand where session.c does, which is the only way this half is reached on
    /// a machine with no console.
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

    /// <summary>
    /// Reports what the managed run answered and what it measured.
    ///
    /// The three cross whatever the error is, and the trampoline decides what to do with them: they
    /// are written into the session's fields only on success, because that is where
    /// chiaki_senkusha_run writes its own. A caller reading them back through
    /// <see cref="Measurements"/> sees what was reported rather than what was used.
    /// </summary>
    public ChiakiError Finish(ChiakiError error, uint mtuIn, uint mtuOut, ulong roundTripMicroseconds)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverFinish(handle, (int)error, mtuIn, mtuOut, roundTripMicroseconds);
    }

    /// <summary>The same, from a run's own reading, which is the shape the run answers in.</summary>
    public ChiakiError Finish(in SenkushaRunReading reading)
        => Finish(reading.Error, reading.MtuIn, reading.MtuOut, reading.RoundTripMicroseconds);

    /// <summary>
    /// What the session thread calls in place of the run: blocks, then answers the run's error.
    ///
    /// A wait that ran out answers <see cref="ChiakiError.Timeout"/> rather than the run's code, so
    /// a measurement that failed can be told from one that never reported - and PP28 sends both to
    /// the fallback, which is the one outcome those two share.
    /// </summary>
    public ChiakiError AwaitFinish(int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        return (ChiakiError)HandoverAwaitFinish(handle, timeoutMs);
    }

    /// <summary>
    /// The three the far side reported, or null before it reported any.
    ///
    /// Null and not zeroes: a link nobody measured and a link measured as zeroes are different
    /// facts, and the session thread tells them apart by the error alone - which a reader of this
    /// does not have.
    /// </summary>
    public SenkushaMeasurementsReading? Measurements
    {
        get
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

            return HandoverMeasurements(handle, out uint mtuIn, out uint mtuOut, out ulong rtt)
                ? new SenkushaMeasurementsReading(mtuIn, mtuOut, rtt)
                : null;
        }
    }

    /// <summary>
    /// The socket the session handed the measurement, or null where it handed none.
    ///
    /// BORROWED, for PP769's reason at the other seam: the session owns it and closes it, so
    /// whoever wraps it must not own it.
    /// </summary>
    public nint? Socket
    {
        get
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

            long raw = HandoverSocket(handle);
            return raw < 0 ? null : (nint)raw;
        }
    }

    /// <summary>
    /// Whether the C session's stop has reached this handover.
    ///
    /// The fifth poke chiaki_session_stop makes. Senkusha is ten pings and two binary searches long,
    /// so a run that could not hear a stop would keep measuring a link nobody is going to use.
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
    /// Install this handover as a session's senkusha phase, and as what its stop reaches.
    ///
    /// One call for both callbacks, because they take the same handover: a phase wired for the run
    /// and not for the stop is one nothing can quit, which is the failure PP338 is about.
    /// </summary>
    public void InstallOn(Native.ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        SenkushaRunInstall(session.Handle, handle);
    }

    /// <summary>
    /// End both waits and mark this stopped, so a waiter can be shut down.
    ///
    /// PP768's export one seam over, and the same order: freeing a handover a thread is blocked on
    /// fails intermittently rather than reliably, which is the expensive way to learn it.
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

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_run_install",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void SenkushaRunInstall(IntPtr session, IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HandoverCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void HandoverFree(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_start",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverStart(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_await_start",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandoverAwaitStart(IntPtr handover, int timeoutMs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_finish",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverFinish(
        IntPtr handover, int error, uint mtuIn, uint mtuOut, ulong roundTripMicroseconds);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_await_finish",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverAwaitFinish(IntPtr handover, int timeoutMs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_measurements",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandoverMeasurements(
        IntPtr handover, out uint mtuIn, out uint mtuOut, out ulong roundTripMicroseconds);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_socket",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern long HandoverSocket(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_stopped",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandoverStopped(IntPtr handover);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_senkusha_handover_cancel",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HandoverCancel(IntPtr handover);
}
