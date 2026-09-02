using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Winwright.InApp;
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
    public void TheMarkupLoads() => Apartment.Run(() =>
    {
        var view = new ConsoleListView();
        Assert.NotNull(view);
    });

    /// <summary>
    /// The rows binding reaches the view model. A mistyped path here is not a build error - it is
    /// an empty list on a network full of consoles.
    /// </summary>
    [Fact]
    public void TheRowsBindingReachesTheViewModel() => Apartment.Run(() =>
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
    public void TheEmptyMessageFollowsVisibilityAndNotCount() => Apartment.Run(() =>
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
    /// PP600: the row's button says WHICH row, which is the one thing markup cannot.
    ///
    /// The row is the button's own DataContext and exists only once the template has been realised,
    /// so the handler is three lines of code-behind - the exception PP13's rule for these files now
    /// carries. What is asserted is the whole path: a realised template, a button found inside it,
    /// a click, and the row that comes back out.
    /// </summary>
    [Fact]
    public void ClickingARowsButtonSaysWhichRow() => Apartment.Run(() =>
    {
        var model = new ConsoleListViewModel();
        model.Refresh([Found("0011223344556677", "Living room")], [], [],
            new HashSet<string>(), new HashSet<string> { "0011223344556677" });

        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        ConsoleRow? asked = null;
        view.ConnectRequested += row => asked = row;

        Button button = FirstButton(view);
        Assert.True(button.IsEnabled, "a registered console on the network offers the action");

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal("Living room", asked?.Name);
    });

    /// <summary>
    /// PP600: and the button is disabled for a console that cannot be connected to, through the
    /// binding rather than through a trigger written in markup.
    /// </summary>
    [Fact]
    public void TheButtonFollowsTheRowsOwnRule() => Apartment.Run(() =>
    {
        var model = new ConsoleListViewModel();
        model.Refresh([Found("0011223344556677", "Living room")], [], [],
            new HashSet<string>(), new HashSet<string>());

        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        Assert.False(FirstButton(view).IsEnabled, "an unregistered console offers no connect");
    });

    /// <summary>
    /// PP626: the row's other two buttons are there, and each says what its rule says.
    ///
    /// A discovered console is awake, so Wake is offered and disabled; it is not registered, so its
    /// removal is Hide. Both are read off the realised template, because the label is a binding and
    /// the enabled state is another one - the two things markup gets wrong silently.
    /// </summary>
    [Fact]
    public void TheWakeAndTheRemovalFollowTheRowsRules() => Apartment.Run(() =>
    {
        var model = new ConsoleListViewModel();
        model.Refresh([Found("0011223344556677", "Living room")], [], [],
            new HashSet<string>(), new HashSet<string>());

        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        Assert.False(FirstButton(view, "Wake").IsEnabled, "a discovered console is already awake");

        // Discovered and not registered: hidden, because deleting it would not remove it.
        Button removal = FirstButton(view, "Hide");
        Assert.True(removal.IsEnabled);
    });

    /// <summary>
    /// PP626: and the silent branch draws a button that does nothing.
    ///
    /// A discovered console that IS registered offers neither removal. PP13 records that the entry
    /// is there and does nothing at all, so the label is a word and the button is live - a control
    /// that vanished or greyed out would be this port filling in the branch the client leaves
    /// empty, and the cost of filling it in is somebody's registration.
    /// </summary>
    [Fact]
    public void TheRemovalThatDoesNothingIsStillDrawn() => Apartment.Run(() =>
    {
        var model = new ConsoleListViewModel();
        model.Refresh([Found("0011223344556677", "Living room")], [], [],
            new HashSet<string>(), new HashSet<string> { "0011223344556677" });

        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        Button removal = FirstButton(view, "Remove");
        Assert.True(removal.IsEnabled);

        ConsoleRow? asked = null;
        view.RemoveRequested += row => asked = row;
        removal.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        // It reaches the model, which is what refuses it - not the markup, which cannot explain.
        Assert.Equal("Living room", asked?.Name);
    });

    /// <summary>
    /// PP600: what the last attempt said, bound where the person is looking - PP224's rule.
    /// </summary>
    [Fact]
    public void TheStatusBindingReachesTheViewModel() => Apartment.Run(() =>
    {
        var model = new ConsoleListViewModel();
        var view = new ConsoleListView { DataContext = model };
        Realise(view);

        model.Connect(new ConsoleRow("Bedroom", "", false, false, false, true));
        Realise(view);

        Assert.Equal(model.Status, ((TextBlock)view.FindName("ConnectStatus")).Text);
        Assert.NotEqual("", model.Status);
    });

    /// <summary>
    /// One of the first row's buttons, by what it says.
    ///
    /// PP626: by LABEL and not by position. The row grew a Wake and a Remove beside the Connect,
    /// and the three are docked right - so the first in visual-tree order is not the first one a
    /// reader would name, and a test that took it was asserting about whichever button the markup
    /// happened to declare first.
    ///
    /// x:Name is no use here either: a name inside a DataTemplate lives in the template's own
    /// namescope, not the view's, so FindName on the control does not reach it.
    /// </summary>
    private static Button FirstButton(ConsoleListView view, string says = "Connect")
    {
        var items = (ItemsControl)view.FindName("Rows");
        DependencyObject container = items.ItemContainerGenerator.ContainerFromIndex(0);

        Assert.NotNull(container);

        return Descendants(container).OfType<Button>()
            .First(one => string.Equals(one.Content as string, says, StringComparison.Ordinal));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (DependencyObject deeper in Descendants(child))
                yield return deeper;
        }
    }

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
