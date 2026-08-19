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
}
