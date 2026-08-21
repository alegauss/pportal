using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the forty labels, and the one rule these two screens turned out to follow.
/// </summary>
public class PlaceboLabelTests
{
    private static IEnumerable<(string Key, double Step)> EverySlider()
    {
        foreach (PlaceboSlider row in PlaceboColorMappingOptions.All)
            yield return (row.Key, row.Step);

        foreach (PlaceboTuningSlider row in PlaceboTuningOptions.All)
            yield return (row.Key, row.Step);
    }

    /// <summary>
    /// Every slider on both screens has a label and every label belongs to a slider. Asserted as
    /// two directions rather than one count, so a row added without a label and a label left after
    /// a row was removed both turn this red.
    /// </summary>
    [Fact]
    public void EverySliderHasALabelAndEveryLabelHasASlider()
    {
        var keys = EverySlider().Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        foreach ((string key, _) in EverySlider())
            Assert.True(PlaceboLabels.Sliders.ContainsKey(key), $"no label for {key}");

        foreach (string labelled in PlaceboLabels.Sliders.Keys)
            Assert.True(keys.Contains(labelled), $"a label for {labelled}, which is not a slider");

        Assert.Equal(40, PlaceboLabels.Sliders.Count);
    }

    /// <summary>
    /// The decimals a value is printed with are the decimals of its step, on all forty rows. The
    /// counts are asserted rather than the rule alone: the rule holding is what makes forty
    /// transcribed formats unnecessary, and a future row that broke it would otherwise be
    /// formatted wrongly in silence.
    /// </summary>
    [Fact]
    public void TheCaptionsDecimalsAreTheStepsDecimals()
    {
        Dictionary<int, int> byDecimals = EverySlider()
            .GroupBy(s => PlaceboCaption.DecimalsFor(s.Step))
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(19, byDecimals[2]);
        Assert.Equal(14, byDecimals[1]);
        Assert.Equal(5, byDecimals[0]);
        Assert.Equal(2, byDecimals[3]);
    }

    /// <summary>And the screens print exactly those counts, which is the other half of the claim.</summary>
    [Fact]
    public void TheScreensPrintExactlyThoseCounts()
    {
        string? colourPath = PlaceboLabelSource.Locate(PlaceboLabelSource.ColorMappingQml);
        string? tuningPath = PlaceboLabelSource.Locate(PlaceboLabelSource.TuningQml);
        if (colourPath is null || tuningPath is null)
            return;

        string colour = File.ReadAllText(colourPath);
        string tuning = File.ReadAllText(tuningPath);

        int Both(int decimals)
            => PlaceboLabelSource.CaptionsWithDecimals(colour, decimals)
                + PlaceboLabelSource.CaptionsWithDecimals(tuning, decimals);

        Assert.Equal(19, Both(2));
        Assert.Equal(14, Both(1));
        Assert.Equal(5, Both(0));
        Assert.Equal(2, Both(3));
    }

    /// <summary>The formatter itself, at each of the four widths.</summary>
    [Theory]
    [InlineData(0.5, 0.01, "0.50")]
    [InlineData(1.5, 0.1, "1.5")]
    [InlineData(16, 1, "16")]
    [InlineData(-1.143, 0.001, "-1.143")]
    public void TheCaptionFormatsToItsStepsWidth(double value, double step, string expected)
        => Assert.Equal(expected, PlaceboCaption.For(value, step));

    /// <summary>
    /// The lower-case h is on the SCREEN as well as in the key, so PP168's inconsistency was not
    /// confined to the store - a port tidying either one would disagree with the other client in
    /// two places rather than one.
    /// </summary>
    [Fact]
    public void TheLowerCaseHIsOnScreenToo()
    {
        Assert.Equal("LUT 3D Size h:", PlaceboLabels.For(Lut3dKeys.SizeH));
        Assert.Equal("LUT 3D Size I:", PlaceboLabels.For(Lut3dKeys.SizeI));
        Assert.Equal("LUT 3D Size C:", PlaceboLabels.For(Lut3dKeys.SizeC));
    }

    /// <summary>And one label is hyphenated where its key is not.</summary>
    [Fact]
    public void AntiringingIsHyphenatedOnScreenAndNotInTheKey()
    {
        string key = PlaceboStore.Key("antiringing_strength");

        Assert.Equal("Anti-ringing Strength:", PlaceboLabels.For(key));
        Assert.DoesNotContain('-', key);
    }

    /// <summary>Every transcribed label is still on the screen it was transcribed from.</summary>
    [Fact]
    public void EveryLabelIsStillOnItsScreen()
    {
        string? colourPath = PlaceboLabelSource.Locate(PlaceboLabelSource.ColorMappingQml);
        string? tuningPath = PlaceboLabelSource.Locate(PlaceboLabelSource.TuningQml);
        if (colourPath is null || tuningPath is null)
            return;

        string colour = File.ReadAllText(colourPath);
        string tuning = File.ReadAllText(tuningPath);

        foreach (string label in PlaceboLabels.Sliders.Values.Concat(PlaceboLabels.Controls.Values))
        {
            Assert.True(
                PlaceboLabelSource.HasLabel(colour, label) || PlaceboLabelSource.HasLabel(tuning, label),
                $"\"{label}\" is on neither screen any more");
        }
    }
}
