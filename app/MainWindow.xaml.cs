using System.Windows;

namespace ChiakiNg;

/// <summary>
/// The window that opens empty (PP1), and PP223's one way to put something in it.
///
/// Its code-behind stayed empty on purpose: PP37 filed the case that a screen ported into
/// code-behind can only be exercised by opening a window, and asked that the first thing here be a
/// binding to a view model rather than the first event handler. What is here is neither a screen
/// nor a handler - it is the host for one, and which screen goes in is somebody else's decision.
/// </summary>
public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts a screen in the window, and takes the placeholder away.
    ///
    /// Named for what it does rather than Show, which is <see cref="System.Windows.Window.Show"/>
    /// and means something else entirely.
    /// </summary>
    public void ShowScreen(object screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        Screen.Content = screen;
        Placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>Whether anything has been put in it. False is how this window starts.</summary>
    public bool HasScreen => Screen.Content is not null;

    /// <summary>
    /// PP224: says why there is no screen, WHERE THE PERSON IS LOOKING.
    ///
    /// A WinExe started from a shortcut has no console, and the one place a failure was written -
    /// Console.Error - is bound to standard output by this application's own redirect, so a run
    /// that found no pad printed its reason somewhere nobody was. The window is where somebody is.
    /// </summary>
    public void ShowMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Placeholder.Text = message;
        Placeholder.Visibility = Visibility.Visible;
    }

    /// <summary>What the window is currently saying, which is its title until told otherwise.</summary>
    public string Message => Placeholder.Text;
}
