using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the tuning screen's sliders, and the four numbers a port would round away.
/// </summary>
public class PlaceboTuningOptionsTests
{
    private static PlaceboTuningSlider Find(string name)
        => PlaceboTuningOptions.All.Single(o => o.Key == PlaceboStore.Key(name));

    /// <summary>
    /// Hue is in radians and stops at 6.28 - a turn cut short at two decimals rather than a turn.
    /// Both halves are asserted: the unit, by being near 2π, and the truncation, by not being it.
    /// </summary>
    [Fact]
    public void HueIsRadiansAndItsCeilingIsATurnCutShort()
    {
        PlaceboTuningSlider hue = Find("hue");

        Assert.Equal(0.00, hue.Minimum);
        Assert.Equal(PlaceboTuningOptions.HueMaximum, hue.Maximum);

        // Near a full turn, and not one - which is the whole of the finding.
        Assert.True(PlaceboTuningOptions.FullTurnRadians - hue.Maximum < 0.01);
        Assert.NotEqual(PlaceboTuningOptions.FullTurnRadians, hue.Maximum);

        // And nothing like a number of degrees.
        Assert.NotEqual(360, hue.Maximum);
    }

    /// <summary>
    /// Temperature carries libplacebo's own bounds, which are round in no unit. Rounding them to
    /// -1..5 removes the top of the range without anything on screen changing.
    /// </summary>
    [Fact]
    public void TemperatureKeepsItsUnroundBounds()
    {
        PlaceboTuningSlider temperature = Find("temperature");

        Assert.Equal(-1.143, temperature.Minimum);
        Assert.Equal(5.286, temperature.Maximum);
        Assert.Equal(0.001, temperature.Step);
    }

    /// <summary>
    /// Exactly two rows start below zero. Counted, so a future row that also does turns this red
    /// rather than joining a list nobody re-reads.
    /// </summary>
    [Fact]
    public void OnlyTwoRowsGoNegative()
    {
        PlaceboTuningSlider[] negative =
            [.. PlaceboTuningOptions.All.Where(o => o.Minimum < 0)];

        Assert.Equal(2, negative.Length);
        Assert.Contains(negative, o => o.Key.EndsWith("brightness", StringComparison.Ordinal));
        Assert.Contains(negative, o => o.Key.EndsWith("temperature", StringComparison.Ordinal));
    }

    /// <summary>
    /// Antiringing belongs to no section, so no switch and no preset can take it off the screen -
    /// the same trap the colour-mapping screen has six of.
    /// </summary>
    [Fact]
    public void AntiringingIsOnScreenWhateverIsChosen()
    {
        PlaceboTuningSlider antiringing = Find("antiringing_strength");

        Assert.Equal(PlaceboSection.Always, antiringing.Section);
        Assert.True(antiringing.VisibleFor(sectionEnabled: false, presetIndex: 1));
        Assert.True(antiringing.VisibleFor(sectionEnabled: true, presetIndex: 0));

        // And it is the only one on this screen.
        Assert.Single(PlaceboTuningOptions.All, o => o.Section == PlaceboSection.Always);
    }

    /// <summary>Every other row needs its switch and its preset together.</summary>
    [Fact]
    public void EveryOtherRowNeedsBothConditions()
    {
        foreach (PlaceboTuningSlider row in PlaceboTuningOptions.All)
        {
            if (row.Section == PlaceboSection.Always)
                continue;

            Assert.True(row.VisibleFor(true, PlaceboSectionPresets.CustomIndex), row.Key);
            Assert.False(row.VisibleFor(true, 1), row.Key);
            Assert.False(row.VisibleFor(false, PlaceboSectionPresets.CustomIndex), row.Key);
        }
    }

    /// <summary>Every default sits inside its own range, which is not automatic with these bounds.</summary>
    [Fact]
    public void EveryDefaultIsInsideItsOwnRange()
    {
        foreach (PlaceboTuningSlider row in PlaceboTuningOptions.All)
        {
            Assert.True(
                row.Default >= row.Minimum && row.Default <= row.Maximum,
                $"{row.Key}: {row.Default} is outside {row.Minimum}..{row.Maximum}");
        }
    }

    /// <summary>And every key is in the placebo store, unlike the renderer preset above them.</summary>
    [Fact]
    public void EveryKeyIsInThePlaceboStore()
    {
        foreach (PlaceboTuningSlider row in PlaceboTuningOptions.All)
            Assert.StartsWith(PlaceboStore.Prefix, row.Key, StringComparison.Ordinal);

        Assert.Equal(18, PlaceboTuningOptions.All.Count);
    }

    /// <summary>Every rule above, still stated the same way in the screen.</summary>
    [Fact]
    public void TheTuningRangesAreStillTheQtClients()
    {
        string? qmlPath = PlaceboTuningSource.Locate(PlaceboTuningSource.DialogQml);
        if (qmlPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);

        Assert.True(PlaceboTuningSource.HueStillEndsAtSixPointTwoEight(qml), "a truncated turn");
        Assert.True(PlaceboTuningSource.TemperatureStillCarriesItsOwnBounds(qml), "unround bounds");
        Assert.True(PlaceboTuningSource.AntiringingStillHasNoCondition(qml), "no condition");
        Assert.True(PlaceboTuningSource.OnlyTwoRowsStillGoNegative(qml), "two negatives");
    }
}
