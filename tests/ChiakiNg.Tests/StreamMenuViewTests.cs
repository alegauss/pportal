using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP10: the menu as markup - that the two inversions really reach the screen.
///
/// The rules are asserted without a window in <see cref="StreamMenuTests"/>. What these add is the
/// half a view model cannot answer: whether a button whose lit state is the OPPOSITE of the
/// property it reads is actually bound to the opposite, which is the one mistake that looks
/// deliberate in both directions.
/// </summary>
public class StreamMenuViewTests
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
        element.Measure(new Size(1280, 720));
        element.Arrange(new Rect(0, 0, 1280, 720));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => OnSta(() => Assert.NotNull(new StreamMenuView()));

    /// <summary>
    /// The mic button on screen: lit for a live microphone, dark for a muted one, and disabled
    /// while the session is not connected.
    /// </summary>
    [Fact]
    public void TheMicButtonShowsTheOppositeOfMuted() => OnSta(() =>
    {
        var model = new StreamMenuViewModel { SessionActive = true, Connected = true };
        var view = new StreamMenuView { DataContext = model };
        Realise(view);

        var mic = (ToggleButton)view.FindName("MicButton");

        Assert.True(mic.IsChecked);
        Assert.True(mic.IsEnabled);

        model.Muted = true;
        Realise(view);
        Assert.False(mic.IsChecked);

        model.Connected = false;
        Realise(view);
        Assert.False(mic.IsEnabled);
    });

    /// <summary>
    /// The zoom slider appears with the mode and its label carries the offset, which is the pair
    /// most easily got right in the model and wrong on screen.
    /// </summary>
    [Fact]
    public void TheZoomSliderAndItsLabelFollowTheMode() => OnSta(() =>
    {
        var model = new StreamMenuViewModel();
        var view = new StreamMenuView { DataContext = model };
        Realise(view);

        var group = (FrameworkElement)view.FindName("ZoomFactorGroup");
        var caption = (TextBlock)view.FindName("ZoomCaption");

        Assert.Equal(Visibility.Collapsed, group.Visibility);

        model.VideoMode = StreamVideoMode.Zoom;
        model.ZoomFactor = 1.5;
        Realise(view);

        Assert.Equal(Visibility.Visible, group.Visibility);
        Assert.Equal("2.50 x", caption.Text);

        model.ZoomFactor = -1;
        Realise(view);
        Assert.Equal("No Black Bars", caption.Text);
    });

    /// <summary>The volume slider's range is the store's, and its label is the percentage.</summary>
    [Fact]
    public void TheVolumeSliderKeepsTheStoresRange() => OnSta(() =>
    {
        var model = new StreamMenuViewModel { Volume = 64 };
        var view = new StreamMenuView { DataContext = model };
        Realise(view);

        var slider = (Slider)view.FindName("VolumeSlider");

        Assert.Equal(0, slider.Minimum);
        Assert.Equal(128, slider.Maximum);
        Assert.Equal(64, slider.Value);
        Assert.Equal("50% Volume", ((TextBlock)view.FindName("VolumeCaption")).Text);
    });

    /// <summary>The Placebo button appears with the Custom preset and with no other.</summary>
    [Fact]
    public void ThePlaceboButtonAppearsOnlyForCustom() => OnSta(() =>
    {
        var model = new StreamMenuViewModel { VideoPreset = StreamVideoPreset.Default };
        var view = new StreamMenuView { DataContext = model };
        Realise(view);

        var placebo = (FrameworkElement)view.FindName("PlaceboSettingsButton");
        Assert.Equal(Visibility.Collapsed, placebo.Visibility);

        model.VideoPreset = StreamVideoPreset.Custom;
        Realise(view);
        Assert.Equal(Visibility.Visible, placebo.Visibility);
    });

    /// <summary>And the dropped-frames line stays away until there is something to report.</summary>
    [Fact]
    public void TheDroppedLineStaysAwayAtZero() => OnSta(() =>
    {
        var model = new StreamMenuViewModel { SessionActive = true, Connected = true };
        var view = new StreamMenuView { DataContext = model };
        Realise(view);

        var group = (FrameworkElement)view.FindName("DroppedFramesGroup");
        Assert.Equal(Visibility.Collapsed, group.Visibility);

        model.DroppedFrames = 4;
        Realise(view);

        Assert.Equal(Visibility.Visible, group.Visibility);
        Assert.Equal("4", ((TextBlock)view.FindName("DroppedFramesValue")).Text);
    });
}
