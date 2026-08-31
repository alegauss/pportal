using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP607, under PP27: a real takion, connected to a peer this process is running.
///
/// This is the piece PP601 said had to exist and PP602 to PP606 built the other half of. takion's
/// receive loop is bound to a socket and a thread, and every handler on it is file-local - so the
/// way in is not an entry point onto the loop but a takion that has been CONNECTED, doing what it
/// normally does, with PP606's responder where the console would be.
///
/// THE CONNECT RETURNS BEFORE THE HANDSHAKE DOES. chiaki_takion_connect starts the thread and comes
/// back; the INIT goes out from that thread, and CHIAKI_TAKION_EVENT_TYPE_CONNECTED fires once the
/// cookie ack has been read. So a caller answers datagrams and watches <see cref="Connected"/>
/// rather than expecting a return value to mean anything about the peer.
///
/// CLOSING JOINS THE THREAD, which is what makes this safe to use in a test: after
/// <see cref="Dispose"/> the callback cannot run, so nothing observes a freed wrapper. A takion
/// whose handshake never completes retries three times at five seconds, so a close on that path
/// waits - bounded, and slow only when the peer is wrong.
/// </summary>
public sealed class NativeTakionLoopback : IDisposable
{
    private IntPtr handle;

    private NativeTakionLoopback(IntPtr handle) => this.handle = handle;

    /// <summary>
    /// Connects a takion to a UDP peer on loopback, or null with the error the C returned.
    /// </summary>
    /// <param name="port">The responder's port.</param>
    /// <param name="protocolVersion">7, 9 or 12; anything else the C refuses.</param>
    public static NativeTakionLoopback? TryConnect(
        ushort port, byte protocolVersion, out ChiakiError error)
    {
        IntPtr created = ConnectLoopback(IntPtr.Zero, port, protocolVersion, out int err);
        error = (ChiakiError)err;

        return created == IntPtr.Zero ? null : new NativeTakionLoopback(created);
    }

    /// <summary>Whether the handshake has completed, as the connected event reports it.</summary>
    public bool Connected => handle != IntPtr.Zero && IsConnected(handle);

    /// <summary>How many events the callback has seen.</summary>
    public int EventCount => handle == IntPtr.Zero ? 0 : EventsSeen(handle);

    /// <summary>Closes the takion, joining its thread, and releases the wrapper.</summary>
    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        IntPtr closing = handle;
        handle = IntPtr.Zero;
        Close(closing);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_connect_loopback",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ConnectLoopback(
        IntPtr log, ushort port, byte protocolVersion, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_connected",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool IsConnected(IntPtr takion);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_event_count",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EventsSeen(IntPtr takion);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_close",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void Close(IntPtr takion);
}
