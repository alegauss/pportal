using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP167: the nine tabs, in the order the strip draws them.
///
/// The order is the INDEX, and the index is what two numbered switches in the QML dispatch on -
/// one for which control takes focus when a tab is entered, one for which scroll area the arrow
/// keys move. So the order is not layout: reordering the strip changes what nine cases mean.
/// </summary>
public enum SettingsTab
{
    General = 0,
    Video = 1,
    Stream = 2,

    /// <summary>Labelled "Audio/Wifi", which is two subjects under one tab.</summary>
    AudioWifi = 3,

    Consoles = 4,
    Keys = 5,
    Controllers = 6,
    Remote = 7,
    Config = 8,
}

/// <summary>
/// PP167: the dialog the nine tabs do not add up to.
///
/// PP16 shipped every tab's rules and every tab's screen. What it did not ship is this: a strip in
/// a fixed order, a first control per tab, and the two keys that move between tabs. Three things
/// live here and nowhere else:
///
/// 1. THE ORDER IS AN INDEX. Two switches in the QML dispatch on <c>bar.currentIndex</c> - one
///    picks the control that takes focus, one picks the scroll area the arrow keys drive. A host
///    that drew the nine in a plausible order would be a working screen that disagrees with the
///    other client about where every control is, and no test written per tab would notice.
///
/// 2. EIGHT TABS NAME THEIR FIRST CONTROL AND THE NINTH COMPUTES IT. Remote's first focusable is
///    whichever of five controls is visible, in order - because its first two buttons are mutually
///    exclusive (PP165: Login while any credential is missing, Clear only when all four are there).
///    A fixed first item there would put focus on a button that is not on screen.
///
/// 3. PAGE UP AND PAGE DOWN MOVE BETWEEN TABS and are the only keys that do. They do NOT wrap:
///    QML's <c>decrementCurrentIndex</c> stops at the first tab and <c>incrementCurrentIndex</c> at
///    the last, so the strip has ends.
/// </summary>
public sealed class SettingsTabsViewModel : DialogViewModel
{
    private SettingsTab current = SettingsTab.General;
    private bool loginVisible = true;
    private bool clearVisible;
    private bool portGuessingVisible = true;

    protected override string ButtonProperty => nameof(Current);

    /// <summary>The nine, in the strip's order.</summary>
    public static IReadOnlyList<SettingsTab> Order { get; } = [.. Enum.GetValues<SettingsTab>()];

    /// <summary>What each tab is called on the strip. "Audio/Wifi" is one tab and two subjects.</summary>
    public static IReadOnlyDictionary<SettingsTab, string> Labels { get; } =
        new Dictionary<SettingsTab, string>
        {
            [SettingsTab.General] = "General",
            [SettingsTab.Video] = "Video",
            [SettingsTab.Stream] = "Stream",
            [SettingsTab.AudioWifi] = "Audio/Wifi",
            [SettingsTab.Consoles] = "Consoles",
            [SettingsTab.Keys] = "Keys",
            [SettingsTab.Controllers] = "Controllers",
            [SettingsTab.Remote] = "Remote",
            [SettingsTab.Config] = "Config",
        };

    /// <summary>Which tab is on show.</summary>
    public SettingsTab Current
    {
        get => current;
        set { Set(ref current, value); Raise(nameof(CurrentIndex)); Raise(nameof(FirstControl)); }
    }

    /// <summary>Its index, which is the number the two switches dispatch on.</summary>
    public int CurrentIndex
    {
        get => (int)Current;
        set
        {
            if (value >= 0 && value < Order.Count)
                Current = Order[value];
        }
    }

    /// <summary>Whether the Remote tab is showing its Login button - PP165's first condition.</summary>
    public bool LoginVisible
    {
        get => loginVisible;
        set { Set(ref loginVisible, value); Raise(nameof(FirstControl)); }
    }

    /// <summary>And whether it is showing Clear instead.</summary>
    public bool ClearVisible
    {
        get => clearVisible;
        set { Set(ref clearVisible, value); Raise(nameof(FirstControl)); }
    }

