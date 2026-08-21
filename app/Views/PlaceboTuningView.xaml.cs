using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP17: the renderer tuning screen.
///
/// Five combos, and the four scaler ones come from only TWO lists - upscaler and plane upscaler
/// share one, downscaler and plane downscaler the other. Filled from the same tables the store
/// reads, so the four combos cannot end up offering different orders of the same choices.
/// </summary>
public partial class PlaceboTuningView : UserControl
{
    public PlaceboTuningView()
    {
        InitializeComponent();

        Fill(UpscalerCombo, PlaceboScalers.Upscaler);
        Fill(PlaneUpscalerCombo, PlaceboScalers.PlaneUpscaler);
        Fill(DownscalerCombo, PlaceboScalers.Downscaler);
        Fill(PlaneDownscalerCombo, PlaceboScalers.PlaneDownscaler);
        Fill(DeinterlaceCombo, PlaceboDeinterlaceChoice.Algorithm);
    }

    private static void Fill(ComboBox combo, StoredChoice choice)
    {
        foreach (string label in choice.Labels)
            combo.Items.Add(label);
    }
}
