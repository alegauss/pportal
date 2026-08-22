using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// PP9: the renderer's seam, which is a second DLL and not more of the first.
///
/// chiaki-shim.dll is loaded by every run of the selftest, on machines with no GPU and in CI.
/// Linking libplacebo into it would make a graphics driver a precondition for parsing a discovery
/// reply. So the renderer has its own DLL, its own ABI, and its own resolver entry - and a host
/// that never draws anything never loads it.
///
/// What this carries so far is a probe rather than a renderer. PP9 chose libplacebo on D3D11 from
/// the source alone: the shaders live above pl_gpu, and the only backend calls without a D3D11
/// counterpart are the ones handing frames to QtQuick, which the port does not have. That was a
/// decision taken without building anything, which is what this answers.
/// </summary>
public static class ChiakiRender
{
    /// <summary>The name the resolver in <see cref="ChiakiNative"/> maps to chiaki-render.dll.</summary>
    internal const string Library = "chiaki-render";

    /// <summary>Must equal CHIAKI_RENDER_ABI in shim/chiaki_render.h. Independent of the shim's.</summary>
    public const uint ExpectedAbi = 7;

    [DllImport(Library, EntryPoint = "chiaki_render_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint AbiVersion();

    /// <summary>
    /// Whether this libplacebo was built with the D3D11 backend, which PP9's decision rests on.
    ///
    /// A property of the copy that was linked, not of the project - so a toolchain that dropped
    /// it should say so here rather than at the first frame of a session.
    /// </summary>
    [DllImport(Library, EntryPoint = "chiaki_render_has_d3d11", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool HasD3d11();

    [DllImport(Library, EntryPoint = "chiaki_render_has_vulkan", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool HasVulkan();

    /// <summary>
    /// A libplacebo D3D11 device, or null.
    ///
    /// <paramref name="forceSoftware"/> selects the WARP adapter, which is what makes this
    /// answerable on a machine with no GPU: a CI runner still gets a real pl_gpu, so "the backend
    /// does not work" and "this box has no hardware" stop being the same answer.
    /// </summary>
    public static RenderDevice? CreateD3d11(bool forceSoftware = false)
    {
        IntPtr handle = D3d11Create(forceSoftware);
        return handle == IntPtr.Zero ? null : new RenderDevice(handle);
    }

    [DllImport(Library, EntryPoint = "chiaki_render_d3d11_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr D3d11Create([MarshalAs(UnmanagedType.I1)] bool forceSoftware);
}

/// <summary>
/// What libplacebo will do with the shared texture, resolved by pl_d3d11_wrap from the D3D11
/// flags it was created with. Separating these is what distinguishes "libplacebo cannot use this
/// texture" from "the draw did not land", which one boolean cannot.
/// </summary>
[Flags]
public enum ShareCaps
{
    None = 0,
    /// <summary>It can be drawn into - pl_tex_clear is a blit, so this is what the renderer needs.</summary>
    BlitDst = 1,
    /// <summary>It can be read back, which a renderer never needs and a check does.</summary>
    HostReadable = 2,
    /// <summary>pl_d3d11_wrap accepted it at all - without this the rest mean nothing.</summary>
    Wrapped = 4,
    Renderable = 8,
    Sampleable = 16,
}

/// <summary>
/// PP163: what a composition swapchain was asked to be, in DXGI's own numbers.
///
/// The two the question turns on. Passed as the DXGI value rather than as a name of this port's,
/// because the answer comes back from DXGI about that exact format.
/// </summary>
public enum SwapchainFormat
{
    /// <summary>DXGI_FORMAT_B8G8R8A8_UNORM - the eight bits D3DImage stops at.</summary>
    Bgra8 = 87,

    /// <summary>DXGI_FORMAT_R10G10B10A2_UNORM - the ten HDR needs.</summary>
    Rgb10A2 = 24,

    /// <summary>DXGI_FORMAT_R16G16B16A16_FLOAT - the wide one scRGB uses.</summary>
    Rgba16Float = 10,
}

/// <summary>Which step of building a composition swapchain failed, or Ok.</summary>
public enum SwapchainStage
{
    Ok = 0,
    NoDevice,
    DxgiDevice,
    Adapter,
    Factory,
    /// <summary>CreateSwapChainForComposition itself, which is where a refused format fails.</summary>
    Create,
    Query3,
}

/// <summary>What DXGI says a swapchain will present.</summary>
/// <param name="Created">Whether the swapchain exists at all.</param>
/// <param name="Hdr10">Whether it accepts ST.2084 with BT.2020 primaries - the HDR10 signal.</param>
/// <param name="Srgb">Whether it accepts the ordinary SDR one, so a false above means something.</param>
/// <param name="ScRgb">Whether it accepts the linear space a float buffer carries HDR in.</param>
/// <param name="Stage">Where it stopped, when it did.</param>
public readonly record struct SwapchainSupport(
    bool Created, bool Hdr10, bool Srgb, bool ScRgb, SwapchainStage Stage);

/// <summary>
/// PP281: which step of the DirectComposition path failed, or Ok.
///
/// Longer than the swapchain's because the path is longer, and the order is the reading: anything
/// up to and including <see cref="Swapchain"/> is a failure the swapchain probe would have caught
/// too, and only <see cref="Content"/> onwards is news about DirectComposition itself.
/// </summary>
public enum DcompStage
{
    Ok = 0,
    NoDevice,
    DxgiDevice,
    Adapter,
    Factory,
    Swapchain,
    /// <summary>The hidden top-level window - CreateTargetForHwnd refuses a message-only one.</summary>
    Window,
    Device,
    Target,
    Visual,
    /// <summary>SetContent with the swapchain, which is the claim PP163 made without measuring.</summary>
    Content,
    Root,
    /// <summary>Commit, where the compositor accepts the tree rather than handing out interfaces.</summary>
    Commit,
}

/// <summary>Which step of a frame's journey through the renderer failed, or Ok.</summary>
public enum RenderStage
{
    Ok = 0,
    NoDevice,
    /// <summary>CreateTexture2D of the NV12 array.</summary>
    Texture,
    /// <summary>pl_d3d11_wrap for the luma plane's R8 view.</summary>
    Luma,
    /// <summary>pl_d3d11_wrap for the chroma plane's R8G8 view.</summary>
    Chroma,
    /// <summary>pl_tex_create for the readable target.</summary>
    Target,
    Renderer,
    /// <summary>pl_render_image itself.</summary>
    Render,
    /// <summary>pl_tex_download of the pixel produced.</summary>
    Download,
}

/// <summary>
/// PP11: what a shared surface is made of.
///
/// Two pairings, each a DXGI format and the D3D9 name with the same bytes in the same order. The
/// ten-bit one is the question HDR asks of PP9's decision, and the pairing is the trap inside it:
/// DXGI's R10G10B10A2 matches D3DFMT_A2B10G10R10, whose letters read backwards.
/// </summary>
public enum ShareFormat
{
    /// <summary>The eight-bit share PP131 built and PP135 handed to WPF.</summary>
    Bgra8 = 0,

    /// <summary>Ten bits per channel, which is the floor for an HDR picture.</summary>
    Rgb10A2 = 1,
}

/// <summary>Which step of the D3D11-to-D3D9Ex share failed, or Ok.</summary>
public enum ShareStage
{
    Ok = 0,
    NoDevice,
    Texture,
    Query,
    Handle,
    D3d9,
    Open,
    Surface,
}

/// <summary>
/// PP131: a D3D11 texture opened again as the IDirect3DSurface9 D3DImage takes.
///
/// WPF composes through D3D9Ex and D3DImage accepts nothing else, so this hop is not an
/// optimisation - it is the whole path from libplacebo to a window. PP9 named it as an accepted
/// cost and an accepted risk; naming a risk is not measuring it.
///
/// The surface here is never handed to WPF. What is being answered is whether the pointer can
/// exist at all, which is the half that fails in a driver rather than in a dispatcher.
/// </summary>
public sealed class SharedSurface : IDisposable
{
    private IntPtr _handle;

    private SharedSurface(IntPtr handle) => _handle = handle;

    /// <summary>
    /// Shares a texture out of a libplacebo D3D11 device. Null when any step fails, with
    /// <paramref name="stage"/> naming which - because "sharing did not work" is the answer this
    /// exists to improve on.
    /// </summary>
    public static SharedSurface? Create(RenderDevice device, int width, int height, out ShareStage stage)
        => Create(device, width, height, ShareFormat.Bgra8, out stage);

    /// <summary>
    /// PP11: the same share in a chosen format, which is how the HDR question is asked.
    ///
    /// <see cref="ShareFormat.Rgb10A2"/> is the one HDR would need. Whether it survives the whole
    /// chain - D3D11, the shared handle, D3D9Ex, and then WPF - is a measurement rather than a
    /// reading, and the stage says where it stopped if it does not.
    /// </summary>
    public static SharedSurface? Create(
        RenderDevice device, int width, int height, ShareFormat format, out ShareStage stage)
    {
        ArgumentNullException.ThrowIfNull(device);

        IntPtr handle = ShareToD3d9Format(device.Raw, width, height, (int)format, out int raw);
        stage = (ShareStage)raw;
        return handle == IntPtr.Zero ? null : new SharedSurface(handle);
    }

    /// <summary>The IDirect3DSurface9 D3DImage.SetBackBuffer would be given.</summary>
    public IntPtr Surface => ShareSurface(_handle);

    /// <summary>Whether DXGI produced a shared handle, which is what D3D9Ex is asked to open.</summary>
    public bool HasSharedHandle => ShareHasHandle(_handle);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        ShareDestroy(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_to_d3d9_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ShareToD3d9Format(
        IntPtr d3d11, int width, int height, int format, out int stage);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_surface",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ShareSurface(IntPtr share);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_has_handle",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ShareHasHandle(IntPtr share);

    /// <summary>
    /// PP132: clears the shared texture through libplacebo and reads the result back.
    ///
    /// The last link. The device exists and the texture reaches D3D9Ex; what neither says is that
    /// libplacebo can render into THAT texture - pl_d3d11_wrap refuses an incompatible format or
    /// flag - and that the result lands in the bytes the shared handle points at rather than in a
    /// copy of them. Reading back is the whole point: a wrap that drew somewhere else would pass
    /// every check that stopped at a return value.
    /// </summary>
    /// <returns>
    /// The pixel at the origin as B,G,R,A - the texture's order and not the argument's, which is
    /// the difference a renderer gets wrong once and then cannot see.
    /// </returns>
    public byte[]? ClearAndRead(RenderDevice device, float r, float g, float b, float a, out ShareCaps caps)
    {
        ArgumentNullException.ThrowIfNull(device);

        float[] rgba = [r, g, b, a];
        var pixel = new byte[4];
        bool ok = ShareClearAndRead(device.Raw, _handle, rgba, pixel, out int raw);
        caps = (ShareCaps)raw;
        return ok ? pixel : null;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_clear_and_read",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ShareClearAndRead(
        IntPtr d3d11, IntPtr share, float[] rgba, byte[] pixel, out int caps);

    /// <summary>
    /// PP133: runs pl_render_image into the shared texture - the call the port makes per frame.
    ///
    /// With a NULL image, which is not a shortcut: qmlmainwindow.cpp makes exactly that call when
    /// it has no new frame to show. So this exercises the renderer, the target frame and the
    /// wrapped texture together, without needing a decoder to have produced anything.
    ///
    /// There is no swapchain involved and there is not meant to be. The Qt client builds its
    /// target with pl_frame_from_swapchain because it presents to a window itself; here WPF
    /// presents, from the shared surface, so the target is the texture directly.
    /// </summary>
    public bool Render(RenderDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return ShareRender(device.Raw, _handle);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_render",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ShareRender(IntPtr d3d11, IntPtr share);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_share_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShareDestroy(IntPtr share);
}

/// <summary>One libplacebo D3D11 device, and the limits a renderer would be written against.</summary>
public sealed class RenderDevice : IDisposable
{
    private IntPtr _handle;

    internal RenderDevice(IntPtr handle) => _handle = handle;

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(RenderDevice));

    /// <summary>The native device, for the share below. Internal: this class owns its lifetime.</summary>
    internal IntPtr Raw => Handle;

    /// <summary>What libplacebo says it is, for a log line that names a version rather than a guess.</summary>
    public string Description => Marshal.PtrToStringUTF8(D3d11Description(Handle)) ?? "";

    /// <summary>
    /// PP163: what a composition swapchain in this format will present.
    ///
    /// The path D3DImage is not. HDR asks two things of it and they are asked separately: whether
    /// the ten-bit buffer can exist, and whether DXGI will carry an ST.2084 signal in it. A port
    /// that stopped at the first would have a deeper buffer showing the same SDR picture.
    /// </summary>
    public SwapchainSupport ProbeSwapchain(SwapchainFormat format)
    {
        bool created = SwapchainProbe(
            Handle, (int)format, out bool hdr10, out bool srgb, out bool scrgb, out int stage);

        return new SwapchainSupport(created, hdr10, srgb, scrgb, (SwapchainStage)stage);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_swapchain_probe",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SwapchainProbe(
        IntPtr d3d11, int format,
        [MarshalAs(UnmanagedType.I1)] out bool hdr10,
        [MarshalAs(UnmanagedType.I1)] out bool srgb,
        [MarshalAs(UnmanagedType.I1)] out bool scrgb,
        out int stage);

    /// <summary>
    /// PP281: and whether DirectComposition takes that swapchain as a visual's content.
    ///
    /// PP163 priced the buffer and asserted the rest. It measured that a composition swapchain
    /// carries ten bits and an ST.2084 signal, then said - without measuring - that a
    /// DirectComposition visual composes a swapchain with WPF content above it. That sentence is
    /// what the whole decision rests on, and it is the reason the child-HWND path is rejected.
    ///
    /// This is the half that does not need WPF: device, target, visual, content, root, commit. A
    /// failure here would end the argument before WPF is even asked.
    /// </summary>
    /// <returns>The stage it reached, which is <see cref="DcompStage.Ok"/> only after Commit.</returns>
    public DcompStage ProbeDirectComposition(SwapchainFormat format)
    {
        bool committed = DcompProbe(Handle, (int)format, out int stage);
        return committed ? DcompStage.Ok : (DcompStage)stage;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_dcomp_probe",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DcompProbe(IntPtr d3d11, int format, out int stage);

    /// <summary>
    /// PP9: one decoded frame through pl_render_image, and the pixel it produced.
    ///
    /// The arguments are the console's own encoding rather than RGB - 16/128/128 is black and
    /// 235/128/128 is white - because that is what a decoder hands over, and converting it is the
    /// half of the renderer nothing before this exercised.
    ///
    /// The frame is an NV12 texture ARRAY and the slice read is not the first, which is the shape
    /// a d3d11va decoder actually produces. Slice 0 holds black, so a renderer that ignored the
    /// index would answer black to every question asked here.
    /// </summary>
    /// <returns>Four bytes at the origin as R,G,B,A, or null with <paramref name="stage"/> saying where it stopped.</returns>
    public byte[]? RenderNv12(byte luma, byte cb, byte cr, out RenderStage stage)
    {
        var pixel = new byte[4];
        bool ok = FrameNv12(Handle, luma, cb, cr, pixel, out int raw);
        stage = (RenderStage)raw;
        return ok ? pixel : null;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_frame_nv12",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool FrameNv12(
        IntPtr d3d11, byte luma, byte cb, byte cr, byte[] rgba, out int stage);

    /// <summary>
    /// The limits a stream is judged against. A maximum 2D texture below 3840 refuses a 4K
    /// stream, and finding that out at the first frame is finding it out from a user.
    /// </summary>
    public (int MaxTexture2D, int MaxBufferBytes) Limits()
    {
        if (!D3d11Limits(Handle, out int maxTexture, out int maxBuffer))
            throw new InvalidOperationException("chiaki_render_d3d11_limits failed.");

        return (maxTexture, maxBuffer);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        D3d11Destroy(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void D3d11Destroy(IntPtr d3d11);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_limits",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool D3d11Limits(IntPtr d3d11, out int maxTexture2D, out int maxBufferBytes);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_description",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr D3d11Description(IntPtr d3d11);
}

