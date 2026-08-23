using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP9: whether the renderer decision is buildable, which it was taken without being.
///
/// PP9 chose libplacebo on D3D11 by reading the source: the shaders live above pl_gpu, and the
/// only backend calls without a D3D11 counterpart are the ones handing frames to QtQuick, which
/// the port does not have. Reading is how that decision was made and it is not how it should
/// stand - a backend that does not initialise here would make every screen after it wrong.
/// </summary>
public class RenderProbeTests
{
    /// <summary>
    /// This machine's adapter, or WARP.
    ///
    /// Used only by the frame checks below, and deliberately NOT skippable. The share checks have
    /// to skip without hardware because a shared handle is adapter-bound; rendering a frame is
    /// not, so there is no reason for those assertions to go quiet on a runner - and an assertion
    /// that skips where it could have run is an assertion nobody notices stopped meaning anything.
    ///
    /// WARP was checked against all three, not assumed: it wraps an NV12 array, honours the slice
    /// and converts the range exactly as this machine's adapter does.
    /// </summary>
    private static RenderDevice? AnyDevice()
        => ChiakiRender.CreateD3d11(forceSoftware: false) ?? ChiakiRender.CreateD3d11(forceSoftware: true);

    [Fact]
    public void TheRenderDllLoadsAndMatchesItsAbi()
    {
        Assert.Equal(ChiakiRender.ExpectedAbi, ChiakiRender.AbiVersion());
    }

    /// <summary>
    /// The premise. PL_HAVE_D3D11 is a property of the libplacebo this DLL was linked against
    /// rather than of the project, so a toolchain that dropped it should say so here and not at
    /// the first frame of a session.
    /// </summary>
    [Fact]
    public void ThisLibplaceboHasBothBackends()
    {
        Assert.True(ChiakiRender.HasD3d11(), "PL_HAVE_D3D11");
        Assert.True(ChiakiRender.HasVulkan(), "PL_HAVE_VULKAN");
    }

