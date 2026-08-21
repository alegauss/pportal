using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: the placebo options live in their OWN store, under their own prefix.
///
/// Every other preference this port reads is <c>settings/…</c> out of the Settings object. These
/// are <c>placebo_settings/…</c> out of a second QSettings that settings.cpp keeps beside it. So a
/// port that put a renderer option in the main store writes a key the Qt client never reads, and
/// the screen shows a default that nothing on it can change.
/// </summary>
public static class PlaceboStore
{
    /// <summary>The prefix, which is also the group name inside the file.</summary>
    public const string Prefix = "placebo_settings/";

    /// <summary>One option's key.</summary>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Prefix + name;
    }
}

/// <summary>
/// PP17: the colour-mapping screen's three combos, and the four labels that are not their words.
///
/// All three store a WORD out of a QMap keyed by the C++ enum, read back through
/// <c>QMap::key(v, default)</c> - so an unknown word is the default rather than an error, and a
/// wrong word is a setting that silently does not stick.
///
/// The words are lower case and the labels are not, which invites a port to derive one from the
/// other. Four of the twenty-seven pairings defeat that, in three different ways:
///
///   "Soft Clip"  → "softclip"     a space to delete
///   "Linear Light" → "linearlight" the same again
///   "HDR10 Plus" → "hdr10plus"    and again
///   "St-2094-10" → "st2094-10"    a hyphen to delete - and the entry BESIDE it is "St2094-40"
///                                 against "st2094-40", where the same hyphen must be kept
///
/// That last pair is the one worth staring at. Two adjacent rows of one combo are spelled
/// differently from each other, so no single rule turns these labels into these words. They are
/// two lists.
/// </summary>
public static class PlaceboColorMapping
{
    /// <summary>The gamut mapping function. Ten choices, one of which needs a space removed.</summary>
    public static StoredChoice GamutMapping { get; } = new(
        PlaceboStore.Key("gamut_mapping"),
        new[]
        {
            "Clip", "Perceptual", "Soft Clip", "Relative", "Saturation",
            "Absolute", "Desaturate", "Darken", "Highlight", "Linear",
        },
        new[]
        {
            "clip", "perceptual", "softclip", "relative", "saturation",
            "absolute", "desaturate", "darken", "highlight", "linear",
        },
        defaultIndex: 1);

    /// <summary>
    /// The tone mapping function. Twelve choices, and the two ST 2094 rows disagree with each
    /// other about where a hyphen goes on screen while agreeing in the store.
    /// </summary>
    public static StoredChoice ToneMapping { get; } = new(
        PlaceboStore.Key("tone_mapping"),
        new[]
        {
            "Clip", "Spline", "St2094-40", "St-2094-10", "Bt2390", "Bt2446a",
            "Reinhard", "Mobius", "Hable", "Gamma", "Linear", "Linear Light",
        },
        new[]
        {
            "clip", "spline", "st2094-40", "st2094-10", "bt2390", "bt2446a",
            "reinhard", "mobius", "hable", "gamma", "linear", "linearlight",
        },
        defaultIndex: 1);

    /// <summary>Which HDR metadata the tone mapping is allowed to use.</summary>
    public static StoredChoice ToneMetadata { get; } = new(
        PlaceboStore.Key("tone_map_metadata"),
        new[] { "Any", "None", "HDR10", "HDR10 Plus", "Cie_y" },
        new[] { "any", "none", "hdr10", "hdr10plus", "cie_y" },
        defaultIndex: 0);

    /// <summary>
    /// Whether lower-casing a label would produce its stored word. False for exactly four of the
    /// twenty-seven - which is the whole reason the two lists above are written out.
    /// </summary>
    public static bool LabelWouldDeriveItsWord(string label, string stored)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(stored);

        return string.Equals(label.ToLowerInvariant(), stored, StringComparison.Ordinal);
    }
}

/// <summary>
/// PP17: the inverse tone-mapping switch, which is a checkbox stored as a WORD.
///
/// It is the only boolean on this screen that is not a boolean in the file: settings.cpp writes
/// "yes" or "no" and compares the string. A port storing true/false writes something
/// <c>value == "yes"</c> reads as off, so the switch appears to have no effect and resets itself.
/// </summary>
public static class InverseToneMapping
{
    public const string Key = PlaceboStore.Prefix + "inverse_tone_mapping";

