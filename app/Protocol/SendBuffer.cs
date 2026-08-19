using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP125: takion's send buffer, which holds every reliable message until the console acks it.
///
/// The semantics are one sentence and the failures are both silent. An ack releases that sequence
/// number and every OLDER one - older by RFC 1982 serial comparison, not by integer order, which
/// is what makes it interesting: sequence numbers wrap, and "older" near the wrap is not "less
/// than". Release too much and a message nobody received is never sent again; release too little
/// and the buffer fills, after which pushes are refused and the session stops sending. Neither
/// failure says anything about a send buffer.
///
/// The payload lives on the C side. The buffer takes ownership and frees it on ack, so a managed
/// array handed over would have a C allocator free memory it never allocated - which is not an
/// error, it is a corrupted heap that surfaces somewhere else entirely.
///
/// What is observable is the COUNT and not which packets remain. ChiakiTakionSendBufferPacket is
/// an incomplete type in the public header; the C's own test reads its fields by #including
/// takionsendbuffer.c, which the shim cannot do because chiaki-lib is already linked in. Every
/// property worth asserting turns out to be expressible in the count anyway - including the wrap.
/// </summary>
public sealed class SendBuffer : IDisposable
{
    private IntPtr _handle;

    /// <param name="size">How many packets may be outstanding before a push is refused.</param>
    public SendBuffer(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        _handle = SendBufferCreate(size);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_takion_send_buffer_init failed.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(SendBuffer));

    /// <summary>How many packets are still waiting to be acknowledged.</summary>
    public int Count => SendBufferCount(Handle);

    /// <summary>
    /// Queues a packet of <paramref name="size"/> bytes under a sequence number.
    ///
    /// Returns the error rather than throwing, because CHIAKI_ERR_OVERFLOW is not a fault - it is
    /// the buffer saying the console is behind, which a caller has to handle rather than crash on.
    /// </summary>
    public ChiakiError Push(uint seqNum, int size)
        => (ChiakiError)SendBufferPush(Handle, seqNum, size);

    /// <summary>Acknowledges a sequence number, releasing it and everything older.</summary>
    public ChiakiError Ack(uint seqNum) => (ChiakiError)SendBufferAck(Handle, seqNum);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        SendBufferFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_send_buffer_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SendBufferCreate(int size);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_send_buffer_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void SendBufferFree(IntPtr sendBuffer);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_send_buffer_push",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SendBufferPush(IntPtr sendBuffer, uint seqNum, int bufSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_send_buffer_ack",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SendBufferAck(IntPtr sendBuffer, uint seqNum);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_send_buffer_count",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SendBufferCount(IntPtr sendBuffer);

}
