using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP7: the login screen as markup - that it loads, and that the mode really does decide what is
/// on it.
///
/// The rules are asserted without a window in <see cref="PsnLoginViewModelTests"/>. What these add
/// is the half a view model cannot answer: whether three groups and a button that all switch on
/// one enum are actually wired to it. A visibility that never repaints is a screen stuck on the
/// browser path - which is exactly the path this port cannot yet show anything on.
/// </summary>
public class PsnLoginViewTests
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
        element.Measure(new Size(800, 600));
        element.Arrange(new Rect(0, 0, 800, 600));
        element.UpdateLayout();
    }

    [Fact]
    public void ItLoads() => OnSta(() => Assert.NotNull(new PsnLoginView()));

    /// <summary>
    /// The screen opens on the browser slot with no button, and falling back shows the paste form
    /// and grows one. Both of those are repaints driven by one enum, so a silent property here is
    /// a dialog with nothing on it.
    /// </summary>
    [Fact]
    public void FallingBackShowsThePasteFormAndItsButton() => OnSta(() =>
    {
        var model = new PsnLoginViewModel();
        var view = new PsnLoginView { DataContext = model };
        Realise(view);

        var browser = (FrameworkElement)view.FindName("BrowserHost");
        var redirect = (FrameworkElement)view.FindName("RedirectGroup");
        var button = (Button)view.FindName("SubmitButton");

        Assert.Equal(Visibility.Visible, browser.Visibility);
        Assert.Equal(Visibility.Collapsed, redirect.Visibility);
        Assert.Equal(Visibility.Collapsed, button.Visibility);

        model.FallBackToExternalBrowser();
        Realise(view);

        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        Assert.Equal(Visibility.Visible, redirect.Visibility);
        Assert.Equal(Visibility.Visible, button.Visibility);
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("RedirectField")).Text =
            PsnAuth.RedirectPage + "?code=ABC123";
        Realise(view);

        Assert.True(button.IsEnabled);
        Assert.Equal("ABC123", model.PastedCode());
    });

    /// <summary>
    /// The lookup form is the other browserless path, and it reaches the same button through a
    /// different field - which is the point of one rule with two arms rather than two buttons.
    /// </summary>
    [Fact]
    public void TheLookupFormReachesTheSameButton() => OnSta(() =>
    {
        var model = new PsnLoginViewModel { Mode = PsnLoginMode.Username };
        var view = new PsnLoginView { DataContext = model };
        Realise(view);

        var group = (FrameworkElement)view.FindName("UsernameGroup");
        var button = (Button)view.FindName("SubmitButton");

        Assert.Equal(Visibility.Visible, group.Visibility);
        Assert.Equal(Visibility.Visible, button.Visibility);
        Assert.False(button.IsEnabled);

        ((TextBox)view.FindName("UsernameField")).Text = "someone";
        Realise(view);

        Assert.Equal("someone", model.Username);
        Assert.True(button.IsEnabled);
    });

    /// <summary>
    /// And the failure asymmetry through the view: the paste path's button comes back and the
    /// error appears, where the same failure on the browser path leaves the button hidden.
    /// </summary>
    [Fact]
    public void AFailureShowsItselfAndReturnsThePastePathsButton() => OnSta(() =>
    {
        var model = new PsnLoginViewModel
        {
            Mode = PsnLoginMode.Redirect,
            RedirectUrl = PsnAuth.RedirectPage + "?code=ABC123",
            Submitting = true,
        };

        var view = new PsnLoginView { DataContext = model };
        Realise(view);

        var error = (TextBlock)view.FindName("ErrorLabel");
        var button = (Button)view.FindName("SubmitButton");

        Assert.Equal(Visibility.Collapsed, error.Visibility);
        Assert.False(button.IsEnabled);

        model.Fail("Retrieving PSN account ID failed");
        Realise(view);

        Assert.Equal(Visibility.Visible, error.Visibility);
        Assert.Equal("Retrieving PSN account ID failed", error.Text);
        Assert.True(button.IsEnabled);
    });
}
