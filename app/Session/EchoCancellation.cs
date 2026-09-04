using System.Text.Json;

namespace ChiakiNg.Session;

/// <summary>One way of getting echo cancellation, as the spike's committed reading records it.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Vendor">Whether it is a vendor path, which the hardware contract binds differently.</param>
/// <param name="Reachable">Whether it is present and usable with nothing installed for it.</param>
/// <param name="Evidence">What was looked at, so a no is refutable and a yes is checkable.</param>
/// <param name="Redistributable">What the port would ship for it, empty where nothing.</param>
public readonly record struct EffectPath(
    string Name,
    bool Vendor,
    bool Reachable,
    string Evidence,
    string Redistributable);

/// <summary>The in-box DSP's shape in one mode, which is where it would sit in the capture chain.</summary>
/// <param name="Created">Whether the class id makes an object at all.</param>
/// <param name="Inputs">Input streams in filter mode. Two: the microphone and a reference of what plays.</param>
/// <param name="Outputs">Output streams, which is the cleaned microphone.</param>
/// <param name="TakesFilterMode">Whether filter mode is accepted, where the host feeds both inputs.</param>
/// <param name="TakesSourceMode">Whether source mode is, where the DSP opens the devices itself.</param>
/// <param name="Note">Both modes' counts, so the choice is readable rather than asserted.</param>
public readonly record struct DspShape(
    bool Created,
    int Inputs,
    int Outputs,
    bool TakesFilterMode,
    bool TakesSourceMode,
    string Note);

/// <summary>
/// PP52: the two ways to clean a captured microphone, and which of them this machine has.
///
/// PP52 proposed NVIDIA's audio effects SDK and called it the first card in this port's audio.
/// PP647's hardware contract binds a vendor path to an absence a user cannot see, and PP648 found
/// that a call which succeeds is not a feature that ran. Both bind a path that EXISTS, and whether
/// it exists had not been asked.
///
/// IT DOES NOT, ON A MACHINE WITH THE CARD. spike/audio-effects read an RTX 4060 with a current
/// driver and the vendor's own app installed: NVAFX_SDK_DIR unset, NVIDIA Broadcast absent, and no
/// audio-effects runtime anywhere under either NVIDIA tree. The SDK is not a driver feature. It is
/// a redistributable this port would have to ship, models included, per effect.
///
/// WINDOWS HAS ONE IN THE BOX. CLSID_CWMAudioAEC - the Voice Capture DSP - is registered in both
/// hives with mfwmaaec.dll present in both, and it has done acoustic echo cancellation and noise
/// suppression for communications audio since Vista. It ships nothing.
///
/// WHICH MAKES THE NON-GOALS MOOT RATHER THAN DECISIVE. Neither forbids this: one is about the
/// network path and the other allows a vendor path with a quiet fallback. What the reading changes
/// is that the fallback is better placed than the thing it would fall back from, on the hardware
/// the vendor path was proposed for.
///
/// NOTHING HERE IS TYPED FROM THE SPIKE, for PP666's reason: a table transcribed from a
/// measurement is the same claim wearing the measurement's authority, and it stops being checked
/// the moment the reading is retaken.
/// </summary>
public static class EchoCancellation
{
    /// <summary>The spike's committed reading, where every fact below comes from.</summary>
    public const string ReadingRelativePath = @"spike\audio-effects\release-audio-effects-win11.json";

    /// <summary>The in-box transform's class id, which is what "registered" means here.</summary>
    public const string VoiceCaptureDspClsid = "{745057c7-f353-4f2d-a7ee-58434477730e}";

    /// <summary>The two non-goals that bound this line and forbid it.</summary>
    public static IReadOnlyList<string> Bounding { get; } =
    [
        "No GPU vendor feature for the network path",
        "No vendor path whose absence is visible to the user",
    ];

    /// <summary>The reading, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(ReadingRelativePath);

    /// <summary>
    /// The paths the spike read, from its file.
    ///
    /// Null outside a checkout, empty where the file holds none - a caller deciding whether to
    /// assert needs to tell those apart.
    /// </summary>
    public static IReadOnlyList<EffectPath>? RecordedPaths()
    {
        if (Locate() is not { } path)
            return null;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("paths", out JsonElement paths))
            return [];

        var found = new List<EffectPath>();

        foreach (JsonElement one in paths.EnumerateArray())
        {
            found.Add(new EffectPath(
                one.GetProperty("Name").GetString() ?? string.Empty,
                one.GetProperty("Vendor").GetBoolean(),
                one.GetProperty("Reachable").GetBoolean(),
                one.GetProperty("Evidence").GetString() ?? string.Empty,
                one.GetProperty("Redistributable").GetString() ?? string.Empty));
        }

        return found;
    }

    /// <summary>
    /// The DSP's two shapes, from the same file.
    ///
    /// FILTER MODE KEEPS PP652'S CAPTURE. Two inputs - the microphone and a reference of what is
    /// playing - and one cleaned output, with the host feeding both. Source mode declares no inputs
    /// because the DSP opens the devices itself, which replaces <see cref="Native.WasapiCapture"/>
    /// and takes the device choice with it.
    ///
    /// The mode has to be set BEFORE the counts are read: an unconfigured object reports a shape it
    /// has not been told to take, and the spike's first reading did exactly that and answered
    /// nothing.
    /// </summary>
    public static DspShape? RecordedDsp()
    {
        if (Locate() is not { } path)
            return null;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("dsp", out JsonElement dsp))
            return null;

        return new DspShape(
            dsp.GetProperty("Created").GetBoolean(),
            dsp.GetProperty("Inputs").GetInt32(),
            dsp.GetProperty("Outputs").GetInt32(),
            dsp.GetProperty("TakesFilterMode").GetBoolean(),
            dsp.GetProperty("TakesSourceMode").GetBoolean(),
            dsp.GetProperty("Note").GetString() ?? string.Empty);
    }

    /// <summary>The adapters the reading was taken on, so a no names the card it is a no about.</summary>
    public static IReadOnlyList<string> RecordedAdapters()
    {
        if (Locate() is not { } path)
            return [];

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.TryGetProperty("adapters", out JsonElement adapters)
            ? [.. adapters.EnumerateArray().Select(one => one.GetString() ?? string.Empty)]
            : [];
    }

    /// <summary>
    /// Whether a path costs the package anything, which is the axis the reading turned on.
    ///
    /// Not the same question as whether it is a vendor path. A vendor path that shipped with the
    /// driver would cost nothing, and that is exactly what this one was assumed to be.
    /// </summary>
    public static bool ShipsSomething(EffectPath path) => path.Redistributable.Length > 0;
}
