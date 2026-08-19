using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// What a client does with a frame. Returning false says it could not be taken, which makes the
/// receiver report a corrupt frame and ask the console for a keyframe.
/// </summary>
/// <param name="frame">
/// The frame processor's own storage, lent for the duration of this call and reused after it. Read
/// it here; copy what has to outlive the call. It is a <c>ReadOnlySpan</c> precisely so the
/// compiler refuses to let it escape.
/// </param>
public delegate bool VideoSampleHandler(ReadOnlySpan<byte> frame, int framesLost, bool frameRecovered);

/// <summary>
/// PP87: who owns the buffers a video frame arrives in - the last of PP4's four questions.
///
/// It was filed as unanswerable without a decoder to feed, on the grounds that nothing offline
/// drives the video sample callback. That was wrong. test/videoreceiver.c drives it with a
/// synthesised session, a real profile header and one whole frame in one unit, and this does the
/// same across the seam: no console, no renderer, no decoder.
///
/// The answer itself is the shape of <see cref="VideoSampleHandler"/>. libchiaki hands over the
/// frame processor's storage and takes it back when the call returns, sixty times a second, on the
/// stream connection's thread. So the managed handler receives a ReadOnlySpan over that memory -
/// no copy, and a ref struct the compiler will not let escape the call. A client that wants to
/// keep a frame copies it; one that only decodes reads it where it lies.
///
/// The thunk and the GCHandle are the same pattern as every other callback in this port. What is
/// new is only that this one carries a buffer rather than scalars.
///
/// ONE FRAME, AT INDEX 1
/// ---------------------
/// This drives a session synthesised for the purpose: zeroed apart from the log, the codec and the
/// sample callback, which is what the path a single complete frame takes actually reads. A SECOND
/// frame index makes the receiver report a corrupt frame into the stream connection, and there is
/// no stream connection here - the report reaches zeroed memory and the process aborts, which is a
/// crash and not a failure. test/videoreceiver.c avoids it the same way and says so: "frame index
/// 1 is the one index that skips the corrupt-frame report".
///
/// So this is a harness for the callback contract, not a driver for a stream. Making it one means
/// giving it a real session, which is where the rest of the transport lives.
/// </summary>
public sealed unsafe class VideoReceiver : IDisposable
{
    private readonly VideoSampleHandler handler;
    private GCHandle _self;
    private IntPtr _handle;

    public VideoReceiver(VideoSampleHandler handler, ChiakiNg.Session.ChiakiCodec codec, ChiakiLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this.handler = handler;

        _self = GCHandle.Alloc(this);
        _handle = ReceiverCreate(log?.Handle ?? IntPtr.Zero, (int)codec, &Dispatch, GCHandle.ToIntPtr(_self));
        if (_handle == IntPtr.Zero)
        {
            _self.Free();
            throw new OutOfMemoryException("chiaki_shim_video_receiver_create returned null.");
        }
    }

    private IntPtr Check()
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(VideoReceiver));

    /// <summary>
    /// The stream info a session opens with. The header is the SPS and PPS the bitstream parser
    /// needs, and the receiver hands it to the sample callback before any frame arrives - so a
    /// client sees it as a frame-shaped thing that is not a frame.
    /// </summary>
    public bool StreamInfo(byte[] header, uint width, uint height)
        => ReceiverStreamInfo(Check(), header, header.Length, width, height);

    /// <summary>
    /// One AV packet. A unit that is the last of its frame makes the receiver flush inside this
    /// call, which is when the sample callback fires.
    /// </summary>
    public void AvPacket(
        ushort frameIndex, ushort unitIndex, ushort total, ushort fec, byte[] data,
        byte adaptiveStreamIndex = 0)
        => ReceiverAvPacket(Check(), frameIndex, unitIndex, unitIndex, total, fec,
            adaptiveStreamIndex, data, data.Length);

    /// <summary>
    /// The same by span, which is the one a stream uses: this runs once per packet, and PP113's
    /// budget there is zero bytes.
    /// </summary>
    public void AvPacket(
        ushort frameIndex, ushort unitIndex, ushort total, ushort fec, ReadOnlySpan<byte> data,
        byte adaptiveStreamIndex = 0)
    {
        IntPtr handle = Check();
        fixed (byte* p = data)
        {
            ReceiverAvPacketPtr(handle, frameIndex, unitIndex, unitIndex, total, fec,
                adaptiveStreamIndex, (IntPtr)p, data.Length);
        }
    }

    public int FramesLost => ReceiverFramesLost(Check());

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            ReceiverFree(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Dispatch(IntPtr buf, int size, int framesLost, byte frameRecovered, IntPtr user)
    {
        try
        {
            if (user == IntPtr.Zero || GCHandle.FromIntPtr(user).Target is not VideoReceiver self)
                return 1;

            // The span is built over libchiaki's memory and never leaves this frame. That is the
            // whole of PP4's fourth question, expressed in a type rather than in a comment.
            var frame = new ReadOnlySpan<byte>((void*)buf, size);
            return self.handler(frame, framesLost, frameRecovered != 0) ? (byte)1 : (byte)0;
        }
        catch
        {
            // Nothing may escape into C. A handler that threw is a frame the client could not
            // take, which is what false means - so that is what it becomes.
            return 0;
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ReceiverCreate(
        IntPtr log, int codec,
        delegate* unmanaged[Cdecl]<IntPtr, int, int, byte, IntPtr, byte> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReceiverFree(IntPtr receiver);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_stream_info",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ReceiverStreamInfo(
        IntPtr receiver, byte[] header, int headerSize, uint width, uint height);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_av_packet",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReceiverAvPacket(
        IntPtr receiver, ushort frameIndex, ushort packetIndex, ushort unitIndex,
        ushort total, ushort fec, byte adaptiveStreamIndex, byte[] data, int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_av_packet",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReceiverAvPacketPtr(
        IntPtr receiver, ushort frameIndex, ushort packetIndex, ushort unitIndex,
        ushort total, ushort fec, byte adaptiveStreamIndex, IntPtr data, int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_video_receiver_frames_lost",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ReceiverFramesLost(IntPtr receiver);
}
