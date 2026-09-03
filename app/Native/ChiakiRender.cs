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

/// <summary>PP53: which step of the tearing probe failed, or Ok.</summary>
public enum TearingStage
{
    Ok = 0,
    NoDevice,
    DxgiDevice,
    Adapter,
    /// <summary>IDXGIFactory5, which is the interface the feature query lives on.</summary>
    Factory,
    /// <summary>The hidden window the control swapchain needs; the composition one needs none.</summary>
    Window,
}

/// <summary>
/// PP53: what this machine will let a present tear on.
///
/// Three answers rather than one, because a single boolean cannot be read. Tearing is how an
/// application asks a variable refresh display to show a frame when it arrives instead of at the
/// next vblank - there is no API called VRR, there is a swapchain flag and a present flag.
/// </summary>
/// <param name="Ran">Whether every step ran. False means the probe stopped, not that tearing is off.</param>
/// <param name="Adapter">What IDXGIFactory5 says the machine supports at all.</param>
/// <param name="Composition">
/// Whether a COMPOSITION swapchain - PP319's choice for the video plane - presents with the flag.
/// </param>
/// <param name="Hwnd">
/// The control: the same request on an ordinary HWND flip swapchain. Without it, a false above
/// cannot be told from a machine that refuses tearing everywhere.
/// </param>
/// <param name="Refused">
/// The negative control, and what makes the rest worth anything: whether DXGI REFUSES the tearing
/// present on a swapchain created without the flag. False means DXGI is not reading these flags,
/// and every true above is a call that succeeded by doing nothing.
/// </param>
/// <param name="Stage">Where it stopped, when it did.</param>
public readonly record struct TearingSupport(
    bool Ran, bool Adapter, bool Composition, bool Hwnd, bool Refused, TearingStage Stage);

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

/// <summary>
/// PP319: which step of the TWO-layer tree failed, or Ok.
///
/// <see cref="Tree"/> is everything <see cref="DcompStage"/> already covers, collapsed into one
/// value: a failure there is PP281's question and it has a probe of its own that answers it in its
/// own vocabulary. Everything after it is new.
/// </summary>
public enum LayersStage
{
    Ok = 0,
    NoDevice,
    /// <summary>The hidden top-level window, as the single-layer probe needs one too.</summary>
    Window,
    /// <summary>The whole single-layer path below this one - PP281's, and not re-reported here.</summary>
    Tree,
    /// <summary>CreateSurface for the overlay, which is where a refused overlay format fails.</summary>
    Surface,
    /// <summary>BeginDraw - the step that makes it a surface the compositor has anything for.</summary>
    Begin,
    Rtv,
    End,
    Visual,
    /// <summary>SetContent with a SURFACE rather than a swapchain, which nothing before asked.</summary>
    Content,
    /// <summary>AddVisual twice, the overlay named above the video rather than merely added second.</summary>
    Order,
    Root,
    /// <summary>Commit, where the compositor accepts two layers of different formats or does not.</summary>
    Commit,
}

/// <summary>
/// PP284: a live DirectComposition tree on somebody else's window.
///
/// Disposable because it outlives the call that made it, which is the entire difference between
/// this and the probes. The window is NOT owned - detaching leaves it standing, because the caller
/// is WPF and it is still using it.
/// </summary>
public sealed class DcompAttachment(IntPtr session) : IDisposable
{
    private IntPtr session = session;

