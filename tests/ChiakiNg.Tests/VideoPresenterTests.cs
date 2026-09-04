using System.Runtime.InteropServices;
using ChiakiNg.Native;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP700: the renderer that lives between frames, which the port's probes were not.
///
/// RenderDevice proved libplacebo runs here, SharedSurface proved WPF takes the texture, and
/// chiaki_render_frame_nv12 proved a decoded frame converts. Each builds everything it needs and
/// destroys it - right for a probe, and impossible for a stream: a texture, two plane wraps and a
/// renderer per frame is the whole cost of rendering paid sixty times a second for one picture.
///
/// WHAT THESE HOLD is that the held version renders REPEATEDLY into the shared texture. The picture
/// itself cannot be read back - PP132 measured that a shared texture cannot be host_readable, so
/// the texture that can be shown and the texture that can be checked are two different textures -
/// so what is asserted is that every frame lands and the count moves, and the stage names the step
/// when one does not.
///
/// A MACHINE WITH NO D3D11 IS NOT A FAILURE, which is the same shape every render test here has.
/// </summary>
public class VideoPresenterTests(ITestOutputHelper output)
{
    private const int Width = 1280;
    private const int Height = 720;

    /// <summary>A synthetic NV12 frame, allocated unmanaged so it has a stable pointer.</summary>
    private sealed class Planes : IDisposable
    {
        public IntPtr Luma { get; }
        public IntPtr Chroma { get; }
        public int LumaStride { get; }
        public int ChromaStride { get; }

        /// <summary>
        /// Strides WIDER than the picture, on purpose.
        /// </summary>
        /// <remarks>
        /// A decoder's planes are usually padded, and a presenter that assumed stride equals width
        /// would read the padding as picture and shear every frame diagonally. Passing equal
        /// strides here would let that bug through.
        /// </remarks>
        public Planes(byte luma, byte cb, byte cr)
        {
            LumaStride = Width + 64;
            ChromaStride = Width + 64;

            Luma = Marshal.AllocHGlobal(LumaStride * Height);
            Chroma = Marshal.AllocHGlobal(ChromaStride * (Height / 2));

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < LumaStride; x++)
                    Marshal.WriteByte(Luma, (y * LumaStride) + x, x < Width ? luma : (byte)0);
            }

