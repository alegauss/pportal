using ChiakiNg.Native;
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
}