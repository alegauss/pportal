using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace AudioEffects;

/// <summary>One way of getting echo cancellation, and whether this machine has it.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Vendor">Whether it is a vendor path, which the hardware contract binds differently.</param>
/// <param name="Reachable">Whether it is present and usable here, with nothing installed for it.</param>
/// <param name="Evidence">What was looked at, so a no is refutable and a yes is checkable.</param>
/// <param name="Redistributable">What the port would have to ship for it, or empty where nothing.</param>
internal readonly record struct EffectPath(
    string Name,
    bool Vendor,
    bool Reachable,
    string Evidence,
    string Redistributable);

/// <summary>The in-box DSP as an object, which is what decides where it sits in the capture chain.</summary>
/// <param name="Created">Whether the class id makes an object at all.</param>
/// <param name="Inputs">Input streams it declares. Two means the mic and a reference of what plays.</param>
/// <param name="Outputs">Output streams, which is the cleaned microphone.</param>
/// <param name="TakesFilterMode">Whether it accepts filter mode, where the host feeds both inputs.</param>
/// <param name="TakesSourceMode">Whether it accepts source mode, where it opens the devices itself.</param>
/// <param name="Note">What happened, so a no says which call refused.</param>
internal readonly record struct DspShape(
    bool Created,
    int Inputs,
    int Outputs,
    bool TakesFilterMode,
    bool TakesSourceMode,
    string Note);

/// <summary>
/// PP52: whether the vendor's echo cancellation is reachable, and what this machine has instead.
///
/// PP52's section proposes NVIDIA's audio effects SDK and calls it "the first card in this port's
/// audio". PP647's hardware contract binds any vendor path to an absence a user cannot see, and
/// PP648 found that a call which succeeds is not a feature that ran. Both bind a path that exists.
///
/// THE PRIOR QUESTION IS WHETHER IT EXISTS. The audio effects SDK is not part of the display
/// driver: it arrives with NVIDIA Broadcast or as a Maxine redistributable an application ships. So
/// a machine can have the card, the driver and the vendor's own app and still have nothing to call.
///
/// AND THE COMPARISON IS NOT AGAINST NOTHING. Windows carries a Voice Capture DSP in the box - the
/// same transform that has done acoustic echo cancellation and noise suppression for communications
/// audio since Vista - and whether it is registered here is one registry read.
/// </summary>
internal static class Program
{
    /// <summary>The in-box Voice Capture DSP, which is CLSID_CWMAudioAEC.</summary>
    private const string VoiceCaptureDsp = "{745057c7-f353-4f2d-a7ee-58434477730e}";

    /// <summary>Where the vendor's SDK announces itself, when something has installed it.</summary>
    private static readonly string[] VendorVariables =
        ["NVAFX_SDK_DIR", "NVAFX_MODELS_DIR"];

    /// <summary>And where its runtime would be, if any of these trees held it.</summary>
    private static readonly string[] VendorRoots =
    [
        @"C:\Program Files\NVIDIA Corporation",
        @"C:\Program Files (x86)\NVIDIA Corporation",
        @"C:\Program Files\NVIDIA Broadcast",
    ];

    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "result.json";

        string[] adapters = Adapters();
        Console.WriteLine("display adapters: " + string.Join(", ", adapters));

        EffectPath vendor = ReadVendor();
        EffectPath inBox = ReadInBox();
        DspShape shape = ReadDspShape();

        foreach (EffectPath path in (EffectPath[])[vendor, inBox])
        {
            Console.WriteLine();
            Console.WriteLine($"  {path.Name}{(path.Vendor ? " (vendor)" : " (in-box)")}");
            Console.WriteLine($"      reachable: {(path.Reachable ? "yes" : "no")}");
            Console.WriteLine($"      evidence:  {path.Evidence}");
            Console.WriteLine(
                $"      ships:     {(path.Redistributable.Length == 0 ? "nothing" : path.Redistributable)}");
        }

