using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the display dialog, whose two sliders carry a mode and a value in one integer.
/// </summary>
public class DisplaySettingsTests
{
    /// <summary>
    /// The finding. Contrast's three choices are Auto, Infinity and Numeric, and the value for
    /// Infinity is MINUS ONE while Auto is zero - so index order is not value order. A port mapping
    /// index to value ascending swaps the two.
    /// </summary>
    [Fact]
    public void ContrastsIndexOrderIsNotItsValueOrder()
    {
        Assert.Equal(0, DisplayTarget.Contrast.StoredForIndex(0));    // Auto
        Assert.Equal(-1, DisplayTarget.Contrast.StoredForIndex(1));   // Infinity
        Assert.Equal(1000, DisplayTarget.Contrast.StoredForIndex(2)); // Numeric

        // Ascending by index, the values are 0, -1, 1000 - not monotonic, which is the whole point.
        var byIndex = new[] { 0, 1, 2 }.Select(DisplayTarget.Contrast.StoredForIndex).ToArray();
        Assert.Equal(new[] { 0, -1, 1000 }, byIndex);
        Assert.NotEqual(byIndex.OrderBy(v => v).ToArray(), byIndex);
    }

    /// <summary>And the round trip back: each sentinel reads as its own index.</summary>
    [Theory]
    [InlineData(0, 0, DisplayTargetMode.Auto)]
    [InlineData(-1, 1, DisplayTargetMode.Infinity)]
    [InlineData(1000, 2, DisplayTargetMode.Numeric)]
    [InlineData(500, 2, DisplayTargetMode.Numeric)]
    [InlineData(1, 2, DisplayTargetMode.Numeric)]
    [InlineData(-2, 2, DisplayTargetMode.Numeric)]
    public void AStoredContrastReadsAsItsMode(int stored, int index, DisplayTargetMode mode)
    {
        Assert.Equal(mode, DisplayTarget.Contrast.ModeOf(stored));
        Assert.Equal(index, DisplayTarget.Contrast.IndexOf(stored));
    }

    /// <summary>
    /// Peak has only two modes and zero is its only sentinel, so anything non-zero is a number -
    /// including values below the slider's floor of 10, which show as a number the slider cannot
    /// reach rather than as Auto.
    /// </summary>
    [Theory]
    [InlineData(0, DisplayTargetMode.Auto)]
    [InlineData(1000, DisplayTargetMode.Numeric)]
    [InlineData(5, DisplayTargetMode.Numeric)]
    [InlineData(-1, DisplayTargetMode.Numeric)]
    public void PeaksOnlySentinelIsZero(int stored, DisplayTargetMode mode)
        => Assert.Equal(mode, DisplayTarget.Peak.ModeOf(stored));

    /// <summary>
    /// The sentinels are outside the slider's range on purpose - that is what lets a value double as
    /// a mode. Zero is below peak's floor of 10, and both zero and minus one are below contrast's.
    /// </summary>
    [Fact]
    public void TheSentinelsAreOutsideTheSlidersRange()
    {
        Assert.True(0 < DisplayTarget.Peak.Minimum);
        Assert.True(0 < DisplayTarget.Contrast.Minimum);
        Assert.True(-1 < DisplayTarget.Contrast.Minimum);

        // And the numeric default is inside it, so choosing that mode lands somewhere reachable.
        Assert.InRange(DisplayTarget.Peak.NumericDefault, DisplayTarget.Peak.Minimum, DisplayTarget.Peak.Maximum);
        Assert.InRange(
            DisplayTarget.Contrast.NumericDefault,
            DisplayTarget.Contrast.Minimum, DisplayTarget.Contrast.Maximum);
    }

    /// <summary>
    /// Choosing the numeric mode RESETS the value to the default rather than restoring whatever was
    /// there. A port that kept the old number would be friendlier and would not match.
    /// </summary>
    [Fact]
    public void ChoosingNumericResetsTheValue()
    {
        var model = new DisplaySettingsViewModel(
            new FakePreferences().Set("settings/display_target_peak", 7500));

        Assert.Equal(1, model.PeakIndex);
        Assert.Equal(7500, model.PeakStored);

        // Back to Auto and forward again: the 7500 is gone.
        model.PeakIndex = 0;
        Assert.Equal(0, model.PeakStored);

        model.PeakIndex = 1;
        Assert.Equal(DisplayTarget.Peak.NumericDefault, model.PeakStored);
    }

    /// <summary>A dialog with no store shows Auto everywhere, which is every default.</summary>
    [Fact]
    public void AnEmptyStoreIsAutoEverywhere()
    {
        var model = new DisplaySettingsViewModel(new FakePreferences());

        Assert.Equal(0, model.Primaries);
        Assert.Equal(0, model.Transfer);
        Assert.Equal(0, model.PeakIndex);
        Assert.Equal(0, model.PeakStored);
        Assert.Equal(0, model.ContrastIndex);
        Assert.Equal(0, model.ContrastStored);
        Assert.False(model.PeakSliderVisible);
        Assert.False(model.ContrastSliderVisible);

        // And the table PP2 transcribed agrees.
        Assert.Equal(0, Preferences.Find(DisplayTarget.Peak.Key)!.Default);
        Assert.Equal(0, Preferences.Find(DisplayTarget.Contrast.Key)!.Default);
        Assert.Equal(0, Preferences.Find(DisplaySettingsViewModel.PrimariesKey)!.Default);
        Assert.Equal(0, Preferences.Find(DisplaySettingsViewModel.TransferKey)!.Default);
    }