    /// <summary>What the file holds for on. One spelling, shared with the other ten switches.</summary>
    public const string On = PlaceboFlags.On;

    /// <summary>And for off, which is also the default.</summary>
    public const string Off = PlaceboFlags.Off;

    /// <summary>The word for a state.</summary>
    public static string Store(bool enabled) => PlaceboFlags.Store(enabled);

    /// <summary>
    /// And the state for a word. Anything that is not "yes" is off, which is settings.cpp's own
    /// comparison rather than a parse - so a file holding "true" reads as off.
    /// </summary>
    public static bool Read(string? word) => PlaceboFlags.Read(word);
}

/// <summary>
/// PP17: the colour-mapping screen's rules where the Qt client states them.
/// </summary>
public static class PlaceboColorMappingSource
{
    /// <summary>The screen.</summary>
    public const string DialogQml = @"gui\src\qml\PlaceboColorMappingDialog.qml";

    /// <summary>Where the words and the defaults live.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether these options still come out of the second store.</summary>
    public static bool TheOptionsComeFromTheSecondStore(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                $"placebo_settings.value(\"{PlaceboColorMapping.GamutMapping.Key}\"",
                StringComparison.Ordinal)
            && cpp.Contains(
                $"placebo_settings.setValue(\"{PlaceboColorMapping.ToneMapping.Key}\"",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the two ST 2094 labels are still spelled differently from each other.</summary>
    public static bool TheTwoSt2094LabelsStillDisagree(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("qsTr(\"St2094-40\"), qsTr(\"St-2094-10\")", StringComparison.Ordinal);
    }

    /// <summary>And whether the store still spells them the same way as each other.</summary>
    public static bool TheTwoSt2094WordsStillAgree(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("St209440, \"st2094-40\"", StringComparison.Ordinal)
            && cpp.Contains("St209410, \"st2094-10\"", StringComparison.Ordinal);
    }

    /// <summary>Whether the three combos still fall back to a default rather than erroring.</summary>
    public static bool AnUnknownWordStillFallsBack(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                "return placebo_gamut_mapping_function_values.key(v, placebo_gamut_mapping_function_default);",
                StringComparison.Ordinal)
            && cpp.Contains(
                "return placebo_tone_mapping_function_values.key(v, placebo_tone_mapping_function_default);",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether Linear Knee is still shown for the two functions it is not named after.
    /// </summary>
    public static bool LinearKneeIsStillShownForMobiusAndGamma(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "visible: Chiaki.settings.placeboToneMappingFunction == "
            + "PlaceboColorMappingDialog.ToneMappingFunction.Mobius "
            + "|| Chiaki.settings.placeboToneMappingFunction == "
            + "PlaceboColorMappingDialog.ToneMappingFunction.Gamma",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the two knee bounds still meet at a half rather than overlapping.</summary>
    public static bool TheTwoKneeBoundsStillMeetAtAHalf(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("to: 0.50", StringComparison.Ordinal)
            && qml.Contains("from: 0.50", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the three LUT keys are still cased inconsistently. The claim is the inconsistency
    /// itself, so all three are pinned - a store that tidied them would be a change to look at.
    /// </summary>
    public static bool TheThreeLutKeysAreStillCasedThisWay(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains($"\"{Lut3dKeys.SizeI}\"", StringComparison.Ordinal)
            && cpp.Contains($"\"{Lut3dKeys.SizeC}\"", StringComparison.Ordinal)
            && cpp.Contains($"\"{Lut3dKeys.SizeH}\"", StringComparison.Ordinal);
    }

    /// <summary>Whether the inverse switch is still a word rather than a bool.</summary>
    public static bool TheInverseSwitchIsStillAWord(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                $"placebo_settings.value(\"{InverseToneMapping.Key}\", \"{InverseToneMapping.Off}\")",
                StringComparison.Ordinal)
            && cpp.Contains(
                $"placebo_settings.setValue(\"{InverseToneMapping.Key}\", \"{InverseToneMapping.On}\");",
                StringComparison.Ordinal);
    }
}
