using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PresentPath;
using VideoUpscale;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VideoHdr;

/// <summary>
/// PP49: what RTX Video HDR costs, and whether it can be reached at all from this application.
///
/// The case is the one PP11 does not cover. PP11 settled how an HDR STREAM meets the display; the
/// console sends SDR on most titles, and an HDR display shows that flat. RTX Video HDR infers a
/// high dynamic range image from an SDR one on the presented frame, driver-side, and it is reached
/// the way RTX Video Super Resolution is - ID3D11VideoContext::VideoProcessorSetStreamExtension
/// with an NVIDIA-defined GUID - rather than through any SDK.
///
/// IT IS NOT THE SAME GUID AS PP47'S, and that is the first thing this spike had to get right.
/// Super resolution is the NVIDIA PPE interface with method 2; true HDR is an interface of its own
/// with method 3 at version 4. A spike that reused PP47's constant would have set an extension the
/// driver knows, been accepted, and changed nothing - which is indistinguishable from the answer
/// PP47 actually got, and would have been read as the same finding twice.
///
/// THE EXPERIMENT TOGGLES ONE THING. Both runs write ten-bit output with the output colour space
/// set to ST.2084 in BT.2020 primaries, because that is the signal an HDR present carries and the
/// feature has no reason to engage without it. Only the extension differs between them, so a
/// non-zero pixel difference is the driver's inference and not a colour conversion this spike
/// asked for in one run and not the other.
///
/// The run refuses to report a number it cannot attribute, exactly as PP47's does: an extension
/// the driver ignores costs nothing and produces an identical picture, which reads like a feature
/// that is free.
/// </summary>
internal static class Program
{
    private const int Repeats = 20;
    private const int BatchFrames = 25;
    private const int Warmup = 30;

    /// <summary>
    /// NVIDIA's true-HDR extension interface, from mpv's vf_d3d11vpp.c - the commit that added
    /// nvidia-true-hdr, not the one that added scaling-mode=nvidia.
    ///
    /// Corroborated across three independent retrievals before a line of this spike was written,
    /// which is PP47's discipline applied to the trap PP47 documented. The first retrieval got it
    /// WRONG in the informative direction: asked for "the NVIDIA stream extension", it returned
    /// PP47's PPE GUID beside true HDR's struct, and the two are different interfaces.
    /// </summary>
    private static readonly Guid NvidiaTrueHdrInterface = new(
        0xfdd62bb4, 0x620b, 0x4fd7, 0x9a, 0xb3, 0x1e, 0x59, 0xd0, 0xd5, 0x44, 0xb3);

    /// <summary>
    /// The extension payload. mpv declares enable as a one-bit field with 31 reserved bits after
    /// it, which is three uints on the wire either way - written as a plain uint here because the
    /// bit layout of a bitfield is the compiler's business and the buffer's size is not.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NvidiaStreamExtension
    {
        public uint Version;
        public uint Method;
        public uint Enable;
    }

    private const uint TrueHdrVersion = 4;
    private const uint TrueHdrMethod = 3;

