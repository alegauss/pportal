using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP15: the token screen as markup - that the log's one signal really reaches the button.
/// </summary>
public class PsnTokenViewTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => Apartment.Run(() => Assert.NotNull(new PsnTokenView()));

    /// <summary>The heading follows the one property that changes it.</summary>
    [Fact]
    public void TheHeadingRepaints() => Apartment.Run(() =>
    {
        var model = new PsnTokenViewModel();
        var view = new PsnTokenView { DataContext = model };
        Realise(view);

        var title = (TextBlock)view.FindName("Title");
        Assert.Equal(PsnTokenViewModel.SetupTitle, title.Text);

        model.Expired = true;
        Realise(view);
        Assert.Equal(PsnTokenViewModel.ExpiredTitle, title.Text);
    });

    /// <summary>
    /// The log appears on Setup and its button changes when the callback says done. That change is
    /// the only thing on screen that marks the end, so a silent binding here is a modal a user
    /// cannot tell has finished.
    /// </summary>
    [Fact]
    public void TheLogsButtonIsTheOnlySignItFinished() => Apartment.Run(() =>
    {
        var model = new PsnTokenViewModel { RedirectUrl = PsnAuth.RedirectPage + "?code=A" };
        var view = new PsnTokenView { DataContext = model };
        Realise(view);

        var overlay = (FrameworkElement)view.FindName("LogOverlay");
        var button = (Button)view.FindName("LogButton");
        var area = (TextBlock)view.FindName("LogArea");

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);

        model.Submit();
        Realise(view);

        Assert.Equal(Visibility.Visible, overlay.Visibility);
        Assert.Equal("Cancel", button.Content);

        model.Report("[I] working", ok: false, done: false);
        Realise(view);
        Assert.Equal("[I] working\n", area.Text);
        Assert.Equal("Cancel", button.Content);

        model.Report("[I] PSN Remote Connection Tokens Generated.", ok: true, done: true);
        Realise(view);
        Assert.Equal("Close", button.Content);
    });

    /// <summary>
    /// The Setup button follows the URL and does NOT lock while the log runs, which is this
    /// screen's difference from the login screen and is asserted through the markup as well.
    /// </summary>
    [Fact]
    public void TheSetupButtonStaysLiveWhileTheLogRuns() => Apartment.Run(() =>
    {
        var model = new PsnTokenViewModel();
        var view = new PsnTokenView { DataContext = model };
        Realise(view);

        var button = (Button)view.FindName("SetupButton");
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("RedirectField")).Text = PsnAuth.RedirectPage + "?code=A";
        Realise(view);
        Assert.True(button.IsEnabled);

        model.Submit();
        Realise(view);

        Assert.True(button.IsEnabled);
    });

    /// <summary>And this screen hosts the same browser control the login screen does.</summary>
    [Fact]
    public void ItHostsTheSameBrowserControl() => Apartment.Run(() =>
    {
        var view = new PsnTokenView { DataContext = new PsnTokenViewModel() };
        Realise(view);

        var host = (Border)view.FindName("BrowserHost");
        Assert.IsType<PsnBrowserPanel>(host.Child);
    });
}
