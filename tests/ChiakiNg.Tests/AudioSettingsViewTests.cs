using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Audio tab as markup - that the five slider ranges come from the rules, that the device
/// lists survive a re-enumeration, and that the build-time gate hides rather than disables.
/// </summary>
public class AudioSettingsViewTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
    }

    /// <summary>
    /// Each range is part of its rule: the buffer's floor of 1 is what lets a stored zero mean
    /// "default", and the volume's ceiling of 128 is what its percentage divides by.
    /// </summary>
    [Fact]
    public void TheSliderRangesComeFromTheRules() => Apartment.Run(() =>
    {
        var view = new AudioSettingsView();

        var buffer = (Slider)view.FindName("BufferSlider");
        Assert.Equal(AudioBuffer.MinimumSteps, buffer.Minimum);
        Assert.Equal(AudioBuffer.MaximumSteps, buffer.Maximum);
        Assert.True(buffer.Minimum > 0, "zero has to be unreachable for it to mean default");

        var volume = (Slider)view.FindName("VolumeSlider");
        Assert.Equal(AudioVolumeSetting.Minimum, volume.Minimum);
        Assert.Equal(AudioVolumeSetting.Maximum, volume.Maximum);

        foreach (string name in new[] { "WifiSlider", "PacketLossSlider" })
        {
            var percent = (Slider)view.FindName(name);
            Assert.Equal(LossThresholds.Minimum, percent.Minimum);
            Assert.Equal(LossThresholds.Maximum, percent.Maximum);
        }
    });

    /// <summary>A fresh store shows the defaults, captions included.</summary>
    [Fact]
    public void AFreshStoreShowsTheDefaults() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(new FakePreferences());
        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        Assert.Equal(5, ((Slider)view.FindName("BufferSlider")).Value);
        Assert.Equal("50 ms", ((TextBlock)view.FindName("BufferCaption")).Text);
        Assert.Equal(128, ((Slider)view.FindName("VolumeSlider")).Value);
        Assert.Equal("100% volume", ((TextBlock)view.FindName("VolumeCaption")).Text);
        Assert.Equal(5, ((Slider)view.FindName("PacketLossSlider")).Value);
        Assert.Equal("5% packet loss", ((TextBlock)view.FindName("PacketLossCaption")).Text);
    });

    /// <summary>
    /// The buffer's two units through the view: the slider is in steps and the store is in frames,
    /// so a drag to 3 stores 5760 and prints 30 ms.
    /// </summary>
    [Fact]
    public void TheBufferSliderIsInStepsAndTheStoreInFrames() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(new FakePreferences());
        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        ((Slider)view.FindName("BufferSlider")).Value = 3;
        Realise(view);

        Assert.Equal(3, model.BufferSteps);
        Assert.Equal(5760u, model.BufferFrames);
        Assert.Equal("30 ms", ((TextBlock)view.FindName("BufferCaption")).Text);
    });

    /// <summary>
    /// And the packet-loss slider is a percent on screen and a fraction in the store - the one of the
    /// two percent sliders that converts.
    /// </summary>
    [Fact]
    public void OnlyOneOfTheTwoPercentSlidersConverts() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(new FakePreferences());
        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        ((Slider)view.FindName("PacketLossSlider")).Value = 12;
        ((Slider)view.FindName("WifiSlider")).Value = 12;
        Realise(view);

        Assert.Equal(12, model.PacketLossPercent);
        Assert.Equal(0.12, model.PacketLossStored, 10);

        // The one beside it stores the percent itself.
        Assert.Equal(12, model.WifiDroppedPercent);
    });

    /// <summary>The device lists are filled, and the stored choice survives the fill.</summary>
    [Fact]
    public void TheStoredDeviceSurvivesTheFill() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/audio_out_device", "Headset"),
            ["Speakers", "Headset"],
            ["Microphone"]);

        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        var output = (ComboBox)view.FindName("OutputCombo");
        Assert.Equal(3, output.Items.Count);       // Auto, Speakers, Headset
        Assert.Equal(2, output.SelectedIndex);
        Assert.Equal("Headset", output.SelectedItem);

        Assert.Equal(2, ((ComboBox)view.FindName("InputCombo")).Items.Count);
    });

    /// <summary>
    /// A re-enumeration refills the lists and keeps the selection showing - which is the case that
    /// binding ItemsSource got wrong on the Stream tab.
    /// </summary>
    [Fact]
    public void ARefreshRefillsTheListAndKeepsTheSelectionVisible() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/audio_out_device", "Headset"),
            ["Speakers", "Headset"],
            []);

        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        var output = (ComboBox)view.FindName("OutputCombo");
        Assert.Equal("Headset", output.SelectedItem);

        model.BecameVisible(["Speakers", "Headset", "Monitor"], ["Microphone"]);
        Realise(view);

        Assert.Equal(4, output.Items.Count);
        Assert.Equal("Headset", output.SelectedItem);
        Assert.Equal("Headset", model.OutputStored);
        Assert.Equal(2, ((ComboBox)view.FindName("InputCombo")).Items.Count);
    });

    /// <summary>
    /// And a device that went away falls back to the first entry ON SCREEN as well as in the model -
    /// the combo shows "Auto" rather than going blank.
    /// </summary>
    [Fact]
    public void ADeviceThatWentAwayShowsAsAuto() => Apartment.Run(() =>
    {
        var model = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/audio_out_device", "Headset"),
            ["Speakers", "Headset"],
            []);

        var view = new AudioSettingsView { DataContext = model };
        Realise(view);

        model.BecameVisible(["Speakers"], []);
        Realise(view);

        var output = (ComboBox)view.FindName("OutputCombo");
        Assert.Equal(0, output.SelectedIndex);
        Assert.Equal(AudioSettingsViewModel.AutoLabel, output.SelectedItem);
        Assert.Equal("", model.OutputStored);
    });

    /// <summary>
    /// The build-time gate HIDES the speech controls rather than disabling them - a control that
    /// cannot work is better absent than greyed out.
    /// </summary>
    [Fact]
    public void TheBuildGateHidesRatherThanDisables() => Apartment.Run(() =>
    {
        var without = new AudioSettingsViewModel(new FakePreferences(), speechAvailable: false);
        var view = new AudioSettingsView { DataContext = without };
        Realise(view);

        var group = (FrameworkElement)view.FindName("SpeechGroup");
        Assert.Equal(Visibility.Collapsed, group.Visibility);

        var with = new AudioSettingsViewModel(new FakePreferences(), speechAvailable: true);
        var enabled = new AudioSettingsView { DataContext = with };
        Realise(enabled);

        Assert.Equal(Visibility.Visible, ((FrameworkElement)enabled.FindName("SpeechGroup")).Visibility);

        // The two sliders need the checkbox as well as the build.
        var suppression = (FrameworkElement)enabled.FindName("SuppressionGroup");
        Assert.Equal(Visibility.Collapsed, suppression.Visibility);

        ((CheckBox)enabled.FindName("SpeechProcessingBox")).IsChecked = true;
        Realise(enabled);
        Assert.Equal(Visibility.Visible, suppression.Visibility);
    });
}
