using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PresentPath;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FrameGeneration;

/// <summary>
/// PP50: whether frame generation is available to a decoded video stream at all, what it would
/// hold back if it were, and what the doubling this card does offer actually produces.
///
/// The famous NVIDIA feature is again the wrong one, for the reason PP47 settled one line over.
/// DLSS Frame Generation is a render-time technique: it wants motion vectors and a depth buffer,
/// and a decoded H.264 frame carries neither. What could interpolate a decoded video frame is the
/// D3D11 video processor's own frame-rate conversion, advertised as
/// <c>D3D11_VIDEO_PROCESSOR_PROCESSOR_CAPS_FRAME_RATE_CONVERSION</c> and driven through
/// <c>VideoProcessorSetStreamOutputRate</c>, whose <c>RepeatFrame</c> flag is literally the switch
/// between duplicating a frame and inventing one.
///
/// Three things come out of the run, and only one of them is a stopwatch reading:
///
///   the OFFER  - every rate-conversion group the driver publishes, under all three video usages,
///                with its caps bits and its custom rates. This is the load-bearing evidence: a
///                driver that does not advertise FRAME_RATE_CONVERSION will not interpolate, and
///                no amount of asking changes that.
///   the HOLD   - <c>FutureFrames</c> from those same caps: how many frames after the one being
///                shown the processor requires before it will produce anything. At 30fps each is
///                33.3 ms, which is the cost PP50's symptom names.
///   the OUTPUT - the doubling actually run, through the driver's own 2-out-per-1-in custom rate.
///                A card can advertise that ratio and mean duplication, which looks like frame
///                generation from the frame counter and is not, so the second output frame is
///                compared against the frames either side of it.
/// </summary>
internal static class Program
{
    private const int Repeats = 20;
    private const int BatchIntervals = 25;
    private const int Warmup = 20;

    /// <summary>The rate a console sends at when this feature would be wanted, and the rate the
    /// processor is asked to produce. 30 to 60 is the doubling the symptom names.</summary>
    private const int InputFps = 30;
    private const int OutputFps = 60;

    private static readonly VideoUsage[] Usages =
    {
        VideoUsage.PlaybackNormal, VideoUsage.OptimalSpeed, VideoUsage.OptimalQuality,
    };

