using System.Windows.Controls;

namespace ChiakiNg.Views;

/// <summary>
/// PP15: the token dialog. A constructor, by the rule PP13 set.
///
/// It hosts the same <see cref="PsnBrowserPanel"/> the login screen does. One browser control for
/// both screens, because upstream's two dialogs create the same WebEngineView with the same
/// profile and differ only in what they do with the code that comes back.
/// </summary>
public partial class PsnTokenView : UserControl
{
    public PsnTokenView() => InitializeComponent();
}
