using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP700: the decoder a session decodes into, which nothing in this port had.
///
/// Block C is titled "Video and input path" and read finished, and that was true about the LINES
/// and false about the path. The shim's session carried a create, a start, an event callback and a
/// controller state, and no video sink at all - so a stream reached the frame processor and
/// stopped. The renderer was chosen (PP9), the shared surface proved (PP131-PP135), the planes
/// composed (PP319, PP322) and the overlay priced (PP641). Nothing was wired to a session.
///
/// THE JOIN IS ONE FIELD. libchiaki hands every assembled frame to the session's video_sample_cb,
/// and chiaki_ffmpeg_decoder_video_sample_cb is the C's own implementation of it. Installing that
/// with a decoder as its user is the whole of what makes a session decode, and this is the managed
/// end of it.
///
/// THE DECODER IS NATIVE AND STAYS NATIVE. "No managed video decoder" is a non-goal of this port,
/// and PP31 settled which side the decode lives on. What is managed is the lifetime and the
/// choosing - which is the half a person changes in a settings screen.
///
/// WHAT THIS SLICE DOES NOT DO is show a picture. That needs the render seam over a decoded frame
/// and a surface to put it on, and both are the rest of PP700. What it does is make a session
/// decode, which is a NUMBER a live run produces - and a number is what tells a run from a hope.
/// </summary>
public sealed class SessionDecoder : IDisposable
{
    private IntPtr handle;

    /// <summary>
    /// The decoder names libchiaki accepts, which are the settings screen's own strings.
    ///
    /// Empty is software, which is what DecoderChoice stores for its first entry. A name the
    /// machine has no device for is REFUSED by the C rather than falling back - so a missing driver
    /// says so instead of decoding on the CPU and looking merely slow.
    /// </summary>
    public static IReadOnlyList<string> HardwareNames { get; } = ["vulkan", "cuda", "d3d11va"];

    /// <summary>What the decoder was asked for, kept so a run can say which one produced its numbers.</summary>
    public string Requested { get; }

