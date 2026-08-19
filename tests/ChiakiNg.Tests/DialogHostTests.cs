using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP19: the dialog host, and the three asymmetries a port would tidy away.
/// </summary>
public class DialogHostTests
{
    /// <summary>
    /// The back button rejects and closes. The action button accepts and does NOT close - PP141 is
    /// why: the registration dialog stays open on both outcomes, which only works because accepting
    /// leaves the host up and lets the screen decide.
    /// </summary>
    [Fact]
    public void BackClosesAndAcceptDoesNot()
    {
        var back = new DialogHostViewModel();
        back.Back();
        Assert.True(back.Rejected);
        Assert.True(back.Closed);
        Assert.Equal(DialogHostExit.Back, back.Exit);

        var accepted = new DialogHostViewModel();
        accepted.Accept();
        Assert.True(accepted.Accepted);
        Assert.False(accepted.Closed);
        Assert.False(accepted.Rejected);
        Assert.Equal(DialogHostExit.Accepted, accepted.Exit);
    }

    /// <summary>
    /// Escape is NOT the back button with a keyboard on it: back fires rejected and Escape does not.
    /// A port wiring one to the other runs reject callbacks the Qt client never runs, and nothing on
    /// screen distinguishes the two dismissals.
    /// </summary>
    [Fact]
    public void EscapeClosesWithoutRejecting()
    {
        var model = new DialogHostViewModel();
        model.Escape();

        Assert.True(model.Closed);
        Assert.False(model.Rejected);
        Assert.Equal(DialogHostExit.Escape, model.Exit);
    }

    /// <summary>The Menu key presses the action button THROUGH its enabled state.</summary>
    [Fact]
    public void TheMenuKeyRespectsTheButtonsRule()
    {
        var disabled = new DialogHostViewModel { ButtonEnabled = false };
        Assert.False(disabled.MenuKey());
        Assert.False(disabled.Accepted);
        Assert.Equal(DialogHostExit.None, disabled.Exit);

        var enabled = new DialogHostViewModel { ButtonEnabled = true };
        Assert.True(enabled.MenuKey());
        Assert.True(enabled.Accepted);
    }

    /// <summary>
    /// The focus restore is ONE-SHOT: activating restores the saved item and clears the slot, so a
    /// second activation falls back to the content's chain. This is the opposite convention to the
    /// confirm dialog's latch, which is never cleared - two screens, two answers to one problem.
    /// </summary>
    [Fact]
    public void TheFocusRestoreIsOneShot()
    {
        var model = new DialogHostViewModel();
        var saved = new object();
        var chainHead = new object();

        Assert.True(model.WillFallBackToTheFocusChain);

        model.Deactivating(saved);
        Assert.False(model.WillFallBackToTheFocusChain);

        Assert.Same(saved, model.Activated(chainHead));

        // Spent - the next activation takes the fallback.
        Assert.True(model.WillFallBackToTheFocusChain);
        Assert.Same(chainHead, model.Activated(chainHead));
    }

    /// <summary>A host that was never deactivated focuses the content, not nothing.</summary>
    [Fact]
    public void AFirstActivationFocusesTheContent()
    {
        var model = new DialogHostViewModel();
        var chainHead = new object();

        Assert.Same(chainHead, model.Activated(chainHead));

        // And with no content to focus either, the answer is null rather than an exception.
        Assert.Null(model.Activated(null));
    }

    /// <summary>Deactivating with nothing focused saves nothing, so the fallback still applies.</summary>
    [Fact]
    public void SavingNothingIsNotSaving()
    {
        var model = new DialogHostViewModel();
        model.Deactivating(null);

        Assert.True(model.WillFallBackToTheFocusChain);
    }

    /// <summary>Every rule above is still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (DialogHostSource.Locate() is null)
            return;

        string qml = File.ReadAllText(DialogHostSource.Locate()!);

