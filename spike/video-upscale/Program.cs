using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PresentPath;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VideoUpscale;

/// <summary>
/// PP47: what RTX Video Super Resolution costs, in milliseconds, at the resolutions this client
/// streams.
///
/// The task exists because the famous NVIDIA feature is the wrong one. DLSS Super Resolution is a
/// render-time technique: it works because a game hands it motion vectors, a depth buffer and a
/// jittered camera. A remote play client has none of those - what arrives is H.264 or HEVC a
/// console encoded, decoded into a plain surface. There is nothing to hand DLSS. The feature that
/// takes a decoded video frame and upscales it is RTX Video Super Resolution, reached through the
/// D3D11 video processor rather than through any SDK, which is how browsers use it.
///
/// So the measurement is: one 1080p NV12 frame, blitted to 2160p through ID3D11VideoContext, with
/// the NVIDIA stream extension off and then on, timed as throughput over a drained batch. GPU
/// timestamp queries were tried first and cannot see this work - see Measure.
///
/// The run refuses to report a number it cannot attribute. An extension the driver ignores costs
/// nothing and produces an identical picture, which reads exactly like a feature that is free - so
/// the two outputs are compared pixel by pixel and a run whose outputs match is reported as
/// "did not engage" rather than as "0 ms". On the machine this was written on it does not engage,
/// and the exit code says so; README.md carries what is known about why.
/// </summary>
internal static class Program
{
    private const int OutWidth = 3840;
    private const int OutHeight = 2160;
    private const int Repeats = 20;
    private const int BatchFrames = 25;
    private const int Warmup = 30;

    /// <summary>
    /// NVIDIA's driver-defined extension GUID, taken from mpv's vf_d3d11vpp.c. It is not asserted
    /// here: a wrong GUID either throws or leaves the output untouched, and the engagement check
    /// below is what tells the two apart. That is why the check exists.
    /// </summary>
    private static readonly Guid NvidiaPpeInterface = new(
        0xd43ce1b3, 0x1f4b, 0x48ac, 0xba, 0xee, 0xc3, 0xc2, 0x53, 0x75, 0xe6, 0xf7);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvidiaStreamExtension
    {
        public uint Version;
        public uint Method;
        public uint Enable;
    }

    private const uint ExtensionVersionV1 = 1;
    private const uint ExtensionMethodSuperResolution = 2;

    private static int Main(string[] argv)
    {
        string outPath = argv.Length > 0 ? argv[0] : "result.json";

        using ID3D11Device device = CreateDevice(out ID3D11DeviceContext context);
        using (context)
        using (var videoDevice = device.QueryInterface<ID3D11VideoDevice>())
        using (var videoContext = context.QueryInterface<ID3D11VideoContext>())
        {
            string adapter = DescribeAdapter(device);
            Console.WriteLine($"adapter    : {adapter}");
            Console.WriteLine($"upscale    : {Frame.Width}x{Frame.Height} NV12 -> {OutWidth}x{OutHeight} BGRA");
            Console.WriteLine($"frames     : {Repeats} batches of {BatchFrames}, {Warmup} warmup");
            Console.WriteLine();

            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputWidth = Frame.Width,
                InputHeight = Frame.Height,
                OutputWidth = OutWidth,
                OutputHeight = OutHeight,
                InputFrameRate = new Rational(60, 1),
                OutputFrameRate = new Rational(60, 1),
                Usage = VideoUsage.PlaybackNormal,
            };

            using ID3D11VideoProcessorEnumerator enumerator = videoDevice.CreateVideoProcessorEnumerator(content);

            // Asked rather than assumed: a processor that cannot take NV12 in or BGRA out would
            // fail later at view creation with E_INVALIDARG and nothing saying which of the two
            // it objected to.
            VideoProcessorFormatSupport nv12 = enumerator.CheckVideoProcessorFormat(Format.NV12);
            VideoProcessorFormatSupport bgra = enumerator.CheckVideoProcessorFormat(Format.B8G8R8A8_UNorm);
            Console.WriteLine($"format     : NV12 {nv12}, BGRA {bgra}");
            using ID3D11VideoProcessor processor = videoDevice.CreateVideoProcessor(enumerator, 0);

            using ID3D11Texture2D source = CreateSource(device);
            using ID3D11Texture2D target = CreateTarget(device);
            using ID3D11Texture2D staging = CreateStaging(device);

            using ID3D11VideoProcessorInputView inputView = videoDevice.CreateVideoProcessorInputView(
                source, enumerator, new VideoProcessorInputViewDescription
                {
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    FourCC = 0,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
                });

            using ID3D11VideoProcessorOutputView outputView = videoDevice.CreateVideoProcessorOutputView(
                target, enumerator, new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                });

