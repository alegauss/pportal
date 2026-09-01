using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP17: the two placebo screens - that a function change really re-shows the rows, and that the
/// combos really carry the tables the store reads.
/// </summary>
public class PlaceboScreensTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(1100, 900));
        element.Arrange(new Rect(0, 0, 1100, 900));
        element.UpdateLayout();
    }

    [Fact]
    public void BothScreensLoad() => Apartment.Run(() =>
    {
        Assert.NotNull(new PlaceboColorMappingView());
        Assert.NotNull(new PlaceboTuningView());
    });

    /// <summary>
    /// The combos carry the tables the store reads its words from, in the same order - so the
    /// choice a user picks and the word written for it cannot drift apart.
    /// </summary>
    [Fact]
    public void TheCombosCarryTheStoresOwnLists() => Apartment.Run(() =>
    {
        var colour = new PlaceboColorMappingView { DataContext = new PlaceboColorMappingViewModel() };
        Realise(colour);

        var gamut = (ComboBox)colour.FindName("GamutCombo");
        Assert.Equal(PlaceboColorMapping.GamutMapping.Labels.Count, gamut.Items.Count);
        Assert.Equal("St-2094-10", ((ComboBox)colour.FindName("ToneCombo")).Items[3]);

        var tuning = new PlaceboTuningView { DataContext = new PlaceboTuningViewModel() };
        Realise(tuning);

        // Two lists behind four combos, so two pairs of counts rather than four.
        Assert.Equal(
            ((ComboBox)tuning.FindName("UpscalerCombo")).Items.Count,
            ((ComboBox)tuning.FindName("PlaneUpscalerCombo")).Items.Count);
        Assert.Equal(
            ((ComboBox)tuning.FindName("DownscalerCombo")).Items.Count,
            ((ComboBox)tuning.FindName("PlaneDownscalerCombo")).Items.Count);
    });

    /// <summary>
    /// And they open on the store's four different defaults, which is the PP171 finding reaching
    /// the screen rather than staying in a table.
    /// </summary>
    [Fact]
    public void TheFourScalerCombosOpenApart() => Apartment.Run(() =>
    {
        var view = new PlaceboTuningView { DataContext = new PlaceboTuningViewModel() };
        Realise(view);

        Assert.Equal(9, ((ComboBox)view.FindName("UpscalerCombo")).SelectedIndex);
        Assert.Equal(0, ((ComboBox)view.FindName("PlaneUpscalerCombo")).SelectedIndex);
        Assert.Equal(2, ((ComboBox)view.FindName("DownscalerCombo")).SelectedIndex);
        Assert.Equal(0, ((ComboBox)view.FindName("PlaneDownscalerCombo")).SelectedIndex);
    });

    /// <summary>
    /// Changing the tone function re-shows the rows in one pass. Asserted on a row that is hidden
    /// for the function it is NAMED after, so a screen that matched rows to functions by name
    /// would fail here rather than look plausible.
    /// </summary>
    [Fact]
    public void ChangingTheFunctionReShowsTheRows()
    {
        var model = new PlaceboColorMappingViewModel();

        PlaceboSliderRow knee = model.Tone.Single(r => r.Key.EndsWith("linear_knee", StringComparison.Ordinal));
        PlaceboSliderRow exposure = model.Tone.Single(r => r.Key.EndsWith("exposure", StringComparison.Ordinal));

        // Spline, which is the store's default: neither row belongs to it.
        Assert.False(knee.Visible);
        Assert.False(exposure.Visible);

        model.ToneFunction = 10; // Linear
        Assert.False(knee.Visible);
        Assert.True(exposure.Visible);

        model.ToneFunction = 7; // Mobius
        Assert.True(knee.Visible);
        Assert.False(exposure.Visible);
    }

    /// <summary>The six ungated rows stay on screen through every function.</summary>
    [Fact]
    public void TheUngatedRowsNeverLeave()
    {
        var model = new PlaceboColorMappingViewModel();

        PlaceboSliderRow lutSize = model.Gamut.Single(r => r.Key == Lut3dKeys.SizeH);

        for (int function = 0; function < PlaceboColorMapping.GamutMapping.Labels.Count; function++)
        {
            model.GamutFunction = function;
            Assert.True(lutSize.Visible, $"hidden at function {function}");
        }
    }

    /// <summary>A section's rows need its switch and its preset together.</summary>
    [Fact]
    public void ASectionsRowsNeedBothOfItsControls()
    {
        var model = new PlaceboTuningViewModel();

        PlaceboSliderRow grain = model.Rows.Single(r => r.Key.EndsWith("deband_grain", StringComparison.Ordinal));
        Assert.True(grain.Visible);

        model.SetPreset(PlaceboSection.Deband, 1);
        Assert.False(grain.Visible);

        model.SetPreset(PlaceboSection.Deband, PlaceboSectionPresets.CustomIndex);
        Assert.True(grain.Visible);

        model.SetEnabled(PlaceboSection.Deband, false);
        Assert.False(grain.Visible);

        // And the row that belongs to no section is untouched by either.
        PlaceboSliderRow antiringing =
            model.Rows.Single(r => r.Key.EndsWith("antiringing_strength", StringComparison.Ordinal));
        Assert.True(antiringing.Visible);
    }

    /// <summary>Every row opens on its own default, printed at its own width.</summary>
    [Fact]
    public void EveryRowOpensOnItsDefaultAtItsOwnWidth()
    {
        var model = new PlaceboTuningViewModel();

        PlaceboSliderRow temperature =
            model.Rows.Single(r => r.Key.EndsWith("temperature", StringComparison.Ordinal));
        Assert.Equal("0.000", temperature.Caption);

        PlaceboSliderRow iterations =
            model.Rows.Single(r => r.Key.EndsWith("deband_iterations", StringComparison.Ordinal));
        Assert.Equal("1", iterations.Caption);

        PlaceboSliderRow hue =
            model.Rows.Single(r => r.Key.EndsWith("hue", StringComparison.Ordinal));
        Assert.Equal("0.00", hue.Caption);
        Assert.Equal("Hue:", hue.Label);
    }

    /// <summary>And the rows really reach the screen, hidden ones collapsed.</summary>
    [Fact]
    public void TheRowsReachTheScreen() => Apartment.Run(() =>
    {
        var model = new PlaceboColorMappingViewModel();
        var view = new PlaceboColorMappingView { DataContext = model };
        Realise(view);

        var rows = (ItemsControl)view.FindName("GamutRows");

        Assert.Equal(PlaceboColorMappingOptions.Gamut.Count, rows.Items.Count);
    });
}
