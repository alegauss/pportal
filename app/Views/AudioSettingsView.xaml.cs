using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Audio and Wifi tab.
///
/// The five slider ranges come from the rules rather than the markup, because each range is part of
/// its rule: the buffer's floor of 1 is what lets a stored zero mean "default", and the volume's
/// ceiling of 128 is what its percentage label divides by.
///
/// There is no list handling here at all, which took three attempts to arrive at. The two device
/// lists are genuinely dynamic - re-enumerated whenever the tab becomes visible - so they cannot be
/// filled once like the other tabs' fixed lists. Assigning ItemsSource on a refresh resets
/// SelectedIndex to -1, and the two-way binding writes that back into the view model; reading the
/// index afterwards then reads the damage rather than the choice. The answer is that the view model
/// holds observable collections and refills them IN PLACE, so the markup binds once and no
/// assignment ever happens.
/// </summary>
public partial class AudioSettingsView : UserControl
{
    public AudioSettingsView()
    {
        InitializeComponent();

        Range(BufferSlider, AudioBuffer.MinimumSteps, AudioBuffer.MaximumSteps);
        Range(VolumeSlider, AudioVolumeSetting.Minimum, AudioVolumeSetting.Maximum);
        Range(WifiSlider, LossThresholds.Minimum, LossThresholds.Maximum);
        Range(PacketLossSlider, LossThresholds.Minimum, LossThresholds.Maximum);

        // The two suppression sliders share a range the QML states per control: 0 to 60 dB.
        Range(NoiseSlider, 0, 60);
        Range(EchoSlider, 0, 60);
    }

    private static void Range(Slider slider, int minimum, int maximum)
    {
        slider.Minimum = minimum;
        slider.Maximum = maximum;
        slider.TickFrequency = 1;
        slider.IsSnapToTickEnabled = true;
    }
}
