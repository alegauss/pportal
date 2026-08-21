using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the presets, which are three names for one choice and six lists that are not alike.
/// </summary>
public class PlaceboPresetsTests
{
    /// <summary>
    /// The renderer preset is in the MAIN store, unlike every other option on these two screens.
    /// </summary>
    [Fact]
    public void TheRendererPresetIsNotInThePlaceboStore()
    {
        Assert.StartsWith("settings/", PlaceboPresetChoice.Key, StringComparison.Ordinal);
        Assert.False(
            PlaceboPresetChoice.Key.StartsWith(PlaceboStore.Prefix, StringComparison.Ordinal),
            "the renderer preset lives with the ordinary settings");
    }

    /// <summary>
    /// Its default is High Quality - not the entry named Default, and not the first. A port taking
    /// the obvious one starts a fresh install a step below the Qt client.
    /// </summary>
    [Fact]
    public void TheDefaultPresetIsHighQualityAndNotTheOneNamedDefault()
    {
        Assert.Equal(2, PlaceboPresetChoice.Preset.DefaultIndex);
        Assert.Equal("high_quality", PlaceboPresetChoice.Preset.StoredFor(
            PlaceboPresetChoice.Preset.DefaultIndex));

        Assert.Equal("Default", PlaceboPresetChoice.Preset.Labels[1]);
        Assert.NotEqual(1, PlaceboPresetChoice.Preset.DefaultIndex);
    }

    /// <summary>
    /// And PP10's menu now opens on the same one. This is the check that made the correction: the
    /// menu had started on Default because that is what the entry is called.
    /// </summary>
    [Fact]
    public void TheStreamMenuOpensOnTheStoresDefault()
    {
        var menu = new StreamMenuViewModel();

        Assert.Equal(StreamVideoPreset.HighQuality, menu.VideoPreset);
        Assert.Equal((int)menu.VideoPreset, PlaceboPresetChoice.Preset.DefaultIndex);
    }

    /// <summary>PP10's six menu values and this store's six words are one list.</summary>
    [Fact]
    public void TheMenusValuesAndTheStoresWordsAgree()
    {
        Assert.Equal("fast", PlaceboPresetChoice.StoredFor(StreamVideoPreset.Fast));
        Assert.Equal("custom", PlaceboPresetChoice.StoredFor(StreamVideoPreset.Custom));
        Assert.Equal(
            "high_quality_advanced_spatial",
            PlaceboPresetChoice.StoredFor(StreamVideoPreset.HighQualityAdvancedSpatial));

        Assert.Equal(StreamVideoPreset.Custom, PlaceboPresetChoice.From("custom"));

        // An unknown word is the store's default, which is the fallback the Qt client has.
        Assert.Equal(StreamVideoPreset.HighQuality, PlaceboPresetChoice.From("nonsense"));
    }

    /// <summary>
    /// Custom is stored as the EMPTY STRING in all five sections that have one. A port writing
    /// "custom" would write a word the client does not know.
    /// </summary>
    [Fact]
    public void CustomIsTheEmptyStringEverywhereItExists()
    {
        foreach (StoredChoice section in PlaceboSectionPresets.WithCustom)
        {
            Assert.Equal("Custom", section.Labels[0]);
            Assert.Equal("", section.StoredFor(0));
            Assert.Equal(0, section.DefaultIndex);
        }

        Assert.Equal(5, PlaceboSectionPresets.WithCustom.Count);
    }

    /// <summary>
    /// The six lists are not alike: two of two, one of two with a different second word, two of
    /// three, and one of one. A port generating six identical combos is wrong about four.
    /// </summary>
    [Fact]
    public void TheSixListsAreNotAlike()
    {
        Assert.Equal(2, PlaceboSectionPresets.Deband.Labels.Count);
        Assert.Equal(2, PlaceboSectionPresets.Sigmoid.Labels.Count);

        Assert.Equal(2, PlaceboSectionPresets.ColorAdjustment.Labels.Count);
        Assert.Equal("neutral", PlaceboSectionPresets.ColorAdjustment.StoredFor(1));

        Assert.Equal(3, PlaceboSectionPresets.PeakDetection.Labels.Count);
        Assert.Equal(3, PlaceboSectionPresets.ColorMapping.Labels.Count);

        Assert.Single(PlaceboSectionPresets.Deinterlace.Labels);
    }

    /// <summary>
    /// Deinterlace has no Custom at all, so its combo cannot hide anything - there is no index for
    /// the sliders' condition to be false at.
    /// </summary>
    [Fact]
    public void DeinterlaceHasNoCustomToHideAnythingBehind()
    {
        StoredChoice deinterlace = PlaceboSectionPresets.Deinterlace;

        Assert.DoesNotContain("Custom", deinterlace.Labels);
        Assert.Equal("default", deinterlace.StoredFor(0));

        Assert.DoesNotContain(deinterlace, PlaceboSectionPresets.WithCustom);
    }

    /// <summary>A section's sliders need its switch AND its preset, not either one.</summary>
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, false)]
    public void TheSlidersNeedBothConditions(bool enabled, int preset, bool visible)
        => Assert.Equal(visible, PlaceboSectionPresets.SlidersVisible(enabled, preset));

    /// <summary>Every rule above, still stated the same way in the screen and the store.</summary>
    [Fact]
    public void ThePresetRulesAreStillTheQtClients()
    {
        string? qmlPath = PlaceboPresetSource.Locate(PlaceboPresetSource.DialogQml);
        string? cppPath = PlaceboPresetSource.Locate(PlaceboPresetSource.SettingsCpp);
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(PlaceboPresetSource.ThePresetIsInTheMainStore(cpp), "the main store");
        Assert.True(PlaceboPresetSource.TheDefaultPresetIsHighQuality(cpp), "HighQuality");
        Assert.True(PlaceboPresetSource.CustomIsStillAnEmptyString(cpp), "an empty string");
        Assert.True(PlaceboPresetSource.DeinterlaceStillHasNoCustom(cpp, qml), "one entry");
        Assert.True(PlaceboPresetSource.TheSlidersNeedBothConditions(qml), "both conditions");
    }
}
