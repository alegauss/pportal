using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using static DecoderChoice.Mf;

namespace DecoderChoice;

/// <summary>One decoder Media Foundation offers for a codec.</summary>
/// <param name="Name">The transform's friendly name, as the registry holds it.</param>
/// <param name="Clsid">Its class id, so two decoders with one name are still two.</param>
/// <param name="Hardware">
/// Whether it carries MFT_ENUM_HARDWARE_URL_Attribute, which is a registration fact and NOT where
/// the decoding happens. A vendor's own MFT - Intel's, say - carries it; Microsoft's H.264 decoder
/// does not and still decodes on the GPU, because being D3D11-aware is what lets it hand the work
/// to the driver's DXVA. Reported because it is what the enumeration says, and labelled carefully
/// because reading it as "this one is the CPU path" is the mistake it invites.
/// </param>
/// <param name="D3D11Aware">
/// Whether it declares MF_SA_D3D11_AWARE, which is the whole of PP650's second question: a decoder
/// that does can be given a D3D11 device and hand its output back as a texture, and one that cannot
/// decodes into system memory and costs the copy the port already measured at 2253 microseconds.
/// </param>
internal readonly record struct Decoder(string Name, string Clsid, bool Hardware, bool D3D11Aware);

/// <summary>
/// PP650: what Media Foundation offers this machine, and what leaving FFmpeg would cost.
///
/// The reading has three parts, and only the first two need Windows to answer. What Media
/// Foundation has for H.264 and HEVC is an enumeration; whether each decodes to a D3D11 texture is
/// one attribute on each; and what the port would lose is countable from the tree it is run in.
/// </summary>
internal static class Program
{
    /// <summary>PP818: where spike/decode-path leaves the stream both halves are measured on.</summary>
    private const string DefaultStream = @"..\decode-path\stream.h264";

    private static int Main(string[] args)
    {
        // PP818: the timing is a mode rather than a second spike, because it answers the question
        // this one's README says it left open - and a number in a different README would have to be
        // carried across by hand to be compared with the FFmpeg column beside it.
        if (args.Length > 0 && args[0] == "--time")
            return Time(args.Skip(1).ToArray());

        string output = args.Length > 0 ? args[0] : "result.json";

        int started = MFStartup(Mf.Version, 0);
        if (started != 0)
        {
            Console.Error.WriteLine(
                $"MFStartup failed with 0x{started:x8} - Media Foundation is not available here, "
                    + "which is itself an answer to PP650's first question.");
            return 1;
        }

        try
        {
            IReadOnlyList<Decoder> h264 = DecodersFor(VideoFormatH264, "H.264");
            IReadOnlyList<Decoder> hevc = DecodersFor(VideoFormatHevc, "HEVC");

            Report("H.264", h264);
            Report("HEVC", hevc);

            var reading = new
            {
                spike = "decoder-choice",
                task = "PP650",
                os = Environment.OSVersion.VersionString,
                h264 = h264,
                hevc = hevc,
                h264_d3d11_aware = h264.Count(one => one.D3D11Aware),
                hevc_d3d11_aware = hevc.Count(one => one.D3D11Aware),
            };

            File.WriteAllText(
                output, JsonSerializer.Serialize(reading, new JsonSerializerOptions { WriteIndented = false }));

            Console.WriteLine();
            Console.WriteLine($"written to {output}");

            // Exit 1 where the machine offers neither, because then there is nothing below to read.
            return h264.Count + hevc.Count > 0 ? 0 : 1;
        }
        finally
        {
            MFShutdown();
        }
    }

    /// <summary>
    /// PP818: decode the stream and print what a frame cost, paced as a console sends them.
    /// </summary>
    private static int Time(string[] args)
    {
        string stream = args.FirstOrDefault(one => !one.StartsWith("--", StringComparison.Ordinal))
            ?? DefaultStream;

        bool unpaced = args.Contains("--unpaced");
        int limit = 0;
        string output = "timing.json";

        foreach (string one in args)
        {
            if (one.StartsWith("--frames=", StringComparison.Ordinal))
                _ = int.TryParse(one.AsSpan(9), CultureInfo.InvariantCulture, out limit);

            if (one.StartsWith("--out=", StringComparison.Ordinal))
                output = one[6..];
        }

        int started = MFStartup(Mf.Version, 0);

        if (started != 0)
        {
            Console.Error.WriteLine($"MFStartup failed with 0x{started:x8}");
            return 1;
        }

        try
        {
            DecodeReading? reading = DecodeTiming.Measure(stream, 1920, 1080, !unpaced, limit);

            if (reading is not { } measured)
                return 1;

            Console.WriteLine();
            Console.WriteLine($"decoder   : {measured.Decoder}");
            Console.WriteLine($"fed       : {(measured.Paced ? "paced at 60 fps" : "as fast as it accepts")}");
            Console.WriteLine($"frames    : {measured.Frames} counted");
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"per frame : mean {measured.MeanMicroseconds:F1} us, p50 {measured.P50Microseconds:F1} us, "
                    + $"p99 {measured.P99Microseconds:F1} us"));

            File.WriteAllText(
                output,
                JsonSerializer.Serialize(
                    new { spike = "decoder-choice", task = "PP818", os = Environment.OSVersion.VersionString, reading = measured },
                    new JsonSerializerOptions { WriteIndented = false }));

