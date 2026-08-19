using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP19: the display settings dialog.
///
/// The four lists and the two slider ranges are set in the constructor, before any DataContext, for
/// the reason the General tab's were: assigning ItemsSource resets SelectedIndex, so a list filled
/// after the bindings resolved would show Auto for a stored choice.
///
/// The ranges come from <see cref="DisplayTarget"/> rather than the markup because they are part of
/// the rule - a floor of 10 is what lets zero mean Auto rather than a peak of zero nits.
/// </summary>
public partial class DisplaySettingsView : UserControl
{
    public DisplaySettingsView()
    {
        InitializeComponent();

        PrimariesCombo.ItemsSource = DisplaySettingsViewModel.PrimaryLabels;
        TransferCombo.ItemsSource = DisplaySettingsViewModel.TransferLabels;
        PeakCombo.ItemsSource = DisplayTarget.Peak.Labels;
        ContrastCombo.ItemsSource = DisplayTarget.Contrast.Labels;

        Apply(PeakSlider, DisplayTarget.Peak);
        Apply(ContrastSlider, DisplayTarget.Contrast);
    }

    private static void Apply(Slider slider, DisplayTarget target)
    {
        slider.Minimum = target.Minimum;
        slider.Maximum = target.Maximum;
        slider.TickFrequency = target.Step;
        slider.IsSnapToTickEnabled = true;
    }
}
