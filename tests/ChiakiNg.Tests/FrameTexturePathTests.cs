using System.IO;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP821: the frame that stops leaving the GPU to arrive where it started.
///
/// chiaki_shim.c downloaded every hardware frame to system memory as NV12 and the presenter
/// uploaded it back - 2253us a frame on d3d11va, 13.5 percent of a 60fps interval. The branch was
/// on hw_frames_ctx alone and the file's reason was AV_PIX_FMT_VULKAN, which a D3D11 presenter
/// cannot share an image with; that reason never applied to d3d11va, whose frame is already an
/// ID3D11Texture2D.
///
/// WHAT ACTUALLY STOPPED IT WAS THE DEVICE. ffmpegdecoder.c's init takes an AVBufferRef
/// hw_device_ctx and builds its own only where none is handed in, so the decoder can be put on the
/// PRESENTER's device and its textures become something this side can copy from. §PP821 said that
/// door did not exist, which was wrong: the parameter has always been there.
///
/// TWO OF THESE HOLD MISTAKES THAT WERE MADE. The box was going to copy 1088 rows into a 1080-row
/// texture, and the shared device hung the process on its first live run.
/// </summary>
public class FrameTexturePathTests(ITestOutputHelper output)
{
    private static string Render()
        => File.ReadAllText(
            SanitizerSource.LocateRelative(@"shim\chiaki_render.c")
            ?? throw new InvalidOperationException("the renderer is not in this checkout."));

    /// <summary>
    /// A frame is a texture or a pair of planes, and the flag is what a caller switches on.
    /// </summary>
    [Fact]
    public void AFrameWithNoTextureIsStillAPairOfPlanes()
    {
        var planes = new SessionDecoder.DecodedFrame(
            1920, 1080, new IntPtr(0x1000), 1920, new IntPtr(0x2000), 1920, 23, 0, 0,
            Texture: IntPtr.Zero, Slice: 0);

        Assert.False(planes.IsTexture);

        var texture = planes with { Texture = new IntPtr(0x3000), Slice = 5 };

        Assert.True(texture.IsTexture);
        Assert.Equal(5, texture.Slice);
    }

    /// <summary>
    /// PP821: the copy is bounded by the PRESENTER's extent and not the decoder's.
    ///
    /// This is the black picture that was one edit away. PP826 recorded the decoded surface as
    /// 1920x1088 - a decoder rounds up to the codec's macroblocks - while the presenter's texture
    /// is the picture, 1920x1080. CopySubresourceRegion with a NULL box asks for the SOURCE's
    /// extent, and D3D11 refuses a copy that does not fit: no exception, no log, a black frame.
    ///
    /// So the box is asserted to come off the presenter's own width and height.
    /// </summary>
    [Fact]
    public void TheCopyIsBoundedByThePresentersOwnExtent()
    {
        string text = Render();

        Assert.Matches(@"(?m)^\s*box\.right = \(UINT\)self->w;", text);
        Assert.Matches(@"(?m)^\s*box\.bottom = \(UINT\)self->h;", text);

        // And the box actually reaches the copy: a built box nobody passed is the same black frame.
        Assert.Contains("(UINT)slice, &box);", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP821: the device is multithread-protected where it is handed out, which a hang taught.
    ///
    /// A D3D11 immediate context is not thread-safe. While the device was the presenter's alone one
    /// thread used it; a decoder sharing it puts FFmpeg's decode thread on the same context as the
    /// draw loop. The first live run of that hung the process with a picture on screen - both the
    /// copy and the render worked - and had to be force-closed.
    ///
    /// SetMultithreadProtected is what FFmpeg turns on for a device it creates itself and what it
    /// documents a caller-supplied one must have. Asserted at the ACCESSOR, because handing the
    /// pointer out is the moment that makes it necessary.
    /// </summary>
    [Fact]
    public void TheSharedDeviceIsMultithreadProtectedAtTheBoundary()
    {
        string text = Render();

        int accessor = text.IndexOf(
            "chiaki_render_d3d11_native_device(void *d3d11)\n{", StringComparison.Ordinal);

        if (accessor < 0)
        {
            accessor = text.IndexOf(
                "chiaki_render_d3d11_native_device(void *d3d11)\r\n{", StringComparison.Ordinal);
        }

        Assert.True(accessor >= 0, "the exported device accessor is gone");

        int protects = text.IndexOf(
            "ID3D10Multithread_SetMultithreadProtected", accessor, StringComparison.Ordinal);

        output.WriteLine($"accessor at {accessor}, protection at {protects}");

        Assert.True(
            protects > accessor,
            "the device is handed out without multithread protection, which hung the process the "
                + "first time a decoder shared it");
    }

    /// <summary>
    /// PP821: the device context is built for d3d11va alone.
    ///
    /// A vulkan or cuda decoder handed a D3D11 device context would be told about a device its own
    /// frames cannot live on. The name is what decides, and it is the setting's own string.
    /// </summary>
    [Fact]
    public void OnlyD3d11vaIsToldAboutTheDevice()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"shim\chiaki_shim.c")
            ?? throw new InvalidOperationException("the shim is not in this checkout."));

        Assert.Contains(
            "strcmp(hw_decoder_name, \"d3d11va\") == 0", text, StringComparison.Ordinal);

        // And the vendored init is handed the buffer rather than a copy of the init being written:
        // the parameter has always been there, which is what §PP821 was wrong about.
        Assert.Matches(@"(?m)^\s*shared,\s*$", text);
    }

    /// <summary>
    /// Both seams bumped their ABI, which is what keeps a stale DLL from landing arguments wrong.
    ///
    /// Two signatures changed in the shim and two exports arrived in the renderer. The managed side
    /// refuses a mismatch on load, and the failure it prevents is the one with no symptom: an older
    /// DLL exports every name this assembly imports.
    /// </summary>
    [Fact]
    public void TheTwoSeamsAgreeAboutTheirAbis()
    {
        Assert.Equal(ChiakiNative.ExpectedAbi, ChiakiNative.AbiVersion());
        Assert.Equal(ChiakiRender.ExpectedAbi, ChiakiRender.AbiVersion());
    }
}