    /// <summary>
    /// Open a decoder, or throw with the C's own error.
    /// </summary>
    /// <param name="log">A shim log, or IntPtr.Zero for libchiaki's own.</param>
    /// <param name="codec">The ChiakiCodec the session negotiated.</param>
    /// <param name="maxFps">The profile's frame rate, which sizes the codec's own buffering.</param>
    /// <param name="hardwareName">One of <see cref="HardwareNames"/>, or empty for software.</param>
    public SessionDecoder(IntPtr log, int codec, int maxFps, string hardwareName)
    {
        ArgumentNullException.ThrowIfNull(hardwareName);

        Requested = hardwareName;
        handle = DecoderCreate(log, codec, maxFps, hardwareName.Length == 0 ? null : hardwareName, out int error);

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"chiaki_ffmpeg_decoder_init refused '{(hardwareName.Length == 0 ? "software" : hardwareName)}': "
                    + $"{(ChiakiError)error}");
        }
    }

    /// <summary>The handle, for the session that borrows it.</summary>
    public IntPtr Handle
        => handle != IntPtr.Zero ? handle : throw new ObjectDisposedException(nameof(SessionDecoder));

    /// <summary>
    /// How many times the decoder reported a frame ready.
    ///
    /// Zero after a live session is the state PP700 exists about: the stream arrived and nothing
    /// decoded it.
    /// </summary>
    public ulong FramesAvailable => handle == IntPtr.Zero ? 0 : DecoderFramesAvailable(handle);

    /// <summary>
    /// PP76: frames the codec has actually handed back.
    ///
    /// The total to account against, and not <see cref="FramesAvailable"/>. That one counts the
    /// callback, on the decoder's own thread, and so includes frames still inside the codec -
    /// subtracting a reader's totals from it compares two clocks and leaves a residue that looks
    /// like loss. This one advances only inside the pull, which one thread calls.
    /// </summary>
    public ulong FramesDecoded => handle == IntPtr.Zero ? 0 : DecoderFramesDecoded(handle);

    /// <summary>
    /// The AVPixelFormat that resolved, which says whether the hardware path was taken.
    ///
    /// PP48 measured the per-frame copy libchiaki runs for any hardware frame that is not
    /// AV_PIX_FMT_VULKAN - 793us on cuda, 2253us on d3d11va, nothing on vulkan. So this is the
    /// difference between a frame that costs nothing to hand on and one that costs a frame's worth
    /// of budget, and it is a fact about the machine rather than about the request.
    /// </summary>
    public int PixelFormat => handle == IntPtr.Zero ? -1 : DecoderPixelFormat(handle);

    /// <summary>
    /// Its NAME, which is what a recorded run should carry.
    ///
    /// Asked of the C rather than mapped here: pixfmt.h's enum is sequential and unnumbered, so a
    /// table on this side is a set of literals a different ffmpeg quietly invalidates.
    /// </summary>
    public string PixelFormatName
    {
        get
        {
            if (handle == IntPtr.Zero)
                return string.Empty;

            byte[] buffer = new byte[64];
            int length = DecoderPixelFormatName(handle, buffer, buffer.Length);

            return length > 0 ? System.Text.Encoding.ASCII.GetString(buffer, 0, length) : string.Empty;
        }
    }

    /// <summary>
    /// Whether libchiaki copies every frame out of the format that resolved.
    ///
    /// PP48's finding is the reason this matters more than the decoder's name: the per-frame copy
    /// runs for any hardware frame that is not AV_PIX_FMT_VULKAN, at 793us on cuda and 2253us on
    /// d3d11va. So this is "is the no-copy path" and not "is hardware", and the C answers it
    /// because that constant is an unnumbered enum member.
    /// </summary>
    public bool CopiesEveryFrame => handle == IntPtr.Zero || DecoderCopiesEveryFrame(handle);

    /// <summary>
    /// The format a FRAME carries, which is not the one above.
    ///
    /// <see cref="PixelFormat"/> is what a frame becomes after a DOWNLOAD - NV12 or P010 with a
    /// hardware context, YUV420P without - and a vulkan decoder's frames arrive as
    /// AV_PIX_FMT_VULKAN. Reading the first as the second is what made the copy question answer
    /// wrongly on a decoder that copies nothing, and a run said so.
    /// </summary>
    public int FrameFormat => handle == IntPtr.Zero ? -1 : DecoderFrameFormat(handle);

    /// <summary>
    /// Any AVPixelFormat's name, for a caller printing one it did not expect.
    ///
    /// Static and general, because the interesting case is a format the port has no constant for -
    /// which is exactly the one worth naming in a log line.
    /// </summary>
    public static string NameOfFormat(int format)
    {
        byte[] buffer = new byte[64];
        int length = FormatName(format, buffer, buffer.Length);

        return length > 0 ? System.Text.Encoding.ASCII.GetString(buffer, 0, length) : $"format {format}";
    }

    /// <summary>
    /// PP76: set whenever a frame becomes available, so a reader waits rather than polls.
    ///
    /// The pull DRAINS the codec and returns only the last frame, counting none of the rest - so a
    /// reader that polls accumulates frames between its ticks and loses them silently, which
    /// measures its own interval under the decoder's name.
    ///
    /// The handle stays this object's: it is cleared on the C side before the wait handle is
    /// disposed, because a decoder signalling a closed handle is the one way this crashes rather
    /// than merely stopping.
    /// </summary>
    public AutoResetEvent Ready { get; } = new(false);

    /// <summary>Start signalling <see cref="Ready"/>, which the reader waits on.</summary>
    public void SignalWhenReady()
        => DecoderSetReadyEvent(Handle, Ready.SafeWaitHandle.DangerousGetHandle());

    /// <summary>
    /// PP787: hand this decoder one frame, from a caller that is not the C's stream connection.
    ///
    /// <see cref="AttachTo"/> is the door PP700 opened and it installs the sink on the SESSION, so
    /// what decodes a frame is the C's run calling the C's callback. A managed run produces the
    /// same frames and had nowhere to put them - which is a flip that streams and shows nothing,
    /// and the failure PP763 already paid for once.
    ///
    /// The SAME callback with the same decoder, so this is a second door rather than a second
    /// decoder. Its shape is <see cref="VideoSampleHandler"/>'s on purpose: the delegate
    /// ManagedStreamPhase already takes is this method group.
    /// </summary>
    /// <param name="frame">The access unit, as the video receiver assembled it.</param>
    /// <param name="framesLost">How many the receiver could not rebuild before this one.</param>
    /// <param name="frameRecovered">Whether FEC put this one back together.</param>
    /// <returns>What the decoder answered; false is a frame it would not take.</returns>
    public bool Sample(ReadOnlySpan<byte> frame, int framesLost, bool frameRecovered)
    {
        if (handle == IntPtr.Zero || frame.IsEmpty)
            return false;

        unsafe
        {
            fixed (byte* bytes = frame)
                return DecoderVideoSample(handle, (IntPtr)bytes, frame.Length, framesLost, frameRecovered);
        }
    }

    /// <summary>One decoded frame's planes, borrowed until the next pull.</summary>
    /// <param name="Width">The picture's own width.</param>
    /// <param name="Height">And its height.</param>
    /// <param name="Luma">The Y plane. Valid until the next <see cref="Pull"/> or the dispose.</param>
    /// <param name="LumaStride">Its stride, which is usually wider than the picture.</param>
    /// <param name="Chroma">The interleaved CbCr plane, for an NV12 frame.</param>
    /// <param name="ChromaStride">And its own stride.</param>
    /// <param name="Format">The AVPixelFormat, which says whether the two planes above exist.</param>
    /// <param name="FramesLost">
    /// What the decoder accumulated. PP528 repaired this counter and the pull ZEROES it, so this is
    /// the only place it is ever readable - a caller that drops it has dropped it for good.
    /// </param>
    /// <param name="Superseded">
    /// PP76: decoded frames this pull threw away. The C drains the codec and keeps only the last,
    /// counting none of the rest - so these are frames nobody will ever see, which is exactly what
    /// frames_dropped means, and this is the only place the number exists.
    /// </param>
    public readonly record struct DecodedFrame(
        int Width, int Height,
        IntPtr Luma, int LumaStride,
        IntPtr Chroma, int ChromaStride,
        int Format, int FramesLost, int Superseded);

    /// <summary>
    /// Take the next decoded frame, or false where there is none or it is not NV12.
    ///
    /// FALSE IS TWO DIFFERENT THINGS and the frame says which: no frame at all leaves Width at
    /// zero, and a frame in a format the presenter cannot take reports its size and its format. A
    /// software decoder resolves to yuv420p here, which is three planes rather than two - refusing
    /// it rather than converting is what keeps a run honest about which decoder produced its
    /// numbers.
    ///
    /// The loss count comes back either way, because the pull zeroed it whatever it returned.
    /// </summary>
    public bool Pull(out DecodedFrame frame)
    {
        frame = default;

        if (handle == IntPtr.Zero)
            return false;

        bool nv12 = DecoderPull(
            handle, out int w, out int h,
            out IntPtr luma, out int lumaStride,
            out IntPtr chroma, out int chromaStride,
            out int format, out int lost, out int superseded);

        frame = new DecodedFrame(w, h, luma, lumaStride, chroma, chromaStride, format, lost, superseded);
        return nv12;
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        // Cleared BEFORE the handle goes, and before the decoder does. libchiaki's thread may be
        // inside the callback right now, and a SetEvent on a closed handle is the one failure here
        // that is a crash rather than a silence.
        DecoderSetReadyEvent(handle, IntPtr.Zero);

        DecoderFree(handle);
        handle = IntPtr.Zero;

        Ready.Dispose();
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_set_ready_event",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void DecoderSetReadyEvent(IntPtr decoder, IntPtr readyEvent);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_pull",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DecoderPull(
        IntPtr decoder,
        out int w, out int h,
        out IntPtr luma, out int lumaStride,
        out IntPtr chroma, out int chromaStride,
        out int format, out int lost, out int superseded);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_create",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr DecoderCreate(
        IntPtr log, int codec, int maxFps, string? hardwareName, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void DecoderFree(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_frames_available",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong DecoderFramesAvailable(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_frames_decoded",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong DecoderFramesDecoded(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_pixel_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DecoderPixelFormat(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_pixel_format_name",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DecoderPixelFormatName(IntPtr decoder, byte[] buffer, int size);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_copies_every_frame",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DecoderCopiesEveryFrame(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_frame_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DecoderFrameFormat(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_pixel_format_name",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int FormatName(int format, byte[] buffer, int size);

    /// <summary>
    /// Installs a decoder as a session's video sink.
    /// </summary>
    /// <remarks>
    /// BEFORE THE START, always. The field is read by the stream connection's own thread, and
    /// installing it after that thread exists is a race whose losing side is a session that decodes
    /// nothing - which is indistinguishable from PP700's original state.
    /// </remarks>
    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_set_decoder",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AttachTo(IntPtr session, IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_decoder_video_sample",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DecoderVideoSample(
        IntPtr decoder, IntPtr buf, int bufSize, int framesLost, [MarshalAs(UnmanagedType.I1)] bool frameRecovered);
}