    private static int Main(string[] argv)
    {
        bool proveArrays = false;
        string outPath = "result.json";
        foreach (string a in argv)
        {
            if (a == "--prove-arrays")
                proveArrays = true;
            else
                outPath = a;
        }

        using ID3D11Device device = CreateDevice(out ID3D11DeviceContext context);
        using (context)
        using (var videoDevice = device.QueryInterface<ID3D11VideoDevice>())
        using (var videoContext = context.QueryInterface<ID3D11VideoContext>())
        {
            string adapter = DescribeAdapter(device);
            Console.WriteLine($"adapter    : {adapter}");
            Console.WriteLine($"convert    : {Frames.Width}x{Frames.Height} NV12 {InputFps}fps -> "
                + $"{OutputFps}fps BGRA, panning {Frames.ShiftPerFrame}px per frame");
            Console.WriteLine();

            // Asked under every usage, not just the one this client would pass. The set of
            // rate-conversion groups is the driver's answer to a content description, and a
            // feature withheld from PLAYBACK_NORMAL but offered to OPTIMAL_QUALITY would make a
            // negative result here an artefact of one argument.
            var surveys = new List<Survey>();
            foreach (VideoUsage usage in Usages)
            {
                using ID3D11VideoProcessorEnumerator e = videoDevice.CreateVideoProcessorEnumerator(Content(usage));
                surveys.Add(new Survey(usage, ReadGroups(e)));
            }

            foreach (Survey s in surveys)
            {
                foreach (Group g in s.Groups)
                    Console.WriteLine($"{s.Usage,-14} {g.Describe()}");
            }
            Console.WriteLine();

            Group? interpolating = null;
            foreach (Survey s in surveys)
            {
                foreach (Group g in s.Groups)
                {
                    if (g.Interpolates && interpolating is null)
                        interpolating = g;
                }
            }

            if (interpolating is null)
            {
                Console.WriteLine("!! NO GROUP UNDER ANY USAGE ADVERTISES FRAME_RATE_CONVERSION.");
                Console.WriteLine("   This adapter's video processor will duplicate frames and will not invent one,");
                Console.WriteLine("   so PP50's trade does not arise on it: there is no latency to pay because there");
                Console.WriteLine("   is no smoothness on offer. The run below shows what the doubling it DOES");
                Console.WriteLine("   advertise produces.");
            }
            else
            {
                Console.WriteLine($"** FRAME_RATE_CONVERSION IS ADVERTISED (group {interpolating.Index}). The run below");
                Console.WriteLine("   measures it, and the hold is real on this machine.");
            }
            Console.WriteLine();

            // The group that carries the doubling, which is not necessarily the one that
            // interpolates and on this card is not one that does.
            Survey chosenSurvey = surveys[0];
            Group chosen = interpolating ?? Doubling(chosenSurvey.Groups) ?? chosenSurvey.Groups[0];
            Rate? doubling = FindDoubling(chosen.CustomRates);

            Console.WriteLine($"chosen     : {chosenSurvey.Usage} group {chosen.Index}, "
                + $"{chosen.PastFrames} past + {chosen.FutureFrames} future frames required");
            Console.WriteLine($"hold       : {chosen.FutureFrames} future frame(s) = "
                + $"{Ms(HoldMs(chosen.FutureFrames, InputFps))} ms at {InputFps}fps, "
                + $"{Ms(HoldMs(chosen.FutureFrames, OutputFps))} ms at {OutputFps}fps");

            if (doubling is null)
            {
                Console.WriteLine();
                Console.WriteLine("!! NO CUSTOM RATE TAKES ONE INPUT FRAME TO TWO OUTPUT FRAMES, so this card offers");
                Console.WriteLine("   no 30-to-60 of any kind and there is nothing further to run.");
                File.WriteAllText(outPath, Json(adapter, surveys, chosen, chosenSurvey.Usage,
                    interpolating is not null, null, null, null, null, null, false));
                return 1;
            }

            Console.WriteLine($"rate       : custom {doubling}, which is the 30-to-60 this run drives");
            Console.WriteLine();

            using ID3D11VideoProcessorEnumerator enumerator =
                videoDevice.CreateVideoProcessorEnumerator(Content(chosenSurvey.Usage));
            using ID3D11VideoProcessor processor = videoDevice.CreateVideoProcessor(enumerator, (uint)chosen.Index);

            videoContext.VideoProcessorSetStreamFrameFormat(processor, 0, VideoFrameFormat.Progressive);
            // Auto processing is left ON deliberately, for the reason video-upscale wrote down one
            // task earlier: driver-side enhancement is the mechanism these features ride on, and
            // turning it off disables the thing being measured while every call still succeeds.
            videoContext.VideoProcessorSetStreamAutoProcessingMode(processor, 0, true);

            int need = chosen.PastFrames + chosen.FutureFrames + 2;
            var sources = new List<ID3D11Texture2D>();
            var views = new List<ID3D11VideoProcessorInputView>();
            try
            {
                for (int i = 0; i < need; i++)
                {
                    ID3D11Texture2D tex = CreateSource(device, i);
                    sources.Add(tex);
                    views.Add(videoDevice.CreateVideoProcessorInputView(
                        tex, enumerator, new VideoProcessorInputViewDescription
                        {
                            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                            FourCC = 0,
                            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
                        }));
                }

                using ID3D11Texture2D target = CreateTarget(device);
                using ID3D11Texture2D staging = CreateStaging(device);
                using ID3D11VideoProcessorOutputView outputView = videoDevice.CreateVideoProcessorOutputView(
                    target, enumerator, new VideoProcessorOutputViewDescription
                    {
                        ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                        Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                    });

                var harness = new Harness(context, videoContext, processor, outputView, target, staging,
                    views, chosen.PastFrames, chosen.FutureFrames,
                    new Rational((uint)doubling.Numerator, (uint)doubling.Denominator));

                if (proveArrays)
                {
                    harness.ProveArraysAreRead();
                    Console.WriteLine("   ... and it did not. The runtime accepted an array of null view pointers");
                    Console.WriteLine("   without dereferencing one, so this harness cannot show its neighbours land.");
                    return 2;
                }

                bool arraysAccepted = harness.ArraysAccepted();
                Console.WriteLine($"control    : filled neighbour arrays accepted = {arraysAccepted}");
                Console.WriteLine(arraysAccepted
                    ? "             --prove-arrays is the other half of this, and it is destructive; README carries"
                    + " what it did"
                    : "             THE FILLED ARRAYS WERE REFUSED - nothing below is evidence about the driver");
                Console.WriteLine();

                Run repeat = harness.Measure("repeat", repeatFrame: true);
                Run interpolate = harness.Measure("interpolate", repeatFrame: false);

                Console.WriteLine(repeat.Timing);
                Console.WriteLine(interpolate.Timing);
                Console.WriteLine();

                Difference repeatVsShown = Compare(repeat.Generated, repeat.Shown);
                Difference interpVsShown = Compare(interpolate.Generated, interpolate.Shown);
                Difference interpVsNext = Compare(interpolate.Generated, interpolate.Next);
                Difference modeVsMode = Compare(repeat.Generated, interpolate.Generated);

                Console.WriteLine($"repeat     : generated vs shown    {repeatVsShown.Describe()}");
                Console.WriteLine($"interpolate: generated vs shown    {interpVsShown.Describe()}");
                Console.WriteLine($"interpolate: generated vs next     {interpVsNext.Describe()}");
                Console.WriteLine($"flag       : repeat vs interpolate {modeVsMode.Describe()}");
                Console.WriteLine();

                // A generated frame that differs from both of the frames it sits between is the
                // only shape an interpolated frame can have. Matching one of them is a duplicate,
                // whatever the flag said, and the flag changing nothing at all says the driver
                // never had a second behaviour to select.
                bool between = interpVsShown.Changed > 0 && interpVsNext.Changed > 0;
                bool flagMatters = modeVsMode.Changed > 0;
                bool engaged = between && flagMatters && arraysAccepted;

                if (!flagMatters)
                {
                    Console.WriteLine("   RepeatFrame changed nothing: the two runs produced the same picture, so the");
                    Console.WriteLine("   difference in time above is scheduling noise and not the cost of anything.");
                }
                if (!between)
                {
                    Console.WriteLine("   The generated frame matches a neighbour. The doubling is duplication - the");
                    Console.WriteLine("   frame counter reads 60 and the picture updates 30 times a second.");
                }

                SaveCrop(interpolate.Shown, "crop-shown.png");
                SaveCrop(interpolate.Generated, "crop-generated.png");
                SaveCrop(interpolate.Next, "crop-next.png");

                File.WriteAllText(outPath, Json(adapter, surveys, chosen, chosenSurvey.Usage,
                    interpolating is not null, doubling, arraysAccepted, repeat, interpolate,
                    new[] { repeatVsShown, interpVsShown, interpVsNext, modeVsMode }, engaged));
                Console.WriteLine();
                Console.WriteLine($"json       : {Path.GetFullPath(outPath)}");

                return engaged ? 0 : 1;
            }
            finally
            {
                foreach (ID3D11VideoProcessorInputView v in views)
                    v.Dispose();
                foreach (ID3D11Texture2D t in sources)
                    t.Dispose();
            }
        }
    }

