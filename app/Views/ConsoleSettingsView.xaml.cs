using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Consoles tab.
///
/// Nothing to fill and nothing to wire: both lists bind a collection the view model refills in
/// place, so there is no ItemsSource assignment to get wrong. Which is the point of PP159's rule
/// rather than an accident of this screen being simple.
/// </summary>
public partial class ConsoleSettingsView : UserControl
{
    public ConsoleSettingsView() => InitializeComponent();
}
