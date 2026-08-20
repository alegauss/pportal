using System.Collections.ObjectModel;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the settings screen's Consoles tab - two lists that look alike and are not.
///
/// Registered consoles and hidden consoles, each a row with a caption and a button. Four things
/// about them are behaviour rather than layout:
///
///   the caption leads with the MAC, not the name. `"%1 (%2, %3)"` is mac, generation, name - so the
///   primary text of a row is its hardware address and the human name is last. A port that put the
///   name first would read better and would not be this screen;
///
///   STREAMER MODE replaces the MAC and leaves the name. It is the address that is private, which is
///   the opposite of what "hidden" suggests, and it is the same substitution in both lists;
///
///   auto-connect is a RADIO GROUP built from N checkboxes over one string. Each row's box is
///   checked when its MAC equals the one setting; ticking another row's box overwrites it, so the
///   first unticks itself through the binding rather than through any code. Unticking clears the
///   setting to the empty string;
///
///   and the two lists identify a row DIFFERENTLY for the same kind of operation. Deleting a
///   registered console passes its INDEX; unhiding a hidden one passes its MAC. Two calls, side by
///   side, one positional and one keyed - so a port that reordered or filtered either list would
///   delete the wrong console and unhide the right one.
/// </summary>
public sealed class ConsoleSettingsViewModel : DialogViewModel
{
    /// <summary>What streamer mode shows in place of the address.</summary>
    public const string HiddenAddress = "hidden";

    private readonly ObservableCollection<string> registeredCaptions = [];
    private readonly ObservableCollection<string> hiddenCaptions = [];

    private IReadOnlyList<RegisteredHost> registered = [];
    private IReadOnlyList<HiddenHost> hidden = [];
    private string autoConnectMac = "";
    private bool streamerMode;

    /// <summary>An empty tab.</summary>
    public ConsoleSettingsViewModel()
    {
    }

    /// <summary>The tab as the store holds it.</summary>
    public ConsoleSettingsViewModel(
        IPreferences preferences,
        IReadOnlyList<RegisteredHost> registeredHosts,
        IReadOnlyList<HiddenHost> hiddenHosts,
        string autoConnect = "")
    {
        ArgumentNullException.ThrowIfNull(preferences);

        streamerMode = preferences.GetBool("settings/streamer_mode");
        autoConnectMac = autoConnect ?? "";

        Load(registeredHosts, hiddenHosts);
    }

    protected override string ButtonProperty => nameof(StreamerMode);

    /// <summary>The registered consoles, in the order the store returned them.</summary>
    public IReadOnlyList<RegisteredHost> Registered => registered;

    /// <summary>The hidden consoles.</summary>
    public IReadOnlyList<HiddenHost> Hidden => hidden;

    /// <summary>The registered rows' captions, refilled in place for PP159's reason.</summary>
    public ObservableCollection<string> RegisteredCaptions => registeredCaptions;

    /// <summary>The hidden rows' captions.</summary>
    public ObservableCollection<string> HiddenCaptions => hiddenCaptions;

    /// <summary>
    /// Whether the address is replaced by <see cref="HiddenAddress"/>. A General-tab preference that
    /// rewrites captions on this tab, which is why it is read here rather than passed in.
    /// </summary>
    public bool StreamerMode
    {
        get => streamerMode;
        set
        {
            Set(ref streamerMode, value);
            Recaption();
        }
    }

    /// <summary>
    /// The one MAC that auto-connects, or the empty string. One setting behind N checkboxes.
    /// </summary>
    public string AutoConnectMac
    {
        get => autoConnectMac;
        set
        {
            Set(ref autoConnectMac, value ?? "");
            Raise(nameof(AutoConnectIndex));
        }
    }

    /// <summary>
    /// Which registered row is ticked, or -1. Derived, because the setting is a MAC and not a
    /// position - so a list that reorders moves the tick with the console rather than leaving it.
    /// </summary>
    public int AutoConnectIndex
    {
        get
        {
            for (int i = 0; i < registered.Count; i++)
            {
                if (IsAutoConnect(i))
                    return i;
            }

            return -1;
        }
    }

    /// <summary>Replaces both lists, keeping the caption collections the markup holds.</summary>
    public void Load(IReadOnlyList<RegisteredHost> registeredHosts, IReadOnlyList<HiddenHost> hiddenHosts)
    {
        ArgumentNullException.ThrowIfNull(registeredHosts);
        ArgumentNullException.ThrowIfNull(hiddenHosts);

        registered = registeredHosts;
        hidden = hiddenHosts;

        Recaption();
        Raise(nameof(Registered));
        Raise(nameof(Hidden));
        Raise(nameof(AutoConnectIndex));
    }

