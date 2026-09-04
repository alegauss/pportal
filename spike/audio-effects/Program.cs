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
