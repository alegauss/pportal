using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the General tab.
///
/// The combo contents are filled here rather than in the markup, and filled in the CONSTRUCTOR -
/// before any DataContext arrives. Setting ItemsSource resets SelectedIndex to -1, so a list
/// assigned after the bindings had resolved would silently discard every stored choice on the tab
/// and show the first row instead.
///
/// They come from the view model's own tables so the labels have one home: the order is the value,
/// and a list retyped in XAML is a second order to keep in step.
/// </summary>
public partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView()
    {
        InitializeComponent();

        DisconnectCombo.ItemsSource = ActionChoice.Disconnect.Labels;
        SuspendCombo.ItemsSource = ActionChoice.Suspend.Labels;
        AudioVideoCombo.ItemsSource = GeneralSettingsViewModel.AudioVideoLabels;

        foreach (ComboBox combo in new[] { Shortcut1Combo, Shortcut2Combo, Shortcut3Combo, Shortcut4Combo })
            combo.ItemsSource = GeneralSettingsViewModel.ShortcutLabels;
    }
}
