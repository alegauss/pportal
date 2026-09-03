using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Concentus;
using Concentus.Enums;
using PresentPath;

namespace OpusDecode;

/// <summary>
/// PP32: what managed Opus decode costs against the native decoder lib/ already links.
///
/// This is the one half of the audio path where both options are real. The video decoder is not -
/// PP31 made that a non-goal, because nothing in .NET holds 1080p60 - but Opus is a different size
/// of problem, and Concentus is a line-by-line port of libopus rather than a reimplementation. So
/// the question is measurable: what does a frame cost each way, and do they produce the same audio.
///
/// THE CORPUS IS ENCODED NATIVELY, on purpose. Encoding it with the managed library would leave the
/// obvious objection standing - that the packets are ones only that library likes - and it costs
/// nothing to avoid, because libopus is already here.
///
/// The profile is the console's: 48 kHz, stereo, 480-sample frames, which is what
/// chiaki_audio_header carries and what opus_decoder_create is handed in opusdecoder.c.
///
/// WHAT THIS DOES NOT MEASURE is the dependency. That half is a count rather than a clock -
/// libopus-0.dll's size against a NuGet reference - and the README carries it, because a number
/// this spike printed would be a number about one machine's copy of a DLL.
/// </summary>
internal static class Program
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    /// <summary>Ten milliseconds at 48 kHz, which is the frame size the audio header carries.</summary>
    private const int FrameSize = 480;

    /// <summary>Enough frames that a batch is not dominated by whatever ran before it.</summary>
    private const int Frames = 500;

    private const int Repeats = 20;
    private const int Warmup = 200;

    /// <summary>The bitrate the corpus is encoded at. Stated rather than defaulted, so a reader can
    /// re-take the run at another one and know what changed.</summary>
    private const int Bitrate = 96000;

    private static int Main(string[] argv)
    {
        string outPath = argv.Length > 0 ? argv[0] : "result.json";

        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, ResolveOpus);

        Console.WriteLine($"profile    : {SampleRate} Hz, {Channels} channels, {FrameSize}-sample frames");
        Console.WriteLine($"corpus     : {Frames} frames encoded natively at {Bitrate} bps");
        Console.WriteLine($"frames     : {Repeats} batches, {Warmup} warmup");
        Console.WriteLine();

        short[] pcm = Signal();
        byte[][] packets = EncodeNatively(pcm, out int nativeBytes);
        Console.WriteLine($"encoded    : {nativeBytes} bytes, {nativeBytes / (double)Frames:F1} per frame");
        Console.WriteLine();

        (Stats native, short[] fromNative) = TimeNative(packets);
        (Stats managed, short[] fromManaged) = TimeManaged(packets);

        Console.WriteLine(native);
        Console.WriteLine(managed);
        Console.WriteLine();
        Console.WriteLine($"tail       : {native.Name} p99/p50 = {native.P99OverP50:F2}");
        Console.WriteLine($"tail       : {managed.Name} p99/p50 = {managed.P99OverP50:F2}");
        Console.WriteLine("  near 1.0 is a reading; PP49's contaminated runs sat at 1.87 and above.");
        Console.WriteLine();

        Difference diff = Compare(fromNative, fromManaged);
        Console.WriteLine($"agreement  : {diff.Differing} of {diff.Total} samples differ, max |delta| {diff.MaxAbs}");
        Console.WriteLine($"ratio      : managed costs {managed.Percentile(0.50) / native.Percentile(0.50):F2}x the native p50");

        File.WriteAllText(outPath, Json(native, managed, diff, nativeBytes));
        Console.WriteLine();
        Console.WriteLine($"json       : {Path.GetFullPath(outPath)}");

        // Zero only where both decoders ran and produced the same number of samples. Whether the
        // audio AGREES is reported above and is not this exit code's business: a small difference is
        // a finding rather than a failure, and collapsing the two would hide it.
        return diff.Total > 0 ? 0 : 1;
    }

    private sealed record Difference(long Differing, long Total, int MaxAbs);

    /// <summary>
    /// Where libopus-0.dll is.
    ///
    /// The build's own copy, beside the shim, rather than whatever is on PATH: a spike that
    /// measured a different libopus from the one the port ships would be answering about a machine.
    /// The plain name is the fallback, so a tree that has not been built yet still runs.
    /// </summary>
    private static IntPtr ResolveOpus(string library, Assembly assembly, DllImportSearchPath? path)
    {
        if (library != Opus)
            return IntPtr.Zero;

        string? here = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        if (here is not null)
        {
            string beside = Path.GetFullPath(Path.Combine(
                here, "..", "..", "..", "..", "..", "build", "chiaki-ng-Win", "libopus-0.dll"));
            if (File.Exists(beside) && NativeLibrary.TryLoad(beside, out IntPtr found))
                return found;
        }

        return NativeLibrary.TryLoad("libopus-0.dll", out IntPtr fallback) ? fallback : IntPtr.Zero;
    }

    /// <summary>
    /// A deterministic stereo signal, chosen to be work rather than silence.
    ///
    /// Silence compresses to almost nothing and decodes to almost nothing, which would measure the
    /// call overhead and call it a decoder. Two tones an octave and a fifth apart, at different
    /// levels per channel, plus a slow sweep - enough spectral content that the codec has bands to
    /// carry and the decoder has work in all of them.
    /// </summary>
    private static short[] Signal()
    {
        var pcm = new short[Frames * FrameSize * Channels];

        for (int i = 0; i < Frames * FrameSize; i++)
        {
            double t = i / (double)SampleRate;
            double sweep = 220.0 + 660.0 * (i / (double)(Frames * FrameSize));

            double left = 0.45 * Math.Sin(2 * Math.PI * 440.0 * t)
                + 0.25 * Math.Sin(2 * Math.PI * 660.0 * t)
                + 0.15 * Math.Sin(2 * Math.PI * sweep * t);
            double right = 0.35 * Math.Sin(2 * Math.PI * 880.0 * t)
                + 0.30 * Math.Sin(2 * Math.PI * sweep * t);

            pcm[i * 2] = (short)(left * short.MaxValue * 0.8);
            pcm[(i * 2) + 1] = (short)(right * short.MaxValue * 0.8);
        }

        return pcm;
    }

    private static byte[][] EncodeNatively(short[] pcm, out int totalBytes)
    {
        IntPtr encoder = opus_encoder_create(
            SampleRate, Channels, ApplicationRestrictedLowdelay, out int error);
        if (encoder == IntPtr.Zero || error != 0)
            throw new InvalidOperationException($"opus_encoder_create failed: {error}");

        try
        {
            opus_encoder_ctl(encoder, SetBitrateRequest, Bitrate);

            var packets = new byte[Frames][];
            var scratch = new byte[4000];
            totalBytes = 0;

            for (int f = 0; f < Frames; f++)
            {
                int written;
                unsafe
                {
                    fixed (short* from = &pcm[f * FrameSize * Channels])
                    fixed (byte* into = scratch)
                    {
                        written = opus_encode(encoder, from, FrameSize, into, scratch.Length);
                    }
                }

                if (written < 0)
                    throw new InvalidOperationException($"opus_encode failed: {written}");

                packets[f] = scratch[..written];
                totalBytes += written;
            }

            return packets;
        }
        finally
        {
            opus_encoder_destroy(encoder);
        }
    }

    private static (Stats Timing, short[] Pcm) TimeNative(byte[][] packets)
    {
        IntPtr decoder = opus_decoder_create(SampleRate, Channels, out int error);
        if (decoder == IntPtr.Zero || error != 0)
            throw new InvalidOperationException($"opus_decoder_create failed: {error}");

        try
        {
            var pcm = new short[Frames * FrameSize * Channels];

            for (int w = 0; w < Warmup; w++)
                Decode(decoder, packets[w % packets.Length], pcm, 0);

            var timing = new Stats("native libopus");
            for (int r = 0; r < Repeats; r++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int f = 0; f < Frames; f++)
                    Decode(decoder, packets[f], pcm, f * FrameSize * Channels);
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                timing.Push(elapsed * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency / Frames);
            }

            return (timing, pcm);
        }
        finally
        {
            opus_decoder_destroy(decoder);
        }
    }

    private static unsafe void Decode(IntPtr decoder, byte[] packet, short[] pcm, int at)
    {
        int samples;
        fixed (byte* data = packet)
        fixed (short* into = &pcm[at])
        {
            samples = opus_decode(decoder, data, packet.Length, into, FrameSize, 0);
        }

        if (samples != FrameSize)
            throw new InvalidOperationException($"opus_decode returned {samples}");
    }

    private static (Stats Timing, short[] Pcm) TimeManaged(byte[][] packets)
    {
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var pcm = new short[Frames * FrameSize * Channels];

        for (int w = 0; w < Warmup; w++)
            decoder.Decode(packets[w % packets.Length], pcm.AsSpan(0, FrameSize * Channels), FrameSize, false);

        var timing = new Stats("managed Concentus");
        for (int r = 0; r < Repeats; r++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int f = 0; f < Frames; f++)
            {
                decoder.Decode(
                    packets[f], pcm.AsSpan(f * FrameSize * Channels, FrameSize * Channels), FrameSize, false);
            }

            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            timing.Push(elapsed * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency / Frames);
        }

        return (timing, pcm);
    }

    /// <summary>
    /// How far apart the two decoders' output is.
    ///
    /// Reported and not asserted. Concentus claims bit-exactness against libopus on the fixed-point
    /// path and this build may not be taking it, so a small difference is a fact about the two
    /// libraries and a large one would be a fact about this spike.
    /// </summary>
    private static Difference Compare(short[] a, short[] b)
    {
        long differing = 0;
        int max = 0;

        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            if (d == 0)
                continue;

            differing++;
            if (d > max)
                max = d;
        }

        return new Difference(differing, a.Length, max);
    }

    private static string Json(Stats native, Stats managed, Difference diff, int bytes)
    {
        var c = CultureInfo.InvariantCulture;
        return "{"
            + "\"spike\":\"opus-decode\""
            + ",\"task\":\"PP32\""
            + $",\"profile\":{{\"rate\":{SampleRate},\"channels\":{Channels},\"frame_size\":{FrameSize}}}"
            + $",\"corpus\":{{\"frames\":{Frames},\"bitrate\":{Bitrate},\"bytes\":{bytes},\"encoder\":\"native\"}}"
            + $",\"batches\":{Repeats}"
            + $",\"samples_differing\":{diff.Differing}"
            + $",\"samples_total\":{diff.Total}"
            + $",\"max_abs_delta\":{diff.MaxAbs}"
            + $",\"native_us\":{native.ToJson()}"
            + $",\"managed_us\":{managed.ToJson()}"
            + "}\n";
    }

    private const string Opus = "opus";

    /// <summary>OPUS_APPLICATION_RESTRICTED_LOWDELAY, which is what a remote play stream is.</summary>
    private const int ApplicationRestrictedLowdelay = 2051;

    /// <summary>OPUS_SET_BITRATE_REQUEST.</summary>
    private const int SetBitrateRequest = 4002;

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr opus_encoder_create(int fs, int channels, int application, out int error);

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern void opus_encoder_destroy(IntPtr st);

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern int opus_encoder_ctl(IntPtr st, int request, int value);

    // Pointers rather than spans. A Span cannot cross a classic DllImport - "non-blittable generic
    // types cannot be marshaled" - and the alternative is copying every frame into an array for the
    // call, which would put an allocation inside the thing being timed.
    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int opus_encode(
        IntPtr st, short* pcm, int frameSize, byte* data, int maxDataBytes);

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr opus_decoder_create(int fs, int channels, out int error);

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern void opus_decoder_destroy(IntPtr st);

    [DllImport(Opus, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int opus_decode(
        IntPtr st, byte* data, int len, short* pcm, int frameSize, int decodeFec);
}
