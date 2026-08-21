using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: the four scaler combos, which are two lists and four different defaults.
///
/// Upscaler and plane upscaler share one list of fourteen; downscaler and plane downscaler share
/// one of nine. What they do not share is where they start:
///
///   upscaler        EwaLanczosSharp, the tenth entry
///   plane upscaler  None, the first
///   downscaler      Hermite, the third
///   plane downscaler None, the first
///
/// So a port giving four combos over two lists one default each - the obvious shape - gets two of
/// the four wrong, and gets them wrong in the direction of "no scaler", which looks like the
/// setting simply not being applied.
///
/// The lists carry two traps of their own:
///
/// 1. THE FIRST ENTRY IS "Custom" ON SCREEN, <c>None</c> IN THE ENUM AND "none" IN THE STORE.
///    That is a third encoding of the same word: the section presets (PP169) call their first
///    entry Custom too and store it as the EMPTY STRING. Two screens, one label, two words.
///
/// 2. ONE LABEL LOSES A DIGIT. The eleventh upscaler is <c>EwaLanczos4Sharpest</c> in the enum and
///    "ewa_lanczos4sharpest" in the store, and "EwaLanczosSharpest" on screen - the 4 is in both
///    of the places a port would read from and in neither of the places a user looks.
/// </summary>
public static class PlaceboScalers
{
    /// <summary>The label every scaler list starts with, whatever its word.</summary>
    public const string CustomLabel = "Custom";

    /// <summary>And the word these four store for it, which is not the presets' empty string.</summary>
    public const string NoneStored = "none";

    private static readonly string[] UpscalerLabels =
    [
        CustomLabel, "Nearest", "Bilinear", "Oversample", "Bicubic", "Gaussian", "Catmull Rom",
        "Lanczos", "EwaLanczos", "EwaLanczosSharp", "EwaLanczosSharpest", "FSR",
        "FSRCNNX x2 8-0-4-1", "FSRCNNX x2 16-0-4-1",
    ];

    private static readonly string[] UpscalerWords =
    [
        NoneStored, "nearest", "bilinear", "oversample", "bicubic", "gaussian", "catmull_rom",
        "lanczos", "ewa_lanczos", "ewa_lanczossharp", "ewa_lanczos4sharpest", "fsr",
        "fsrcnnx_x2_8_0_4_1", "fsrcnnx_x2_16_0_4_1",
    ];

    private static readonly string[] DownscalerLabels =
    [
        CustomLabel, "Box", "Hermite", "Bilinear", "Bicubic", "Gaussian", "Catmull Rom",
        "Mitchell", "Lanczos",
    ];

    private static readonly string[] DownscalerWords =
    [
        NoneStored, "box", "hermite", "bilinear", "bicubic", "gaussian", "catmull_rom",
        "mitchell", "lanczos",
    ];

    /// <summary>The frame upscaler. Starts on EwaLanczosSharp, the tenth entry.</summary>
    public static StoredChoice Upscaler { get; } = new(
        PlaceboStore.Key("upscaler"), UpscalerLabels, UpscalerWords, defaultIndex: 9);

    /// <summary>The plane upscaler, over the same list and starting on the first entry.</summary>
    public static StoredChoice PlaneUpscaler { get; } = new(
        PlaceboStore.Key("plane_upscaler"), UpscalerLabels, UpscalerWords, defaultIndex: 0);

    /// <summary>The frame downscaler. Starts on Hermite, the third.</summary>
    public static StoredChoice Downscaler { get; } = new(
        PlaceboStore.Key("downscaler"), DownscalerLabels, DownscalerWords, defaultIndex: 2);

    /// <summary>The plane downscaler, over the same list and starting on the first.</summary>
    public static StoredChoice PlaneDownscaler { get; } = new(
        PlaceboStore.Key("plane_downscaler"), DownscalerLabels, DownscalerWords, defaultIndex: 0);

    /// <summary>All four, for the checks that are about the set rather than about one of them.</summary>
    public static IReadOnlyList<StoredChoice> All { get; } =
        [Upscaler, PlaneUpscaler, Downscaler, PlaneDownscaler];
}

