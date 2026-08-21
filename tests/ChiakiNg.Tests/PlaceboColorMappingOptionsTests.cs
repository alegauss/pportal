using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the colour-mapping sliders, and the three things the markup's repetition hides.
/// </summary>
public class PlaceboColorMappingOptionsTests
{
    private static PlaceboSlider Find(string name)
        => PlaceboColorMappingOptions.All.Single(o => o.Key == PlaceboStore.Key(name));

    /// <summary>
    /// Linear Knee is shown for Mobius and Gamma and hidden for Linear and Linear Light. A port
    /// matching an option to a function by name would put it in exactly the wrong two places.
    /// </summary>
    [Fact]
    public void LinearKneeIsNotForTheLinearFunctions()
    {
        PlaceboSlider knee = Find("linear_knee");

        Assert.True(knee.VisibleFor(7), "Mobius");
        Assert.True(knee.VisibleFor(9), "Gamma");
        Assert.False(knee.VisibleFor(10), "Linear");
        Assert.False(knee.VisibleFor(11), "Linear Light");

        // What those two get instead.
        PlaceboSlider exposure = Find("exposure");
        Assert.True(exposure.VisibleFor(10));
        Assert.True(exposure.VisibleFor(11));
        Assert.False(exposure.VisibleFor(7));
    }

    /// <summary>
    /// The two knee bounds meet at 0.5 rather than overlapping, so neither can be set past the
    /// other. Giving both 0..1 - which every other knee slider has - would let a minimum be set
    /// above its maximum.
    /// </summary>
    [Fact]
    public void TheTwoKneeBoundsMeetRatherThanOverlap()
    {
        PlaceboSlider min = Find("knee_minimum");
        PlaceboSlider max = Find("knee_maximum");

        Assert.Equal(0.00, min.Minimum);
        Assert.Equal(0.50, min.Maximum);
        Assert.Equal(0.50, max.Minimum);
        Assert.Equal(1.00, max.Maximum);

        Assert.Equal(min.Maximum, max.Minimum);

        // And the neighbours that DO run the full range, so the pair above is not just this
        // screen's habit.
        Assert.Equal(1.00, Find("knee_adaptation").Maximum);
        Assert.Equal(1.00, Find("knee_default").Maximum);
    }

    /// <summary>
    /// Six rows have no condition at all. Counted, because "every row has a gate" is the shape a
    /// port assumes and it hides all six.
    /// </summary>
    [Fact]
    public void SixRowsAreAlwaysOnScreen()
    {
        PlaceboSlider[] ungated =
            [.. PlaceboColorMappingOptions.All.Where(o => o.ShownFor.Count == 0)];

        Assert.Equal(6, ungated.Length);

        // Whatever function is chosen, those six stay.
        foreach (PlaceboSlider row in ungated)
        {
            Assert.True(row.VisibleFor(0));
            Assert.True(row.VisibleFor(11));
        }
    }

    /// <summary>
    /// Soft Clip Knee is shown for Perceptual as well, and the row below it - which shares the
    /// name - is not. Two options named alike with different conditions.
    /// </summary>
    [Fact]
    public void TheTwoSoftClipRowsHaveDifferentConditions()
    {
        PlaceboSlider knee = Find("softclip_knee");
        PlaceboSlider desat = Find("softclip_desat");

        Assert.True(knee.VisibleFor(1), "Perceptual");
        Assert.True(knee.VisibleFor(2), "Soft Clip");

        Assert.False(desat.VisibleFor(1));
        Assert.True(desat.VisibleFor(2));
    }

    /// <summary>
    /// The three LUT keys differ only in the case of their last letter, and one of the three is
    /// lower case. Transcribed rather than normalised: they are keys in a file the other client
    /// reads, and a tidied key is a slider stuck on its default forever.
    /// </summary>
    [Fact]
    public void TheThreeLutKeysAreNotConsistentlyCased()
    {
        Assert.EndsWith("lut3d_size_I", Lut3dKeys.SizeI, StringComparison.Ordinal);
        Assert.EndsWith("lut3d_size_C", Lut3dKeys.SizeC, StringComparison.Ordinal);
        Assert.EndsWith("lut3d_size_h", Lut3dKeys.SizeH, StringComparison.Ordinal);

        // The claim is that normalising them would produce three keys that are not these.
        Assert.NotEqual(Lut3dKeys.SizeH, Lut3dKeys.SizeH.ToUpperInvariant());
        Assert.NotEqual(Lut3dKeys.SizeI.ToLowerInvariant(), Lut3dKeys.SizeI);
    }

    /// <summary>Every slider's default sits inside its own range, which is not automatic here.</summary>
    [Fact]
    public void EveryDefaultIsInsideItsOwnRange()
    {
        foreach (PlaceboSlider row in PlaceboColorMappingOptions.All)
        {
            Assert.True(
                row.Default >= row.Minimum && row.Default <= row.Maximum,
                $"{row.Key}: {row.Default} is outside {row.Minimum}..{row.Maximum}");
        }
    }

    /// <summary>And every key is in the placebo store rather than the main one.</summary>
    [Fact]
    public void EveryKeyIsInThePlaceboStore()
    {
        foreach (PlaceboSlider row in PlaceboColorMappingOptions.All)
            Assert.StartsWith(PlaceboStore.Prefix, row.Key, StringComparison.Ordinal);

        Assert.Equal(22, PlaceboColorMappingOptions.All.Count);
    }

    /// <summary>
    /// The three findings above, still stated the same way in the screen and the store. Without
    /// this the table is a transcription with nothing holding it to what it was transcribed from.
    /// </summary>
    [Fact]
    public void TheThreeFindingsAreStillTheQtClients()
    {
        string? qmlPath = PlaceboColorMappingSource.Locate(PlaceboColorMappingSource.DialogQml);
        string? cppPath = PlaceboColorMappingSource.Locate(PlaceboColorMappingSource.SettingsCpp);
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);

        Assert.True(
            PlaceboColorMappingSource.LinearKneeIsStillShownForMobiusAndGamma(qml),
            "linear knee is still not for the linear functions");
        Assert.True(
            PlaceboColorMappingSource.TheTwoKneeBoundsStillMeetAtAHalf(qml),
            "the two knee bounds still meet at a half");
        Assert.True(
            PlaceboColorMappingSource.TheThreeLutKeysAreStillCasedThisWay(File.ReadAllText(cppPath)),
            "the three LUT keys are still cased inconsistently");
    }
}