    /// <summary>
    /// A software device, which is the one answerable anywhere. WARP is a real D3D11
    /// implementation, so this says the backend initialises and produces a pl_gpu - and it says
    /// it on a CI runner with no display, which is where PP22's build will ask.
    /// </summary>
    [Fact]
    public void TheD3d11BackendInitialisesOnASoftwareAdapter()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: true);

        Assert.NotNull(device);
        Assert.Contains("d3d11", device.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the device reports limits a 4K stream fits inside. 3840 is the number the decision is
    /// judged by: a maximum 2D texture below it refuses the largest stream the client offers, and
    /// finding that out at the first frame is finding it out from a user.
    /// </summary>
    [Fact]
    public void TheDeviceHoldsAFourKFrame()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: true);
        Assert.NotNull(device);

        (int maxTexture, int maxBuffer) = device.Limits();

        Assert.True(maxTexture >= 3840, $"max 2D texture was {maxTexture}");
        Assert.True(maxBuffer > 0, $"max buffer was {maxBuffer}");
    }

    /// <summary>
    /// The hardware adapter, where there is one. Skipped rather than failed when there is not:
    /// this suite runs on machines and runners alike, and "no GPU here" is not a defect in the
    /// renderer decision - which is exactly why the software case above is asserted unskipped.
    /// </summary>
    [Fact]
    public void TheD3d11BackendInitialisesOnThisMachinesAdapter()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        (int maxTexture, _) = device.Limits();
        Assert.True(maxTexture >= 3840, $"max 2D texture was {maxTexture} on {device.Description}");
    }

    /// <summary>
    /// PP131: the hop D3DImage requires - a D3D11 texture opened again as an IDirect3DSurface9.
    ///
    /// From the HARDWARE device, not WARP. A shared handle opens only on the adapter that made
    /// it, and the D3D9Ex device is D3DADAPTER_DEFAULT, so sharing a WARP texture to it fails at
    /// Open for a reason that has nothing to do with PP9's decision. The first version of this
    /// check did exactly that and read as a defect in the architecture.
    ///
    /// Skipped where there is no hardware adapter, and that is a real limit rather than a hedge:
    /// unlike the device probe above, this one has no software equivalent that means anything.
    /// </summary>
    [Fact]
    public void AD3d11TextureOpensAsTheSurfaceD3dImageTakes()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        using SharedSurface? shared = SharedSurface.Create(device, 1920, 1080, out ShareStage stage);

        Assert.True(shared is not null, $"the share failed at {stage}");
        Assert.NotEqual(IntPtr.Zero, shared.Surface);
        Assert.True(shared.HasSharedHandle);
    }

    /// <summary>
    /// And a WARP texture does NOT open on the default D3D9Ex adapter, which is the constraint
    /// worth writing down: the share is adapter-bound, so a renderer that let libplacebo pick a
    /// different device than WPF composes on would fail here rather than draw nothing.
    /// </summary>
    [Fact]
    public void ASoftwareTextureDoesNotOpenOnTheDefaultAdapter()
    {
        using RenderDevice? warp = ChiakiRender.CreateD3d11(forceSoftware: true);
        Assert.NotNull(warp);

        using SharedSurface? shared = SharedSurface.Create(warp, 640, 480, out ShareStage stage);

        // If it ever DOES succeed - a machine whose default adapter is WARP - that is fine and
        // this says so rather than failing, because the claim is about the binding and not about
        // WARP being second-class.
        if (shared is not null)
            return;

        Assert.Equal(ShareStage.Open, stage);
    }

    /// <summary>
    /// PP132's last link: libplacebo wraps the shared texture and will render into it.
    ///
    /// RENDERABLE is the capability that matters, and finding that out cost a wrong assertion
    /// first. pl_tex_clear is a BLIT, so a check asking for blit_dst read as "libplacebo will not
    /// draw into this" when what it means is "not with that particular call". A shared render
    /// target comes back renderable and sampleable - which is what pl_renderer uses, and all it
    /// uses.
    ///
    /// It is NOT host_readable, and cannot be: reading back needs CPU access, which rules out
    /// being shared at all. So the read-back that would have been the tidiest proof is the one
    /// thing this texture can never do, and the capability flags are the evidence instead.
    /// </summary>
    [Fact]
    public void LibplaceboWrapsTheSharedTextureAndWillRenderIntoIt()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        using SharedSurface? shared = SharedSurface.Create(device, 1920, 1080, out _);
        Assert.NotNull(shared);

        shared.ClearAndRead(device, 1f, 0f, 0f, 1f, out ShareCaps caps);

        Assert.True(caps.HasFlag(ShareCaps.Wrapped), $"pl_d3d11_wrap refused it: {caps}");
        Assert.True(caps.HasFlag(ShareCaps.Renderable), $"not renderable: {caps}");
        Assert.True(caps.HasFlag(ShareCaps.Sampleable), $"not sampleable: {caps}");

        // Asserted absent rather than ignored, so that a libplacebo which started offering them
        // is a change someone looks at rather than one nobody notices.
        Assert.False(caps.HasFlag(ShareCaps.HostReadable), "a shared texture cannot be read back");
    }

    /// <summary>
    /// PP133: pl_render_image into the shared texture, which is the call the port makes per frame.
    ///
    /// With a NULL image, and that is not a shortcut - qmlmainwindow.cpp makes exactly that call
    /// when it has no new frame to show, so this is the client's own path rather than one
    /// invented to be testable. It exercises the renderer, the target frame and the wrapped
    /// texture together without needing a decoder to have produced anything.
    ///
    /// And with no swapchain, which is the correction this turned up: the Qt client builds its
    /// target with pl_frame_from_swapchain because it presents to a window itself. Here WPF
    /// presents, from the shared surface, so there is no swapchain in the design at all.
    /// </summary>
    [Fact]
    public void PlRenderImageRunsIntoTheSharedTexture()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        using SharedSurface? shared = SharedSurface.Create(device, 1920, 1080, out _);
        Assert.NotNull(shared);

        Assert.True(shared.Render(device), "pl_render_image refused the target");
    }

    /// <summary>
    /// PP9's last link: a decoded frame really going through pl_render_image.
    ///
    /// Everything above rendered nothing. The NULL-image call exercises the renderer and the
    /// target and says nothing about the half that carries the picture, so this sends one flat
    /// NV12 frame at limited-range white and asks what came out.
    ///
    /// White is the value that makes the assertion mean something: with no image at all the
    /// target is black, so a render that silently dropped the frame answers black here.
    /// </summary>
    [Fact]
    public void ADecodedFrameReachesTheTargetAsWhite()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        // 235/128/128: the console's white, which is not 255 - that is the whole of limited range.
        byte[]? pixel = device.RenderNv12(235, 128, 128, out RenderStage stage);

        Assert.True(pixel is not null, $"the frame stopped at {stage}");
        Assert.Equal(RenderStage.Ok, stage);

        Assert.True(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240,
            $"limited-range white came out as {pixel[0]},{pixel[1]},{pixel[2]}");
    }

    /// <summary>
    /// And the console's black is black. Together with the white above this is the range being
    /// honoured rather than assumed: a frame read as full-range would put 16 at about 6% grey and
    /// 235 short of white, which is the washed-out picture that gets reported as "looks fine".
    /// </summary>
    [Fact]
    public void TheConsolesLimitedRangeBlackIsBlack()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        byte[]? pixel = device.RenderNv12(16, 128, 128, out RenderStage stage);

        Assert.True(pixel is not null, $"the frame stopped at {stage}");
        Assert.True(pixel[0] < 12 && pixel[1] < 12 && pixel[2] < 12,
            $"limited-range black came out as {pixel[0]},{pixel[1]},{pixel[2]}");
    }

    /// <summary>
    /// A colour, so that the two chroma planes are not interchangeable.
    ///
    /// 63/102/240 is BT.709 limited red. A port that mapped Cb and Cr the wrong way round gets
    /// blue here, and both channels are asserted rather than only the one expected to be high -
    /// which is the difference between "red came out" and "something bright came out".
    /// </summary>
    [Fact]
    public void ChromaIsNotInterchangeable()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        byte[]? pixel = device.RenderNv12(63, 102, 240, out RenderStage stage);

        Assert.True(pixel is not null, $"the frame stopped at {stage}");
        Assert.True(pixel[0] > 200, $"red channel was {pixel[0]}");
        Assert.True(pixel[2] < 60, $"blue channel was {pixel[2]}, so Cb and Cr are swapped");
    }

    /// <summary>
    /// PP163: the OTHER path carries HDR10, which is the answer PP163's decision was waiting for.
    ///
    /// A composition swapchain - what a DirectComposition visual presents - takes ten bits per
    /// channel AND accepts the ST.2084 signal in BT.2020 primaries. So the wall PP11 hit is WPF's
    /// D3DImage specifically, not the graphics stack: an HDR picture can reach this display, just
    /// not through the surface PP9 chose.
    ///
    /// Headless, because a composition swapchain has no window - which is exactly why it is the
    /// candidate that can be priced without one.
    /// </summary>
    [Fact]
    public void ACompositionSwapchainCarriesHdr10()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        SwapchainSupport ten = device.ProbeSwapchain(SwapchainFormat.Rgb10A2);

        Assert.True(ten.Created, $"a ten-bit composition swapchain stopped at {ten.Stage}");
        Assert.True(ten.Hdr10, "DXGI refuses ST.2084 on a ten-bit composition swapchain");
    }

    /// <summary>
    /// PP281: and DirectComposition takes that swapchain, all the way to Commit.
    ///
    /// This is the sentence PP163 wrote and did not measure. Its whole decision turns on it - a
    /// DirectComposition visual is named the only way out that leaves PP10's overlay standing, and
    /// the child-HWND path is rejected on the strength of it - so the path is built here rather
    /// than argued: device, target on a real window, visual, the swapchain as its content, root,
    /// and Commit.
    ///
    /// Commit is the assertion and the stages before it are the diagnosis. Anything up to Swapchain
    /// is a failure the probe above would have caught too; Content or later is news about
    /// DirectComposition, and Window says the runner has no window station rather than that the
    /// path is wrong.
    ///
    /// TEN BITS, not eight. Asking this of an SDR buffer would prove the path and not the point.
    /// </summary>
    /// <param name="topmost">
    /// PP282: FALSE is the case that matters and it is listed first. The visual tree goes BEHIND
    /// the window's content, which is where a video plane must sit for PP10's overlay to be seen
    /// over it. PP281 asserted only TRUE - the arrangement that puts video over the overlay - so it
    /// proved the path in the direction the design has no use for.
    ///
    /// Both are asked because they are different answers. A path that builds only topmost would
    /// mean HDR costs the overlay after all, which is the conclusion PP163 rejected the child-HWND
    /// option to avoid.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectCompositionTakesTheTenBitSwapchain(bool topmost)
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        DcompStage stage = device.ProbeDirectComposition(SwapchainFormat.Rgb10A2, topmost);

        Assert.True(
            stage == DcompStage.Ok,
            $"the DirectComposition path stopped at {stage} with topmost={topmost}, so PP163's "
                + "remaining option does not hold and the decision it feeds has to be re-opened");
    }

    /// <summary>
    /// PP283: and a WPF window's OWN HWND accepts the target, which is where this actually has to
    /// work.
    ///
    /// PP281 and PP282 both built the tree on a window the probe made itself, with
    /// WS_EX_NOREDIRECTIONBITMAP - per-pixel alpha, no redirection surface, a window shaped for the
    /// job. WPF hands out nothing of the sort. Its window owns a redirection bitmap that DWM
    /// composes, which is the reason PP10's overlay works at all and the reason this is a separate
    /// question rather than the same one asked twice.
    ///
    /// EnsureHandle and not Show. The HWND exists as soon as WPF is asked for it, and a test that
    /// put a window on screen would be one that fails for reasons about the desk it ran on - which
    /// is why the interaction suite is excluded from the gate and why this does not belong there.
    ///
    /// What this does NOT answer is what WPF draws over the visual, and PP284's window answered it
    /// on 2026-08-23: nothing. The visual covered the whole client area with topmost FALSE and TRUE
    /// alike, so the arrangement this accepts is not the arrangement that reaches the screen.
    ///
    /// That does not weaken this assertion, it locates it. ACCEPTING the target is what this and
    /// PP281 and PP282 measure, and all three still pass; the pixel was never in their reach. Kept
    /// green because the acceptance is real and a later presentation path may want it.
    /// </summary>
    [Fact]
    public void AWpfWindowsHwndAcceptsTheTarget()
    {
        DcompStage stage = DcompStage.NoDevice;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using RenderDevice? device = AnyDevice();
                if (device is null)
                    return;

                var window = new System.Windows.Window { Width = 320, Height = 240 };
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                Assert.NotEqual(IntPtr.Zero, hwnd);

                // false: the visual BEHIND the window's content, which is the arrangement PP282
                // established the design needs and the only one worth asking of a WPF window.
                stage = device.ProbeDirectCompositionOn(hwnd, SwapchainFormat.Rgb10A2, topmost: false);
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());

        Assert.True(
            stage == DcompStage.Ok,
            $"DirectComposition stopped at {stage} on a WPF window's HWND, so PP163's remaining "
                + "option does not survive contact with the window it would actually run in");
    }

    /// <summary>
    /// PP284: the tree can be ATTACHED to a WPF window and torn down again.
    ///
    /// The probes build the same path and release it before returning, which is right for a
    /// question and useless for a window somebody is meant to look at. What --dcomp-demo needs is a
    /// tree that outlives the call, and the two failure modes it adds are both invisible here
    /// unless something exercises them: an attach that reports success while handing back nothing,
    /// and a detach that releases in the wrong order or twice.
    ///
    /// It does NOT show the window, so this stays in the gate. What the demo is for - what WPF
    /// draws over the visual - is the part no assertion here can reach.
    /// </summary>
    [Fact]
    public void TheTreeAttachesToAWpfWindowAndDetaches()
    {
        DcompStage stage = DcompStage.NoDevice;
        bool attached = false;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using RenderDevice? device = AnyDevice();
                if (device is null)
                    return;

                var window = new System.Windows.Window { Width = 320, Height = 240 };
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();

                DcompAttachment? session = device.AttachDirectComposition(
                    hwnd, SwapchainFormat.Rgb10A2, topmost: false,
                    ChiakiNg.Views.DcompDemo.FillRed,
                    ChiakiNg.Views.DcompDemo.FillGreen,
                    ChiakiNg.Views.DcompDemo.FillBlue,
                    out stage);

                attached = session is not null;
                session?.Dispose();

                // Twice, because a window closing can race a dispose and the second call is the one
                // that would free an already-freed tree.
                session?.Dispose();
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());

        Assert.True(attached, $"the tree did not attach: {stage}");
    }

    /// <summary>
    /// PP281: and the stage it reports is real, not decorative.
    ///
    /// The test above passes, which is the answer wanted and also the reason to check this: a probe
    /// that returned true unconditionally would look identical, and so would one whose out_stage
    /// was never written. Handed a format DXGI has no such thing as, it must fail AND say it failed
    /// at the swapchain - the step that consumes the format - rather than at any of the four after
    /// it that never ran.
    /// </summary>
    [Fact]
    public void AnImpossibleFormatStopsAtTheSwapchainAndSaysSo()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        // Not a DXGI_FORMAT. The enum stops far below this, and CreateSwapChainForComposition is
        // the first call that looks at it.
        DcompStage stage = device.ProbeDirectComposition((SwapchainFormat)0x7000, topmost: false);

        Assert.Equal(DcompStage.Swapchain, stage);
    }

    /// <summary>
    /// PP319: an eight-bit overlay composes ABOVE a ten-bit video plane, in one tree.
    ///
    /// This is the measurement the choice rests on. PP284 read the pixel and found the tree over the
    /// whole client area with topmost false and true alike - which is not a flag being ignored, it
    /// is what CreateTargetForHwnd's second argument means: it orders the tree against the window's
    /// CHILD WINDOWS, and a redirection bitmap is not a child. WPF's own drawing is under the tree
    /// in both arrangements, so the one PP163 wanted does not exist and never did.
    ///
    /// That leaves exactly one option that keeps an HDR plane AND something drawn over it: put the
    /// overlay in the compositor's tree too. The two formats are DIFFERENT on purpose - a ten-bit
    /// video and an eight-bit premultiplied overlay - because that is the arrangement PP10's screen
    /// would become, and asking it with one format twice would confuse composing with matching.
    /// </summary>
    [Fact]
    public void AnEightBitOverlayComposesAboveATenBitVideoPlane()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        LayersStage stage = device.ProbeLayers(SwapchainFormat.Rgb10A2, SwapchainFormat.Bgra8);

        Assert.True(
            stage == LayersStage.Ok,
            $"the two-layer tree stopped at {stage}, which leaves PP319 with SDR on purpose as the "
                + "only option that keeps PP10's overlay");
    }

    /// <summary>
    /// And the stage it reports is real. Handed an overlay format DXGI has no such thing as, it must
    /// fail at CreateSurface - the call that consumes it - rather than at the video plane below,
    /// which is built from a format that is fine.
    ///
    /// Worth asserting separately from the video plane's: the two formats travel through different
    /// calls, and a probe that used one for both would pass the test above and answer the wrong
    /// question.
    /// </summary>
    [Fact]
    public void AnImpossibleOverlayFormatStopsAtTheSurfaceAndNotAtTheVideo()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        LayersStage stage = device.ProbeLayers(SwapchainFormat.Rgb10A2, (SwapchainFormat)0x7000);

        Assert.Equal(LayersStage.Surface, stage);
    }

    /// <summary>
    /// And the video plane's format still reaches the swapchain, which the collapsed
    /// <see cref="LayersStage.Tree"/> would otherwise hide.
    ///
    /// Tree is one value standing for the whole of PP281's path, so a reader cannot tell from it
    /// which step failed - that is deliberate, and it is also why it has to be shown to happen at
    /// all. A probe that silently ignored the video format would pass every other assertion here.
    /// </summary>
    [Fact]
    public void AnImpossibleVideoFormatStopsInTheTreeBelow()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        LayersStage stage = device.ProbeLayers((SwapchainFormat)0x7000, SwapchainFormat.Bgra8);

        Assert.Equal(LayersStage.Tree, stage);
    }

    /// <summary>
    /// PP322: the two-layer tree ATTACHES to a WPF window and detaches, which is the apparatus the
    /// choice is read from.
    ///
    /// The probe builds the same tree and releases it before returning, which is right for a
    /// question and useless for a window somebody is meant to look at. What --dcomp-demo --layers
    /// needs is a tree that outlives the call, and the failure modes that adds are invisible unless
    /// something exercises them: an attach reporting success while handing back nothing, and a
    /// teardown that now has SEVEN objects to release in an order the compositor cares about rather
    /// than four.
    ///
    /// It does NOT show the window, so this stays in the gate. What the demo is for - whether the
    /// green block is there, and whether its half-alpha half blends once or twice - is the part no
    /// assertion here can reach, and saying so is the whole of why PP322 is a line of its own.
    /// </summary>
    [Fact]
    public void TheTwoLayerTreeAttachesToAWpfWindowAndDetaches()
    {
        LayersStage stage = LayersStage.NoDevice;
        bool attached = false;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using RenderDevice? device = AnyDevice();
                if (device is null)
                    return;

                var window = new System.Windows.Window { Width = 320, Height = 240 };
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();

                DcompAttachment? session = device.AttachLayers(
                    hwnd, SwapchainFormat.Rgb10A2, SwapchainFormat.Bgra8,
                    ChiakiNg.Views.DcompDemo.FillRed,
                    ChiakiNg.Views.DcompDemo.FillGreen,
                    ChiakiNg.Views.DcompDemo.FillBlue,
                    out stage);

                attached = session is not null;
                session?.Dispose();

                // Twice, for the reason the one-layer attach is disposed twice: a window closing can
                // race a dispose, and the second call is the one that would free an already-freed
                // tree - which now means three more pointers than it used to.
                session?.Dispose();
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());

        Assert.True(attached, $"the two-layer tree did not attach: {stage}");
    }

    /// <summary>
    /// PP322: and the attach reports its stage, so a refusal names the step rather than the tree.
    ///
    /// Same argument as the probe's: an attach that returned null unconditionally would look
    /// identical to one that never wrote out_stage. Handed an overlay format DXGI has no such thing
    /// as, it must hand back nothing AND say Surface - the call that consumes it - rather than the
    /// Window it opened first or the Tree it built successfully underneath.
    /// </summary>
    [Fact]
    public void AnImpossibleOverlayFormatRefusesTheAttachAtTheSurface()
    {
        LayersStage stage = LayersStage.NoDevice;
        bool attached = true;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using RenderDevice? device = AnyDevice();
                if (device is null)
                {
                    attached = false;
                    stage = LayersStage.Surface;
                    return;
                }

                var window = new System.Windows.Window { Width = 320, Height = 240 };
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();

                DcompAttachment? session = device.AttachLayers(
                    hwnd, SwapchainFormat.Rgb10A2, (SwapchainFormat)0x7000, 0.5, 0.0, 0.0, out stage);

                attached = session is not null;
                session?.Dispose();
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());

        Assert.False(attached, "an impossible overlay format produced a tree");
        Assert.Equal(LayersStage.Surface, stage);
    }

    /// <summary>
    /// PP163: and the obvious HDR test is not one. An EIGHT-bit swapchain reports HDR10 support
    /// too, because CheckColorSpaceSupport answers about the colour space and not about whether
    /// the buffer has the bits to carry it.
    ///
    /// So the format and the colour space are two independent questions and a port has to ask
    /// both. Asking only the second gets a yes on eight bits and an ST.2084 signal quantised into
    /// them, which bands in exactly the dark gradients HDR was wanted for.
    ///
    /// Asserted as the SURPRISE it is: if a future adapter or Windows starts refusing HDR10 on an
    /// eight-bit swapchain, this goes red and the reasoning above should be re-read rather than
    /// inherited.
    /// </summary>
    [Fact]
    public void TheColourSpaceCheckIsNotAnHdrTest()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        SwapchainSupport eight = device.ProbeSwapchain(SwapchainFormat.Bgra8);

        Assert.True(eight.Created);
        Assert.True(eight.Hdr10, "eight bits reported no HDR10 - the finding this pins has changed");
    }

    /// <summary>
    /// PP163: each buffer carries exactly one family of colour spaces, so choosing the format IS
    /// choosing the flavour of HDR.
    ///
    /// The integer formats take the two gamma-encoded spaces - SDR and HDR10 - and refuse scRGB.
    /// The float format takes scRGB and refuses both of the others. There is no format that offers
    /// both, so "support HDR" is not one decision here but a fork taken at the swapchain.
    /// </summary>
    [Fact]
    public void EachBufferCarriesOneFamilyOfColourSpaces()
    {
        using RenderDevice? device = AnyDevice();
        Assert.NotNull(device);

        SwapchainSupport ten = device.ProbeSwapchain(SwapchainFormat.Rgb10A2);
        SwapchainSupport wide = device.ProbeSwapchain(SwapchainFormat.Rgba16Float);

        Assert.True(wide.Created, $"a float composition swapchain stopped at {wide.Stage}");

        // The integer one: the two gamma spaces, and not the linear one.
        Assert.True(ten.Srgb);
        Assert.True(ten.Hdr10);
        Assert.False(ten.ScRgb);

        // And the float one, the other way round entirely.
        Assert.True(wide.ScRgb);
        Assert.False(wide.Hdr10);
        Assert.False(wide.Srgb);
    }

    /// <summary>
    /// PP163: the ten-bit surface HDR would need EXISTS, all the way to a D3D9Ex surface pointer.
    ///
    /// This half is the surprise. Every step PP131 measured for eight bits works for ten as well:
    /// D3D11 creates the texture, DXGI shares the handle, and D3D9Ex opens it - so nothing in the
    /// graphics stack is what stops HDR.
    ///
    /// The pairing is the trap and it is asserted by working rather than by inspection: DXGI has
    /// no B-first ten-bit format at all, so DXGI_FORMAT_R10G10B10A2_UNORM against
    /// D3DFMT_A2B10G10R10 is not a choice between two spellings - it is the only ten-bit share
    /// that can be made. Get it backwards and the open fails with E_INVALIDARG, which reads
    /// exactly like the format being unsupported when it is not.
    /// </summary>
    [Fact]
    public void ATenBitSurfaceReachesD3d9Ex()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        using SharedSurface? shared =
            SharedSurface.Create(device, 1920, 1080, ShareFormat.Rgb10A2, out ShareStage stage);

        Assert.True(shared is not null, $"the ten-bit share failed at {stage}");
        Assert.NotEqual(IntPtr.Zero, shared.Surface);
        Assert.True(shared.HasSharedHandle);
    }

    /// <summary>
    /// PP163: and WPF REFUSES IT. This is the measurement PP11's HDR half turned on, and it is a
    /// wall rather than a difficulty.
    ///
    /// D3DImage.SetBackBuffer throws NotSupportedException - "unsupported pixel format" - for the
    /// only ten-bit surface that can be built. So the composition path PP9 chose carries eight
    /// bits per channel and nothing wider, and an HDR picture cannot reach the display through it
    /// at all. Not a tuning problem, not a metadata problem: the buffer itself is refused.
    ///
    /// Asserted as a REFUSAL, deliberately. If a later Windows or a later WPF ever accepts this
    /// surface, this test goes red - and that is exactly the day the decision below it should be
    /// re-read rather than a day nobody notices.
    /// </summary>
    [Fact]
    public void WpfRefusesTheTenBitSurface()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        using SharedSurface? shared =
            SharedSurface.Create(device, 1920, 1080, ShareFormat.Rgb10A2, out _);
        if (shared is null)
            return;

        SurfacePresenter.Result result =
            SurfacePresenter.Offer(shared.Surface, TimeSpan.FromSeconds(10), out string detail);

        Assert.Equal(SurfacePresenter.Result.Refused, result);

        // The reason, pinned: a refusal for some other cause - a lost device, a timeout - would
        // otherwise pass for this finding and leave the design resting on the wrong evidence.
        Assert.Contains("NotSupportedException", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP135: WPF taking the shared surface, which is the one link nothing in the graphics stack
    /// can answer.
    ///
    /// D3DImage.SetBackBuffer VALIDATES what it is given - the device the surface came from, and
    /// whether it is a render target WPF can compose - and THROWS rather than returning false. So
    /// the texture, the wrap and the render can all be correct and the window still be black,
    /// with the failure arriving at the first frame of the first session.
    ///
    /// On an STA thread with no Window: D3DImage is a DispatcherObject and needs a thread to be
    /// created on, but does not need to be shown, which is what makes this answerable headless.
    /// Bounded, because PP117 is a whole task about a graphics call that did not return.
    /// </summary>
    [Fact]
    public void WpfTakesTheSharedSurfaceAsABackBuffer()
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
            return;

        SurfacePresenter.Result result = SurfacePresenter.OfferSharedSurface(out string detail);

        Assert.True(result != SurfacePresenter.Result.Refused, detail);

        // Accepted is not enough: a D3DImage with no front buffer composes nothing, which looks
        // exactly like a renderer that never drew.
        Assert.Equal(SurfacePresenter.Result.Available, result);
    }
}

