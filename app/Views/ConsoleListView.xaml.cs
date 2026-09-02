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

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConsoleRow row })
            ConnectRequested?.Invoke(row);
    }
}