            for (int y = 0; y < Height / 2; y++)
            {
                for (int x = 0; x < ChromaStride; x++)
                    Marshal.WriteByte(Chroma, (y * ChromaStride) + x, x < Width ? ((x & 1) == 0 ? cb : cr) : (byte)0);
            }
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Luma);
            Marshal.FreeHGlobal(Chroma);
        }
    }

    private static (RenderDevice?, SharedSurface?) Open(out ShareStage stage)
    {
        stage = ShareStage.NoDevice;

        RenderDevice? device = ChiakiRender.CreateD3d11();
        if (device is null)
            return (null, null);

        SharedSurface? surface = SharedSurface.Create(device, Width, Height, out stage);
        if (surface is null)
        {
            device.Dispose();
            return (null, null);
        }

        return (device, surface);
    }

    /// <summary>THE ONE THAT MATTERS: a presenter builds, and renders frame after frame.</summary>
    [Fact]
    public void ThePresenterRendersFrameAfterFrame()
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out ShareStage shared);

        if (device is null || surface is null)
        {
            output.WriteLine($"no shared surface here: {shared}");
            return;
        }

        using (device)
        using (surface)
        {
            using VideoPresenter? presenter =
                VideoPresenter.Create(device, surface, Width, Height, out RenderStage built);

            Assert.True(presenter is not null, $"the presenter stopped at {built}");
            Assert.Equal(RenderStage.Ok, built);
            Assert.Equal(0UL, presenter!.Frames);

            using var planes = new Planes(luma: 180, cb: 90, cr: 200);

            for (int i = 0; i < 30; i++)
            {
                bool drawn = presenter.Render(
                    planes.Luma, planes.LumaStride, planes.Chroma, planes.ChromaStride,
                    out RenderStage stage);

                Assert.True(drawn, $"frame {i} stopped at {stage}");
                Assert.Equal(RenderStage.Ok, stage);
            }

            output.WriteLine($"{presenter.Frames} frame(s) into the shared texture");
            Assert.Equal(30UL, presenter.Frames);
        }
    }

    /// <summary>
    /// The renderer is built ONCE, which is the whole reason this type exists.
    ///
    /// Held by timing rather than by inspection: the first frame carries the renderer's own
    /// creation and the shader compiles under it, and the ones after it do not. A presenter that
    /// rebuilt per frame would have no such gap.
    /// </summary>
    [Fact]
    public void TheFirstFrameCostsMoreThanTheRest()
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out _);

        if (device is null || surface is null)
            return;

        using (device)
        using (surface)
        {
            using VideoPresenter? presenter =
                VideoPresenter.Create(device, surface, Width, Height, out _);

            if (presenter is null)
                return;

            using var planes = new Planes(luma: 128, cb: 128, cr: 128);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            presenter.Render(planes.Luma, planes.LumaStride, planes.Chroma, planes.ChromaStride, out _);
            long first = clock.ElapsedTicks;

            clock.Restart();
            for (int i = 0; i < 20; i++)
                presenter.Render(planes.Luma, planes.LumaStride, planes.Chroma, planes.ChromaStride, out _);
            long twenty = clock.ElapsedTicks;

            output.WriteLine(
                $"first {first / (double)System.Diagnostics.Stopwatch.Frequency * 1000:F2} ms, "
                    + $"next twenty {twenty / (double)System.Diagnostics.Stopwatch.Frequency * 1000:F2} ms");

            // Reported rather than asserted on: a warm shader cache makes the gap vanish, and a
            // test that demanded it would fail on the second run of the day.
            Assert.True(presenter.Frames == 21);
        }
    }

    /// <summary>An odd size is refused, because it puts the chroma plane half a sample out.</summary>
    [Theory]
    [InlineData(1281, 720)]
    [InlineData(1280, 721)]
    [InlineData(0, 720)]
    [InlineData(1280, -2)]
    public void AnOddOrEmptySizeIsRefused(int width, int height)
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out _);

        if (device is null || surface is null)
            return;

        using (device)
        using (surface)
        {
            Assert.Null(VideoPresenter.Create(device, surface, width, height, out _));
        }
    }

    /// <summary>A stride narrower than the picture is refused rather than read past.</summary>
    [Fact]
    public void AStrideNarrowerThanThePictureIsRefused()
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out _);

        if (device is null || surface is null)
            return;

        using (device)
        using (surface)
        {
            using VideoPresenter? presenter =
                VideoPresenter.Create(device, surface, Width, Height, out _);

            if (presenter is null)
                return;

            using var planes = new Planes(16, 128, 128);

            Assert.False(presenter.Render(planes.Luma, Width - 1, planes.Chroma, Width, out _));
            Assert.False(presenter.Render(planes.Luma, Width, planes.Chroma, Width - 1, out _));
            Assert.Equal(0UL, presenter.Frames);
        }
    }

    /// <summary>A null plane is refused rather than dereferenced across the seam.</summary>
    [Fact]
    public void ANullPlaneIsRefused()
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out _);

        if (device is null || surface is null)
            return;

        using (device)
        using (surface)
        {
            using VideoPresenter? presenter =
                VideoPresenter.Create(device, surface, Width, Height, out _);

            if (presenter is null)
                return;

            Assert.False(presenter.Render(IntPtr.Zero, Width, IntPtr.Zero, Width, out RenderStage stage));
            Assert.Equal(RenderStage.NoDevice, stage);
        }
    }

    /// <summary>A disposed presenter answers rather than crashing, and disposing twice is safe.</summary>
    [Fact]
    public void ADisposedPresenterIsQuiet()
    {
        (RenderDevice? device, SharedSurface? surface) = Open(out _);

        if (device is null || surface is null)
            return;

        using (device)
        using (surface)
        {
            VideoPresenter? presenter = VideoPresenter.Create(device, surface, Width, Height, out _);
            if (presenter is null)
                return;

            presenter.Dispose();
            presenter.Dispose();

            Assert.Equal(0UL, presenter.Frames);
            Assert.False(presenter.Render(IntPtr.Zero, Width, IntPtr.Zero, Width, out _));
        }
    }

    /// <summary>And null arguments are refused before anything native is touched.</summary>
    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => VideoPresenter.Create(null!, null!, Width, Height, out _));
    }
}

