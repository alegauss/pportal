using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Session;

namespace ChiakiNg.Views;

/// <summary>
/// PP13: the front door's code-behind, which was a constructor and nothing else.
///
/// PP37's argument in its final form: everything worth asserting about this screen is in
/// <see cref="Session.ConsoleListViewModel"/> and is asserted without a window. What is left here
/// is the call that loads the markup, and a rule for the next screen - if a method appears in one
/// of these files, it belongs in a view model instead.
///
/// PP600 ADDED THE ONE THING THAT CANNOT. A row's button has to say WHICH row it is about, and the
/// row is the button's own DataContext - a fact that exists only once the template has been
/// realised, so it cannot come from the view model and it cannot come from markup. The handler is
/// therefore three lines that read the row and raise an event, in the shape every other screen in
/// this host uses: the view says what happened and App.xaml.cs decides what answers it.
/// </summary>
public partial class ConsoleListView : UserControl
{
    public ConsoleListView() => InitializeComponent();

    /// <summary>Somebody asked to connect to a console, and which one.</summary>
    public event Action<ConsoleRow>? ConnectRequested;

    /// <summary>
    /// PP625: somebody asked to end the session this list is holding.
    ///
    /// No row, because there is only one: a console accepts one remote play session, so the way out
    /// is about the list rather than about a row - and a per-row Disconnect would ask which console
    /// the user meant when the answer is always the one that is running.
    /// </summary>
    public event Action? DisconnectRequested;

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConsoleRow row })
            ConnectRequested?.Invoke(row);
    }

    private void OnDisconnect(object sender, RoutedEventArgs e) => DisconnectRequested?.Invoke();
}