            // The frame is progressive video, and saying so is not cosmetic: the processor picks a
            // different path for an interlaced stream.
            videoContext.VideoProcessorSetStreamFrameFormat(processor, 0, VideoFrameFormat.Progressive);
            // Auto processing is left ON deliberately. Turning it off was the first thing this
            // spike did, and it is how the extension was made to look inert: driver-side
            // enhancement is the mechanism super resolution rides on, so disabling it disables the
            // thing being measured while every call still succeeds. That is the failure mode the
            // engagement check exists to catch, and it caught this one.
            videoContext.VideoProcessorSetStreamAutoProcessingMode(processor, 0, true);

            Run off = Measure(device, context, videoContext, processor, inputView, outputView, target, staging,
                "off", enable: false);
            Run on = Measure(device, context, videoContext, processor, inputView, outputView, target, staging,
                "on", enable: true);

            Difference diff = Compare(off.Pixels, on.Pixels);

            Console.WriteLine(off.Timing);
            Console.WriteLine(on.Timing);
            Console.WriteLine();
            Console.WriteLine($"engagement : {diff.Changed} of {diff.Total} pixels differ "
                + $"({100.0 * diff.Changed / diff.Total:F2}%), mean |delta| {diff.MeanAbs:F2}/255, max {diff.MaxAbs}");

            bool engaged = diff.Changed > 0;
            if (!engaged)
            {
                Console.WriteLine();
                Console.WriteLine("!! THE EXTENSION DID NOT ENGAGE. The two runs produced the same picture, so the");
                Console.WriteLine("   difference in time below is scheduling noise and not the cost of anything.");
                Console.WriteLine($"   set-extension call: {on.ExtensionResult}");
            }

            SaveCrop(off.Pixels, "crop-off.png");
            SaveCrop(on.Pixels, "crop-on.png");

            File.WriteAllText(outPath, Json(adapter, off, on, diff, engaged));
            Console.WriteLine();
            Console.WriteLine($"json       : {Path.GetFullPath(outPath)}");

