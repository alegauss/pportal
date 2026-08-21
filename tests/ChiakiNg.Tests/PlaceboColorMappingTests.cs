using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the colour-mapping screen's three tables, and the four pairings no rule produces.
/// </summary>
public class PlaceboColorMappingTests
{
    /// <summary>These options are in a second store, under their own prefix.</summary>
    [Fact]
    public void TheOptionsAreNotInTheMainStore()
    {
        Assert.StartsWith("placebo_settings/", PlaceboColorMapping.GamutMapping.Key, StringComparison.Ordinal);
        Assert.StartsWith("placebo_settings/", PlaceboColorMapping.ToneMapping.Key, StringComparison.Ordinal);
        Assert.StartsWith("placebo_settings/", InverseToneMapping.Key, StringComparison.Ordinal);

        // Not a substring test: "placebo_settings/gamut_mapping" contains "settings/gamut", which
        // is exactly the confusion this is about. The claim is where the key STARTS.
        Assert.False(
            PlaceboColorMapping.GamutMapping.Key.StartsWith("settings/", StringComparison.Ordinal),
            "a placebo option must not land in the main store's group");
    }

    /// <summary>
    /// Exactly four of the twenty-seven labels do not lower-case into their stored word. Counted
    /// rather than listed, so that a future entry which also breaks the rule turns this red rather
    /// than joining a list nobody re-reads.
    /// </summary>
    [Fact]
    public void FourOfTheTwentySevenLabelsDoNotDeriveTheirWord()
    {
        var broken = new List<string>();

        foreach (StoredChoice choice in new[]
        {
            PlaceboColorMapping.GamutMapping,
            PlaceboColorMapping.ToneMapping,
            PlaceboColorMapping.ToneMetadata,
        })
        {
            for (int i = 0; i < choice.Labels.Count; i++)
            {
                if (!PlaceboColorMapping.LabelWouldDeriveItsWord(choice.Labels[i], choice.StoredFor(i)))
                    broken.Add(choice.Labels[i]);
            }
        }

        Assert.Equal(
            new[] { "Soft Clip", "St-2094-10", "Linear Light", "HDR10 Plus" },
            broken);
    }

    /// <summary>
    /// The pair worth staring at: two adjacent rows of one combo, spelled differently on screen
    /// and identically in the store. No single rule turns one list into the other.
    /// </summary>
    [Fact]
    public void TheTwoSt2094RowsDisagreeOnScreenAndAgreeInTheStore()
    {
        StoredChoice tone = PlaceboColorMapping.ToneMapping;

        Assert.Equal("St2094-40", tone.Labels[2]);
        Assert.Equal("St-2094-10", tone.Labels[3]);

        Assert.Equal("st2094-40", tone.StoredFor(2));
        Assert.Equal("st2094-10", tone.StoredFor(3));

        // Lower-casing works for the first and not for the second, which is the whole trap.
        Assert.True(PlaceboColorMapping.LabelWouldDeriveItsWord(tone.Labels[2], tone.StoredFor(2)));
        Assert.False(PlaceboColorMapping.LabelWouldDeriveItsWord(tone.Labels[3], tone.StoredFor(3)));
    }

    /// <summary>The three defaults, which are not all the first entry.</summary>
    [Fact]
    public void TheDefaultsArePerceptualSplineAndAny()
    {
        Assert.Equal("perceptual", PlaceboColorMapping.GamutMapping.StoredFor(
            PlaceboColorMapping.GamutMapping.DefaultIndex));
        Assert.Equal("spline", PlaceboColorMapping.ToneMapping.StoredFor(
            PlaceboColorMapping.ToneMapping.DefaultIndex));
        Assert.Equal("any", PlaceboColorMapping.ToneMetadata.StoredFor(
            PlaceboColorMapping.ToneMetadata.DefaultIndex));
    }

    /// <summary>
    /// A word the client does not know is the default, not an error - which is why a port writing
    /// the wrong word gets a setting that resets rather than one that complains.
    /// </summary>
    [Fact]
    public void AnUnknownWordIsTheDefault()
    {
        StoredChoice tone = PlaceboColorMapping.ToneMapping;

        Assert.Equal(3, tone.IndexOf("st2094-10"));

        // What a port lower-casing the label would have written.
        Assert.Equal(tone.DefaultIndex, tone.IndexOf("st-2094-10"));
        Assert.Equal(1, tone.DefaultIndex);
    }

    /// <summary>
    /// The inverse switch is a checkbox stored as a word, and anything that is not "yes" is off -
    /// a comparison rather than a parse, so "true" reads as off.
    /// </summary>
    [Fact]
    public void TheInverseSwitchIsYesOrAnythingElse()
    {
        Assert.Equal("yes", InverseToneMapping.Store(true));
        Assert.Equal("no", InverseToneMapping.Store(false));

        Assert.True(InverseToneMapping.Read("yes"));
        Assert.False(InverseToneMapping.Read("no"));
        Assert.False(InverseToneMapping.Read("true"));
        Assert.False(InverseToneMapping.Read("YES"));
        Assert.False(InverseToneMapping.Read(null));
    }

    /// <summary>Every rule above, still stated the same way in the screen and the store.</summary>
    [Fact]
    public void TheColourMappingRulesAreStillTheQtClients()
    {
        string? qmlPath = PlaceboColorMappingSource.Locate(PlaceboColorMappingSource.DialogQml);
        string? cppPath = PlaceboColorMappingSource.Locate(PlaceboColorMappingSource.SettingsCpp);
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(PlaceboColorMappingSource.TheOptionsComeFromTheSecondStore(cpp), "a second store");
        Assert.True(PlaceboColorMappingSource.TheTwoSt2094LabelsStillDisagree(qml), "labels disagree");
        Assert.True(PlaceboColorMappingSource.TheTwoSt2094WordsStillAgree(cpp), "words agree");
        Assert.True(PlaceboColorMappingSource.AnUnknownWordStillFallsBack(cpp), "unknown is default");
        Assert.True(PlaceboColorMappingSource.TheInverseSwitchIsStillAWord(cpp), "yes and no");
    }
}
