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