    private static int Main(string[] argv)
    {
        string outPath = argv.Length > 0 ? argv[0] : "result.json";

        using ID3D11Device device = CreateDevice(out ID3D11DeviceContext context);
        using (context)
        using (var videoDevice = device.QueryInterface<ID3D11VideoDevice>())
        using (var videoContext = context.QueryInterface<ID3D11VideoContext>())
        using (var videoContext1 = context.QueryInterface<ID3D11VideoContext1>())
        {
            string adapter = DescribeAdapter(device);
            Console.WriteLine($"adapter    : {adapter}");
            Console.WriteLine($"convert    : {Frame.Width}x{Frame.Height} NV12 BT.709 SDR -> R10G10B10A2 ST.2084 BT.2020");
            Console.WriteLine($"frames     : {Repeats} batches of {BatchFrames}, {Warmup} warmup");
            Console.WriteLine();

            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputWidth = Frame.Width,
                InputHeight = Frame.Height,
                // Same size in and out. This is not PP47's upscale: the whole of what is being
                // asked for here is a change of dynamic range, and resizing at the same time would
                // put the scaler's cost inside the number.
                OutputWidth = Frame.Width,
                OutputHeight = Frame.Height,
                InputFrameRate = new Rational(60, 1),
                OutputFrameRate = new Rational(60, 1),
                Usage = VideoUsage.PlaybackNormal,
            };

            using ID3D11VideoProcessorEnumerator enumerator = videoDevice.CreateVideoProcessorEnumerator(content);

            // Asked rather than assumed. Ten bits out is the half PP163 measured a swapchain for
            // and nothing has asked the VIDEO PROCESSOR about - a processor that cannot write
            // R10G10B10A2 would fail at view creation with E_INVALIDARG and nothing saying which
            // of the two formats it objected to.
            VideoProcessorFormatSupport nv12 = enumerator.CheckVideoProcessorFormat(Format.NV12);
            VideoProcessorFormatSupport ten = enumerator.CheckVideoProcessorFormat(Format.R10G10B10A2_UNorm);
            Console.WriteLine($"format     : NV12 {nv12}, R10G10B10A2 {ten}");
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

            videoContext.VideoProcessorSetStreamFrameFormat(processor, 0, VideoFrameFormat.Progressive);

            // Auto processing left ON, for the reason PP47 recorded after turning it off cost it a
            // run: driver-side enhancement is the mechanism these extensions ride on, so disabling
            // it disables the thing being measured while every call still succeeds.
            videoContext.VideoProcessorSetStreamAutoProcessingMode(processor, 0, true);

            // The colour spaces, and the whole reason the 1-suffixed entry points are used: the
            // pre-1 VideoProcessorSetOutputColorSpace takes a struct with a one-bit "nominal range"
            // and no way to name ST.2084 at all. In goes limited-range BT.709 YCbCr, which is what
            // a console's SDR stream is; out goes full-range ST.2084 in BT.2020 primaries, which is
            // what an HDR10 present carries.
            //
            // BOTH RUNS, not just the one with the extension on. If the output space were the
            // thing that changed, the pixel comparison below would measure a colour conversion
            // rather than the feature.
            videoContext1.VideoProcessorSetStreamColorSpace1(
                processor, 0, ColorSpaceType.YcbcrStudioG22LeftP709);
            videoContext1.VideoProcessorSetOutputColorSpace1(
                processor, ColorSpaceType.RgbFullG2084NoneP2020);

            Run off = Measure(context, videoContext, processor, inputView, outputView, target, staging,
                "off", enable: false);
            Run on = Measure(context, videoContext, processor, inputView, outputView, target, staging,
                "on", enable: true);

            Difference diff = Compare(off.Pixels, on.Pixels);

            Console.WriteLine(off.Timing);
            Console.WriteLine(on.Timing);
            Console.WriteLine();
            Console.WriteLine($"engagement : {diff.Changed} of {diff.Total} pixels differ "
                + $"({100.0 * diff.Changed / diff.Total:F2}%), mean |delta| {diff.MeanAbs:F2}/1023, max {diff.MaxAbs}");

            bool engaged = diff.Changed > 0;
            if (!engaged)
            {
                Console.WriteLine();
                Console.WriteLine("!! THE EXTENSION DID NOT ENGAGE. The two runs produced the same picture, so the");
                Console.WriteLine("   difference in time above is scheduling noise and not the cost of anything.");
                Console.WriteLine($"   extension call: {on.ExtensionResult}");
                Console.WriteLine();
                Console.WriteLine("   Setting the extension is not the same as turning the feature on - the finding");
                Console.WriteLine("   PP47 recorded for super resolution, which rides the same driver switch.");
                Console.WriteLine();
                Console.WriteLine("   NVIDIA Control Panel -> Video -> Adjust video image settings ->");
                Console.WriteLine("   RTX Video Enhancement -> HDR. Turn it on and run this again.");
            }

            SaveCrop(off.Pixels, "crop-off.png");
            SaveCrop(on.Pixels, "crop-on.png");

            File.WriteAllText(outPath, Json(adapter, off, on, diff, engaged));
            Console.WriteLine();
            Console.WriteLine($"json       : {Path.GetFullPath(outPath)}");

            return engaged ? 0 : 1;
        }
    }

    private sealed record Run(string Name, Stats Timing, ushort[] Pixels, string ExtensionResult);

    private sealed record Difference(long Changed, long Total, double MeanAbs, int MaxAbs);

    private static Run Measure(
        ID3D11DeviceContext context, ID3D11VideoContext videoContext,
        ID3D11VideoProcessor processor, ID3D11VideoProcessorInputView inputView,
        ID3D11VideoProcessorOutputView outputView, ID3D11Texture2D target, ID3D11Texture2D staging,
        string name, bool enable)
    {
        string extensionResult = "not attempted";
        try
        {
            var ext = new NvidiaStreamExtension
            {
                Version = TrueHdrVersion,
                Method = TrueHdrMethod,
                Enable = enable ? 1u : 0u,
            };
            int size = Marshal.SizeOf<NvidiaStreamExtension>();
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ext, data, false);
                videoContext.VideoProcessorSetStreamExtension(processor, 0, NvidiaTrueHdrInterface, (uint)size, data);

                // Set is void and cannot refuse, so it is not evidence the driver knows this GUID.
                // Reading it back is a hint and not more than that - PP47's README corrected itself
                // on exactly this point: Get is driver-defined like Set is, so a driver that
                // recognises the interface is still entitled to leave the buffer alone.
                Marshal.WriteInt32(data, 0, 0);
                Marshal.WriteInt32(data, 4, 0);
                Marshal.WriteInt32(data, 8, 0);
                videoContext.VideoProcessorGetStreamExtension(processor, 0, NvidiaTrueHdrInterface, (uint)size, data);
                var echoed = Marshal.PtrToStructure<NvidiaStreamExtension>(data);
                extensionResult = $"set accepted; get echoed version={echoed.Version} "
                    + $"method={echoed.Method} enable={echoed.Enable}";
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

        // Wall clock over a drained batch, which is PP47's instrument and for its reason: D3D11
        // timestamp queries are taken on the 3D queue and VideoProcessorBlt runs on the video
        // engine, so 194 of 200 intervals came back with the end stamp not later than the begin
        // stamp when that was tried. The distribution below is over batch means, so 20 samples.
        var timing = new Stats($"rtx video hdr {name}");
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
    /// Block until the queued blts have run. Mapping a staging copy is the wait; Flush alone only
    /// submits, and PP47 proved that by removing this - the same run then reported 0.2us a frame
    /// against 262.9, which is a 1300x lie rather than a fast path.
    /// </summary>
    private static void Drain(ID3D11DeviceContext context, ID3D11Texture2D target, ID3D11Texture2D staging)
    {
        context.CopyResource(staging, target);
        context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        context.Unmap(staging, 0);
    }

    /// <summary>
    /// The output as three ten-bit channels per pixel, not as bytes.
    ///
    /// PP47 compared BGRA bytes and could, because eight bits is what its target held. Comparing
    /// this frame's bytes would throw away the two low bits of every channel - which is where a
    /// tone expansion's smallest changes live, and the difference between "did not engage" and
    /// "engaged and moved nothing visible" is exactly that band.
    /// </summary>
    private static ushort[] ReadBack(ID3D11DeviceContext context, ID3D11Texture2D staging)
    {
        MappedSubresource map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var row = new byte[Frame.Width * 4];
            var pixels = new ushort[Frame.Width * Frame.Height * 3];
            for (int y = 0; y < Frame.Height; y++)
            {
                Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, row, 0, row.Length);
                for (int x = 0; x < Frame.Width; x++)
                {
                    uint packed = BitConverter.ToUInt32(row, x * 4);
                    int at = (y * Frame.Width + x) * 3;
                    pixels[at] = (ushort)(packed & 0x3FF);
                    pixels[at + 1] = (ushort)((packed >> 10) & 0x3FF);
                    pixels[at + 2] = (ushort)((packed >> 20) & 0x3FF);
                }
            }
            return pixels;
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    private static Difference Compare(ushort[] a, ushort[] b)
    {
        long changed = 0, sum = 0;
        int max = 0;
        for (int i = 0; i < a.Length; i += 3)
        {
            int d = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
            if (d != 0)
                changed++;
            sum += d;
            int per = d / 3;
            if (per > max)
                max = per;
        }
        long total = a.Length / 3;
        return new Difference(changed, total, sum / 3.0 / total, max);
    }

    /// <summary>
    /// A 512x512 crop at 1:1, taken from the gradient quadrant rather than PP47's ringing one: a
    /// tone expansion shows itself in a smooth ramp, where banding and a lifted midtone are
    /// visible, and not on the edges an upscaler is judged by.
    ///
    /// The PNG is eight bits and the comparison above is not. It is a preview of a ten-bit frame
    /// on an SDR page, so it cannot show what the extra range holds - which is the second reason
    /// the pixel difference is the result and the image is the illustration.
    /// </summary>
    private static void SaveCrop(ushort[] pixels, string file)
    {
        // The bottom-right quadrant of Frame.cs is the two-axis gradient, and the crop is inside
        // it with 8 rows to spare: 560+512 is 1072 against a 1080-line frame. PP47's crop was
        // taken from the ringing quadrant, which is the right place to look at an upscaler and the
        // wrong one to look at a tone curve.
        const int size = 512;
        const int left = 1100;
        const int top = 560;
        var crop = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int from = ((top + y) * Frame.Width + left + x) * 3;
                int to = (y * size + x) * 4;
                crop[to] = (byte)(pixels[from + 2] >> 2);     // B
                crop[to + 1] = (byte)(pixels[from + 1] >> 2); // G
                crop[to + 2] = (byte)(pixels[from] >> 2);     // R
                crop[to + 3] = 0xFF;
            }
        }

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
        Width = Frame.Width,
        Height = Frame.Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.R10G10B10A2_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        CPUAccessFlags = CpuAccessFlags.None,
        MiscFlags = ResourceOptionFlags.None,
    });

    private static ID3D11Texture2D CreateStaging(ID3D11Device device) => device.CreateTexture2D(new Texture2DDescription
    {
        Width = Frame.Width,
        Height = Frame.Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.R10G10B10A2_UNorm,
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
            + "\"spike\":\"video-hdr\""
            + ",\"task\":\"PP49\""
            + $",\"adapter\":\"{adapter.Replace("\"", "'")}\""
            + $",\"input\":{{\"width\":{Frame.Width},\"height\":{Frame.Height},\"format\":\"NV12\""
            + ",\"colour_space\":\"YCbCr studio G22 left P709\",\"synthetic\":true}"
            + $",\"output\":{{\"width\":{Frame.Width},\"height\":{Frame.Height},\"format\":\"R10G10B10A2\""
            + ",\"colour_space\":\"RGB full G2084 none P2020\"}"
            + $",\"batches\":{Repeats},\"frames_per_batch\":{BatchFrames}"
            + $",\"engaged\":{(engaged ? "true" : "false")}"
            + $",\"set_extension\":\"{on.ExtensionResult.Replace("\"", "'")}\""
            + $",\"pixels_changed\":{diff.Changed}"
            + $",\"pixels_total\":{diff.Total}"
            + $",\"mean_abs_delta_1023\":{diff.MeanAbs.ToString("F3", c)}"
            + $",\"max_abs_delta_1023\":{diff.MaxAbs}"
            + $",\"hdr_off_us\":{off.Timing.ToJson()}"
            + $",\"hdr_on_us\":{on.Timing.ToJson()}"
            + "}\n";
    }
}
