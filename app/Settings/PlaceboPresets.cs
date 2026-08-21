using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: the renderer preset the stream menu switches, which is the one placebo setting that is
/// NOT in the placebo store.
///
/// <c>settings/placebo_preset</c> is read out of the main Settings object, unlike every other
/// option on these two screens. It is also stored as a WORD - "high_quality_advanced_spatial" and
/// the rest - where PP10's menu writes an enum value into the property, so the number the menu
/// deals in and the string the file holds are two different representations of one choice.
///
/// And the store's default is HIGH QUALITY, which is neither the first entry nor the one called
/// Default. A port taking the enum's second value as "the default preset" - the reasonable guess,
/// since it is literally named Default - starts a fresh install one step below where the Qt client
/// starts it, and nothing on screen says so.
/// </summary>
public static class PlaceboPresetChoice
{
    /// <summary>The key, in the MAIN store.</summary>
    public const string Key = "settings/placebo_preset";

    /// <summary>
    /// The six, in the order both enums declare them - <c>PlaceboPreset</c> in settings.h and
    /// <c>VideoPreset</c> in qmlmainwindow.h, which are two enums with one order.
    /// </summary>
    public static StoredChoice Preset { get; } = new(
        Key,
        new[] { "Fast", "Default", "High Quality", "HQ + Spatial", "HQ + Adv Spatial", "Custom" },
        new[]
        {
            "fast", "default", "high_quality",
            "high_quality_spatial", "high_quality_advanced_spatial", "custom",
        },
        // HighQuality, which is index two - not the entry named Default, and not the first.
        defaultIndex: 2);

    /// <summary>What the store holds for one of PP10's menu values.</summary>
    public static string StoredFor(StreamVideoPreset preset) => Preset.StoredFor((int)preset);

    /// <summary>And the menu value for a stored word, falling back the way the Qt client does.</summary>
    public static StreamVideoPreset From(string? word)
        => (StreamVideoPreset)Preset.IndexOf(word ?? "");
}

/// <summary>
/// PP17: the six per-section presets on the renderer tuning screen, and the empty string they call
/// Custom.
///
/// Each section of that screen - deband, sigmoid, colour adjustment, peak detection, colour
/// mapping - has a preset combo above its sliders, and the sliders are shown only when the preset
/// is the FIRST entry. That entry is labelled "Custom", is named <c>None</c> in the C++ enum, and
/// is stored as the EMPTY STRING. Three names for one choice, and the store's is the one with no
/// characters in it - so a port writing "custom" produces a word <c>QMap::key</c> does not know
/// and the section falls back to Custom anyway, by accident, until the day the list grows a real
/// "custom".
///
/// The six lists are not uniform. Colour adjustment's second entry is "neutral" rather than
/// "default"; peak detection and colour mapping have a third; and DEINTERLACE HAS NO CUSTOM AT
/// ALL - one entry, "default", so that combo can never hide anything. A port that generated six
/// identical two-entry combos would be wrong about four of them.
/// </summary>
public static class PlaceboSectionPresets
{
    /// <summary>The label the empty string is shown as.</summary>
    public const string CustomLabel = "Custom";

    /// <summary>What the store holds for it.</summary>
    public const string CustomStored = "";

    /// <summary>The index it sits at, which is what the sliders' condition tests.</summary>
    public const int CustomIndex = 0;

    /// <summary>Deband: Custom or Default.</summary>
    public static StoredChoice Deband { get; } = new(
        PlaceboStore.Key("deband_preset"),
        new[] { CustomLabel, "Default" },
        new[] { CustomStored, "default" },
        defaultIndex: CustomIndex);

    /// <summary>Sigmoid: the same two.</summary>
    public static StoredChoice Sigmoid { get; } = new(
        PlaceboStore.Key("sigmoid_preset"),
        new[] { CustomLabel, "Default" },
        new[] { CustomStored, "default" },
        defaultIndex: CustomIndex);