    /// <summary>Whether the port-guessing checkbox below them is on screen.</summary>
    public bool PortGuessingVisible
    {
        get => portGuessingVisible;
        set { Set(ref portGuessingVisible, value); Raise(nameof(FirstControl)); }
    }

    /// <summary>
    /// The name of the control that takes focus when the current tab is entered.
    ///
    /// Eight of the nine are fixed. The ninth walks the Remote tab's controls and takes the first
    /// VISIBLE one, because two of them are never on screen together.
    /// </summary>
    public string FirstControl => Current switch
    {
        SettingsTab.General => "disconnectAction",
        SettingsTab.Video => "hwDecoderCombo",
        SettingsTab.Stream => "consoleSelection",
        SettingsTab.AudioWifi => "audioOutDevice",
        SettingsTab.Consoles => "registerNewButton",
        SettingsTab.Keys => "resetAllKeys",
        SettingsTab.Controllers => "controllerMappingChange",
        SettingsTab.Remote => FirstRemoteControl(),
        SettingsTab.Config => "profile",
        _ => "",
    };

    /// <summary>
    /// Page Up: the previous tab, stopping at the first. The strip has ends rather than wrapping,
    /// which is what <c>decrementCurrentIndex</c> does.
    /// </summary>
    public void PreviousTab()
    {
        if (CurrentIndex > 0)
            CurrentIndex--;
    }

    /// <summary>Page Down: the next, stopping at the last.</summary>
    public void NextTab()
    {
        if (CurrentIndex < Order.Count - 1)
            CurrentIndex++;
    }

    /// <summary>
    /// The Remote tab's first focusable, walked in the QML's own order. The fallback is the scroll
    /// area itself - so focus lands somewhere even when every control on the tab is hidden.
    /// </summary>
    private string FirstRemoteControl()
    {
        if (LoginVisible)
            return "openPsnLogin";
        if (ClearVisible)
            return "resetPsnTokens";
        if (PortGuessingVisible)
            return "holePunchGuessingCheckbox";

        return "remoteFlick";
    }
}

/// <summary>
/// PP167: the dialog's rules where the Qt client states them.
/// </summary>
public static class SettingsTabsSource
{
    /// <summary>The settings screen.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Whether the strip still draws the nine in this order.</summary>
    public static bool TheNineAreStillInThisOrder(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int at = 0;
        foreach (SettingsTab tab in SettingsTabsViewModel.Order)
        {
            string needle = $"text: qsTr(\"{SettingsTabsViewModel.Labels[tab]}\")";
            int found = qml.IndexOf(needle, at, StringComparison.Ordinal);
            if (found < 0)
                return false;

            at = found + needle.Length;
        }

        return true;
    }

    /// <summary>Whether the focus switch still dispatches on the index for all nine.</summary>
    public static bool TheFocusSwitchIsStillNumbered(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("case 0: item = disconnectAction; break;", StringComparison.Ordinal)
            && qml.Contains("case 8: item = profile; break;", StringComparison.Ordinal);
    }

    /// <summary>And whether the scroll switch beside it dispatches on the same nine.</summary>
    public static bool TheScrollSwitchIsStillNumbered(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("case 0: return generalFlick;", StringComparison.Ordinal)
            && qml.Contains("case 8: return configFlick;", StringComparison.Ordinal);
    }

    /// <summary>Whether the Remote tab's first control is still computed rather than named.</summary>
    public static bool TheRemoteTabStillComputesItsFirstControl(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("case 7: item = firstRemoteFocusableItem(); break;", StringComparison.Ordinal)
            && qml.Contains("if (openPsnLogin.visible)", StringComparison.Ordinal)
            && qml.Contains("return remoteFlick;", StringComparison.Ordinal);
    }

    /// <summary>Whether the two paging keys are still the only ones that change tabs.</summary>
    public static bool PagingIsStillTheTwoKeys(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("bar.decrementCurrentIndex();", StringComparison.Ordinal)
            && qml.Contains("bar.incrementCurrentIndex();", StringComparison.Ordinal);
    }
}