    /// <summary>Whether a registered row's auto-connect box is ticked.</summary>
    public bool IsAutoConnect(int index)
    {
        if (index < 0 || index >= registered.Count)
            return false;

        return AutoConnectMac.Length > 0
            && string.Equals(registered[index].MacText, AutoConnectMac, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A row's box toggled. Ticking overwrites the single setting, so every other row unticks itself;
    /// unticking clears it to the empty string.
    /// </summary>
    public void SetAutoConnect(int index, bool ticked)
    {
        if (index < 0 || index >= registered.Count)
            return;

        AutoConnectMac = ticked ? registered[index].MacText : "";
    }

    /// <summary>
    /// The argument the delete call takes: an INDEX. Positional, so it is only meaningful against
    /// the list as it currently stands.
    /// </summary>
    public int DeleteArgument(int index) => index;

    /// <summary>
    /// And the argument the unhide call takes: a MAC. The same kind of operation on the list beside
    /// it, identified the other way.
    /// </summary>
    public string UnhideArgument(int index)
        => index >= 0 && index < hidden.Count ? hidden[index].MacText : "";

    /// <summary>
    /// A registered row's caption: the address, the generation, then the name - in that order,
    /// because that is the order the screen puts them in.
    /// </summary>
    public string CaptionFor(RegisteredHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        string address = StreamerMode ? HiddenAddress : host.MacText;
        string generation = TouchpadExtents.IsPs5((ChiakiTarget)host.Target) ? "PS5" : "PS4";
        return $"{address} ({generation}, {host.ServerNickname})";
    }

    /// <summary>A hidden row's caption: the address then the name, with no generation.</summary>
    public string CaptionFor(HiddenHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        string address = StreamerMode ? HiddenAddress : host.MacText;
        return $"{address} ({host.ServerNickname})";
    }

    private void Recaption()
    {
        Sync(registeredCaptions, registered.Select(CaptionFor).ToList());
        Sync(hiddenCaptions, hidden.Select(CaptionFor).ToList());
    }

    /// <summary>
    /// The same in-place refill PP159 arrived at. Kept here rather than shared with the audio tab
    /// because these are captions and those are device names - one is derived from a list this screen
    /// owns and the other from what the machine reported, and a change to either has no business
    /// moving the other.
    /// </summary>
    private static void Sync(ObservableCollection<string> target, IReadOnlyList<string> items)
    {
        for (int i = target.Count - 1; i >= items.Count; i--)
            target.RemoveAt(i);

        for (int i = 0; i < items.Count; i++)
        {
            if (i >= target.Count)
                target.Add(items[i]);
            else if (!string.Equals(target[i], items[i], StringComparison.Ordinal))
                target[i] = items[i];
        }
    }
}

/// <summary>
/// PP16: the Consoles tab's rules where the QML states them.
/// </summary>
public static class ConsoleSettingsSource
{
    /// <summary>The settings screen, or null outside a checkout.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Whether a registered row's caption is still address, generation, name.</summary>
    public static bool TheRegisteredCaptionLeadsWithTheAddress(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "text: \"%1 (%2, %3)\".arg(Chiaki.settings.streamerMode ? \"hidden\" : modelData.mac)"
                + ".arg(modelData.ps5 ? \"PS5\" : \"PS4\").arg(modelData.name)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a hidden row's caption is still address then name.</summary>
    public static bool TheHiddenCaptionLeadsWithTheAddress(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "text: \"%1 (%2)\".arg(Chiaki.settings.streamerMode ? \"hidden\" : modelData.mac)"
                + ".arg(modelData.name)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether auto-connect is still one setting compared against each row's MAC.</summary>
    public static bool AutoConnectIsOneSettingPerRow(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                "checked: Chiaki.settings.autoConnectMac == modelData.mac", StringComparison.Ordinal)
            && qml.Contains(
                "onToggled: Chiaki.settings.autoConnectMac = checked ? modelData.mac : \"\";",
                StringComparison.Ordinal);
    }

    /// <summary>Whether deleting still takes an index and unhiding still takes a MAC.</summary>
    public static bool TheTwoListsIdentifyRowsDifferently(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Chiaki.settings.deleteRegisteredHost(index)", StringComparison.Ordinal)
            && qml.Contains("Chiaki.unhideHost(modelData.mac)", StringComparison.Ordinal);
    }

    /// <summary>Whether both destructive actions still go through a confirm dialog.</summary>
    public static bool BothActionsConfirm(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("root.showConfirmDialog(qsTr(\"Delete Console\")", StringComparison.Ordinal)
            && qml.Contains("root.showConfirmDialog(qsTr(\"Unhide Console\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the two lists still come from different owners.</summary>
    public static bool TheListsHaveDifferentOwners(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("model: Chiaki.settings.registeredHosts", StringComparison.Ordinal)
            && qml.Contains("model: Chiaki.hiddenHosts", StringComparison.Ordinal);
    }
}