            return engaged ? 0 : 1;
        }
    }

    private sealed record Run(string Name, Stats Timing, byte[] Pixels, string ExtensionResult);

    private sealed record Difference(long Changed, long Total, double MeanAbs, int MaxAbs);

    private static Run Measure(
        ID3D11Device device, ID3D11DeviceContext context, ID3D11VideoContext videoContext,
        ID3D11VideoProcessor processor, ID3D11VideoProcessorInputView inputView,
        ID3D11VideoProcessorOutputView outputView, ID3D11Texture2D target, ID3D11Texture2D staging,
        string name, bool enable)
    {
        string extensionResult = "not attempted";
        try
        {
            var ext = new NvidiaStreamExtension
            {
                Version = ExtensionVersionV1,
                Method = ExtensionMethodSuperResolution,
                Enable = enable ? 1u : 0u,
            };
            int size = Marshal.SizeOf<NvidiaStreamExtension>();
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ext, data, false);
                videoContext.VideoProcessorSetStreamExtension(processor, 0, NvidiaPpeInterface, (uint)size, data);

                // Set is void and cannot refuse, so it is not evidence that the driver knows this
                // GUID. Reading it back is: a driver that recognises the interface returns what
                // was stored, and one that does not leaves the buffer as it found it.
                Marshal.WriteInt32(data, 0, 0);
                Marshal.WriteInt32(data, 4, 0);
                Marshal.WriteInt32(data, 8, 0);
                videoContext.VideoProcessorGetStreamExtension(processor, 0, NvidiaPpeInterface, (uint)size, data);
                var echoed = Marshal.PtrToStructure<NvidiaStreamExtension>(data);
                bool known = echoed.Version == ExtensionVersionV1 && echoed.Method == ExtensionMethodSuperResolution;
                extensionResult = known
                    ? $"accepted, driver echoed version={echoed.Version} method={echoed.Method} enable={echoed.Enable}"
                    : $"accepted, but the driver echoed version={echoed.Version} method={echoed.Method} "
                      + $"enable={echoed.Enable} - it does not recognise this GUID";
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
        catch (Exception ex)
        {
            extensionResult = $"refused: {ex.GetType().Name}: {ex.Message}";
        }

        var stream = new VideoProcessorStream
        {
            Enable = true,
            OutputIndex = 0,
            InputFrameOrField = 0,
            PastFrames = 0,
            FutureFrames = 0,
            InputSurface = inputView,
        };
        VideoProcessorStream[] streams = { stream };

        for (int i = 0; i < Warmup; i++)
            videoContext.VideoProcessorBlt(processor, outputView, 0, 1, streams);
        Drain(context, target, staging);

        // Wall clock over a batch, not a GPU timestamp per frame.
        //
        // Timestamp queries were the first instrument here and they are the wrong one: D3D11
        // timestamps are taken on the 3D queue, and VideoProcessorBlt executes on the video
        // engine. Measured, rather than reasoned about - 194 of 200 intervals came back with the
        // end stamp not later than the begin stamp, on a run whose disjoint flag was false and
        // whose frequency was a clean 1 GHz. A pair of stamps that never advances is not a fast
        // blt, it is a blt the clock being read cannot see.
        //
        // So each sample is a batch of blts divided by its count, with the queue drained at the
        // end to make the GPU's work land inside the interval. That measures throughput and not
        // one frame's latency, and the distribution below is over batch means rather than over
        // frames - which is why it has 20 samples and not 500.
        var timing = new Stats($"vsr {name}");
        for (int r = 0; r < Repeats; r++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < BatchFrames; i++)
                videoContext.VideoProcessorBlt(processor, outputView, 0, 1, streams);
            Drain(context, target, staging);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            double us = elapsed * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
            timing.Push(us / BatchFrames);
        }

        return new Run(name, timing, ReadBack(context, staging), extensionResult);
    }

    /// <summary>
    /// Block until the queued blts have actually run. Copying the output to a staging texture and
    /// mapping it is the wait: Map on a staging resource cannot return until everything written to
    /// its source has landed. Flush alone only submits, which would time the submission rather
    /// than the work and make every batch look free.
    /// </summary>
    private static void Drain(ID3D11DeviceContext context, ID3D11Texture2D target, ID3D11Texture2D staging)
    {
        context.CopyResource(staging, target);
        context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        context.Unmap(staging, 0);
    }

    private static byte[] ReadBack(ID3D11DeviceContext context, ID3D11Texture2D staging)
    {
        MappedSubresource map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixels = new byte[OutWidth * OutHeight * 4];
            for (int y = 0; y < OutHeight; y++)
            {
                IntPtr row = map.DataPointer + y * (int)map.RowPitch;
                Marshal.Copy(row, pixels, y * OutWidth * 4, OutWidth * 4);
            }
            return pixels;
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    private static Difference Compare(byte[] a, byte[] b)
    {
        long changed = 0, sum = 0;
        int max = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            int d = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
            if (d != 0)
                changed++;
            sum += d;
            int per = d / 3;
            if (per > max)
                max = per;
        }
        long total = a.Length / 4;
        return new Difference(changed, total, sum / 3.0 / total, max);
    }

    /// <summary>
    /// A 512x512 crop at 1:1, not the whole 4K frame. Two 8-megapixel PNGs in a repository would
    /// be 30 MB to answer a question a reader settles by looking at edges - and the crop is taken
    /// from the ringing quadrant, where an upscaler that overshoots shows it.
    /// </summary>
    private static void SaveCrop(byte[] pixels, string file)
    {
        const int size = 512;
        const int left = 1200;
        const int top = 300;
        var crop = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            Array.Copy(pixels, ((top + y) * OutWidth + left) * 4, crop, y * size * 4, size * 4);

        BitmapSource bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, crop, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(file);
        encoder.Save(fs);
        Console.WriteLine($"crop       : {Path.GetFullPath(file)}");
    }

    private static ID3D11Device CreateDevice(out ID3D11DeviceContext context)
    {
        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            levels, out ID3D11Device device, out context).CheckError();
        return device;
    }

    private static string DescribeAdapter(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        AdapterDescription d = adapter.Description;
        return $"{d.Description.Trim()} (vendor 0x{d.VendorId:x4}, device 0x{d.DeviceId:x4})";
    }

    private static ID3D11Texture2D CreateSource(ID3D11Device device)
    {
        byte[] nv12 = Frame.BuildNv12();
        GCHandle pin = GCHandle.Alloc(nv12, GCHandleType.Pinned);
        try
        {
            var desc = new Texture2DDescription
            {
                Width = Frame.Width,
                Height = Frame.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.Decoder,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };
            var initial = new SubresourceData(pin.AddrOfPinnedObject(), (uint)Frame.Width);
            return device.CreateTexture2D(desc, new[] { initial });
        }
        finally
        {
            pin.Free();
        }
    }

    private static ID3D11Texture2D CreateTarget(ID3D11Device device) => device.CreateTexture2D(new Texture2DDescription
    {
        Width = OutWidth,
        Height = OutHeight,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        CPUAccessFlags = CpuAccessFlags.None,
        MiscFlags = ResourceOptionFlags.None,
    });

    private static ID3D11Texture2D CreateStaging(ID3D11Device device) => device.CreateTexture2D(new Texture2DDescription
    {
        Width = OutWidth,
        Height = OutHeight,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CPUAccessFlags = CpuAccessFlags.Read,
        MiscFlags = ResourceOptionFlags.None,
    });

    private static string Json(string adapter, Run off, Run on, Difference diff, bool engaged)
    {
        var c = CultureInfo.InvariantCulture;
        return "{"
            + "\"spike\":\"video-upscale\""
            + ",\"task\":\"PP47\""
            + $",\"adapter\":\"{adapter.Replace("\"", "'")}\""
            + $",\"input\":{{\"width\":{Frame.Width},\"height\":{Frame.Height},\"format\":\"NV12\",\"synthetic\":true}}"
            + $",\"output\":{{\"width\":{OutWidth},\"height\":{OutHeight},\"format\":\"BGRA\"}}"
            + $",\"batches\":{Repeats},\"frames_per_batch\":{BatchFrames}"
            + $",\"engaged\":{(engaged ? "true" : "false")}"
            + $",\"set_extension\":\"{on.ExtensionResult.Replace("\"", "'")}\""
            + $",\"pixels_changed\":{diff.Changed}"
            + $",\"pixels_total\":{diff.Total}"
            + $",\"mean_abs_delta\":{diff.MeanAbs.ToString("F3", c)}"
            + $",\"max_abs_delta\":{diff.MaxAbs}"
            + $",\"vsr_off_us\":{off.Timing.ToJson()}"
            + $",\"vsr_on_us\":{on.Timing.ToJson()}"
            + "}\n";
    }
}
