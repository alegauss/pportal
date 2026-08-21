using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: the number beside each slider, whose decimals are the STEP's decimals - on all forty rows.
///
/// The two screens print a slider's value four different ways: <c>toFixed(2)</c> nineteen times,
/// <c>toFixed(1)</c> fourteen, the bare value five, and <c>toFixed(3)</c> twice. Those counts are
/// exactly the counts of the four step sizes - 0.01, 0.1, 1 and 0.001 - and the pairing holds for
/// every row without exception.
///
/// So the format is derivable and forty more transcribed strings are not needed. That is worth
/// saying out loud because the rest of these screens is the opposite: nothing else here follows a
/// rule, and this is the one place where checking for one paid.
///
/// <see cref="PlaceboLabelTests"/> asserts the counts rather than the rule alone, so a future row
/// whose format disagrees with its step turns the check red instead of being quietly formatted the
/// wrong way.
/// </summary>
public static class PlaceboCaption
{
    /// <summary>How many decimals a step implies: 0.001 gives three, 1 gives none.</summary>
    public static int DecimalsFor(double step)
    {
        if (step >= 1)
            return 0;
        if (step >= 0.1)
            return 1;
        return step >= 0.01 ? 2 : 3;
    }

    /// <summary>The number as the slider prints it.</summary>
    public static string For(double value, double step)
        => value.ToString(
            "F" + DecimalsFor(step).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
}

/// <summary>
/// PP17: the forty labels only the QML states, transcribed.
///
/// There is no finding in this list and that is the point of collecting it in one place: it is the
/// half of these two screens that cannot be derived from the store, the enums or the ranges, so it
/// is the half a port has to copy. Two of them are worth a glance anyway:
///
///   "LUT 3D Size h" carries the same lower-case h the KEY does (PP168), so the inconsistency is
///   on screen as well as in the file - it was not a typo confined to one place.
///
///   "Anti-ringing Strength" is hyphenated where its key, <c>antiringing_strength</c>, is not.
/// </summary>
public static class PlaceboLabels
{
    private static readonly Dictionary<string, string> Rows = new(StringComparer.Ordinal)
    {
        // The colour mapping screen, in the order it draws them.
        [PlaceboStore.Key("perceptual_deadzone")] = "Perceptual Deadzone:",
        [PlaceboStore.Key("perceptual_strength")] = "Perceptual Strength:",
        [PlaceboStore.Key("colorimetric_gamma")] = "Colorimetric Gamma:",
        [PlaceboStore.Key("softclip_knee")] = "Soft Clip Knee:",
        [PlaceboStore.Key("softclip_desat")] = "Soft Clip Desaturation:",
        [Lut3dKeys.SizeI] = "LUT 3D Size I:",
        [Lut3dKeys.SizeC] = "LUT 3D Size C:",
        [Lut3dKeys.SizeH] = "LUT 3D Size h:",
        [PlaceboStore.Key("knee_adaptation")] = "Knee Adaptation:",
        [PlaceboStore.Key("knee_minimum")] = "Knee Minimum:",
        [PlaceboStore.Key("knee_maximum")] = "Knee Maximum:",
        [PlaceboStore.Key("knee_default")] = "Knee Default:",
        [PlaceboStore.Key("knee_offset")] = "Knee Offset:",
        [PlaceboStore.Key("slope_tuning")] = "Slope Tuning:",
        [PlaceboStore.Key("slope_offset")] = "Slope Offset:",
        [PlaceboStore.Key("spline_contrast")] = "Spline Contrast:",
        [PlaceboStore.Key("reinhard_contrast")] = "Reinhard Contrast:",
        [PlaceboStore.Key("linear_knee")] = "Linear Knee:",
        [PlaceboStore.Key("exposure")] = "Exposure:",
        [PlaceboStore.Key("tone_lut_size")] = "Tone LUT Size:",
        [PlaceboStore.Key("contrast_recovery")] = "Contrast Recovery:",
        [PlaceboStore.Key("contrast_smoothness")] = "Contrast Smoothness:",

        // And the tuning screen.
        [PlaceboStore.Key("antiringing_strength")] = "Anti-ringing Strength:",
        [PlaceboStore.Key("deband_iterations")] = "Deband Iterations:",
        [PlaceboStore.Key("deband_threshold")] = "Deband Threshold:",
        [PlaceboStore.Key("deband_radius")] = "Deband Radius:",
        [PlaceboStore.Key("deband_grain")] = "Deband Grain:",
        [PlaceboStore.Key("sigmoid_center")] = "Sigmoid Center:",
        [PlaceboStore.Key("sigmoid_slope")] = "Sigmoid Slope:",
        [PlaceboStore.Key("brightness")] = "Brightness:",
        [PlaceboStore.Key("contrast")] = "Contrast:",
        [PlaceboStore.Key("saturation")] = "Saturation:",
        [PlaceboStore.Key("hue")] = "Hue:",
        [PlaceboStore.Key("gamma")] = "Gamma:",
        [PlaceboStore.Key("temperature")] = "Temperature:",
        [PlaceboStore.Key("peak_smoothing_period")] = "Peak Smoothing Period:",
        [PlaceboStore.Key("scene_threshold_low")] = "Scene Threshold Low:",
        [PlaceboStore.Key("scene_threshold_high")] = "Scene Threshold High:",
        [PlaceboStore.Key("peak_percentile")] = "Peak Percentile:",
        [PlaceboStore.Key("black_cutoff")] = "Black Cutoff:",
    };

