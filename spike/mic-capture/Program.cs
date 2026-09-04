using System.Runtime.InteropServices;
using System.Text.Json;

namespace MicCapture;

/// <summary>One capture device this machine offers.</summary>
/// <param name="Name">Its friendly name, as the property store holds it.</param>
/// <param name="Id">Its endpoint id, so two devices with one name are still two.</param>
/// <param name="Default">Whether it is the default for communications, which is what a mic path opens.</param>
/// <param name="MixChannels">Channels in the shared-mode mix format, which shared capture must match.</param>
/// <param name="MixBits">Bits per sample in that mix format.</param>
/// <param name="MixRate">Its sample rate.</param>
/// <param name="MixIsFloat">Whether its samples are float, which the announced sixteen-bit format is not.</param>
/// <param name="TakesAnnouncedShared">Whether IsFormatSupported accepts the announced format in shared mode.</param>
/// <param name="TakesAnnouncedExclusive">And in exclusive mode, which is the other way to get it without converting.</param>
/// <param name="InitialisesWithAutoConvert">Whether Initialize succeeds on the announced format with AUTOCONVERTPCM, which is Windows converting rather than the port.</param>
internal readonly record struct CaptureDevice(
    string Name,
    string Id,
    bool Default,
    int MixChannels,
    int MixBits,
    int MixRate,
    bool MixIsFloat,
    bool TakesAnnouncedShared,
    bool TakesAnnouncedExclusive,
    bool InitialisesWithAutoConvert);

/// <summary>
/// PP652: what this machine's capture devices offer, and whether the console's format is one of them.
///
/// The question is not "does Windows have audio capture" - it does, several ways. It is whether the
/// port can open a device and get the format streamconnection.c announces, because that decides two
/// things at once: whether a sample-rate and format conversion stage is owed, and whether any new
/// dependency is.
///
/// WASAPI THROUGH ITS OWN COM INTERFACES, no package. That is the shape PP650 used for Media
/// Foundation and it is deliberate here too: adding NAudio to answer a question would prejudge the
/// dependency half of what is being asked.
///
/// Shared mode and exclusive mode are both asked, because they fail differently. Shared mode mixes
/// with everything else on the device and must be the mix format; exclusive mode takes the device
/// and may accept the announced format directly, at the cost of every other application on it.
/// </summary>
internal static class Program
{
    /// <summary>What streamconnection.c announces, which is what is being asked about.</summary>
    private const int Channels = 1;
    private const int Bits = 16;
    private const int Rate = 48000;
    private const int FrameSize = 480;

    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "result.json";

        int hr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
        if (hr < 0 && hr != RPC_E_CHANGED_MODE)
        {
            Console.Error.WriteLine($"CoInitializeEx failed with 0x{hr:x8}");
            return 1;
        }

