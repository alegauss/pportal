using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: four combos over two lists, and eleven switches that are words.
/// </summary>
public class PlaceboScalersTests
{
    /// <summary>
    /// Two lists, four combos, and four different defaults - two of them "no scaler" and two not.
    /// A port giving each list one default gets half of them wrong, in the direction that looks
    /// like the setting not being applied.
    /// </summary>
    [Fact]
    public void TheFourCombosOverTwoListsDefaultDifferently()
    {
        Assert.Same(PlaceboScalers.Upscaler.Labels, PlaceboScalers.PlaneUpscaler.Labels);
        Assert.Same(PlaceboScalers.Downscaler.Labels, PlaceboScalers.PlaneDownscaler.Labels);

        Assert.Equal("ewa_lanczossharp", PlaceboScalers.Upscaler.StoredFor(
            PlaceboScalers.Upscaler.DefaultIndex));
        Assert.Equal("none", PlaceboScalers.PlaneUpscaler.StoredFor(
            PlaceboScalers.PlaneUpscaler.DefaultIndex));
        Assert.Equal("hermite", PlaceboScalers.Downscaler.StoredFor(
            PlaceboScalers.Downscaler.DefaultIndex));
        Assert.Equal("none", PlaceboScalers.PlaneDownscaler.StoredFor(
            PlaceboScalers.PlaneDownscaler.DefaultIndex));
    }

    /// <summary>
    /// "Custom" means "none" here and the EMPTY STRING in the section presets. One label, two
    /// screens, two words - which is why neither can be derived from what a user reads.
    /// </summary>
    [Fact]
    public void CustomIsNoneHereAndNothingInThePresets()
    {
        foreach (StoredChoice scaler in PlaceboScalers.All)
        {
            Assert.Equal("Custom", scaler.Labels[0]);
            Assert.Equal("none", scaler.StoredFor(0));
        }

        // The other screen's first entry, with the same label.
        Assert.Equal("Custom", PlaceboSectionPresets.Deband.Labels[0]);
        Assert.Equal("", PlaceboSectionPresets.Deband.StoredFor(0));
    }

    /// <summary>
    /// One upscaler label loses a digit that both the enum and the stored word carry. The 4 is in
    /// every place a port reads from and in none of the places a user looks.
    /// </summary>
    [Fact]
    public void OneUpscalerLabelLosesItsDigit()
    {
        StoredChoice upscaler = PlaceboScalers.Upscaler;

        Assert.Equal("EwaLanczosSharpest", upscaler.Labels[10]);
        Assert.Equal("ewa_lanczos4sharpest", upscaler.StoredFor(10));

        Assert.DoesNotContain('4', upscaler.Labels[10]);
        Assert.Contains('4', upscaler.StoredFor(10));
    }

    /// <summary>
    /// And no rule turns these labels into these words in general - hyphens, spaces and case all
    /// move at once in the two FSRCNNX rows.
    /// </summary>
    [Fact]
    public void TheFsrcnnxLabelsAreNotTheirWordsEither()
    {
        StoredChoice upscaler = PlaceboScalers.Upscaler;

        Assert.Equal("FSRCNNX x2 8-0-4-1", upscaler.Labels[12]);
        Assert.Equal("fsrcnnx_x2_8_0_4_1", upscaler.StoredFor(12));

        Assert.Equal("FSRCNNX x2 16-0-4-1", upscaler.Labels[13]);
        Assert.Equal("fsrcnnx_x2_16_0_4_1", upscaler.StoredFor(13));
    }

    /// <summary>An unknown word takes each combo to its own default rather than to a shared one.</summary>
    [Fact]
    public void AnUnknownWordTakesEachComboToItsOwnDefault()
    {
        Assert.Equal(9, PlaceboScalers.Upscaler.IndexOf("nonsense"));
        Assert.Equal(0, PlaceboScalers.PlaneUpscaler.IndexOf("nonsense"));
        Assert.Equal(2, PlaceboScalers.Downscaler.IndexOf("nonsense"));
    }