    /// <summary>An Infinity contrast is read back as Infinity and stored back as minus one.</summary>
    [Fact]
    public void AnInfiniteContrastSurvivesTheRoundTrip()
    {
        var model = new DisplaySettingsViewModel(
            new FakePreferences().Set("settings/display_target_contrast", -1));

        Assert.Equal(1, model.ContrastIndex);
        Assert.Equal(-1, model.ContrastStored);
        Assert.False(model.ContrastSliderVisible);
        Assert.True(model.ContrastComboIsLastInFocusChain);
    }

    /// <summary>
    /// The slider shows for the numeric mode and nothing else, and the focus chain's end moves with
    /// it - a fixed end would trap focus on a hidden control in two of the three modes.
    /// </summary>
    [Fact]
    public void TheFocusChainEndMovesWithTheMode()
    {
        var model = new DisplaySettingsViewModel();

        foreach (int index in new[] { 0, 1, 2 })
        {
            model.ContrastIndex = index;
            bool numeric = DisplayTarget.Contrast.Modes[index] == DisplayTargetMode.Numeric;

            Assert.Equal(numeric, model.ContrastSliderVisible);
            Assert.Equal(!numeric, model.ContrastComboIsLastInFocusChain);
        }
    }

    /// <summary>The two long lists are the QML's, including the typo in the transfer one.</summary>
    [Fact]
    public void TheLongListsAreTheQmlsIncludingItsTypo()
    {
        Assert.Equal(18, DisplaySettingsViewModel.PrimaryLabels.Count);
        Assert.Equal(17, DisplaySettingsViewModel.TransferLabels.Count);
        Assert.Equal("Auto", DisplaySettingsViewModel.PrimaryLabels[0]);
        Assert.Equal("Auto", DisplaySettingsViewModel.TransferLabels[0]);

        // "IPure power gamma 1.8" - reproduced, because a label a user reads in one client and
        // cannot find in the other is a port defect even when it is upstream's typo.
        Assert.Equal("IPure power gamma 1.8 (SDR)", DisplaySettingsViewModel.TransferLabels[4]);
    }

    /// <summary>Every rule above is still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (DisplaySettingsSource.Locate() is null)
            return;

        string qml = File.ReadAllText(DisplaySettingsSource.Locate()!);

        Assert.True(DisplaySettingsSource.ModeIsDerivedFromTheValue(qml, DisplayTarget.Peak), "peak mode");
        Assert.True(
            DisplaySettingsSource.ModeIsDerivedFromTheValue(qml, DisplayTarget.Contrast), "contrast mode");
        Assert.True(DisplaySettingsSource.InfinityIsMinusOne(qml), "infinity is -1");

        Assert.True(
            DisplaySettingsSource.ChoosingNumericWritesTheDefault(qml, DisplayTarget.Peak), "peak default");
        Assert.True(
            DisplaySettingsSource.ChoosingNumericWritesTheDefault(qml, DisplayTarget.Contrast),
            "contrast default");

        Assert.True(DisplaySettingsSource.SliderRangeIs(qml, DisplayTarget.Peak), "peak range");
        Assert.True(DisplaySettingsSource.SliderRangeIs(qml, DisplayTarget.Contrast), "contrast range");

        Assert.True(DisplaySettingsSource.TheFocusChainEndMovesWithTheMode(qml), "focus chain end");

        Assert.True(GeneralSettingsSource.ComboOffers(qml, DisplaySettingsViewModel.PrimaryLabels),
            "primary labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qml, DisplaySettingsViewModel.TransferLabels),
            "transfer labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qml, DisplayTarget.Peak.Labels), "peak labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qml, DisplayTarget.Contrast.Labels), "contrast labels");
    }

    /// <summary>And the property names are the ones PP142 read off the QML.</summary>
    [Fact]
    public void ThePropertyNamesAreTheQmlsOwn()
    {
        Assert.Equal("displayTargetPeak", PreferenceNames.For(Preferences.Find(DisplayTarget.Peak.Key)!));
        Assert.Equal(
            "displayTargetContrast", PreferenceNames.For(Preferences.Find(DisplayTarget.Contrast.Key)!));
        Assert.Equal(
            "displayTargetPrim",
            PreferenceNames.For(Preferences.Find(DisplaySettingsViewModel.PrimariesKey)!));
        Assert.Equal(
            "displayTargetTrc",
            PreferenceNames.For(Preferences.Find(DisplaySettingsViewModel.TransferKey)!));
    }
}
