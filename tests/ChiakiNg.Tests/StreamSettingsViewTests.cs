using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Stream tab as markup. Twelve controls of which six show, so the interest is what
/// happens when the console changes - and whether choosing a resolution really does move the
/// bitrate beside it.
/// </summary>
public class StreamSettingsViewTests
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
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoadsWithTheFixedListsFilled() => OnSta(() =>
    {
        var view = new StreamSettingsView();

        Assert.Equal(2, ((ComboBox)view.FindName("ConsoleCombo")).Items.Count);
        Assert.Equal(2, ((ComboBox)view.FindName("Ps4LocalFpsCombo")).Items.Count);
        Assert.Equal(2, ((ComboBox)view.FindName("Ps4RemoteFpsCombo")).Items.Count);

        var slider = (Slider)view.FindName("Ps4LocalBitrateSlider");
        Assert.Equal(StreamBitrate.MinimumMbps, slider.Minimum);
        Assert.Equal(StreamBitrate.MaximumMbps, slider.Maximum);
    });

    /// <summary>A fresh store shows each row's own default, and the two consoles differ.</summary>
    [Fact]
    public void EachConsoleShowsItsOwnDefaults() => OnSta(() =>
    {
        var model = new StreamSettingsViewModel(new FakePreferences());
        var view = new StreamSettingsView { DataContext = model };
        Realise(view);

        // PS4: 720p both ways.
        Assert.Equal("720p (Default)", ((ComboBox)view.FindName("Ps4LocalResolutionCombo")).SelectedItem);
        Assert.Equal("720p (Default)", ((ComboBox)view.FindName("Ps4RemoteResolutionCombo")).SelectedItem);

        ((ComboBox)view.FindName("ConsoleCombo")).SelectedIndex = 1;
        Realise(view);

        // PS5 has its own two combos, and its two defaults are the pair that differ.
        Assert.Equal("1080p (Default)", ((ComboBox)view.FindName("Ps5LocalResolutionCombo")).SelectedItem);
        Assert.Equal("720p (Default)", ((ComboBox)view.FindName("Ps5RemoteResolutionCombo")).SelectedItem);
        Assert.Equal("1080p", model.ResolutionStored(StreamConsole.Ps5, StreamNetwork.Local));
        Assert.Equal("720p", model.ResolutionStored(StreamConsole.Ps5, StreamNetwork.Remote));

        // And the PS4 pair is hidden rather than repurposed.
        Assert.Equal(
            Visibility.Collapsed, ((ComboBox)view.FindName("Ps4LocalResolutionCombo")).Visibility);
        Assert.Equal(
            Visibility.Visible, ((ComboBox)view.FindName("Ps5LocalResolutionCombo")).Visibility);
    });

    /// <summary>
    /// Switching console swaps which four of the twelve the six controls address, and the stored
    /// values of the other console are untouched by the trip.
    /// </summary>
    [Fact]
    public void SwitchingConsoleSwapsTheRowsWithoutLosingThem() => OnSta(() =>
    {
        var model = new StreamSettingsViewModel(new FakePreferences());
        var view = new StreamSettingsView { DataContext = model };
        Realise(view);

        ((ComboBox)view.FindName("Ps4LocalResolutionCombo")).SelectedIndex = 0;   // PS4 local 360p
        Realise(view);
        Assert.Equal("360p", model.ResolutionStored(StreamConsole.Ps4, StreamNetwork.Local));

        var console = (ComboBox)view.FindName("ConsoleCombo");
        console.SelectedIndex = 1;
        Realise(view);
        console.SelectedIndex = 0;
        Realise(view);

        Assert.Equal("360p", model.ResolutionStored(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal("1080p", model.ResolutionStored(StreamConsole.Ps5, StreamNetwork.Local));
    });

    /// <summary>
    /// The finding through the view: choosing a resolution moves the bitrate slider beside it to
    /// that resolution's default, because the choice zeroed the stored value.
    /// </summary>
    [Fact]
    public void ChoosingAResolutionMovesTheBitrateSlider() => OnSta(() =>
    {
        var model = new StreamSettingsViewModel(new FakePreferences());
        var view = new StreamSettingsView { DataContext = model };
        Realise(view);

        var slider = (Slider)view.FindName("Ps4LocalBitrateSlider");
        var resolution = (ComboBox)view.FindName("Ps4LocalResolutionCombo");

        // 720p by default, so 10 Mbps.
        Assert.Equal(10, slider.Value);

        // Drag it somewhere of the user's own choosing.
        slider.Value = 40;
        Realise(view);
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));

        // Now change the resolution: the drag is discarded and the new default takes over.
        resolution.SelectedIndex = 0;   // 360p, whose default is 2
        Realise(view);

        Assert.Equal(0u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(2, slider.Value);
        Assert.Contains("2 Mbps", ((TextBlock)view.FindName("Ps4LocalBitrateCaption")).Text);
    });

    /// <summary>And it moves only that row's slider - the one beside it stays where it was.</summary>
    [Fact]
    public void TheOtherColumnsSliderDoesNotMove() => OnSta(() =>
    {
        var model = new StreamSettingsViewModel(new FakePreferences());
        var view = new StreamSettingsView { DataContext = model };
        Realise(view);

        ((Slider)view.FindName("Ps4RemoteBitrateSlider")).Value = 25;
        Realise(view);

        ((ComboBox)view.FindName("Ps4LocalResolutionCombo")).SelectedIndex = 0;
        Realise(view);

        Assert.Equal(25, ((Slider)view.FindName("Ps4RemoteBitrateSlider")).Value);
        Assert.Equal(25000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Remote));
    });

    /// <summary>The frame rate reaches the store as 30 or 60, never as the index.</summary>
    [Fact]
    public void TheFrameRateReachesTheStoreAsARate() => OnSta(() =>
    {
        var model = new StreamSettingsViewModel(new FakePreferences());
        var view = new StreamSettingsView { DataContext = model };
        Realise(view);

        var fps = (ComboBox)view.FindName("Ps4LocalFpsCombo");
        Assert.Equal(1, fps.SelectedIndex);
        Assert.Equal(60, model.Rate(StreamConsole.Ps4, StreamNetwork.Local));

        fps.SelectedIndex = 0;
        Realise(view);

        Assert.Equal(30, model.Rate(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.NotEqual(0, model.Rate(StreamConsole.Ps4, StreamNetwork.Local));
    });
}

