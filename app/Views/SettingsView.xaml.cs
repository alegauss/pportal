using System.Windows.Controls;
using System.Windows.Input;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP167: the settings dialog.
///
/// The two paging keys are wired here rather than bound, because they are the only keys on this
/// screen that mean anything to it: Page Up and Page Down move between tabs, and they stop at the
/// ends. Everything else belongs to whichever tab is on show.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsTabsViewModel model || Keyboard.Modifiers != ModifierKeys.None)
            return;

        switch (e.Key)
        {
            case Key.PageUp:
                model.PreviousTab();
                e.Handled = true;
                break;

            case Key.PageDown:
                model.NextTab();
                e.Handled = true;
                break;
        }
    }
}
