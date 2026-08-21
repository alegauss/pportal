using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP17: the colour mapping screen.
///
/// Three combos to fill, and they are filled from the SAME <see cref="StoredChoice"/> tables the
/// store reads its words out of. That is the point of filling them here rather than listing the
/// labels in the markup: four of the twenty-seven labels are not their stored words, so a second
/// copy of the list on screen is a second chance for the two to disagree.
/// </summary>
public partial class PlaceboColorMappingView : UserControl
{
    public PlaceboColorMappingView()
    {
        InitializeComponent();

        Fill(GamutCombo, PlaceboColorMapping.GamutMapping);
        Fill(ToneCombo, PlaceboColorMapping.ToneMapping);
        Fill(MetadataCombo, PlaceboColorMapping.ToneMetadata);
    }

    private static void Fill(ComboBox combo, StoredChoice choice)
    {
        foreach (string label in choice.Labels)
            combo.Items.Add(label);
    }
}
