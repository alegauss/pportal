using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>The only question the video path asks about a frame.</summary>
public enum FrameFlushResult
{
    /// <summary>It arrived whole.</summary>
    Success = 0,

    /// <summary>Units were missing and FEC put them back.</summary>
    FecSuccess = 1,

    /// <summary>Units were missing and there were not enough parity units to recover them.</summary>
    FecFailed = 2,

    /// <summary>There is no frame to flush.</summary>
    Failed = 3,
}

/// <summary>Which timed stage, as the baseline row names them.</summary>
public enum FrameStage { Reassemble = 0, Correct = 1 }

/// <summary>
/// PP23: the frame processor, where units become a frame and FEC gets driven.
///
/// It is the join between two modules already across this seam - takion hands it units and it
/// hands <see cref="Fec"/> the ones that are missing - so it is the first piece of the harness
/// that exercises the others rather than sitting beside them.
///
/// A unit is passed as scalars for the same reason every struct at this seam is: a
/// ChiakiTakionAVPacket ends in a borrowed pointer, and building one on this side would put .NET
/// in charge of a layout it cannot check.
/// </summary>
public sealed class FrameProcessor : IDisposable
{
    private IntPtr _handle;

    /// <param name="log">
    /// Where the processor's own messages go. Null means libchiaki's default, which prints to
    /// stdout - so a caller that expects a failure passes a log rather than letting the library
    /// write a red line into output somebody is reading for something else.
    /// </param>
    public FrameProcessor(ChiakiLog? log = null)
    {
        _handle = ProcessorCreate(log?.Handle ?? IntPtr.Zero);
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_frame_processor_create returned null.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(FrameProcessor));

    /// <summary>
    /// Sizes the frame from its first unit. Everything after depends on it: the processor cannot
    /// know how many units to expect until one of them says.
    /// </summary>
    public ChiakiError AllocFrame(ushort frameIndex, ushort unitIndex, ushort total, ushort fec, byte[] data)
        => (ChiakiError)ProcessorAllocFrame(Handle, true, frameIndex, unitIndex, unitIndex, total, fec,
            data, data.Length);

    public ChiakiError PutUnit(ushort frameIndex, ushort unitIndex, ushort total, ushort fec, byte[] data)
        => (ChiakiError)ProcessorPutUnit(Handle, true, frameIndex, unitIndex, unitIndex, total, fec,
            data, data.Length);

    /// <summary>
    /// The same, and the same for <see cref="FlushInto"/>: written so that a steady-state packet
    /// costs NOTHING.
    ///
    /// PP44 measured the C transport and found it allocates zero bytes and makes zero allocator
    /// calls per packet after the first frame - the buffers are sized once from the frame's own
    /// header and reused. That makes the budget the managed side inherits unusually strict and
    /// unusually defensible: not "allocate little" but "allocate nothing", because that is what
    /// the code being replaced does.
    ///
    /// A managed transport that allocates per packet turns thousands of small packets a second
    /// into a collection under load, and what that costs is the worst frame of a minute - which is
    /// invisible to every check that watches a mean.
    /// </summary>
    public ChiakiError PutUnit(
        ushort frameIndex, ushort unitIndex, ushort total, ushort fec,
        ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                return (ChiakiError)ProcessorPutUnitPtr(Handle, true, frameIndex, unitIndex, unitIndex,
                    total, fec, (IntPtr)p, data.Length);
            }
        }
    }

    /// <summary>Whether enough units are in for a flush to be worth trying.</summary>
    public bool FlushPossible => ProcessorFlushPossible(Handle);

    /// <summary>
    /// Flushes the frame. The bytes are copied out of the processor's buffer, which stops being
    /// valid at the next call to it - so what comes back is the caller's.
    /// </summary>
    public (FrameFlushResult Result, byte[] Frame) Flush(int maxBytes = 1 << 20)
    {
        var buf = new byte[maxBytes];
        int size = buf.Length;
        var result = (FrameFlushResult)ProcessorFlush(Handle, buf, ref size);
        return (result, result == FrameFlushResult.Failed ? [] : buf[..size]);
    }

    /// <summary>
    /// Flushes into a buffer the caller already has, so a steady-state frame allocates nothing.
    /// <paramref name="written"/> is how much of it was filled.
    /// </summary>
    public FrameFlushResult FlushInto(Span<byte> destination, out int written)
    {
        int size = destination.Length;
        FrameFlushResult result;

        unsafe
        {
            fixed (byte* p = destination)
            {
                result = (FrameFlushResult)ProcessorFlushPtr(Handle, (IntPtr)p, ref size);
            }
        }

        written = result == FrameFlushResult.Failed ? 0 : size;
        return result;
    }

    /// <summary>How many frames a timed stage has been charged for.</summary>
    public ulong StageSamples(FrameStage stage) => ProcessorStageSamples(Handle, (int)stage);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        ProcessorFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ProcessorCreate(IntPtr log);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ProcessorFree(IntPtr processor);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_alloc_frame",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorAllocFrame(
        IntPtr processor, [MarshalAs(UnmanagedType.I1)] bool isVideo,
        ushort frameIndex, ushort packetIndex, ushort unitIndex,
        ushort total, ushort fec, byte[] data, int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_put_unit",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorPutUnit(
        IntPtr processor, [MarshalAs(UnmanagedType.I1)] bool isVideo,
        ushort frameIndex, ushort packetIndex, ushort unitIndex,
        ushort total, ushort fec, byte[] data, int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_flush_possible",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ProcessorFlushPossible(IntPtr processor);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_flush",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorFlush(IntPtr processor, byte[] frame, ref int frameSize);

    // The same two entry points taken by pointer. A byte[] parameter is marshalled by pinning and
    // costs nothing itself, but it forces the caller to HAVE an array - and the caller that has a
    // span into a pooled buffer is the one that allocates nothing.
    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_put_unit",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorPutUnitPtr(
        IntPtr processor, [MarshalAs(UnmanagedType.I1)] bool isVideo,
        ushort frameIndex, ushort packetIndex, ushort unitIndex,
        ushort total, ushort fec, IntPtr data, int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_flush",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ProcessorFlushPtr(IntPtr processor, IntPtr frame, ref int frameSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_frame_processor_stage_samples",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong ProcessorStageSamples(IntPtr processor, int stage);
}