        Assert.True(DialogHostSource.BackRejectsThenCloses(qml), "back rejects then closes");
        Assert.True(DialogHostSource.EscapeClosesWithoutRejecting(qml), "escape does not reject");
        Assert.True(DialogHostSource.AcceptDoesNotClose(qml), "accept does not close");
        Assert.True(DialogHostSource.TheMenuKeyRespectsTheButton(qml), "menu key");
        Assert.True(DialogHostSource.TheFocusRestoreIsOneShot(qml), "one-shot restore");
        Assert.True(DialogHostSource.TheFallbackIsTheContentChain(qml), "fallback is the chain");
    }

    /// <summary>The host loads, shows what the screen configured, and holds the screen.</summary>
    [Fact]
    public void TheHostShowsWhatTheScreenConfigured() => OnSta(() =>
    {
        var model = new DialogHostViewModel
        {
            Title = "Display Settings",
            Header = "* Defaults in () to right of value",
            ButtonText = "Create",
            ButtonEnabled = false,
        };

        var view = new DialogHostView { DataContext = model };
        var screen = new ConfirmDialogView();
        view.Content = screen;
        Realise(view);

        Assert.Equal("Display Settings", ((TextBlock)view.FindName("TitleLabel")).Text);
        Assert.StartsWith("* Defaults", ((TextBlock)view.FindName("HeaderLabel")).Text);

        var action = (Button)view.FindName("ActionButton");
        Assert.Equal("Create", action.Content);
        Assert.False(action.IsEnabled);
        Assert.Same(screen, view.Content);

        model.ButtonEnabled = true;
        Realise(view);
        Assert.True(action.IsEnabled);
    });

    /// <summary>Hiding the button is separate from disabling it - several screens open with it gone.</summary>
    [Fact]
    public void HidingTheButtonIsNotDisablingIt() => OnSta(() =>
    {
        var model = new DialogHostViewModel { ButtonVisible = false, ButtonEnabled = true };
        var view = new DialogHostView { DataContext = model };
        Realise(view);

        var action = (Button)view.FindName("ActionButton");
        Assert.Equal(Visibility.Collapsed, action.Visibility);
        Assert.True(action.IsEnabled);

        model.ButtonVisible = true;
        Realise(view);
        Assert.Equal(Visibility.Visible, action.Visibility);
    });

    /// <summary>
    /// The two dismissals through the view: the back button rejects, Escape does not. This is the
    /// finding taken the long way round, because the difference is invisible on screen.
    /// </summary>
    [Fact]
    public void TheTwoDismissalsDifferThroughTheView() => OnSta(() =>
    {
        var clicked = new DialogHostViewModel();
        var view = new DialogHostView { DataContext = clicked };
        Realise(view);

        ((Button)view.FindName("BackButton")).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(clicked.Rejected);
        Assert.True(clicked.Closed);

        // Escape goes through the view's own routing rather than a synthesised key event: raising
        // one needs a shown Window, and a shown Window leaks WPF state into every other STA test in
        // the assembly - measured at 46 unrelated failures and a suite three minutes slower.
        var escaped = new DialogHostViewModel();
        var escapedView = new DialogHostView { DataContext = escaped };
        Realise(escapedView);

        Assert.True(escapedView.HandleKey(Key.Escape));
        Assert.True(escaped.Closed);
        Assert.False(escaped.Rejected);
    });

    /// <summary>And the Menu key through the same routing, refused when the button is.</summary>
    [Fact]
    public void TheMenuKeyGoesThroughTheViewsRouting() => OnSta(() =>
    {
        var model = new DialogHostViewModel { ButtonEnabled = false };
        var view = new DialogHostView { DataContext = model };
        Realise(view);

        Assert.False(view.HandleKey(Key.Apps));
        Assert.False(model.Accepted);

        model.ButtonEnabled = true;
        Assert.True(view.HandleKey(Key.Apps));
        Assert.True(model.Accepted);

        // Anything else is not the host's business.
        Assert.False(view.HandleKey(Key.A));
    });

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
        element.Measure(new Size(900, 600));
        element.Arrange(new Rect(0, 0, 900, 600));
        element.UpdateLayout();
    }
}