    private static VideoProcessorContentDescription Content(VideoUsage usage) => new()
    {
        InputFrameFormat = VideoFrameFormat.Progressive,
        InputWidth = Frames.Width,
        InputHeight = Frames.Height,
        OutputWidth = Frames.Width,
        OutputHeight = Frames.Height,
        InputFrameRate = new Rational(InputFps, 1),
        OutputFrameRate = new Rational(OutputFps, 1),
        Usage = usage,
    };

    /// <summary>The first group offering more output frames than input frames, which is the shape
    /// a doubling has whether the driver interpolates it or duplicates it.</summary>
    private static Group? Doubling(IReadOnlyList<Group> groups)
    {
        foreach (Group g in groups)
        {
            if (FindDoubling(g.CustomRates) is not null)
                return g;
        }
        return null;
    }

    /// <summary>
    /// The progressive rate that takes one input frame to two output frames. The interlaced
    /// entries beside it convert fields and are a different conversion wearing the same ratio.
    /// </summary>
    private static Rate? FindDoubling(IReadOnlyList<Rate> rates)
    {
        foreach (Rate r in rates)
        {
            if (!r.Interlaced && r.InputFrames == 1 && r.OutputFrames == 2)
                return r;
        }
        return null;
    }

    /// <summary>Milliseconds one held frame is worth at a given rate.</summary>
    private static double HoldMs(int frames, int fps) => 1000.0 * frames / fps;