        Console.WriteLine();
        Console.WriteLine("  the DSP, instantiated");
        Console.WriteLine($"      created:    {(shape.Created ? "yes" : "no")}");
        Console.WriteLine($"      streams:    {shape.Inputs} in, {shape.Outputs} out");
        Console.WriteLine($"      filter mode accepted: {(shape.TakesFilterMode ? "yes" : "no")}");
        Console.WriteLine($"      source mode accepted: {(shape.TakesSourceMode ? "yes" : "no")}");
        Console.WriteLine($"      note:       {shape.Note}");

        Console.WriteLine();
        Console.WriteLine(
            vendor.Reachable
                ? "the vendor path is reachable here"
                : "the vendor path is NOT reachable on a machine with the card, the driver and the "
                    + "vendor's own app: it would have to be shipped");
        Console.WriteLine(
            inBox.Reachable
                ? "the in-box path is registered and costs no redistributable"
                : "the in-box path is not registered either, which would be a machine with no "
                    + "communications audio at all");

        File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    taken = DateTimeOffset.UtcNow,
                    machine = Environment.MachineName,
                    os = Environment.OSVersion.VersionString,
                    dotnet = Environment.Version.ToString(),
                    adapters,
                    paths = new[] { vendor, inBox },
                    dsp = shape,
                },
                new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"written to {output}");
        return 0;
    }

    /// <summary>
    /// The vendor path: an environment variable, or a runtime under one of its trees.
    ///
    /// Both are asked, because either would do. The SDK sets NVAFX_SDK_DIR when installed as an
    /// SDK, and NVIDIA Broadcast carries the runtime without setting it.
    /// </summary>
    private static EffectPath ReadVendor()
    {
        var evidence = new List<string>();

        foreach (string variable in VendorVariables)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            evidence.Add($"{variable}={(string.IsNullOrEmpty(value) ? "(unset)" : value)}");
        }

        var runtimes = new List<string>();

        foreach (string root in VendorRoots)
        {
            if (!Directory.Exists(root))
            {
                evidence.Add($"{root} absent");
                continue;
            }

            try
            {
                runtimes.AddRange(
                    Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
                        .Where(one => Path.GetFileName(one)
                            .Contains("AudioEffect", StringComparison.OrdinalIgnoreCase)
                            || Path.GetFileName(one).StartsWith("nvafx", StringComparison.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException)
            {
                evidence.Add($"{root} unreadable");
            }
        }

        evidence.Add(
            runtimes.Count == 0
                ? "no audio-effects runtime under any NVIDIA tree"
                : "runtime: " + string.Join(", ", runtimes));

        return new EffectPath(
            "NVIDIA audio effects (Maxine)",
            Vendor: true,
            Reachable: runtimes.Count > 0
                || VendorVariables.Any(one => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(one))),
            string.Join("; ", evidence),
            "the SDK's DLLs and its model files, per effect");
    }

    /// <summary>
    /// The in-box path: the Voice Capture DSP's registration, and the server it names.
    ///
    /// Registered in both hives is what a 64-bit host and a 32-bit one each need, and the server
    /// path is read rather than assumed so a registration pointing at nothing is not a yes.
    /// </summary>
    private static EffectPath ReadInBox()
    {
        var evidence = new List<string>();
        bool reachable = false;

        foreach (string hive in (string[])
            [$@"CLSID\{VoiceCaptureDsp}", $@"WOW6432Node\CLSID\{VoiceCaptureDsp}"])
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{hive}\InprocServer32");

            if (key?.GetValue(null) is not string server)
            {
                evidence.Add($"{hive}: not registered");
                continue;
            }

            bool there = File.Exists(Environment.ExpandEnvironmentVariables(server));
            evidence.Add($"{hive}: {server} {(there ? "present" : "MISSING")}");

            // Either hive being whole is enough for the host that matches it; this port is x64.
            if (there && !hive.Contains("WOW6432Node", StringComparison.Ordinal))
                reachable = true;
        }

        return new EffectPath(
            "Windows Voice Capture DSP (CLSID_CWMAudioAEC)",
            Vendor: false,
            reachable,
            string.Join("; ", evidence),
            string.Empty);
    }

    /// <summary>
    /// The DSP as an object, which is what decides where it sits in the capture chain.
    ///
    /// TWO MODES, AND THEY ARE DIFFERENT ARCHITECTURES. In FILTER mode the transform takes two
    /// input streams - the microphone and a reference of what is being played - and the host feeds
    /// both, which fits a capture the port already owns. In SOURCE mode the DSP opens the devices
    /// itself and hands back one cleaned stream, which REPLACES that capture rather than following
    /// it.
    ///
    /// So the stream counts and which modes the property store accepts are the reading: they say
    /// whether PP652's WasapiCapture stays and gains a stage, or is replaced by this.
    /// </summary>
    private static DspShape ReadDspShape()
    {
        Type? type = Type.GetTypeFromCLSID(new Guid(VoiceCaptureDsp));
        if (type is null)
            return new DspShape(false, 0, 0, false, false, "no type for the class id");

        object? instance;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception error)
        {
            return new DspShape(false, 0, 0, false, false, $"could not create: 0x{error.HResult:x8}");
        }

        Marshal.ReleaseComObject(instance);

        // THE COUNT IS READ AFTER THE MODE IS SET, on a fresh object each time. An unconfigured DSP
        // reports the shape it has not been told to take yet, and the first run of this read it
        // before setting anything and got "0 in, 1 out" - which is neither mode and answered
        // nothing. The mode is the property that decides the shape, so it comes first.
        (bool filter, int filterIn, int filterOut, int filterHr) = Shape(type, source: false);
        (bool source, int sourceIn, int sourceOut, int sourceHr) = Shape(type, source: true);

        var note = new List<string>();
        note.Add(filter ? $"filter: {filterIn} in, {filterOut} out" : $"filter refused 0x{filterHr:x8}");
        note.Add(source ? $"source: {sourceIn} in, {sourceOut} out" : $"source refused 0x{sourceHr:x8}");

        // The filter shape is reported as THE shape, because it is the one that keeps PP652's
        // capture: source mode would replace it.
        return new DspShape(true, filterIn, filterOut, filter, source, string.Join("; ", note));
    }

    /// <summary>One mode, set on a fresh object, and the stream counts it then declares.</summary>
    private static (bool Set, int Inputs, int Outputs, int Hr) Shape(Type type, bool source)
    {
        object? instance;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception error)
        {
            return (false, 0, 0, error.HResult);
        }

        try
        {
            if (!SetsMode(instance!, source, out int hr))
                return (false, 0, 0, hr);

            if (instance is not IMediaObject media || media.GetStreamCount(out int inputs, out int outputs) != 0)
                return (true, 0, 0, 0);

            return (true, inputs, outputs, 0);
        }
        finally
        {
            if (instance is not null)
                Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>Whether the property store takes a mode, which is how the DSP is configured at all.</summary>
    private static bool SetsMode(object instance, bool source, out int hr)
    {
        hr = 0;

        if (instance is not IPropertyStore store)
        {
            hr = -1;
            return false;
        }

        // MFPKEY_WMAAECMA_DMO_SOURCE_MODE, VT_BOOL. The one property that must be set before any
        // other, because it decides which of the two shapes the object is.
        var key = new PropertyKey(new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), 3);

        var value = new PropVariant
        {
            Type = VT_BOOL,
            // VARIANT_TRUE is -1 and VARIANT_FALSE is 0, which is the one place a bool is not a bit.
            Value = source ? new IntPtr(-1) : IntPtr.Zero,
        };

        hr = store.SetValue(ref key, ref value);
        return hr == 0;
    }

    private const short VT_BOOL = 11;

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
    }

    [ComImport]
    [Guid("d8ad0f58-5494-4102-97c5-ec798e59bcf4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaObject
    {
        [PreserveSig]
        int GetStreamCount(out int inputs, out int outputs);
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

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);
    }

    /// <summary>The display adapters, so a no about the vendor path says which card it is a no on.</summary>
    private static string[] Adapters()
    {
        var found = new List<string>();

        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

        if (root is null)
            return [];

        foreach (string name in root.GetSubKeyNames().Where(one => one.Length == 4 && one.All(char.IsDigit)))
        {
            using RegistryKey? adapter = root.OpenSubKey(name);

            if (adapter?.GetValue("DriverDesc") is string description)
                found.Add(description);
        }

        return [.. found.Distinct(StringComparer.Ordinal)];
    }
}