/// <summary>
/// PP17: the eleven switches on these two screens, every one of them a WORD in the store.
///
/// settings.cpp writes "yes" or "no" and compares the string, so none of them is a bool in the
/// file. Four default on and seven default off, and the split does not follow anything a port
/// could guess - deband and sigmoid are on, deinterlace and gamut expansion are off.
///
/// The comparison is <c>== "yes"</c> rather than a parse, so a file holding "true" or "1" reads as
/// off. That is not leniency worth adding: two clients share the file, and a value only one of
/// them understands is a switch that appears to work until the other one opens it.
/// </summary>
public static class PlaceboFlags
{
    /// <summary>What the store holds for on.</summary>
    public const string On = "yes";

    /// <summary>And for off.</summary>
    public const string Off = "no";

    /// <summary>The word for a state.</summary>
    public static string Store(bool enabled) => enabled ? On : Off;

    /// <summary>The state for a word. Anything that is not exactly "yes" is off.</summary>
    public static bool Read(string? word) => string.Equals(word, On, StringComparison.Ordinal);

    /// <summary>Every switch, with the state it takes when the store holds nothing.</summary>
    public static IReadOnlyDictionary<string, bool> Defaults { get; } =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            // On.
            [PlaceboStore.Key("deband")] = true,
            [PlaceboStore.Key("sigmoid")] = true,
            [PlaceboStore.Key("peak_detect")] = true,
            [PlaceboStore.Key("color_adjustment")] = true,
            [PlaceboStore.Key("color_map")] = true,

            // Off.
            [PlaceboStore.Key("deinterlace")] = false,
            [PlaceboStore.Key("deinterlace_skip_spatial")] = false,
            [PlaceboStore.Key("gamut_expansion")] = false,
            [PlaceboStore.Key("lut3d_tricubic")] = false,
            [PlaceboStore.Key("allow_delayed_peak")] = false,
            [InverseToneMapping.Key] = false,
        };
}

/// <summary>
/// PP17: the scalers' and switches' rules where the Qt client states them.
/// </summary>
public static class PlaceboScalerSource
{
    /// <summary>The store, which is where all of this is decided.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>The tuning screen, for the labels.</summary>
    public const string DialogQml = @"gui\src\qml\PlaceboSettingsDialog.qml";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether the four combos still take four different defaults.</summary>
    public static bool TheFourScalersStillDefaultDifferently(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                "placebo_upscaler_default = PlaceboUpscaler::EwaLanczosSharp;", StringComparison.Ordinal)
            && cpp.Contains(
                "placebo_plane_upscaler_default = PlaceboUpscaler::None;", StringComparison.Ordinal)
            && cpp.Contains(
                "placebo_downscaler_default = PlaceboDownscaler::Hermite;", StringComparison.Ordinal)
            && cpp.Contains(
                "placebo_plane_downscaler_default = PlaceboDownscaler::None;", StringComparison.Ordinal);
    }

    /// <summary>Whether the scalers' first entry is still "none" rather than an empty string.</summary>
    public static bool TheScalersFirstWordIsStillNone(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains($"{{ PlaceboUpscaler::None, \"{PlaceboScalers.NoneStored}\" }}", StringComparison.Ordinal)
            && cpp.Contains($"{{ PlaceboDownscaler::None, \"{PlaceboScalers.NoneStored}\" }}", StringComparison.Ordinal);
    }

    /// <summary>Whether the fourth-from-last upscaler still loses its digit on screen.</summary>
    public static bool TheSharpestLabelStillLosesItsFour(string cpp, string qml)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(qml);

        return cpp.Contains("EwaLanczos4Sharpest, \"ewa_lanczos4sharpest\"", StringComparison.Ordinal)
            && qml.Contains("qsTr(\"EwaLanczosSharpest\")", StringComparison.Ordinal)
            && !qml.Contains("qsTr(\"EwaLanczos4Sharpest\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the switches are still compared against "yes" rather than parsed.</summary>
    public static bool TheSwitchesAreStillComparedToYes(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains($"== \"{PlaceboFlags.On}\"", StringComparison.Ordinal);
    }
}
