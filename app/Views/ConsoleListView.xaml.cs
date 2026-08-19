using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP13: the front door's code-behind, which is a constructor and nothing else.
///
/// PP37's argument in its final form: everything worth asserting about this screen is in
/// <see cref="Session.ConsoleListViewModel"/> and is asserted without a window. What is left here
/// is the call that loads the markup, and a rule for the next screen - if a method appears in one
/// of these files, it belongs in a view model instead.
/// </summary>
public partial class ConsoleListView : UserControl
{
    public ConsoleListView() => InitializeComponent();
}
