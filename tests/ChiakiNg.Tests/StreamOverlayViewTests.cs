using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP10's premise, asserted rather than argued: XAML really does draw over the video.
///
/// The task was filed as undecidable until PP9 chose a renderer. A child HWND would have made
/// every element here impossible - an HWND is not in WPF's visual tree and nothing composes over
/// it - and the overlay would have had to be drawn into the frame or into a layered window. PP9
/// chose D3DImage, so what these check is the consequence: a D3DImage is an ImageSource, an Image
/// takes one, and everything after it in the Grid draws on top.
///
/// What is NOT checked is a picture. Composition produces pixels on a screen, and this suite has
/// no screen; the claim asserted is the structural one, and the pixels are PP9's own read-back.
/// </summary>
public class StreamOverlayViewTests
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
    public void ItLoads() => OnSta(() => Assert.NotNull(new StreamOverlayView()));

    /// <summary>
    /// The renderer's own surface type is accepted as the video, which is PP10's whole answer:
    /// D3DImage is an ImageSource, so the video is a brush and the overlay is elements.
    /// </summary>
    [Fact]
    public void TheRenderersSurfaceIsAnOrdinaryImageSource() => OnSta(() =>
    {
        var model = new StreamOverlayViewModel { Video = new D3DImage() };
        var view = new StreamOverlayView { DataContext = model };
        Realise(view);

        var video = (Image)view.FindName("Video");

        Assert.IsType<D3DImage>(video.Source);
        Assert.Same(model.Video, video.Source);
    });

    /// <summary>
    /// And the overlay is after it in the same Grid, which is how WPF puts one thing over another.
    /// Asserted by index rather than by looking: a later child draws on top, and an overlay that
    /// was accidentally first would be invisible behind an opaque video with nothing to show for
    /// it in any other check.
    /// </summary>
    [Fact]
    public void TheOverlayComesAfterTheVideoInTheSameGrid() => OnSta(() =>
    {
        var view = new StreamOverlayView { DataContext = new StreamOverlayViewModel() };
        Realise(view);

        var video = (Image)view.FindName("Video");
        var stats = (FrameworkElement)view.FindName("Stats");
        var grid = (Grid)VisualTreeHelper.GetParent(video);

        Assert.Same(grid, VisualTreeHelper.GetParent(stats));
        Assert.True(grid.Children.IndexOf(stats) > grid.Children.IndexOf(video),
            "the overlay must be a later child than the video, or it draws underneath it");
    });

    /// <summary>
    /// The panel really repaints on each of its three reasons. One enum would have been enough for
    /// a screen; three independent booleans is what the QML has, and a visibility that never
    /// repaints is a stream stuck behind a black rectangle.
    /// </summary>
    [Fact]
    public void EachOfThePanelsThreeReasonsRepaintsIt() => OnSta(() =>
    {
        var model = new StreamOverlayViewModel { Loading = false };
        var view = new StreamOverlayView { DataContext = model };
        Realise(view);

        var panel = (FrameworkElement)view.FindName("LoadingPanel");
        Assert.Equal(Visibility.Collapsed, panel.Visibility);

        model.Loading = true;
        Realise(view);
        Assert.Equal(Visibility.Visible, panel.Visibility);

        model.Loading = false;
        model.Error = true;
        Realise(view);
        Assert.Equal(Visibility.Visible, panel.Visibility);

        model.Error = false;
        model.Disabled = AudioVideoDisabled.Video;
        Realise(view);
        Assert.Equal(Visibility.Visible, panel.Visibility);
    });

    /// <summary>
    /// The readouts reach the screen with their units applied - the frame age in milliseconds and
    /// the loss as a percentage, both converted between the value and the label.
    /// </summary>
    [Fact]
    public void TheReadoutsReachTheScreenConverted() => OnSta(() =>
    {
        var model = new StreamOverlayViewModel
        {
            SessionActive = true,
            MeasuredBitrate = 14.26,
            PendingFrameAgeSeconds = 0.0421,
            PacketLoss = 0.0314,
        };

        var view = new StreamOverlayView { DataContext = model };
        Realise(view);

        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("Stats")).Visibility);
        Assert.Equal("14.3", ((TextBlock)view.FindName("BitrateValue")).Text);
        Assert.Equal("42 ms", ((TextBlock)view.FindName("FrameAgeValue")).Text);
        Assert.Equal("3.1%", ((TextBlock)view.FindName("PacketLossValue")).Text);
    });

    /// <summary>And the readouts are gone with no session, rather than reading zero.</summary>
    [Fact]
    public void WithNoSessionThereAreNoReadouts() => OnSta(() =>
    {
        var view = new StreamOverlayView { DataContext = new StreamOverlayViewModel() };
        Realise(view);

        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("Stats")).Visibility);
    });
}
