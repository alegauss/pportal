using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Which end an overflow drops from.</summary>
public enum ReorderDropStrategy { Begin = 0, End = 1 }

/// <summary>One element the queue let go of, with the reason implicit in when it happened.</summary>
public readonly record struct ReorderDrop(ulong SeqNum, long Payload);

/// <summary>
/// PP23: the reorder queue, which is what turns a UDP arrival order back into a stream.
///
/// This is the module a managed rewrite would most confidently write from scratch, because it
/// looks like a ring buffer with a window. What it is is a ring buffer indexed by RFC 1982
/// arithmetic, with a drop policy that fires on four separate occasions - a packet older than the
/// window, a duplicate, an overflow at the low end and an overflow at the high end - and which of
/// the four a push takes is the whole behaviour.
///
/// The payload never crosses as an object. libchiaki only hands the pointer back and never
/// dereferences it, so this passes an index and keeps the GC out of it; the only thing the seam
/// carries is the drop callback, through the same [UnmanagedCallersOnly] thunk and GCHandle as
/// every other callback in this port.
/// </summary>
public sealed unsafe class ReorderQueue : IDisposable
{
    private readonly List<ReorderDrop> drops = [];
    private GCHandle _self;
    private IntPtr _handle;

    /// <param name="sizeExp">The window is 2^sizeExp elements.</param>
    /// <param name="seqNumStart">The sequence number of the first element expected.</param>
    public ReorderQueue(int sizeExp, ushort seqNumStart)
    {
        _self = GCHandle.Alloc(this);
        _handle = QueueCreate16(sizeExp, seqNumStart, &Dispatch, GCHandle.ToIntPtr(_self));
        if (_handle == IntPtr.Zero)
        {
            _self.Free();
            throw new InvalidOperationException("chiaki_reorder_queue_init_16 failed.");
        }
    }

    /// <summary>Everything the queue has dropped, oldest first.</summary>
    public IReadOnlyList<ReorderDrop> Drops => drops;

    public int Size => QueueSize(Check());

    public ulong Count => QueueCount(Check());

    public ReorderDropStrategy DropStrategy
    {
        set => QueueSetDropStrategy(Check(), (int)value);
    }

    public void Push(ulong seqNum, long payload) => QueuePush(Check(), seqNum, (IntPtr)payload);

    /// <summary>The next element in order, or null when the window's head has not arrived.</summary>
    public (ulong SeqNum, long Payload)? Pull()
        => QueuePull(Check(), out ulong seq, out IntPtr user) ? (seq, (long)user) : null;

    /// <summary>
    /// The element at an OFFSET from the window's start - not at a sequence number, which is the
    /// mistake the parameter name in libchiaki shouts about.
    /// </summary>
    public (ulong SeqNum, long Payload)? Peek(ulong index)
        => QueuePeek(Check(), index, out ulong seq, out IntPtr user) ? (seq, (long)user) : null;

    /// <summary>Drops one element by offset. The window's start does not move.</summary>
    public void Drop(ulong index) => QueueDrop(Check(), index);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            // Freeing drops whatever is still queued, and each of those is a callback - so the
            // native side is torn down before the handle that the callback would arrive through.
            QueueFree(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    private IntPtr Check()
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(ReorderQueue));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(ulong seqNum, IntPtr elemUser, IntPtr user)
    {
        try
        {
            if (user != IntPtr.Zero && GCHandle.FromIntPtr(user).Target is ReorderQueue self)
                self.drops.Add(new ReorderDrop(seqNum, (long)elemUser));
        }
        catch
        {
            // Nothing may escape into C, for the reason every other thunk here says so.
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_create_16",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueueCreate16(
        int sizeExp, ushort seqNumStart,
        delegate* unmanaged[Cdecl]<ulong, IntPtr, IntPtr, void> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void QueueFree(IntPtr queue);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_set_drop_strategy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void QueueSetDropStrategy(IntPtr queue, int strategy);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int QueueSize(IntPtr queue);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_count",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong QueueCount(IntPtr queue);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_push",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void QueuePush(IntPtr queue, ulong seqNum, IntPtr elemUser);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_pull",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool QueuePull(IntPtr queue, out ulong seqNum, out IntPtr elemUser);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_peek",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool QueuePeek(IntPtr queue, ulong index, out ulong seqNum, out IntPtr elemUser);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_reorder_queue_drop",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void QueueDrop(IntPtr queue, ulong index);
}