            Console.WriteLine();
            Console.WriteLine($"written to {output}");

            return 0;
        }
        finally
        {
            MFShutdown();
        }
    }

    private static void Report(string codec, IReadOnlyList<Decoder> decoders)
    {
        Console.WriteLine($"{codec}: {decoders.Count} decoder(s)");

        foreach (Decoder one in decoders)
        {
            Console.WriteLine(
                "  {0,-52} {1,-18} {2}",
                one.Name,
                one.Hardware ? "vendor MFT" : "no hardware URL",
                one.D3D11Aware ? "D3D11-aware" : "system memory only");
        }
    }

    /// <summary>
    /// Every video decoder registered for one input subtype, hardware and software alike.
    ///
    /// MFT_ENUM_FLAG_SORTANDFILTER is deliberately NOT passed: it applies the preference order and
    /// the blocklist a player would want, and what this asks is what EXISTS. The three flags below
    /// are the kinds of transform a decoder can be registered as, and asking for all three is what
    /// makes "software fallback" a countable thing rather than an assumption.
    ///
    /// TRANSCODE_ONLY IS NOT AMONG THEM, and the first run of this spike had it. It reads as another
    /// kind to include and is a FILTER - "only MFTs optimised for transcoding" - so passing it
    /// returned one software decoder per codec on a machine with a discrete GPU, which is the wrong
    /// answer arriving quietly. LOCAL_MFT is left out for a plainer reason: it enumerates transforms
    /// this process registered itself, and this process registers none.
    /// </summary>
    private static IReadOnlyList<Decoder> DecodersFor(Guid subtype, string codec)
    {
        var input = new RegisterTypeInfo { MajorType = MediaTypeVideo, Subtype = subtype };

        const int flags = MftEnumFlagHardware | MftEnumFlagSyncMft | MftEnumFlagAsyncMft;

        int hr = MFTEnumEx(
            CategoryVideoDecoder, flags, ref input, IntPtr.Zero, out IntPtr array, out uint count);

        if (hr != 0)
        {
            Console.Error.WriteLine($"MFTEnumEx for {codec} failed with 0x{hr:x8}");
            return [];
        }

        var found = new List<Decoder>((int)count);

        try
        {
            for (var i = 0; i < count; i++)
            {
                IntPtr activate = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (activate == IntPtr.Zero)
                    continue;

                try
                {
                    found.Add(Describe(activate));
                }
                finally
                {
                    Marshal.Release(activate);
                }
            }
        }
        finally
        {
            CoTaskMemFree(array);
        }

        return found;
    }

    /// <summary>One transform's name, class and the two flags this is about.</summary>
    private static Decoder Describe(IntPtr activate)
    {
        object activation = Marshal.GetObjectForIUnknown(activate);

        try
        {
            var attributes = (IMFAttributes)activation;

            return new Decoder(
                Name: StringOf(attributes, FriendlyNameAttribute) ?? "(unnamed)",
                Clsid: GuidOf(attributes, TransformClsidAttribute)?.ToString() ?? "(none)",
                Hardware: FlagOf(attributes, EnumHardwareUrlAttribute),
                D3D11Aware: IsD3D11Aware((IMFActivate)activation));
        }
        finally
        {
            Marshal.ReleaseComObject(activation);
        }
    }

    /// <summary>
    /// Whether a decoder declares MF_SA_D3D11_AWARE, which is PP650's second question.
    ///
    /// THE ATTRIBUTE IS THE TRANSFORM'S, NOT THE ACTIVATE'S, which the first run of this spike got
    /// wrong and reported as "no decoder here is D3D11-aware" - a plausible answer that would have
    /// settled the question backwards. The activate is a factory and carries registration data;
    /// the flag is a property of the object it makes, so the object has to be made.
    ///
    /// Created and shut down inside this call. A decoder left alive holds a hardware context, and
    /// enumerating a machine's decoders is not a reason to hold every one of them at once.
    /// </summary>
    private static bool IsD3D11Aware(IMFActivate activation)
    {
        Guid transform = IidTransform;

        if (activation.ActivateObject(ref transform, out object? made) != 0 || made is not IMFTransform decoder)
            return false;

        try
        {
            if (decoder.GetAttributes(out IMFAttributes? own) != 0 || own is null)
                return false;

            try
            {
                return UintOf(own, SaD3D11Aware) is > 0;
            }
            finally
            {
                Marshal.ReleaseComObject(own);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(decoder);
            activation.ShutdownObject();
        }
    }

    private static string? StringOf(IMFAttributes attributes, Guid key)
    {
        if (attributes.GetStringLength(ref key, out uint length) != 0)
            return null;

        var buffer = new char[length + 1];
        return attributes.GetString(ref key, buffer, (uint)buffer.Length, out uint written) == 0
            ? new string(buffer, 0, (int)written)
            : null;
    }

    private static Guid? GuidOf(IMFAttributes attributes, Guid key)
        => attributes.GetGUID(ref key, out Guid value) == 0 ? value : null;

    private static uint? UintOf(IMFAttributes attributes, Guid key)
        => attributes.GetUINT32(ref key, out uint value) == 0 ? value : null;

    /// <summary>Whether an attribute is present at all, which is how the hardware URL reads.</summary>
    private static bool FlagOf(IMFAttributes attributes, Guid key)
        => attributes.GetStringLength(ref key, out _) == 0;

}
