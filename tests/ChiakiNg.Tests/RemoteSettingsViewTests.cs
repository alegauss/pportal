using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;
using ChiakiNg.Views;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Remote tab as markup - that the two buttons really swap rather than both appearing.
/// </summary>
public class RemoteSettingsViewTests
{
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => Apartment.Run(() => Assert.NotNull(new RemoteSettingsView()));

    /// <summary>
    /// One button at a time, and the swap happens on the fourth credential rather than the first.
    /// Asserted through the view because two visibilities driven by one condition is exactly the
    /// pair a port leaves half-bound.
    /// </summary>
    [Fact]
    public void OnlyOneOfTheTwoButtonsIsEverOnScreen() => Apartment.Run(() =>
    {
        var model = new RemoteSettingsViewModel();
        var view = new RemoteSettingsView { DataContext = model };
        Realise(view);

        var login = (FrameworkElement)view.FindName("LoginButton");
        var clear = (FrameworkElement)view.FindName("ClearTokenButton");

        Assert.Equal(Visibility.Visible, login.Visibility);
        Assert.Equal(Visibility.Collapsed, clear.Visibility);

        model.RefreshToken = "r";
        model.AuthToken = "a";
        model.Expiry = "2026-08-21";
        Realise(view);

        // Three of four is still not logged in.
        Assert.Equal(Visibility.Visible, login.Visibility);
        Assert.Equal(Visibility.Collapsed, clear.Visibility);

        model.AccountId = "id";
        Realise(view);

        Assert.Equal(Visibility.Collapsed, login.Visibility);
        Assert.Equal(Visibility.Visible, clear.Visibility);

        model.ClearTokens();
        Realise(view);

        Assert.Equal(Visibility.Visible, login.Visibility);
        Assert.Equal(Visibility.Collapsed, clear.Visibility);
    });

    /// <summary>The two sliders keep their own ranges and their own words.</summary>
    [Fact]
    public void TheSlidersKeepTheirRangesAndWords() => Apartment.Run(() =>
    {
        var model = new RemoteSettingsViewModel();
        var view = new RemoteSettingsView { DataContext = model };
        Realise(view);

        var count = (Slider)view.FindName("CountSlider");
        var sockets = (Slider)view.FindName("SocketSlider");

        Assert.Equal(PortGuessing.CountMaximum, count.Maximum);
        Assert.Equal(PortGuessing.SocketMaximum, sockets.Maximum);

        Assert.Equal("75 guesses", ((TextBlock)view.FindName("CountCaption")).Text);
        Assert.Equal("250 sockets", ((TextBlock)view.FindName("SocketCaption")).Text);

        model.PortGuessCount = 12;
        Realise(view);
        Assert.Equal("12 guesses", ((TextBlock)view.FindName("CountCaption")).Text);
    });
}
