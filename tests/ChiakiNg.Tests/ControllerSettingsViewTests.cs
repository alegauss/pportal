using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Controllers tab as markup - that the five rows really move together, and that the
/// four combos really carry the same list.
/// </summary>
public class ControllerSettingsViewTests
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
        element.Measure(new Size(1000, 800));
        element.Arrange(new Rect(0, 0, 1000, 800));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoadsWithEveryComboFilled() => OnSta(() =>
    {
        var view = new ControllerSettingsView { DataContext = new ControllerSettingsViewModel() };
        Realise(view);

        foreach (string name in new[] { "Shortcut1", "Shortcut2", "Shortcut3", "Shortcut4" })
        {
            var combo = (ComboBox)view.FindName(name);
            Assert.Equal(DpadTouchShortcut.Buttons.Count, combo.Items.Count);
            Assert.Equal("Not Used", combo.Items[0]);
        }

        Assert.Equal(
            RumbleHapticsChoice.Intensity.Labels.Count,
            ((ComboBox)view.FindName("RumbleCombo")).Items.Count);
    });

    /// <summary>
    /// The five rows are one group on one checkbox. Asserted through the view because a port that
    /// bound each row separately would look identical until one of them was missed.
    /// </summary>
    [Fact]
    public void TheFiveRowsMoveTogether() => OnSta(() =>
    {
        var model = new ControllerSettingsViewModel();
        var view = new ControllerSettingsView { DataContext = model };
        Realise(view);

        var rows = (FrameworkElement)view.FindName("DpadTouchRows");
        Assert.Equal(Visibility.Visible, rows.Visibility);

        model.ToggleDpadTouch();
        Realise(view);

        Assert.Equal(Visibility.Collapsed, rows.Visibility);
    });

    /// <summary>
    /// The two labels that carry a conversion reach the screen converted: the increment in
    /// millimetres and the multiplier as a word inside its band.
    /// </summary>
    [Fact]
    public void TheTwoConvertedLabelsReachTheScreen() => OnSta(() =>
    {
        var model = new ControllerSettingsViewModel();
        var view = new ControllerSettingsView { DataContext = model };
        Realise(view);

        Assert.Equal("0.3 mm", ((TextBlock)view.FindName("IncrementCaption")).Text);
        Assert.Equal("console setting", ((TextBlock)view.FindName("HapticCaption")).Text);

        model.DpadTouchIncrement = 250;
        model.HapticOverride = 1.5;
        Realise(view);

        Assert.Equal("2.5 mm", ((TextBlock)view.FindName("IncrementCaption")).Text);
        Assert.Equal("150 % console setting", ((TextBlock)view.FindName("HapticCaption")).Text);
    });

    /// <summary>And the sliders keep the store's own ranges rather than a tidier pair.</summary>
    [Fact]
    public void TheSlidersKeepTheStoresRanges() => OnSta(() =>
    {
        var view = new ControllerSettingsView { DataContext = new ControllerSettingsViewModel() };
        Realise(view);

        var increment = (Slider)view.FindName("IncrementSlider");
        Assert.Equal(DpadTouchIncrementSetting.Minimum, increment.Minimum);
        Assert.Equal(DpadTouchIncrementSetting.Maximum, increment.Maximum);

        var haptic = (Slider)view.FindName("HapticSlider");
        Assert.Equal(HapticOverrideSetting.Minimum, haptic.Minimum);
        Assert.Equal(HapticOverrideSetting.Maximum, haptic.Maximum);
    });
}
