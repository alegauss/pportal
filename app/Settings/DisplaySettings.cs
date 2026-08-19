using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>Which of a display target's modes is in force. Not stored - derived from the value.</summary>
public enum DisplayTargetMode
{
    /// <summary>Let libplacebo decide. Stored as zero.</summary>
    Auto,

    /// <summary>Infinite contrast, which only the contrast setting has. Stored as MINUS ONE.</summary>
    Infinity,

    /// <summary>A number the user picked, which is the value itself.</summary>
    Numeric,
}

/// <summary>
/// PP19: one integer carrying a mode AND a value, with the modes spelled as sentinels.
///
/// The display dialog's peak and contrast rows look like an ordinary combo plus a slider. They are
/// not: there is no stored mode. The combo's index is COMPUTED from the value - zero means Auto -
/// and picking a mode writes a value that means it. So the round trip is value → index → value, and
/// each direction is a separate table.
///
/// Contrast is where that bites. Its three choices are Auto, Infinity and Numeric, and the value
/// for Infinity is MINUS ONE while Auto is zero - so the index order and the value order do not
/// match. A port mapping index to value in the obvious ascending way swaps Auto and Infinity, and
/// the symptom is a picture graded for a display nobody owns.
/// </summary>
public sealed class DisplayTarget
{
    private readonly int[] modeValues;

    private DisplayTarget(
        string key, IReadOnlyList<string> labels, DisplayTargetMode[] modes, int[] modeValues,
        int numericDefault, int min, int max, int step)
    {
        Key = key;
        Labels = labels;
        Modes = modes;
        this.modeValues = modeValues;
        NumericDefault = numericDefault;
        Minimum = min;
        Maximum = max;
        Step = step;
    }

    /// <summary>The preference this reads and writes.</summary>
    public string Key { get; }

    /// <summary>What the combo shows.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>Which mode each index is, in the combo's order.</summary>
    public IReadOnlyList<DisplayTargetMode> Modes { get; }

    /// <summary>The value written when the numeric mode is chosen.</summary>
    public int NumericDefault { get; }

    /// <summary>The slider's bounds and step, which do not cover the mode sentinels.</summary>
    public int Minimum { get; }

    public int Maximum { get; }

    public int Step { get; }

    /// <summary>
    /// Target Peak. Two choices, and its slider starts at 10 - so the Auto value of zero is not a
    /// value the slider could ever produce, which is what makes zero usable as a sentinel.
    /// </summary>
    public static DisplayTarget Peak { get; } = new(
        "settings/display_target_peak",
        ["Auto", "Numeric Value"],
        [DisplayTargetMode.Auto, DisplayTargetMode.Numeric],
        [0, 1000],
        1000,
        10, 10000, 10);

    /// <summary>
    /// Target Contrast. Three choices, and the index order is NOT the value order: index 1 is
    /// Infinity, stored as -1, while index 0 is Auto, stored as 0.
    /// </summary>
    public static DisplayTarget Contrast { get; } = new(
        "settings/display_target_contrast",
        ["Auto", "Infinity", "Numeric Value"],
        [DisplayTargetMode.Auto, DisplayTargetMode.Infinity, DisplayTargetMode.Numeric],
        [0, -1, 1000],
        1000,
        10, 1000000, 1000);

    /// <summary>
    /// Which mode a stored value means. Everything that is not a sentinel is Numeric, including
    /// values the slider cannot reach - a stored 5 shows as a number, not as Auto.
    /// </summary>
    public DisplayTargetMode ModeOf(int stored)
    {
        for (int i = 0; i < Modes.Count; i++)
        {
            if (Modes[i] != DisplayTargetMode.Numeric && modeValues[i] == stored)
                return Modes[i];
        }

        return DisplayTargetMode.Numeric;
    }

