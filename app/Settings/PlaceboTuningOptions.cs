using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: which section of the tuning screen a slider belongs to, which is what gates it.
/// </summary>
public enum PlaceboSection
{
    /// <summary>None - the row is always on screen, whatever any switch or preset says.</summary>
    Always,

    Deband,
    Sigmoid,
    ColorAdjustment,
    PeakDetection,
    ColorMapping,
}

/// <summary>
/// PP17: one slider on the renderer tuning screen.
/// </summary>
/// <param name="Key">The key in the placebo store.</param>
/// <param name="Minimum">The floor, which is negative for two of them.</param>
/// <param name="Maximum">The ceiling, which is 6.28 for one and 5.286 for another.</param>
/// <param name="Step">0.1 for most, 0.01 or 0.001 for the fine ones, 1 for the one count.</param>
/// <param name="Default">What the store answers when it holds nothing.</param>
/// <param name="Section">Which switch and preset decide whether it is drawn.</param>
public sealed record PlaceboTuningSlider(
    string Key,
    double Minimum,
    double Maximum,
    double Step,
    double Default,
    PlaceboSection Section)
{
    /// <summary>
    /// Whether this row is on screen: its section's switch AND its section's preset being Custom,
    /// or nothing at all for a row with no section.
    /// </summary>
    public bool VisibleFor(bool sectionEnabled, int presetIndex)
        => Section == PlaceboSection.Always
            || PlaceboSectionPresets.SlidersVisible(sectionEnabled, presetIndex);
}

/// <summary>
/// PP17: the renderer tuning screen's sliders, as a table.
///
/// Sixteen rows across five sections and one that belongs to none. The gates are the presets'
/// (PP169); what this table adds is the RANGES, and four of them are numbers a port would not
/// invent:
///
/// 1. HUE IS IN RADIANS, and its ceiling is 6.28 - a truncated 2π rather than a full turn. A port
///    offering degrees, or 2π to more places, disagrees with the Qt client at both ends of the
///    slider and everywhere in between once the value is stored.
///
/// 2. TEMPERATURE RUNS -1.143 TO 5.286 in steps of a thousandth. Those are libplacebo's own
///    bounds and they are not round in any unit; rounding them to -1..5 quietly removes the top
///    of the range.
///
/// 3. BRIGHTNESS IS THE ONLY OTHER SLIDER THAT GOES NEGATIVE. Every other row on the screen
///    starts at zero or one, so a table built from a shared "0 to max" shape loses half of one
///    control and nothing else.
///
/// 4. ANTIRINGING BELONGS TO NO SECTION. It sits among rows that are all gated and is not, which
///    is the same trap the colour-mapping screen has six of (PP168).
/// </summary>
public static class PlaceboTuningOptions
{
    /// <summary>The sixteen, in the order the screen draws them.</summary>
    public static IReadOnlyList<PlaceboTuningSlider> All { get; } =
    [
        // No section: on screen whatever else is chosen.
        new(PlaceboStore.Key("antiringing_strength"), 0.00, 1.00, 0.01, 0.0, PlaceboSection.Always),

        new(PlaceboStore.Key("deband_iterations"), 0, 16, 1, 1, PlaceboSection.Deband),
        new(PlaceboStore.Key("deband_threshold"), 0.0, 1000.0, 0.1, 3.0, PlaceboSection.Deband),
        new(PlaceboStore.Key("deband_radius"), 0.0, 1000.0, 0.1, 16.0, PlaceboSection.Deband),
        new(PlaceboStore.Key("deband_grain"), 0.0, 1000.0, 0.1, 4.0, PlaceboSection.Deband),

        new(PlaceboStore.Key("sigmoid_center"), 0.00, 1.00, 0.01, 0.75, PlaceboSection.Sigmoid),
        new(PlaceboStore.Key("sigmoid_slope"), 1.0, 20.0, 0.1, 6.5, PlaceboSection.Sigmoid),

        // The only two rows on the screen that start below zero.
        new(PlaceboStore.Key("brightness"), -1.00, 1.00, 0.01, 0.0, PlaceboSection.ColorAdjustment),
        new(PlaceboStore.Key("contrast"), 0.0, 100.0, 0.1, 1.0, PlaceboSection.ColorAdjustment),
        new(PlaceboStore.Key("saturation"), 0.0, 100.0, 0.1, 1.0, PlaceboSection.ColorAdjustment),

        // Radians, and a ceiling that is 2π cut short rather than 2π.
        new(PlaceboStore.Key("hue"), 0.00, 6.28, 0.01, 0.0, PlaceboSection.ColorAdjustment),

        new(PlaceboStore.Key("gamma"), 0.0, 100.0, 0.1, 1.0, PlaceboSection.ColorAdjustment),
        new(PlaceboStore.Key("temperature"), -1.143, 5.286, 0.001, 0.0, PlaceboSection.ColorAdjustment),

        new(PlaceboStore.Key("peak_smoothing_period"), 0.0, 1000.0, 0.1, 20.0, PlaceboSection.PeakDetection),
        new(PlaceboStore.Key("scene_threshold_low"), 0.0, 100.0, 0.1, 1.0, PlaceboSection.PeakDetection),
        new(PlaceboStore.Key("scene_threshold_high"), 0.0, 100.0, 0.1, 3.0, PlaceboSection.PeakDetection),
        new(PlaceboStore.Key("peak_percentile"), 0.0, 100.0, 0.001, 100.0, PlaceboSection.PeakDetection),
        new(PlaceboStore.Key("black_cutoff"), 0.0, 100.0, 0.1, 1.0, PlaceboSection.PeakDetection),
    ];

    /// <summary>A full turn in radians, for the one slider that is measured in them.</summary>
    public const double FullTurnRadians = 2 * Math.PI;

    /// <summary>The hue slider's ceiling, which is that number cut short at two decimals.</summary>
    public const double HueMaximum = 6.28;
}

/// <summary>
/// PP17: the tuning screen's ranges where the Qt client states them.
/// </summary>
public static class PlaceboTuningSource
{
    /// <summary>The screen.</summary>
    public const string DialogQml = @"gui\src\qml\PlaceboSettingsDialog.qml";

    /// <summary>The store, for the defaults.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether hue is still a truncated turn rather than a round number of anything.</summary>
    public static bool HueStillEndsAtSixPointTwoEight(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("to: 6.28", StringComparison.Ordinal);
    }

    /// <summary>Whether temperature still carries libplacebo's own unround bounds.</summary>
    public static bool TemperatureStillCarriesItsOwnBounds(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("from: -1.143", StringComparison.Ordinal)
            && qml.Contains("to: 5.286", StringComparison.Ordinal);
    }

    /// <summary>Whether antiringing is still the row on this screen with no condition.</summary>
    public static bool AntiringingStillHasNoCondition(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int slider = qml.IndexOf("value: Chiaki.settings.placeboAntiringingStrength", StringComparison.Ordinal);
        if (slider < 0)
            return false;

        // The twenty lines around it, which is the whole of that control in this file's layout.
        int from = Math.Max(0, slider - 600);
        int to = Math.Min(qml.Length, slider + 600);

        return !qml[from..to].Contains("placeboAntiringingPreset", StringComparison.Ordinal);
    }

    /// <summary>Whether the two negative floors are still the only two.</summary>
    public static bool OnlyTwoRowsStillGoNegative(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int negatives = 0;
        int at = 0;
        while ((at = qml.IndexOf("from: -", at, StringComparison.Ordinal)) >= 0)
        {
            negatives++;
            at += "from: -".Length;
        }

        return negatives == 2;
    }
}
