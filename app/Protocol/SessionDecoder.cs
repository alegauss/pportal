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

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        DecoderFree(handle);
        handle = IntPtr.Zero;
    }

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
}