        object enumeratorObject;
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid)!)!;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"MMDeviceEnumerator could not be created: {error.Message}. WASAPI is not reachable "
                    + "here, which is itself an answer.");
            return 1;
        }

        var enumerator = (IMMDeviceEnumerator)enumeratorObject;

        string defaultId = string.Empty;
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out IMMDevice? fallback) == 0
            && fallback is not null)
        {
            fallback.GetId(out defaultId);
            Marshal.ReleaseComObject(fallback);
        }

        int listed = enumerator.EnumAudioEndpoints(EDataFlow.Capture, DEVICE_STATE_ACTIVE, out IMMDeviceCollection? all);
        if (listed != 0 || all is null)
        {
            Console.Error.WriteLine($"EnumAudioEndpoints failed with 0x{listed:x8}");
            return 1;
        }

        all.GetCount(out int count);
        Console.WriteLine($"{count} active capture device(s)");

        var devices = new List<CaptureDevice>();

        for (int i = 0; i < count; i++)
        {
            if (all.Item(i, out IMMDevice? device) != 0 || device is null)
                continue;

            try
            {
                devices.Add(Read(device, defaultId));
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"device {i} could not be read: {error.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }

        foreach (CaptureDevice device in devices)
        {
            Console.WriteLine(
                $"  {(device.Default ? "*" : " ")} {device.Name}");
            Console.WriteLine(
                $"      mix {device.MixChannels}ch {device.MixBits}-bit {(device.MixIsFloat ? "float" : "int")} "
                    + $"{device.MixRate} Hz");
            Console.WriteLine(
                $"      announced {Channels}ch {Bits}-bit {Rate} Hz: "
                    + $"shared {(device.TakesAnnouncedShared ? "yes" : "no")}, "
                    + $"exclusive {(device.TakesAnnouncedExclusive ? "yes" : "no")}, "
                    + $"autoconvert {(device.InitialisesWithAutoConvert ? "yes" : "no")}");
        }

        bool anyShared = devices.Any(one => one.TakesAnnouncedShared);
        bool anyExclusive = devices.Any(one => one.TakesAnnouncedExclusive);
        bool allAutoconvert = devices.Count > 0 && devices.All(one => one.InitialisesWithAutoConvert);

        Console.WriteLine();
        Console.WriteLine(
            anyShared
                ? "shared mode takes the announced format directly on at least one device"
                : "no device takes the announced format in shared mode, so a conversion is owed");
        Console.WriteLine(
            anyExclusive
                ? "exclusive mode takes it, at the cost of every other application on the device"
                : "exclusive mode does not take it either");
        Console.WriteLine(
            allAutoconvert
                ? "AUTOCONVERTPCM initialises on EVERY device, so the conversion is Windows's and "
                    + "the port owes no resampler"
                : "AUTOCONVERTPCM does not cover every device, so the port owes the conversion");

        File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    taken = DateTimeOffset.UtcNow,
                    machine = Environment.MachineName,
                    os = Environment.OSVersion.VersionString,
                    dotnet = Environment.Version.ToString(),
                    announced = new { channels = Channels, bits = Bits, rate = Rate, frameSize = FrameSize },
                    devices,
                    anyTakesAnnouncedShared = anyShared,
                    anyTakesAnnouncedExclusive = anyExclusive,
                    allInitialiseWithAutoConvert = allAutoconvert,
                },
                new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"written to {output}");
        return 0;
    }

    private static CaptureDevice Read(IMMDevice device, string defaultId)
    {
        device.GetId(out string id);

        string name = "(unnamed)";
        if (device.OpenPropertyStore(STGM_READ, out IPropertyStore? store) == 0 && store is not null)
        {
            try
            {
                if (store.GetValue(ref PKEY_Device_FriendlyName, out PropVariant value) == 0)
                {
                    name = value.AsString() ?? name;
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        int channels = 0, bits = 0, rate = 0;
        bool isFloat = false, shared = false, exclusive = false, autoconverts = false;

        if (device.Activate(AudioClientIid, CLSCTX_ALL, IntPtr.Zero, out object? activated) == 0
            && activated is IAudioClient client)
        {
            try
            {
                if (client.GetMixFormat(out IntPtr mix) == 0 && mix != IntPtr.Zero)
                {
                    WaveFormatEx format = Marshal.PtrToStructure<WaveFormatEx>(mix);
                    channels = format.Channels;
                    bits = format.BitsPerSample;
                    rate = format.SamplesPerSec;

                    // WAVE_FORMAT_EXTENSIBLE hides the real tag in a sub-format GUID; a mix format is
                    // float in practice, and reading the tag alone would call every one of them int.
                    isFloat = format.FormatTag == WAVE_FORMAT_IEEE_FLOAT
                        || (format.FormatTag == WAVE_FORMAT_EXTENSIBLE && format.BitsPerSample == 32);

                    Marshal.FreeCoTaskMem(mix);
                }

                shared = Accepts(client, AudioClientShareMode.Shared);
                exclusive = Accepts(client, AudioClientShareMode.Exclusive);
                autoconverts = Initialises(client);
            }
            finally
            {
                Marshal.ReleaseComObject(client);
            }
        }

        return new CaptureDevice(
            name,
            id,
            string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
            channels,
            bits,
            rate,
            isFloat,
            shared,
            exclusive,
            autoconverts);
    }

    /// <summary>
    /// Whether the client INITIALISES on the announced format with Windows converting.
    ///
    /// The question IsFormatSupported answers is not the question that matters. It says whether the
    /// engine takes the format as it is, and in shared mode the answer is always no because the
    /// engine's format is the mix format. AUTOCONVERTPCM asks something else: put a converter in
    /// front of it. IsFormatSupported does not know about the flag, so the only way to ask is to
    /// initialise and see.
    ///
    /// If this is yes, the port owes no resampler at all - which is a different conclusion from the
    /// one the first two columns support, and the reason a spike initialises rather than asking.
    /// </summary>
    private static bool Initialises(IAudioClient client)
    {
        IntPtr buffer = Announced();
        try
        {
            // A hundred milliseconds in hundred-nanosecond units, which is the buffer WASAPI keeps
            // for a shared client. Ten units of the announced 480 frames.
            const long Duration = 100 * 10_000L;

            int hr = client.Initialize(
                AudioClientShareMode.Shared,
                AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                Duration,
                0,
                buffer,
                IntPtr.Zero);

            return hr == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The announced format, allocated unmanaged for a call that takes a pointer.</summary>
    private static IntPtr Announced()
    {
        var wanted = new WaveFormatEx
        {
            FormatTag = WAVE_FORMAT_PCM,
            Channels = (short)Channels,
            SamplesPerSec = Rate,
            BitsPerSample = (short)Bits,
            BlockAlign = (short)(Channels * Bits / 8),
            Size = 0,
        };
        wanted.AvgBytesPerSec = wanted.SamplesPerSec * wanted.BlockAlign;

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(wanted, buffer, false);
        return buffer;
    }

    /// <summary>Whether a client takes the announced format in a mode. S_OK is yes; anything else is no.</summary>
    private static bool Accepts(IAudioClient client, AudioClientShareMode mode)
    {
        var wanted = new WaveFormatEx
        {
            FormatTag = WAVE_FORMAT_PCM,
            Channels = (short)Channels,
            SamplesPerSec = Rate,
            BitsPerSample = (short)Bits,
            BlockAlign = (short)(Channels * Bits / 8),
            Size = 0,
        };
        wanted.AvgBytesPerSec = wanted.SamplesPerSec * wanted.BlockAlign;

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(wanted, buffer, false);

            // S_FALSE means "no, but here is the nearest" - which is a no to the question asked.
            int hr = client.IsFormatSupported(mode, buffer, out IntPtr closest);
            if (closest != IntPtr.Zero)
                Marshal.FreeCoTaskMem(closest);

            return hr == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int COINIT_MULTITHREADED = 0;
    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    private const int CLSCTX_ALL = 23;
    private const int STGM_READ = 0;
    private const int DEVICE_STATE_ACTIVE = 1;
    private const int AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000);
    private const int AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const short WAVE_FORMAT_PCM = 1;
    private const short WAVE_FORMAT_IEEE_FLOAT = 3;
    private const short WAVE_FORMAT_EXTENSIBLE = unchecked((short)0xFFFE);

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private static PropertyKey PKEY_Device_FriendlyName = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int flags);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    private enum AudioClientShareMode
    {
        Shared = 0,
        Exclusive = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public short Type;
        public short Reserved1;
        public short Reserved2;
        public short Reserved3;
        public IntPtr Value;
        public IntPtr Value2;

        /// <summary>VT_LPWSTR is 31, and a friendly name is one. Anything else is not a string here.</summary>
        public readonly string? AsString() => Type == 31 ? Marshal.PtrToStringUni(Value) : null;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            int context,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore? store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetAt(int index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(AudioClientShareMode mode, int flags, long duration, long period, IntPtr format, IntPtr session);

        [PreserveSig]
        int GetBufferSize(out int frames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out int frames);

        [PreserveSig]
        int IsFormatSupported(AudioClientShareMode mode, IntPtr format, out IntPtr closest);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);
    }
}


