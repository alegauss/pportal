using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// PP700: the renderer that lives between frames, which the port's one-frame probe was not.
///
/// <see cref="RenderDevice"/> proved libplacebo runs here, <see cref="SharedSurface"/> proved WPF
/// takes the texture, and chiaki_render_frame_nv12 proved a decoded frame can be converted and
/// read back. All three build everything they need and destroy it again, which is right for a probe
/// and impossible for a stream: a texture, two plane wraps and a renderer per frame is the whole
/// cost of rendering paid sixty times a second to draw one picture.
///
/// So this holds them, and renders into the SHARED texture rather than a readable one. PP132
/// measured that a shared texture cannot be host_readable, so the texture that can be shown and the
/// texture that can be checked are two different textures - and a presenter wants the first.
///
/// WHAT A CALLER OWES IT: even dimensions, and the decoder's two strides rather than the picture's
/// width. An AVFrame's planes are usually wider than the picture and an odd size puts the chroma
/// plane half a sample out, which reads as a one-pixel colour fringe rather than as an error.
/// </summary>
public sealed class VideoPresenter : IDisposable
{
    private IntPtr handle;

    private VideoPresenter(IntPtr handle, int width, int height)
    {
        this.handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>The picture's size, which every frame handed over has to match.</summary>
    public int Width { get; }

    /// <summary>And its height.</summary>
    public int Height { get; }

    /// <summary>
    /// Build a presenter over a device and the share it draws into, or null with the stage.
    /// </summary>
    /// <param name="device">The libplacebo D3D11 device.</param>
    /// <param name="surface">The share whose texture is the target, and whose surface WPF shows.</param>
    /// <param name="width">Even, and the decoded picture's own width.</param>
    /// <param name="height">Even.</param>
    /// <param name="stage">Which step failed, where one did.</param>
    public static VideoPresenter? Create(
        RenderDevice device, SharedSurface surface, int width, int height, out RenderStage stage)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);

        IntPtr made = VideoCreate(device.Raw, surface.Raw, width, height, out int raw);
        stage = (RenderStage)raw;

        return made == IntPtr.Zero ? null : new VideoPresenter(made, width, height);
    }

    /// <summary>How many frames have been rendered, which is the number a run reports.</summary>
    public ulong Frames => handle == IntPtr.Zero ? 0 : VideoFrames(handle);

    /// <summary>
    /// Render one decoded frame's NV12 planes into the shared texture.
    /// </summary>
    /// <param name="luma">The Y plane, as the decoder holds it.</param>
    /// <param name="lumaStride">Its stride, which is usually wider than the picture.</param>
    /// <param name="chroma">The interleaved CbCr plane.</param>
    /// <param name="chromaStride">And its own stride.</param>
    /// <param name="stage">Which step failed, where one did.</param>
    public bool Render(
        IntPtr luma, int lumaStride, IntPtr chroma, int chromaStride, out RenderStage stage)
    {
        if (handle == IntPtr.Zero)
        {
            stage = RenderStage.NoDevice;
            return false;
        }

        bool ok = VideoFrame(handle, luma, lumaStride, chroma, chromaStride, out int raw);
        stage = (RenderStage)raw;
        return ok;
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        VideoDestroy(handle);
        handle = IntPtr.Zero;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_video_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr VideoCreate(
        IntPtr d3d11, IntPtr share, int width, int height, out int stage);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_video_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void VideoDestroy(IntPtr video);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_video_frames",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong VideoFrames(IntPtr video);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_video_frame",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool VideoFrame(
        IntPtr video, IntPtr luma, int lumaStride, IntPtr chroma, int chromaStride, out int stage);
}

