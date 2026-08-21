using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Remote tab. A constructor, by the rule PP13 set.
///
/// Nothing to fill: no combo on this tab, and the two buttons swap by visibility rather than by
/// one button changing its text - which is upstream's shape and is also the one that cannot show
/// "Clear PSN Token" over credentials that are half gone.
/// </summary>
public partial class RemoteSettingsView : UserControl
{
    public RemoteSettingsView() => InitializeComponent();
}