    /// <summary>The combos and switches, which are not sliders and are labelled all the same.</summary>
    private static readonly Dictionary<string, string> Others = new(StringComparer.Ordinal)
    {
        [PlaceboColorMapping.GamutMapping.Key] = "Gamut Mapping Function:",
        [PlaceboColorMapping.ToneMapping.Key] = "Tone-mapping Function:",
        [PlaceboColorMapping.ToneMetadata.Key] = "Tone-mapping Metadata:",
        [InverseToneMapping.Key] = "Inverse Tone-mapping Enabled:",
        [PlaceboStore.Key("lut3d_tricubic")] = "LUT 3D Tricubic Enabled:",
        [PlaceboStore.Key("gamut_expansion")] = "Gamut Expansion Enabled:",

        [PlaceboScalers.Upscaler.Key] = "Upscaler:",
        [PlaceboScalers.PlaneUpscaler.Key] = "Plane Upscaler:",
        [PlaceboScalers.Downscaler.Key] = "Downscaler:",
        [PlaceboScalers.PlaneDownscaler.Key] = "Plane Downscaler:",

        [PlaceboStore.Key("deinterlace")] = "Enable Deinterlace:",
        [PlaceboSectionPresets.Deinterlace.Key] = "Preset:",
        [PlaceboDeinterlaceChoice.Algorithm.Key] = "Algorithm:",
        [PlaceboStore.Key("deinterlace_skip_spatial")] = "Skip spatial check:",

        [PlaceboStore.Key("deband")] = "Deband Enabled:",
        [PlaceboSectionPresets.Deband.Key] = "Deband Preset:",
        [PlaceboStore.Key("sigmoid")] = "Sigmoidization Enabled:",
        [PlaceboSectionPresets.Sigmoid.Key] = "Sigmoid Preset:",
        [PlaceboStore.Key("color_adjustment")] = "Color Adjustment Enabled:",
        [PlaceboSectionPresets.ColorAdjustment.Key] = "Color Adjustment Preset:",
        [PlaceboStore.Key("peak_detect")] = "HDR Peak Detection Enabled:",
        [PlaceboSectionPresets.PeakDetection.Key] = "Peak Detection Preset:",
        [PlaceboStore.Key("allow_delayed_peak")] = "Allow Delayed Peak:",
        [PlaceboStore.Key("color_map")] = "Color Mapping Enabled:",
        [PlaceboSectionPresets.ColorMapping.Key] = "Color Mapping Preset:",
    };

    /// <summary>Every slider's label, keyed the way the store keys it.</summary>
    public static IReadOnlyDictionary<string, string> Sliders => Rows;

    /// <summary>Every combo's and switch's.</summary>
    public static IReadOnlyDictionary<string, string> Controls => Others;

    /// <summary>The label for a key, or the key itself where none was transcribed.</summary>
    public static string For(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Rows.TryGetValue(key, out string? row))
            return row;

        return Others.TryGetValue(key, out string? other) ? other : key;
    }
}

/// <summary>
/// PP17: the labels and captions where the Qt client states them.
/// </summary>
public static class PlaceboLabelSource
{
    /// <summary>The colour mapping screen.</summary>
    public const string ColorMappingQml = @"gui\src\qml\PlaceboColorMappingDialog.qml";

    /// <summary>The renderer tuning screen.</summary>
    public const string TuningQml = @"gui\src\qml\PlaceboSettingsDialog.qml";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>How many times a screen prints a value with a given number of decimals.</summary>
    public static int CaptionsWithDecimals(string qml, int decimals)
    {
        ArgumentNullException.ThrowIfNull(qml);

        string needle = decimals == 0
            ? "text: qsTr(\"%1\").arg(parent.value)"
            : $"text: qsTr(parent.value.toFixed({decimals}))";

        int count = 0;
        int at = 0;
        while ((at = qml.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    /// <summary>Whether a label is still on the screen it was transcribed from.</summary>
    public static bool HasLabel(string qml, string label)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(label);
        return qml.Contains($"text: qsTr(\"{label}\")", StringComparison.Ordinal);
    }
}
