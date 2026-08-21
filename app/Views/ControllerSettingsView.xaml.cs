using System.Windows.Controls;
using ChiakiNg.Settings;

namespace ChiakiNg.Views;

/// <summary>
/// PP16: the Controllers tab.
///
/// Five combos to fill and nothing else - the four shortcut combos share ONE list of seventeen and
/// the rumble combo has its own six. Filled here rather than bound, because their contents never
/// change: PP159's refill-in-place rule is for the Audio tab's device lists, which do.
/// </summary>
public partial class ControllerSettingsView : UserControl
{
    public ControllerSettingsView()
    {
        InitializeComponent();

        // One list, four combos. A separate copy per combo would be four places for a future
        // seventeenth button to be forgotten in three of them.
        foreach (ComboBox combo in new[] { Shortcut1, Shortcut2, Shortcut3, Shortcut4 })
        {
            foreach (string button in DpadTouchShortcut.Buttons)
                combo.Items.Add(button);
        }

        foreach (string label in RumbleHapticsChoice.Intensity.Labels)
            RumbleCombo.Items.Add(label);
    }
}
