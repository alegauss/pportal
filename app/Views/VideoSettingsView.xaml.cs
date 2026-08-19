using System.Windows;
using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Video tab.
///
/// The window-type list is fixed and filled in the constructor for the reason the General tab's
/// were - setting ItemsSource resets SelectedIndex, so a list assigned after the bindings resolved
/// discards the stored choice.
///
/// The decoder list is NOT fixed: availableDecoders() is built from what ffmpeg reports at runtime,
/// so it comes off the view model when one arrives. Same hazard, later - hence the DataContextChanged
/// handler rather than a binding, so the index is restored explicitly after the fill.
/// </summary>
public partial class VideoSettingsView : UserControl
{
    public VideoSettingsView()
    {
        InitializeComponent();

        WindowTypeCombo.ItemsSource = WindowTypeChoice.Window.Labels;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not VideoSettingsViewModel model)
            return;

        // Fill, then put the index back. ItemsSource clears SelectedIndex to -1 and the binding
        // does not re-push on its own, so the chosen decoder would be lost on every tab open.
        DecoderCombo.ItemsSource = model.AvailableDecoders;
        DecoderCombo.SelectedIndex = model.DecoderIndex;
    }
}
