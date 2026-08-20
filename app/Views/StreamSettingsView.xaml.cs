using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Stream tab.
///
/// Every list is assigned once, in the constructor, before any DataContext arrives. That is the
/// General tab's rule - assigning ItemsSource resets SelectedIndex, so a list assigned after the
/// bindings resolved discards the stored choice - and here it is also why all twelve controls exist
/// rather than six that switch contents: re-assigning a list later left a combo with the right index
/// and a blank selection.
///
/// The four resolution combos get four different lists. Three distinct ones between them, because
/// the default marker sits on a different entry per row.
/// </summary>
public partial class StreamSettingsView : UserControl
{
    public StreamSettingsView()
    {
        InitializeComponent();

        ConsoleCombo.ItemsSource = StreamSettingsViewModel.ConsoleLabels;

        Ps4LocalResolutionCombo.ItemsSource = StreamResolution.LocalPs4.Labels;
        Ps4RemoteResolutionCombo.ItemsSource = StreamResolution.RemotePs4.Labels;
        Ps5LocalResolutionCombo.ItemsSource = StreamResolution.LocalPs5.Labels;
        Ps5RemoteResolutionCombo.ItemsSource = StreamResolution.RemotePs5.Labels;

        foreach (ComboBox fps in new[]
                 { Ps4LocalFpsCombo, Ps4RemoteFpsCombo, Ps5LocalFpsCombo, Ps5RemoteFpsCombo })
        {
            fps.ItemsSource = StreamFps.Labels;
        }

        foreach (Slider slider in new[]
                 {
                     Ps4LocalBitrateSlider, Ps4RemoteBitrateSlider,
                     Ps5LocalBitrateSlider, Ps5RemoteBitrateSlider,
                 })
        {
            slider.Minimum = StreamBitrate.MinimumMbps;
            slider.Maximum = StreamBitrate.MaximumMbps;
            slider.TickFrequency = StreamBitrate.StepMbps;
            slider.IsSnapToTickEnabled = true;
        }
    }
}