    /// <summary>Tears the tree down. Idempotent, because a window closing may race a dispose.</summary>
    public void Dispose()
    {
        if (session == IntPtr.Zero)
            return;

        IntPtr going = session;
        session = IntPtr.Zero;
        Detach(going);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_dcomp_detach",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void Detach(IntPtr session);
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
    /// PP53: what this machine will let a present tear on, which is what variable refresh needs.
    ///
    /// A frame from a console arrives when the network delivers it, and a fixed-refresh present
    /// rounds every one up to the next vblank - up to 16ms at 60Hz on top of a frame that already
    /// crossed a network. Variable refresh is the answer and this is how it is asked for: a
    /// flip-model swapchain carrying DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING, presented at sync interval
    /// zero with DXGI_PRESENT_ALLOW_TEARING.
    ///
    /// The composition answer is the one PP53 turns on, because PP319 chose a composition swapchain
    /// for the video plane. The other two are what make it readable.
    /// </summary>
    public TearingSupport ProbeTearing()
    {
        bool ran = TearingProbe(
            Handle, out bool adapter, out bool composition, out bool hwnd, out bool refused,
            out int stage);

        return new TearingSupport(ran, adapter, composition, hwnd, refused, (TearingStage)stage);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_tearing_probe",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TearingProbe(
        IntPtr d3d11,
        [MarshalAs(UnmanagedType.I1)] out bool adapter,
        [MarshalAs(UnmanagedType.I1)] out bool composition,
        [MarshalAs(UnmanagedType.I1)] out bool hwnd,
        [MarshalAs(UnmanagedType.I1)] out bool refused,
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
    /// <param name="format">The swapchain's format. Ten bits is the one HDR needs.</param>
    /// <param name="topmost">
    /// PP282: whether the visual tree sits ON TOP of the window's own content (true) or BEHIND it
    /// (false). False is the arrangement PP163's design rests on - the video plane below, PP10's
    /// XAML overlay above it - and true is the one that hides that overlay. PP281 measured only
    /// true, which answered a question the design does not ask.
    /// </param>
    /// <returns>The stage it reached, which is <see cref="DcompStage.Ok"/> only after Commit.</returns>
    public DcompStage ProbeDirectComposition(SwapchainFormat format, bool topmost)
    {
        bool committed = DcompProbe(Handle, (int)format, topmost, out int stage);
        return committed ? DcompStage.Ok : (DcompStage)stage;
    }

    /// <summary>
    /// PP283: the same path over a window the caller owns, which is the only way to ask it of WPF.
    ///
    /// <see cref="ProbeDirectComposition"/> builds its own window with WS_EX_NOREDIRECTIONBITMAP -
    /// per-pixel alpha and no redirection surface, exactly what a composed visual wants. A WPF
    /// window is not that window: it owns a redirection bitmap DWM composes, which is the reason
    /// PP10's overlay works at all. Whether DirectComposition binds a target to one is a different
    /// question from whether it binds to a window built to suit it.
    ///
    /// This is the narrow half of what PP163 has left - not what WPF DRAWS over the visual, which
    /// needs a screenshot, but whether the compositor takes the tree on that HWND at all.
    /// </summary>
    /// <param name="hwnd">The window's handle. It is not destroyed; the caller still owns it.</param>
    public DcompStage ProbeDirectCompositionOn(IntPtr hwnd, SwapchainFormat format, bool topmost)
    {
        bool committed = DcompProbeHwnd(Handle, (int)format, topmost, hwnd, out int stage);
        return committed ? DcompStage.Ok : (DcompStage)stage;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_dcomp_probe",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DcompProbe(
        IntPtr d3d11, int format, [MarshalAs(UnmanagedType.I1)] bool topmost, out int stage);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_dcomp_probe_hwnd",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DcompProbeHwnd(
        IntPtr d3d11, int format, [MarshalAs(UnmanagedType.I1)] bool topmost,
        IntPtr hwnd, out int stage);

    /// <summary>
    /// PP284: the same tree, kept, with the buffer filled - so a person can look at the answer.
    ///
    /// What is left of PP163 is what WPF DRAWS over the visual, and that is a fact about pixels. A
    /// composed window does not screenshot reliably, so a test claiming to read one would be
    /// reporting on its own capture stack rather than on the compositor. This builds the apparatus
    /// and leaves the reading to eyes.
    /// </summary>
    /// <returns>A live tree to dispose, or null with <paramref name="stage"/> saying where it stopped.</returns>
    public DcompAttachment? AttachDirectComposition(
        IntPtr hwnd, SwapchainFormat format, bool topmost,
        double red, double green, double blue, out DcompStage stage)
    {
        IntPtr session = DcompAttach(
            Handle, (int)format, topmost, hwnd, (float)red, (float)green, (float)blue, out int raw);

        stage = session == IntPtr.Zero ? (DcompStage)raw : DcompStage.Ok;
        return session == IntPtr.Zero ? null : new DcompAttachment(session);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_dcomp_attach",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DcompAttach(
        IntPtr d3d11, int format, [MarshalAs(UnmanagedType.I1)] bool topmost, IntPtr hwnd,
        float r, float g, float b, out int stage);

    /// <summary>
    /// PP319: the overlay ABOVE the video, as a second visual rather than as WPF content.
    ///
    /// PP284 read the pixel: the tree covered the whole client area with topmost false and true
    /// alike. That is not a flag being ignored - CreateTargetForHwnd orders the tree against the
    /// window's CHILD WINDOWS, and a redirection bitmap is not a child, so WPF's own drawing is
    /// under the tree either way. The arrangement PP163 wanted does not exist.
    ///
    /// What is left that keeps both is this one: the overlay in the compositor's tree, above the
    /// video. Two claims, and only the first has been measured before - that a swapchain is
    /// content, and that a surface of a DIFFERENT format composes over it in the same tree.
    /// </summary>
    /// <param name="format">The video plane's. Ten bits is the one the whole question is about.</param>
    /// <param name="overlayFormat">
    /// The overlay surface's, asked separately: the interesting answer is the one where the two
    /// differ, and passing the same value for both confuses "they compose" with "they match".
    /// </param>
    public LayersStage ProbeLayers(SwapchainFormat format, SwapchainFormat overlayFormat)
    {
        bool committed = LayersProbe(Handle, (int)format, (int)overlayFormat, out int stage);
        return committed ? LayersStage.Ok : (LayersStage)stage;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_layers_probe",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool LayersProbe(IntPtr d3d11, int format, int overlayFormat, out int stage);

    /// <summary>
    /// PP322: the same two-layer tree, kept, both planes filled - which is the apparatus the choice
    /// is read from rather than argued from.
    ///
    /// <see cref="ProbeLayers"/> measures that the compositor ACCEPTS the tree, and PP319 chose on
    /// that. It is the same depth PP281 to PP283 reached one layer down, and PP284 then read a pixel
    /// none of them had predicted - so the acceptance is not the answer here either.
    ///
    /// The overlay is drawn in two halves, one opaque and one at half alpha. The second half is the
    /// question no return value reports: whether a premultiplied surface blends once or twice over
    /// the plane below, which is not an error anywhere and looks like a slightly wrong colour.
    /// </summary>
    /// <returns>A live tree to dispose, or null with <paramref name="stage"/> saying where it stopped.</returns>
    public DcompAttachment? AttachLayers(
        IntPtr hwnd, SwapchainFormat format, SwapchainFormat overlayFormat,
        double red, double green, double blue, out LayersStage stage)
    {
        IntPtr session = LayersAttach(
            Handle, (int)format, (int)overlayFormat, hwnd,
            (float)red, (float)green, (float)blue, out int raw);

        stage = session == IntPtr.Zero ? (LayersStage)raw : LayersStage.Ok;
        return session == IntPtr.Zero ? null : new DcompAttachment(session);
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_layers_attach",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LayersAttach(
        IntPtr d3d11, int format, int overlayFormat, IntPtr hwnd,
        float r, float g, float b, out int stage);

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