    /// <summary>
    /// The eleven switches are words, and the split between on and off follows nothing a port
    /// could guess: deband and sigmoid are on, deinterlace and gamut expansion are off.
    /// </summary>
    [Fact]
    public void ElevenSwitchesAreWordsAndFiveOfThemDefaultOn()
    {
        Assert.Equal(11, PlaceboFlags.Defaults.Count);
        Assert.Equal(5, PlaceboFlags.Defaults.Count(f => f.Value));

        Assert.True(PlaceboFlags.Defaults[PlaceboStore.Key("deband")]);
        Assert.True(PlaceboFlags.Defaults[PlaceboStore.Key("sigmoid")]);
        Assert.False(PlaceboFlags.Defaults[PlaceboStore.Key("deinterlace")]);
        Assert.False(PlaceboFlags.Defaults[PlaceboStore.Key("gamut_expansion")]);
    }

    /// <summary>
    /// The switch is compared against "yes" rather than parsed, so a file holding "true" or "1"
    /// reads as off. Not leniency worth adding: two clients share the file.
    /// </summary>
    [Fact]
    public void AnythingThatIsNotYesIsOff()
    {
        Assert.True(PlaceboFlags.Read("yes"));
        Assert.False(PlaceboFlags.Read("no"));
        Assert.False(PlaceboFlags.Read("true"));
        Assert.False(PlaceboFlags.Read("1"));
        Assert.False(PlaceboFlags.Read("Yes"));
        Assert.False(PlaceboFlags.Read(null));

        // And the colour-mapping screen's own switch is one of the eleven rather than its own idiom.
        Assert.Equal(PlaceboFlags.On, InverseToneMapping.On);
        Assert.True(PlaceboFlags.Defaults.ContainsKey(InverseToneMapping.Key));
    }

    /// <summary>Every key here is in the placebo store.</summary>
    [Fact]
    public void EveryKeyIsInThePlaceboStore()
    {
        foreach (StoredChoice scaler in PlaceboScalers.All)
            Assert.StartsWith(PlaceboStore.Prefix, scaler.Key, StringComparison.Ordinal);

        foreach (string key in PlaceboFlags.Defaults.Keys)
            Assert.StartsWith(PlaceboStore.Prefix, key, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deinterlace combo is the exception: its four labels ARE their words, lower-cased. It is
    /// asserted as the exception rather than left unasserted, because a port that met this one
    /// first would take the wrong lesson from everything around it.
    /// </summary>
    [Fact]
    public void TheDeinterlaceComboIsTheOneThatDerives()
    {
        StoredChoice algo = PlaceboDeinterlaceChoice.Algorithm;

        for (int i = 0; i < algo.Labels.Count; i++)
        {
            Assert.True(
                PlaceboColorMapping.LabelWouldDeriveItsWord(algo.Labels[i], algo.StoredFor(i)),
                algo.Labels[i]);
        }

        Assert.Equal(2, algo.DefaultIndex);
        Assert.Equal("yadif", algo.StoredFor(algo.DefaultIndex));
    }

    /// <summary>Every rule above, still stated the same way in the store and the screen.</summary>
    [Fact]
    public void TheScalerRulesAreStillTheQtClients()
    {
        string? cppPath = PlaceboScalerSource.Locate(PlaceboScalerSource.SettingsCpp);
        string? qmlPath = PlaceboScalerSource.Locate(PlaceboScalerSource.DialogQml);
        if (cppPath is null || qmlPath is null)
            return;

        string cpp = File.ReadAllText(cppPath);

        Assert.True(PlaceboScalerSource.TheFourScalersStillDefaultDifferently(cpp), "four defaults");
        Assert.True(PlaceboScalerSource.TheScalersFirstWordIsStillNone(cpp), "none, not empty");
        Assert.True(
            PlaceboScalerSource.TheSharpestLabelStillLosesItsFour(cpp, File.ReadAllText(qmlPath)),
            "the missing 4");
        Assert.True(PlaceboScalerSource.TheSwitchesAreStillComparedToYes(cpp), "compared to yes");
        Assert.True(
            PlaceboScalerSource.TheDeinterlaceComboStillBindsBoth(File.ReadAllText(qmlPath)),
            "enabled and visible on one condition");
    }
}