    /// <summary>The combo index a stored value shows at.</summary>
    public int IndexOf(int stored)
    {
        DisplayTargetMode mode = ModeOf(stored);
        for (int i = 0; i < Modes.Count; i++)
        {
            if (Modes[i] == mode)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// The value picking an index writes. For the numeric mode that is the default and not the
    /// value already stored - choosing "Numeric Value" RESETS it, which is the QML's behaviour and
    /// the one a port would smooth over by keeping the old number.
    /// </summary>
    public int StoredForIndex(int index)
        => index >= 0 && index < modeValues.Length ? modeValues[index] : modeValues[0];

    /// <summary>Whether the slider is on screen, which is the numeric mode and nothing else.</summary>
    public bool SliderVisibleAt(int index)
        => index >= 0 && index < Modes.Count && Modes[index] == DisplayTargetMode.Numeric;
}

/// <summary>
/// PP19: the display settings dialog - four rows, two of them the mode-in-a-value pattern above.
///
/// The other two are ordinary index combos: target primaries with eighteen choices and transfer
/// characteristics with seventeen, both stored as the index and both defaulting to Auto at zero.
/// They are long lists and nothing else, which is why the interest is all in the two sliders.
///
/// One layout rule is part of the behaviour rather than the drawing: the contrast combo declares
/// `lastInFocusChain: currentIndex != 2`, so where the focus chain ENDS moves depending on whether
/// the value slider is showing. A port that fixed the chain's end would trap focus on a hidden
/// control in one of the three modes.
/// </summary>
public sealed class DisplaySettingsViewModel : DialogViewModel
{
    private int primaries;
    private int transfer;
    private int peakIndex;
    private int peakValue;
    private int contrastIndex;
    private int contrastValue;

    /// <summary>The eighteen target-primary choices, in the QML's order.</summary>
    public static IReadOnlyList<string> PrimaryLabels { get; } =
    [
        "Auto",
        "ITU-R Rec. BT.601 NTSC (Standard Gamut)",
        "ITU-R Rec. BT.601 PAL (Standard Gamut)",
        "ITU-R Rec. BT.709 (Standard Gamut)",
        "ITU-R Rec. BT.470 M (Standard Gamut)",
        "EBU Tech. 3213-E (Standard Gamut)",
        "ITU-R Rec. BT.2020 (Wide Gamut)",
        "Apple RGB (Wide Gamut)",
        "Adobe RGB (Wide Gamut)",
        "ProPhoto RGB (Wide Gamut)",
        "CIE 1931 RGB primaries (Wide Gamut)",
        "DCI-P3 (Wide Gamut)",
        "DCI-P3 with D65 white point (Wide Gamut)",
        "Panasonic V-Gamut (Wide Gamut)",
        "Sony S-Gamut (Wide Gamut)",
        "Traditional film primaries with Illuminant C (Wide Gamut)",
        "ACES Primaries #0 (Wide Gamut)",
        "ACES Primaries #1 (Wide Gamut)",
    ];

    /// <summary>
    /// The seventeen transfer-characteristic choices, including the typo the QML carries: index 4
    /// is "IPure power gamma 1.8 (SDR)". Reproduced, because the label is what a user reads in one
    /// client and would then not find in the other.
    /// </summary>
    public static IReadOnlyList<string> TransferLabels { get; } =
    [
        "Auto",
        "ITU-R Rec. BT.1886 (SDR)",
        "IEC 61966-2-4 sRGB (SDR)",
        "Linear light content (SDR)",
        "IPure power gamma 1.8 (SDR)",
        "Pure power gamma 2.0 (SDR)",
        "Pure power gamma 2.2 (SDR)",
        "Pure power gamma 2.4 (SDR)",
        "Pure power gamma 2.6 (SDR)",
        "Pure power gamma 2.8 (SDR)",
        "ProPhoto RGB (SDR)",
        "Digital Cinema Distribution Master (SDR)",
        "ITU-R BT.2100 PQ / SMPTE ST2048 (HDR)",
        "ITU-R BT.2100 HLG / ARIB STD-B67 (HDR)",
        "Panasonic V-Log (HDR)",
        "Sony S-Log1 (HDR)",
        "Sony S-Log2 (HDR)",
    ];

    /// <summary>The two long index combos' keys.</summary>
    public const string PrimariesKey = "settings/display_target_prim";

    public const string TransferKey = "settings/display_target_trc";

    /// <summary>A dialog with the Qt defaults.</summary>
    public DisplaySettingsViewModel()
    {
        peakValue = DisplayTarget.Peak.NumericDefault;
        contrastValue = DisplayTarget.Contrast.NumericDefault;
    }

    /// <summary>The dialog as the store holds it.</summary>
    public DisplaySettingsViewModel(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        primaries = preferences.GetInt(PrimariesKey);
        transfer = preferences.GetInt(TransferKey);

        int storedPeak = preferences.GetInt(DisplayTarget.Peak.Key);
        peakIndex = DisplayTarget.Peak.IndexOf(storedPeak);
        peakValue = storedPeak;

        int storedContrast = preferences.GetInt(DisplayTarget.Contrast.Key);
        contrastIndex = DisplayTarget.Contrast.IndexOf(storedContrast);
        contrastValue = storedContrast;
    }

    protected override string ButtonProperty => nameof(PeakSliderVisible);

    public int Primaries
    {
        get => primaries;
        set => Set(ref primaries, value);
    }

    public int Transfer
    {
        get => transfer;
        set => Set(ref transfer, value);
    }

    /// <summary>The peak combo's index. Changing it REWRITES the value, sentinel or default.</summary>
    public int PeakIndex
    {
        get => peakIndex;
        set
        {
            Set(ref peakIndex, value);
            peakValue = DisplayTarget.Peak.StoredForIndex(value);
            Raise(nameof(PeakValue));
            Raise(nameof(PeakStored));
            Raise(nameof(PeakSliderVisible));
        }
    }

    /// <summary>Where the peak slider sits. Only meaningful in the numeric mode.</summary>
    public int PeakValue
    {
        get => peakValue;
        set
        {
            Set(ref peakValue, value);
            Raise(nameof(PeakStored));
        }
    }

    /// <summary>What the store receives: the sentinel in Auto, the slider's value otherwise.</summary>
    public int PeakStored => DisplayTarget.Peak.Modes[PeakIndex] == DisplayTargetMode.Numeric
        ? PeakValue
        : DisplayTarget.Peak.StoredForIndex(PeakIndex);

    public bool PeakSliderVisible => DisplayTarget.Peak.SliderVisibleAt(PeakIndex);

    /// <summary>The contrast combo's index, whose values are not in index order.</summary>
    public int ContrastIndex
    {
        get => contrastIndex;
        set
        {
            Set(ref contrastIndex, value);
            contrastValue = DisplayTarget.Contrast.StoredForIndex(value);
            Raise(nameof(ContrastValue));
            Raise(nameof(ContrastStored));
            Raise(nameof(ContrastSliderVisible));
            Raise(nameof(ContrastComboIsLastInFocusChain));
        }
    }

    public int ContrastValue
    {
        get => contrastValue;
        set
        {
            Set(ref contrastValue, value);
            Raise(nameof(ContrastStored));
        }
    }

    /// <summary>What the store receives, which for Infinity is minus one.</summary>
    public int ContrastStored => DisplayTarget.Contrast.Modes[ContrastIndex] == DisplayTargetMode.Numeric
        ? ContrastValue
        : DisplayTarget.Contrast.StoredForIndex(ContrastIndex);

    public bool ContrastSliderVisible => DisplayTarget.Contrast.SliderVisibleAt(ContrastIndex);

    /// <summary>
    /// Whether the contrast combo is the end of the focus chain, which it is unless the value
    /// slider is showing - `lastInFocusChain: currentIndex != 2` in the QML. Part of the behaviour:
    /// a fixed chain end traps focus on a hidden control in one of the three modes.
    /// </summary>
    public bool ContrastComboIsLastInFocusChain => !ContrastSliderVisible;
}

/// <summary>
/// PP19: the display dialog's rules where the QML states them.
/// </summary>
public static class DisplaySettingsSource
{
    /// <summary>The dialog, or null outside a checkout.</summary>
    public static string? Locate() => DialogSource.Locate("DisplaySettingsDialog");

    /// <summary>Whether a target still derives its mode from the value rather than storing one.</summary>
    public static bool ModeIsDerivedFromTheValue(string qml, DisplayTarget target)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(target);

        string property = PreferenceNames.For(Preferences.Find(target.Key)!)!;
        return qml.Contains($"if(Chiaki.settings.{property} == 0)", StringComparison.Ordinal);
    }

    /// <summary>Whether Infinity is still stored as minus one, and read back as index 1.</summary>
    public static bool InfinityIsMinusOne(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(Chiaki.settings.displayTargetContrast == -1)", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.displayTargetContrast = -1;", StringComparison.Ordinal);
    }

    /// <summary>Whether choosing the numeric mode still writes the default rather than keeping the value.</summary>
    public static bool ChoosingNumericWritesTheDefault(string qml, DisplayTarget target)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(target);

        string property = PreferenceNames.For(Preferences.Find(target.Key)!)!;
        return qml.Contains(
            $"Chiaki.settings.{property} = {target.NumericDefault};", StringComparison.Ordinal);
    }

    /// <summary>Whether the slider bounds are still these, which is what excludes the sentinels.</summary>
    public static bool SliderRangeIs(string qml, DisplayTarget target)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(target);

        return qml.Contains($"from: {target.Minimum}", StringComparison.Ordinal)
            && qml.Contains($"to: {target.Maximum}", StringComparison.Ordinal)
            && qml.Contains($"stepSize: {target.Step}", StringComparison.Ordinal);
    }

    /// <summary>Whether the focus chain's end still moves with the contrast mode.</summary>
    public static bool TheFocusChainEndMovesWithTheMode(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("lastInFocusChain: currentIndex != 2", StringComparison.Ordinal);
    }
}
