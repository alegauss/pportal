namespace ChiakiNg.Settings;

/// <summary>
/// PP17: one slider on a placebo screen - its key, its range, and which functions show it.
/// </summary>
/// <param name="Key">The key in the placebo store, prefix included.</param>
/// <param name="Minimum">The slider's floor, which is not always zero.</param>
/// <param name="Maximum">And its ceiling.</param>
/// <param name="Step">The step, which is 0.01 for most and 0.1 or 1 for the rest.</param>
/// <param name="Default">What the store answers when it holds nothing.</param>
/// <param name="ShownFor">
/// The function indices this row appears for. EMPTY means always - three rows on each half of the
/// screen are ungated, and a port that gave every row a condition would hide them all.
/// </param>
public sealed record PlaceboSlider(
    string Key,
    double Minimum,
    double Maximum,
    double Step,
    double Default,
    IReadOnlyList<int> ShownFor)
{
    /// <summary>Whether this row is on screen for a chosen function.</summary>
    public bool VisibleFor(int function)
        => ShownFor.Count == 0 || ShownFor.Contains(function);
}

/// <summary>
/// PP17: the colour-mapping screen's twenty-two sliders, as a table rather than as markup.
///
/// The screen repeats each row's condition on its label, its slider and its default hint - three
/// copies per row, sixty-odd copies in the file - so the condition is the thing worth holding in
/// one place. What that table then makes visible is three findings the markup buries:
///
/// 1. LINEAR KNEE IS NOT FOR LINEAR. <c>placeboToneMappingLinearKnee</c> is shown for Mobius and
///    for Gamma, and hidden for Linear and Linear Light - which get Exposure instead. A port
///    matching options to functions by name would show it in exactly the wrong two places.
///
/// 2. KNEE MINIMUM AND KNEE MAXIMUM DO NOT SHARE A RANGE. One runs 0 to 0.5 and the other 0.5 to
///    1, meeting at a point neither can pass. Giving both 0..1, which is what every other knee
///    slider has, would let a user set a minimum above the maximum.
///
/// 3. SIX ROWS HAVE NO CONDITION AT ALL - the three LUT sizes and the three at the end of the
///    tone half. They are always on screen, which is the one thing a port writing "every row has
///    a gate" gets uniformly wrong.
///
/// The three LUT keys carry a fourth finding of their own; see <see cref="Lut3dKeys"/>.
/// </summary>
public static class PlaceboColorMappingOptions
{
    // Gamut mapping function indices, from the enum declared inside the QML itself.
    private const int Perceptual = 1;
    private const int SoftClip = 2;
    private const int Relative = 3;
    private const int Absolute = 5;
    private const int Darken = 7;

    // And tone mapping's.
    private const int Spline = 1;
    private const int St209440 = 2;
    private const int St209410 = 3;
    private const int Bt2390 = 4;
    private const int Reinhard = 6;
    private const int Mobius = 7;
    private const int Gamma = 9;
    private const int Linear = 10;
    private const int LinearLight = 11;

    private static readonly int[] Always = [];

    /// <summary>The gamut half: five gated rows and the three LUT sizes.</summary>
    public static IReadOnlyList<PlaceboSlider> Gamut { get; } =
    [
        new(PlaceboStore.Key("perceptual_deadzone"), 0.00, 1.00, 0.01, 0.30, [Perceptual]),
        new(PlaceboStore.Key("perceptual_strength"), 0.00, 1.00, 0.01, 0.80, [Perceptual]),
        new(PlaceboStore.Key("colorimetric_gamma"), 0.00, 10.00, 0.01, 1.80, [Relative, Absolute, Darken]),

        // Shown for Perceptual as well as Soft Clip, though its name says otherwise - and the row
        // directly below it, which shares the name, is not.
        new(PlaceboStore.Key("softclip_knee"), 0.00, 1.00, 0.01, 0.70, [Perceptual, SoftClip]),
        new(PlaceboStore.Key("softclip_desat"), 0.00, 1.00, 0.01, 0.35, [SoftClip]),

        new(Lut3dKeys.SizeI, 0, 1024, 1, 48, Always),
        new(Lut3dKeys.SizeC, 0, 1024, 1, 32, Always),
        new(Lut3dKeys.SizeH, 0, 1024, 1, 256, Always),
    ];

    /// <summary>The tone half: eleven gated rows and three that are always there.</summary>
    public static IReadOnlyList<PlaceboSlider> Tone { get; } =
    [
        new(PlaceboStore.Key("knee_adaptation"), 0.00, 1.00, 0.01, 0.4, [Spline, St209440, St209410]),

        // The two that meet at 0.5 rather than overlapping.
        new(PlaceboStore.Key("knee_minimum"), 0.00, 0.50, 0.01, 0.1, [Spline, St209440, St209410]),
        new(PlaceboStore.Key("knee_maximum"), 0.50, 1.00, 0.01, 0.8, [Spline, St209440, St209410]),

        new(PlaceboStore.Key("knee_default"), 0.00, 1.00, 0.01, 0.4, [Spline, St209440, St209410]),
        new(PlaceboStore.Key("knee_offset"), 0.50, 2.00, 0.01, 1.0, [Bt2390]),

        new(PlaceboStore.Key("slope_tuning"), 0.0, 10.0, 0.1, 1.5, [Spline]),
        new(PlaceboStore.Key("slope_offset"), 0.00, 1.00, 0.01, 0.2, [Spline]),
        new(PlaceboStore.Key("spline_contrast"), 0.00, 1.50, 0.01, 0.5, [Spline]),

        new(PlaceboStore.Key("reinhard_contrast"), 0.00, 1.00, 0.01, 0.5, [Reinhard]),

        // Named for a function it is hidden for.
        new(PlaceboStore.Key("linear_knee"), 0.00, 1.00, 0.01, 0.3, [Mobius, Gamma]),

        new(PlaceboStore.Key("exposure"), 0.0, 10.0, 0.1, 1.0, [Linear, LinearLight]),

        new(PlaceboStore.Key("tone_lut_size"), 0, 4096, 1, 256, Always),
        new(PlaceboStore.Key("contrast_recovery"), 0.00, 2.00, 0.01, 0.0, Always),
        new(PlaceboStore.Key("contrast_smoothness"), 1.0, 32.0, 0.1, 3.5, Always),
    ];

    /// <summary>Every slider on the screen, both halves.</summary>
    public static IReadOnlyList<PlaceboSlider> All { get; } = [.. Gamut, .. Tone];
}

/// <summary>
/// PP17: three sibling keys whose only difference is the CASE of their last letter.
///
/// settings.cpp writes <c>lut3d_size_I</c>, <c>lut3d_size_C</c> and <c>lut3d_size_h</c>: two
/// capitals and one lower case, in three lines that were plainly written together. The letters
/// are the axes of a perceptual colour space - I, C and h - so the case is not decoration, but
/// nothing makes the third one small except that it was typed that way.
///
/// It is transcribed rather than normalised because these are keys in a file the other client
/// reads. A port that tidied them to one case would write a fourth key nobody reads and leave the
/// third slider on its default forever.
/// </summary>
public static class Lut3dKeys
{
    /// <summary>Intensity. Capital I.</summary>
    public static string SizeI { get; } = PlaceboStore.Key("lut3d_size_I");

    /// <summary>Chroma. Capital C.</summary>
    public static string SizeC { get; } = PlaceboStore.Key("lut3d_size_C");

    /// <summary>Hue, and the odd one out: a small h.</summary>
    public static string SizeH { get; } = PlaceboStore.Key("lut3d_size_h");
}
