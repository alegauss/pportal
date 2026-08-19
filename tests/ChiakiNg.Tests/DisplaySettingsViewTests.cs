using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the display dialog as markup - that the four lists are filled before the bindings resolve,
/// and that each slider appears for exactly the mode that owns it.
/// </summary>
public class DisplaySettingsViewTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 800));
        element.Arrange(new Rect(0, 0, 900, 800));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoadsWithEveryListFilled() => OnSta(() =>
    {
        var view = new DisplaySettingsView();

        Assert.Equal(18, ((ComboBox)view.FindName("PrimariesCombo")).Items.Count);
        Assert.Equal(17, ((ComboBox)view.FindName("TransferCombo")).Items.Count);
        Assert.Equal(2, ((ComboBox)view.FindName("PeakCombo")).Items.Count);
        Assert.Equal(3, ((ComboBox)view.FindName("ContrastCombo")).Items.Count);
    });

    /// <summary>The slider bounds come from the rule, not the markup - the floor is what excludes zero.</summary>
    [Fact]
    public void TheSliderBoundsExcludeTheSentinels() => OnSta(() =>
    {
        var view = new DisplaySettingsView();

        var peak = (Slider)view.FindName("PeakSlider");
        Assert.Equal(DisplayTarget.Peak.Minimum, peak.Minimum);
        Assert.Equal(DisplayTarget.Peak.Maximum, peak.Maximum);
        Assert.True(peak.Minimum > 0, "zero has to be unreachable for it to mean Auto");

        var contrast = (Slider)view.FindName("ContrastSlider");
        Assert.Equal(DisplayTarget.Contrast.Minimum, contrast.Minimum);
        Assert.True(contrast.Minimum > 0, "zero and minus one both have to be unreachable");
    });

    /// <summary>A stored choice survives the fill, including an Infinity contrast.</summary>
    [Fact]
    public void AStoredChoiceSurvivesTheFill() => OnSta(() =>
    {
        var model = new DisplaySettingsViewModel(new FakePreferences()
            .Set("settings/display_target_prim", 6)
            .Set("settings/display_target_trc", 12)
            .Set("settings/display_target_contrast", -1));

        var view = new DisplaySettingsView { DataContext = model };
        Realise(view);

        Assert.Equal(6, ((ComboBox)view.FindName("PrimariesCombo")).SelectedIndex);
        Assert.Equal(12, ((ComboBox)view.FindName("TransferCombo")).SelectedIndex);
        Assert.Equal(1, ((ComboBox)view.FindName("ContrastCombo")).SelectedIndex);
        Assert.Equal(-1, model.ContrastStored);
    });

    /// <summary>
    /// Each slider group appears for its own numeric mode and no other - and switching contrast to
    /// Infinity hides it again, which the sentinel-carrying value has to survive.
    /// </summary>
    [Fact]
    public void EachSliderAppearsForItsOwnModeOnly() => OnSta(() =>
    {
        var model = new DisplaySettingsViewModel();
        var view = new DisplaySettingsView { DataContext = model };
        Realise(view);

        var peakGroup = (FrameworkElement)view.FindName("PeakValueGroup");
        var contrastGroup = (FrameworkElement)view.FindName("ContrastValueGroup");

        Assert.Equal(Visibility.Collapsed, peakGroup.Visibility);
        Assert.Equal(Visibility.Collapsed, contrastGroup.Visibility);

        ((ComboBox)view.FindName("PeakCombo")).SelectedIndex = 1;
        Realise(view);
        Assert.Equal(Visibility.Visible, peakGroup.Visibility);
        Assert.Equal(Visibility.Collapsed, contrastGroup.Visibility);
        Assert.Equal(DisplayTarget.Peak.NumericDefault, model.PeakStored);

        var contrastCombo = (ComboBox)view.FindName("ContrastCombo");
        contrastCombo.SelectedIndex = 2;
        Realise(view);
        Assert.Equal(Visibility.Visible, contrastGroup.Visibility);
        Assert.Equal(DisplayTarget.Contrast.NumericDefault, model.ContrastStored);

        contrastCombo.SelectedIndex = 1;
        Realise(view);
        Assert.Equal(Visibility.Collapsed, contrastGroup.Visibility);
        Assert.Equal(-1, model.ContrastStored);
    });

    /// <summary>Moving the slider reaches the store, in nits and unitless respectively.</summary>
    [Fact]
    public void MovingTheSliderReachesTheStore() => OnSta(() =>
    {
        var model = new DisplaySettingsViewModel { PeakIndex = 1 };
        var view = new DisplaySettingsView { DataContext = model };
        Realise(view);

        var slider = (Slider)view.FindName("PeakSlider");
        slider.Value = 4000;
        Realise(view);

        Assert.Equal(4000, model.PeakValue);
        Assert.Equal(4000, model.PeakStored);
        Assert.Contains("nits", ((TextBlock)view.FindName("PeakValueLabel")).Text);
    });
}