    /// <summary>Colour adjustment, whose second entry is Neutral rather than Default.</summary>
    public static StoredChoice ColorAdjustment { get; } = new(
        PlaceboStore.Key("color_adjustment_preset"),
        new[] { CustomLabel, "Neutral" },
        new[] { CustomStored, "neutral" },
        defaultIndex: CustomIndex);

    /// <summary>Peak detection, which has three.</summary>
    public static StoredChoice PeakDetection { get; } = new(
        PlaceboStore.Key("peak_detect_preset"),
        new[] { CustomLabel, "Default", "High Quality" },
        new[] { CustomStored, "default", "high_quality" },
        defaultIndex: CustomIndex);

    /// <summary>Colour mapping, which has the same three.</summary>
    public static StoredChoice ColorMapping { get; } = new(
        PlaceboStore.Key("color_map_preset"),
        new[] { CustomLabel, "Default", "High Quality" },
        new[] { CustomStored, "default", "high_quality" },
        defaultIndex: CustomIndex);

    /// <summary>
    /// Deinterlace, the odd one: ONE entry and no Custom. Its combo cannot hide a slider, because
    /// there is no index for the sliders' condition to be false at.
    /// </summary>
    public static StoredChoice Deinterlace { get; } = new(
        PlaceboStore.Key("deinterlace_preset"),
        new[] { "Default" },
        new[] { "default" },
        defaultIndex: 0);

    /// <summary>The five that have a Custom, which is every section except deinterlace.</summary>
    public static IReadOnlyList<StoredChoice> WithCustom { get; } =
        [Deband, Sigmoid, ColorAdjustment, PeakDetection, ColorMapping];

    /// <summary>
    /// Whether a section's sliders are on screen: the section's own switch AND the preset being
    /// Custom. Two conditions, repeated on every row of the section in the QML - which is what
    /// makes it worth stating once.
    /// </summary>
    public static bool SlidersVisible(bool sectionEnabled, int presetIndex)
        => sectionEnabled && presetIndex == CustomIndex;
}

/// <summary>
/// PP17: the presets' rules where the Qt client states them.
/// </summary>
public static class PlaceboPresetSource
{
    /// <summary>The renderer tuning screen.</summary>
    public const string DialogQml = @"gui\src\qml\PlaceboSettingsDialog.qml";

    /// <summary>Where the words and the defaults live.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether the renderer preset still comes out of the main store.</summary>
    public static bool ThePresetIsInTheMainStore(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains($"settings.value(\"{PlaceboPresetChoice.Key}\"", StringComparison.Ordinal)
            && !cpp.Contains(
                $"placebo_settings.value(\"{PlaceboPresetChoice.Key}\"", StringComparison.Ordinal);
    }

    /// <summary>And whether its default is still High Quality rather than the one named Default.</summary>
    public static bool TheDefaultPresetIsHighQuality(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            "static const PlaceboPreset placebo_preset_default = PlaceboPreset::HighQuality;",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the five sections still store Custom as an empty string.</summary>
    public static bool CustomIsStillAnEmptyString(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("{ PlaceboDebandPreset::None, \"\" }", StringComparison.Ordinal)
            && cpp.Contains("{ PlaceboSigmoidPreset::None, \"\" }", StringComparison.Ordinal)
            && cpp.Contains("{ PlaceboColorAdjustmentPreset::None, \"\" }", StringComparison.Ordinal);
    }

    /// <summary>Whether the deinterlace preset still has exactly one entry.</summary>
    public static bool DeinterlaceStillHasNoCustom(string cpp, string qml)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(qml);

        return cpp.Contains("{ PlaceboDeinterlacePreset::Default, \"default\" },", StringComparison.Ordinal)
            && !cpp.Contains("PlaceboDeinterlacePreset::None", StringComparison.Ordinal)
            && qml.Contains("model: [qsTr(\"Default\")]", StringComparison.Ordinal);
    }

    /// <summary>Whether a section's sliders still need the switch AND the preset together.</summary>
    public static bool TheSlidersNeedBothConditions(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "visible: Chiaki.settings.placeboDebandEnabled && (Chiaki.settings.placeboDebandPreset == 0)",
            StringComparison.Ordinal);
    }
}