    private static string Ms(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private sealed record Survey(VideoUsage Usage, IReadOnlyList<Group> Groups);

    private sealed record Rate(int Numerator, int Denominator, int OutputFrames, int InputFrames, bool Interlaced)
    {
        public override string ToString() =>
            $"{Numerator}/{Denominator} ({OutputFrames} out per {InputFrames} in{(Interlaced ? ", interlaced" : "")})";
    }

    private sealed record Group(int Index, int PastFrames, int FutureFrames, uint ProcessorCaps,
        IReadOnlyList<Rate> CustomRates)
    {
        public bool Interpolates =>
            (ProcessorCaps & (uint)VideoProcessorProcessorCaps.FrameRateConversion) != 0;

        public string Describe()
        {
            string rates = CustomRates.Count == 0 ? "none" : string.Join(", ", CustomRates);
            return $"group {Index}  past={PastFrames} future={FutureFrames} caps=0x{ProcessorCaps:x2} "
                + $"[{CapNames(ProcessorCaps)}]  rates: {rates}";
        }

        private static string CapNames(uint caps)
        {
            var names = new List<string>();
            foreach (VideoProcessorProcessorCaps bit in Enum.GetValues<VideoProcessorProcessorCaps>())
            {
                if ((caps & (uint)bit) != 0)
                    names.Add(bit.ToString());
            }
            return names.Count == 0 ? "none" : string.Join("|", names);
        }
    }

    private sealed record Run(string Name, Stats Timing, byte[] Shown, byte[] Generated, byte[] Next);

    private sealed record Difference(long Changed, long Total, double MeanAbs, int MaxAbs)
    {
        public string Describe() =>
            $"{Changed,9} of {Total} px ({100.0 * Changed / Total,6:F2}%), "
            + $"mean |delta| {MeanAbs,6:F2}/255, max {MaxAbs}";
    }

    private static List<Group> ReadGroups(ID3D11VideoProcessorEnumerator enumerator)
    {
        VideoProcessorCaps caps = enumerator.VideoProcessorCaps;
        var groups = new List<Group>();
        for (uint i = 0; i < caps.RateConversionCapsCount; i++)
        {
            enumerator.GetVideoProcessorRateConversionCaps(i, out VideoProcessorRateConversionCaps rc);
            var rates = new List<Rate>();
            for (uint r = 0; r < rc.CustomRateCount; r++)
            {
                enumerator.GetVideoProcessorCustomRate(i, r, out VideoProcessorCustomRate cr);
                // OutputFrames per InputFramesOrFields is the conversion being offered, and it is
                // the field that looks like frame generation. Whether it IS one is the caps bit
                // beside it, not this ratio.
                rates.Add(new Rate((int)cr.CustomRate.Numerator, (int)cr.CustomRate.Denominator,
                    (int)cr.OutputFrames, (int)cr.InputFramesOrFields, cr.InputInterlaced));
            }
            groups.Add(new Group((int)i, (int)rc.PastFrames, (int)rc.FutureFrames, rc.ProcessorCaps, rates));
        }
        return groups;
    }

    /// <summary>
    /// Holds everything a blt needs, and the pinned pointer arrays the stream description wants.
    /// </summary>
    private sealed class Harness
    {
        private readonly ID3D11DeviceContext context;
        private readonly ID3D11VideoContext videoContext;
        private readonly ID3D11VideoProcessor processor;
        private readonly ID3D11VideoProcessorOutputView outputView;
        private readonly ID3D11Texture2D target;
        private readonly ID3D11Texture2D staging;
        private readonly List<ID3D11VideoProcessorInputView> views;
        private readonly int past;
        private readonly int future;
        private readonly Rational customRate;

        public Harness(ID3D11DeviceContext context, ID3D11VideoContext videoContext,
            ID3D11VideoProcessor processor, ID3D11VideoProcessorOutputView outputView,
            ID3D11Texture2D target, ID3D11Texture2D staging,
            List<ID3D11VideoProcessorInputView> views, int past, int future, Rational customRate)
        {
            this.context = context;
            this.videoContext = videoContext;
            this.processor = processor;
            this.outputView = outputView;
            this.target = target;
            this.staging = staging;
            this.views = views;
            this.past = past;
            this.future = future;
            this.customRate = customRate;
        }

        /// <summary>
        /// The half of the neighbour-array check that a normal run can carry: a blt with the past
        /// and future arrays filled, which the runtime has to accept.
        /// </summary>
        public bool ArraysAccepted()
        {
            videoContext.VideoProcessorSetStreamOutputRate(
                processor, 0, VideoProcessorOutputRate.Custom, false, customRate);
            try
            {
                Blt(past, 0, nulled: false);
                Drain();
                return true;
            }
            catch (SharpGenException)
            {
                return false;
            }
        }

        /// <summary>
        /// The other half, and it takes the process down on purpose.
        ///
        /// A generated frame that matches the frame before it has two possible causes - a driver
        /// that duplicates, and a harness whose neighbour arrays never arrived - and they are
        /// indistinguishable from the output alone. An accepted blt does not separate them either,
        /// because a runtime that ignores the pointer accepts everything.
        ///
        /// What separates them is nulling the array's elements while the counts still say there
        /// are that many frames. A runtime that reads through the address dereferences a null and
        /// the process dies at 0xC0000005; one that never looks carries on. So the crash IS the
        /// result, which is why this is behind a flag and recorded in README rather than run on
        /// every invocation - the same shape as video-upscale proving its drain by removing it.
        /// </summary>
        public void ProveArraysAreRead()
        {
            videoContext.VideoProcessorSetStreamOutputRate(
                processor, 0, VideoProcessorOutputRate.Custom, false, customRate);
            Console.WriteLine("prove      : blitting with nulled neighbour arrays. If the runtime reads through the");
            Console.WriteLine("             address this harness supplies, the next line never prints.");
            Blt(past, 0, nulled: true);
            Drain();
        }

        public Run Measure(string name, bool repeatFrame)
        {
            // NORMAL is not the doubling. It means one output frame per input frame whatever the
            // content description's output rate says, and asking it for output frame 1 returns
            // E_INVALIDARG - which is how this run found out. The doubling is the driver's own
            // enumerated custom rate, and it is the only way to ask for a second frame at all.
            videoContext.VideoProcessorSetStreamOutputRate(
                processor, 0, VideoProcessorOutputRate.Custom, repeatFrame, customRate);

            // The window sits at index `past`, so every past and future surface the driver asked
            // for exists. Sliding it by one gives the frame the generated one is supposed to be
            // approaching, which is the second half of the between-ness check.
            byte[] shown = Capture(centre: past, outputFrame: 0);
            byte[] generated = Capture(centre: past, outputFrame: 1);
            byte[] next = Capture(centre: past + 1, outputFrame: 0);

            var timing = new Stats(name);
            for (int i = 0; i < Warmup; i++)
                Blt(past, 0, nulled: false);
            Drain();

            // Wall clock over a batch, not a GPU timestamp per frame - video-upscale measured that
            // D3D11 timestamps are taken on the 3D queue and cannot see VideoProcessorBlt at all.
            // One interval is the pair a rate doubling produces: the frame that arrived and the
            // frame produced after it, which together are what one input frame now costs.
            for (int r = 0; r < Repeats; r++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < BatchIntervals; i++)
                {
                    Blt(past, 0, nulled: false);
                    Blt(past, 1, nulled: false);
                }
                Drain();
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                double us = elapsed * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
                timing.Push(us / (BatchIntervals * 2));
            }

            return new Run(name, timing, shown, generated, next);
        }

        private byte[] Capture(int centre, int outputFrame)
        {
            Blt(centre, outputFrame, nulled: false);
            Drain();
            return ReadBack();
        }

        private void Blt(int centre, int outputFrame, bool nulled)
        {
            var pastPtrs = new IntPtr[Math.Max(past, 1)];
            var futurePtrs = new IntPtr[Math.Max(future, 1)];
            if (!nulled)
            {
                for (int i = 0; i < past; i++)
                    pastPtrs[i] = views[centre - 1 - i].NativePointer;
                for (int i = 0; i < future; i++)
                    futurePtrs[i] = views[centre + 1 + i].NativePointer;
            }

            GCHandle pastPin = GCHandle.Alloc(pastPtrs, GCHandleType.Pinned);
            GCHandle futurePin = GCHandle.Alloc(futurePtrs, GCHandleType.Pinned);
            ID3D11VideoProcessorInputView? pastList = SurfaceList(pastPin, past);
            ID3D11VideoProcessorInputView? futureList = SurfaceList(futurePin, future);
            try
            {
                var stream = new VideoProcessorStream
                {
                    Enable = true,
                    OutputIndex = (uint)outputFrame,
                    InputFrameOrField = (uint)centre,
                    PastFrames = (uint)past,
                    FutureFrames = (uint)future,
                    PpPastSurfaces = pastList!,
                    InputSurface = views[centre],
                    PpFutureSurfaces = futureList!,
                };
                videoContext.VideoProcessorBlt(processor, outputView, (uint)outputFrame, 1,
                    new[] { stream });
            }
            finally
            {
                pastPin.Free();
                futurePin.Free();
            }
        }

        /// <summary>
        /// The one place this spike does not take its binding at its word.
        ///
        /// <c>D3D11_VIDEO_PROCESSOR_STREAM::ppPastSurfaces</c> is an ARRAY of input-view pointers,
        /// sized by <c>PastFrames</c>. Vortice types that field as a single
        /// <c>ID3D11VideoProcessorInputView</c> and marshals it by writing the object's own native
        /// pointer into the slot - which hands the driver a COM object where it will dereference
        /// an array, and is wrong for every count, one included.
        ///
        /// So the array is built here, pinned by the caller, and passed inside a wrapper whose
        /// native pointer IS the array's address. That is a lie about the C# type and the truth
        /// about the native field, and <see cref="CheckSurfaceArrays"/> is what checks it rather
        /// than asserting it. The wrapper owns nothing: its finalizer is suppressed, because
        /// Release on an array of pointers would call through whatever the first element's first
        /// word happens to be.
        /// </summary>
        private static ID3D11VideoProcessorInputView? SurfaceList(GCHandle pin, int count)
        {
            if (count == 0)
                return null;
            var wrapper = new ID3D11VideoProcessorInputView(pin.AddrOfPinnedObject());
            GC.SuppressFinalize(wrapper);
            return wrapper;
        }

        /// <summary>
        /// Block until the queued blts have actually run. Copying the output to a staging texture
        /// and mapping it is the wait: Map on a staging resource cannot return until everything
        /// written to its source has landed. Flush alone only submits, which video-upscale showed
        /// turns a 262.9 us blt into a 0.2 us one.
        /// </summary>
        private void Drain()
        {
            context.CopyResource(staging, target);
            context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            context.Unmap(staging, 0);
        }

        private byte[] ReadBack()
        {
            MappedSubresource map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var pixels = new byte[Frames.Width * Frames.Height * 4];
                for (int y = 0; y < Frames.Height; y++)
                {
                    IntPtr row = map.DataPointer + y * (int)map.RowPitch;
                    Marshal.Copy(row, pixels, y * Frames.Width * 4, Frames.Width * 4);
                }
                return pixels;
            }
            finally
            {
                context.Unmap(staging, 0);
            }
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
    /// A 512x512 crop at 1:1, from the hard-edged band at the top of the pattern. Three
    /// 2-megapixel PNGs in a repository would be weight to answer a question a reader settles by
    /// looking at one edge and asking whether it moved.
    /// </summary>
    private static void SaveCrop(byte[] pixels, string file)
    {
        const int size = 512;
        const int left = 600;
        const int top = 40;
        var crop = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            Array.Copy(pixels, ((top + y) * Frames.Width + left) * 4, crop, y * size * 4, size * 4);

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

    private static ID3D11Texture2D CreateSource(ID3D11Device device, int frameIndex)
    {
        byte[] nv12 = Frames.BuildNv12(frameIndex);
        GCHandle pin = GCHandle.Alloc(nv12, GCHandleType.Pinned);
        try
        {
            var desc = new Texture2DDescription
            {
                Width = Frames.Width,
                Height = Frames.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.Decoder,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };
            var initial = new SubresourceData(pin.AddrOfPinnedObject(), (uint)Frames.Width);
            return device.CreateTexture2D(desc, new[] { initial });
        }
        finally
        {
            pin.Free();
        }
    }

    private static ID3D11Texture2D CreateTarget(ID3D11Device device) => device.CreateTexture2D(new Texture2DDescription
    {
        Width = Frames.Width,
        Height = Frames.Height,
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
        Width = Frames.Width,
        Height = Frames.Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CPUAccessFlags = CpuAccessFlags.Read,
        MiscFlags = ResourceOptionFlags.None,
    });

    private static string Json(string adapter, List<Survey> surveys, Group chosen, VideoUsage chosenUsage,
        bool advertised, Rate? doubling, bool? arraysAccepted, Run? repeat, Run? interpolate,
        Difference[]? diffs, bool engaged)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"spike\":\"frame-generation\",\"task\":\"PP50\"");
        sb.Append($",\"adapter\":\"{adapter.Replace("\"", "'")}\"");
        sb.Append($",\"input\":{{\"width\":{Frames.Width},\"height\":{Frames.Height},\"format\":\"NV12\"")
          .Append($",\"fps\":{InputFps},\"shift_px_per_frame\":{Frames.ShiftPerFrame},\"synthetic\":true}}");
        sb.Append($",\"output\":{{\"fps\":{OutputFps},\"format\":\"BGRA\"}}");

        sb.Append(",\"surveys\":[");
        for (int s = 0; s < surveys.Count; s++)
        {
            if (s > 0)
                sb.Append(',');
            sb.Append($"{{\"usage\":\"{surveys[s].Usage}\",\"groups\":[");
            IReadOnlyList<Group> gs = surveys[s].Groups;
            for (int i = 0; i < gs.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append($"{{\"index\":{gs[i].Index},\"past_frames\":{gs[i].PastFrames}")
                  .Append($",\"future_frames\":{gs[i].FutureFrames},\"processor_caps\":{gs[i].ProcessorCaps}")
                  .Append($",\"frame_rate_conversion\":{(gs[i].Interpolates ? "true" : "false")}")
                  .Append(",\"custom_rates\":[");
                for (int r = 0; r < gs[i].CustomRates.Count; r++)
                {
                    Rate rate = gs[i].CustomRates[r];
                    if (r > 0)
                        sb.Append(',');
                    sb.Append($"{{\"numerator\":{rate.Numerator},\"denominator\":{rate.Denominator}")
                      .Append($",\"output_frames\":{rate.OutputFrames},\"input_frames\":{rate.InputFrames}")
                      .Append($",\"interlaced\":{(rate.Interlaced ? "true" : "false")}}}");
                }
                sb.Append("]}");
            }
            sb.Append("]}");
        }
        sb.Append(']');

        sb.Append($",\"frame_rate_conversion_advertised\":{(advertised ? "true" : "false")}");
        sb.Append($",\"chosen\":{{\"usage\":\"{chosenUsage}\",\"group\":{chosen.Index}}}");
        sb.Append($",\"held_frames\":{chosen.FutureFrames}");
        sb.Append($",\"held_ms_at_input_fps\":{HoldMs(chosen.FutureFrames, InputFps).ToString("F2", c)}");
        sb.Append($",\"held_ms_at_output_fps\":{HoldMs(chosen.FutureFrames, OutputFps).ToString("F2", c)}");
        sb.Append($",\"doubling_rate\":{(doubling is null ? "null" : $"\"{doubling}\"")}");
        sb.Append($",\"engaged\":{(engaged ? "true" : "false")}");

        if (arraysAccepted is not null)
            sb.Append($",\"neighbour_arrays_accepted\":{(arraysAccepted.Value ? "true" : "false")}");

        if (repeat is not null && interpolate is not null && diffs is not null)
        {
            sb.Append($",\"repeat_us\":{repeat.Timing.ToJson()}");
            sb.Append($",\"interpolate_us\":{interpolate.Timing.ToJson()}");
            string[] names =
            {
                "repeat_vs_shown", "interpolate_vs_shown", "interpolate_vs_next", "repeat_vs_interpolate",
            };
            for (int i = 0; i < names.Length; i++)
            {
                sb.Append($",\"{names[i]}\":{{\"pixels_changed\":{diffs[i].Changed}")
                  .Append($",\"pixels_total\":{diffs[i].Total}")
                  .Append($",\"mean_abs_delta\":{diffs[i].MeanAbs.ToString("F3", c)}")
                  .Append($",\"max_abs_delta\":{diffs[i].MaxAbs}}}");
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }
}
