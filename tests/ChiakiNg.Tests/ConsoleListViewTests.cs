using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP13: the screen itself - that its markup loads and its bindings resolve.
///
/// A XAML file's correctness is checkable and it is worth checking, because the failures are
/// loud in a way the compiler is not: a mistyped binding path is not a build error, it is a
/// control that silently shows nothing. Instantiating the view and letting WPF resolve the
/// bindings against a real view model is what turns that into a test.
///
/// What is NOT asserted here is what the screen looks like. That is the part a test could only
/// photograph, and it is why the rules live in ConsoleList where they can be asserted properly.
/// </summary>
public class ConsoleListViewTests
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

    /// <summary>
    /// Forces a layout pass so bindings actually evaluate. Without it the control is constructed
    /// and nothing is bound, and the test would pass for a view whose paths are all wrong.
    /// </summary>
    private static void Realise(FrameworkElement element)
    {
        element.Measure(new Size(800, 600));
        element.Arrange(new Rect(0, 0, 800, 600));
        element.UpdateLayout();
    }

    private static DiscoveredConsole Found(string mac, string nick, bool ps5 = true)
        => new("10.0.0.5", "1.0", "00030010", nick, ps5 ? "PS5" : "PS4", mac, null, null,
            DiscoveryHostState.Ready, 9295);

    [Fact]
    public void TheMarkupLoads() => OnSta(() =>
    {
        var view = new ConsoleListView();
        Assert.NotNull(view);
    });

    /// <summary>
    /// The rows binding reaches the view model. A mistyped path here is not a build error - it is
    /// an empty list on a network full of consoles.
    /// </summary>
    [Fact]
    public void TheRowsBindingReachesTheViewModel() => OnSta(() =>
    {
        var model = new ConsoleListViewModel();
        model.Refresh([Found("AA", "Living room")], [], [],
            new HashSet<string>(), new HashSet<string>());

        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        var items = (ItemsControl)view.FindName("Rows");
        Assert.Same(model.Rows, items.ItemsSource);
        Assert.Single(items.Items);
    });

    /// <summary>
    /// The empty message shows when nothing is visible - and "nothing visible" is not "no rows".
    /// A list of nothing but hidden consoles shows nothing, and a screen that decided by count
    /// would leave a blank panel where the message belongs.
    /// </summary>
    [Fact]
    public void TheEmptyMessageFollowsVisibilityAndNotCount() => OnSta(() =>
    {
        var model = new ConsoleListViewModel();
        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        var message = (TextBlock)view.FindName("EmptyMessage");
        Assert.Equal(Visibility.Visible, message.Visibility);

        // One console, hidden: a row exists and nothing is on screen, so the message stays.
        model.Refresh([Found("AA", "Bedroom")], [], [],
            new HashSet<string> { "AA" }, new HashSet<string>());
        Realise(view);

        Assert.Single(model.Rows);
        Assert.False(model.HasVisibleRows);
        Assert.Equal(Visibility.Visible, message.Visibility);

        // And it goes away once something is actually shown.
        model.Refresh([Found("AA", "Bedroom")], [], [],
            new HashSet<string>(), new HashSet<string>());
        Realise(view);

        Assert.True(model.HasVisibleRows);
        Assert.Equal(Visibility.Collapsed, message.Visibility);
    });

    /// <summary>
    /// The view model's own answer, without the view - because HasVisibleRows is the thing the
    /// screen decides on and it should be assertable on its own.
    /// </summary>
    [Fact]
    public void HasVisibleRowsIsAboutDisplayNotCount()
    {
        var model = new ConsoleListViewModel();
        Assert.False(model.HasVisibleRows);

        model.Refresh([Found("AA", "Bedroom")], [], [],
            new HashSet<string> { "AA" }, new HashSet<string>());

        Assert.NotEmpty(model.Rows);
        Assert.False(model.HasVisibleRows);
    }
}
