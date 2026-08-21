using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Config tab, and the last of the settings screen's nine.
///
/// The About button's text is the one thing on this tab that is built rather than written: the
/// application's own name with -ng appended, exactly as the QML does it. Set here rather than in
/// the markup because the name comes from PP3's QtPaths, which is where the two clients agree
/// about what this application is called.
/// </summary>
public partial class ConfigSettingsView : UserControl
{
    public ConfigSettingsView()
    {
        InitializeComponent();
        AboutButton.Content = ConfigSettingsViewModel.AboutCaption(QtPaths.Application);
    }
}
