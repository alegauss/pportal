using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Keys tab.
///
/// Nothing to fill and nothing to wire: the grid binds a collection the view model refills in place,
/// so there is no ItemsSource assignment to get wrong. Three tabs paid for that rule; this one just
/// uses it.
/// </summary>
public partial class KeySettingsView : UserControl
{
    public KeySettingsView() => InitializeComponent();
}
