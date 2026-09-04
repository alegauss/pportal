using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

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
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "result.json";

        int started = MFStartup(Version, 0);
        if (started != 0)
        {
            Console.Error.WriteLine(
                $"MFStartup failed with 0x{started:x8} - Media Foundation is not available here, "
                    + "which is itself an answer to PP650's first question.");
            return 1;
        }

        try
        {
            IReadOnlyList<Decoder> h264 = DecodersFor(MfVideoFormatH264, "H.264");
            IReadOnlyList<Decoder> hevc = DecodersFor(MfVideoFormatHevc, "HEVC");

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
        var input = new MftRegisterTypeInfo { MajorType = MfMediaTypeVideo, Subtype = subtype };

        const int flags = MftEnumFlagHardware | MftEnumFlagSyncMft | MftEnumFlagAsyncMft;

        int hr = MFTEnumEx(
            MftCategoryVideoDecoder, flags, ref input, IntPtr.Zero, out IntPtr array, out uint count);

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
                Name: StringOf(attributes, MftFriendlyNameAttribute) ?? "(unnamed)",
                Clsid: GuidOf(attributes, MftTransformClsidAttribute)?.ToString() ?? "(none)",
                Hardware: FlagOf(attributes, MftEnumHardwareUrlAttribute),
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
        Guid transform = IidMfTransform;

        if (activation.ActivateObject(ref transform, out object? made) != 0 || made is not IMFTransform decoder)
            return false;

        try
        {
            if (decoder.GetAttributes(out IMFAttributes? own) != 0 || own is null)
                return false;

            try
            {
                return UintOf(own, MfSaD3D11Aware) is > 0;
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

    private const int Version = 0x00020070;

    private const int MftEnumFlagSyncMft = 0x00000001;
    private const int MftEnumFlagAsyncMft = 0x00000002;
    private const int MftEnumFlagHardware = 0x00000004;

    private static readonly Guid MftCategoryVideoDecoder =
        new("d6c02d4b-6833-45b4-971a-05a4b04bab91");

    private static readonly Guid MfMediaTypeVideo =
        new("73646976-0000-0010-8000-00aa00389b71");

    /// <summary>MFVideoFormat_H264, which is the FOURCC 'H264' in the media subtype namespace.</summary>
    private static readonly Guid MfVideoFormatH264 =
        new("34363248-0000-0010-8000-00aa00389b71");

    /// <summary>And MFVideoFormat_HEVC, 'HEVC'.</summary>
    private static readonly Guid MfVideoFormatHevc =
        new("43564548-0000-0010-8000-00aa00389b71");

    private static readonly Guid MftFriendlyNameAttribute =
        new("314ffbae-5b41-4c95-9c19-4e7d586face3");

    private static readonly Guid MftTransformClsidAttribute =
        new("6821c42b-65a4-4e82-99bc-9a88205ecd0c");

    private static readonly Guid MftEnumHardwareUrlAttribute =
        new("2fb866ac-b078-4942-ab6c-003d05cda674");

    private static readonly Guid MfSaD3D11Aware =
        new("206b4fc8-fcf9-4c51-afe3-9764369e33a0");

    /// <summary>IID_IMFTransform, which is what an activate is asked to make.</summary>
    private static readonly Guid IidMfTransform =
        new("bf94c121-5b05-4e6f-8000-ba598961414d");

    [StructLayout(LayoutKind.Sequential)]
    private struct MftRegisterTypeInfo
    {
        public Guid MajorType;
        public Guid Subtype;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(
        Guid category, int flags, ref MftRegisterTypeInfo input, IntPtr output,
        out IntPtr activate, out uint count);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(IntPtr memory);

    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);
    }

    /// <summary>
    /// IMFActivate, which is IMFAttributes plus the three methods that make the object.
    ///
    /// Declared to the end of its vtable rather than to the method wanted, because a COM interface
    /// is its layout: stopping early would put ShutdownObject where DetachObject is and call the
    /// wrong one, which is a crash rather than a wrong answer.
    /// </summary>
    [ComImport]
    [Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFActivate
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint capacity, out uint size);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);

        [PreserveSig]
        int ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? value);

        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    /// <summary>
    /// IMFTransform, to its second method and no further.
    ///
    /// Only GetAttributes is called, and the two before it are declared so its slot is right.
    /// </summary>
    [ComImport]
    [Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFTransform
    {
        [PreserveSig]
        int GetStreamLimits(
            out uint inputMinimum, out uint inputMaximum,
            out uint outputMinimum, out uint outputMaximum);

        [PreserveSig] int GetStreamCount(out uint inputs, out uint outputs);

        [PreserveSig]
        int GetStreamIDs(
            uint inputCapacity, [Out] uint[] inputs,
            uint outputCapacity, [Out] uint[] outputs);

        [PreserveSig] int GetInputStreamInfo(uint id, IntPtr info);
        [PreserveSig] int GetOutputStreamInfo(uint id, IntPtr info);
        [PreserveSig] int GetAttributes(out IMFAttributes? attributes);
    }
}
