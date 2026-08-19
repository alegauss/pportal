using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: a combo whose INDEX is the property and whose STRING is what the store holds.
///
/// The settings screen binds `currentIndex: Chiaki.settings.disconnectAction` and assigns the
/// index straight back, so the property is an int and the port would reasonably store an int. The
/// Qt client does not: Settings::SetDisconnectAction writes "nothing", "sleep" or "ask", and
/// GetDisconnectAction reads the string back through `QMap::key(v, default)`.
///
/// That is the whole reason this type exists rather than a cast. A port that stored the index
/// writes 2 where the Qt client writes "ask"; the Qt client then finds no key for "2", falls back
/// to its default, and the user's choice is gone. Nothing throws, nothing logs, and the two
/// clients share one settings file - so the symptom is a preference that resets when the other
/// client is opened, which is not a symptom anybody traces back to a screen.
///
/// The fallback is copied too. An unknown string is the default, not an error: a settings file
/// written by a newer version, or edited by hand, must leave the screen usable.
/// </summary>
public sealed class ActionChoice
{
    private readonly string[] stored;

    private ActionChoice(string key, IReadOnlyList<string> labels, string[] stored, int defaultIndex)
    {
        Key = key;
        Labels = labels;
        this.stored = stored;
        DefaultIndex = defaultIndex;
    }

    /// <summary>The preference this choice is stored under.</summary>
    public string Key { get; }

    /// <summary>What the combo shows, in the order the QML declares - the index IS the enum value.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>The index taken when the store holds nothing, or holds something unrecognised.</summary>
    public int DefaultIndex { get; }

    /// <summary>
    /// Action On Disconnect. Three choices, and the default is the LAST of them - which is worth
    /// noticing, because an index-based port that defaulted to 0 would silently change what a
    /// fresh install does on disconnect from asking to doing nothing.
    /// </summary>
    public static ActionChoice Disconnect { get; } = new(
        "settings/disconnect_action",
        new[] { "Do Nothing", "Enter Sleep Mode", "Ask" },
        // DisconnectAction: AlwaysNothing, AlwaysSleep, Ask - settings.h's order, which is the
        // combo's order, which is why the index can be the enum value at all.
        new[] { "nothing", "sleep", "ask" },
        2);

    /// <summary>
    /// Action On Suspend. Two choices out of a DIFFERENT enum that happens to start the same way,
    /// so index 0 is "nothing" in both and the two are still not interchangeable: "ask" is a
    /// disconnect string only, and one shared converter would accept it here.
    /// </summary>
    public static ActionChoice Suspend { get; } = new(
        "settings/suspend_action",
        new[] { "Do Nothing", "Enter Sleep Mode" },
        new[] { "nothing", "sleep" },
        0);

    /// <summary>What the store holds for an index. Out-of-range takes the default's string.</summary>
    public string StoredFor(int index)
        => stored[index >= 0 && index < stored.Length ? index : DefaultIndex];

    /// <summary>
    /// The index for a stored string, or the default index where it is not one of them - which is
    /// `QMap::key(v, default)` and includes the case of a key never written.
    /// </summary>
    public int IndexOf(string? storedValue)
    {
        if (storedValue is null)
            return DefaultIndex;

        int found = Array.IndexOf(stored, storedValue);
        return found < 0 ? DefaultIndex : found;
    }

    /// <summary>Whether a string is one this choice recognises. Used to state the fallback, not to gate it.</summary>
    public bool Recognises(string? storedValue)
        => storedValue is not null && Array.IndexOf(stored, storedValue) >= 0;
}

/// <summary>
/// PP16: the settings screen's General tab.
///
/// Ten controls, and they are worth taking first because between them they cover all three ways
/// this screen stores a value - and two of the three are not what the property looks like:
///
///   two combos are int properties stored as STRINGS (see <see cref="ActionChoice"/>);
///   one combo is an int property stored as an int, on the same tab, three rows down;
///   four more are uint indices into a 17-entry list, stored as the index.
///
/// The last group carries a cross-check worth keeping: the stored defaults are 9, 10, 11 and 12,
/// the list's entries at those positions are L1, R1, L3 and R3, and the label beside the row
/// prints "(L1+R1+L3+R3)". Three statements of one fact in two files, so the assertion holds them
/// together rather than trusting any one of them.
///
/// Writing is not here. <see cref="IPreferences"/> reads, and nothing in the port writes a
/// preference yet, so each control exposes the value the store should receive
/// (<see cref="DisconnectStored"/> and friends) and the write path has one place to take it from
/// instead of re-deriving the mapping at the call site - which is where it would go wrong.
/// </summary>
public sealed class GeneralSettingsViewModel : DialogViewModel
{
    /// <summary>The list every stream-menu shortcut combo offers, in the QML's order.</summary>
    public static IReadOnlyList<string> ShortcutLabels { get; } = new[]
    {
        "Not Used", "Cross", "Moon", "Box", "Pyramid",
        "Dpad Left", "Dpad Right", "Dpad Up", "Dpad Down",
        "L1", "R1", "L3", "R3", "Options", "Share", "Touchpad", "PS",
    };

    /// <summary>What the Audio/Video combo offers. The index is the value, stored as an int.</summary>
    public static IReadOnlyList<string> AudioVideoLabels { get; } = new[]
    {
        "Audio and Video Enabled", "Audio Disabled", "Video Disabled", "Audio and Video Disabled",
    };

    /// <summary>The four shortcut preferences, in the order the row shows them.</summary>
    public static IReadOnlyList<string> ShortcutKeys { get; } = new[]
    {
        "settings/stream_menu_shortcut1", "settings/stream_menu_shortcut2",
        "settings/stream_menu_shortcut3", "settings/stream_menu_shortcut4",
    };

    private readonly int[] shortcuts = new int[ShortcutKeys.Count];

    private int disconnectIndex = ActionChoice.Disconnect.DefaultIndex;
    private int suspendIndex = ActionChoice.Suspend.DefaultIndex;
    private int audioVideoDisabled;
    private bool streamerMode;
    private bool streamMenuEnabled = true;
    private string logDirectory = "";

    /// <summary>A tab with its defaults, for a screen shown before a store is available.</summary>
    public GeneralSettingsViewModel()
    {
        logDirectory = QtPaths.LogDirectory;
        shortcuts[0] = 9;
        shortcuts[1] = 10;
        shortcuts[2] = 11;
        shortcuts[3] = 12;
    }

    /// <summary>The tab as the store holds it. Every default comes from PP2's table, not from here.</summary>
    public GeneralSettingsViewModel(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        disconnectIndex = ActionChoice.Disconnect.IndexOf(
            preferences.GetString(ActionChoice.Disconnect.Key));
        suspendIndex = ActionChoice.Suspend.IndexOf(
            preferences.GetString(ActionChoice.Suspend.Key));

        audioVideoDisabled = preferences.GetInt("settings/audio_video_disabled");
        streamerMode = preferences.GetBool("settings/streamer_mode");
        streamMenuEnabled = preferences.GetBool("settings/stream_menu_enabled");

        for (int i = 0; i < ShortcutKeys.Count; i++)
            shortcuts[i] = (int)preferences.GetUInt(ShortcutKeys[i]);

        logDirectory = QtPaths.LogDirectory;
    }

    /// <summary>
    /// Nothing on this tab enables a button - it is a page of preferences, not a dialog. The base
    /// class raises this on every change anyway, and naming a property that exists keeps that
    /// harmless rather than making it a binding nobody resolves.
    /// </summary>
    protected override string ButtonProperty => nameof(StreamMenuShortcutsVisible);

    /// <summary>Action On Disconnect, as the combo's index.</summary>
    public int DisconnectIndex
    {
        get => disconnectIndex;
        set
        {
            Set(ref disconnectIndex, value);
            Raise(nameof(DisconnectStored));
        }
    }

    /// <summary>Action On Suspend, as the combo's index.</summary>
    public int SuspendIndex
    {
        get => suspendIndex;
        set
        {
            Set(ref suspendIndex, value);
            Raise(nameof(SuspendStored));
        }
    }

    /// <summary>What the store must receive for the current disconnect choice.</summary>
    public string DisconnectStored => ActionChoice.Disconnect.StoredFor(DisconnectIndex);

    /// <summary>And for the suspend choice, out of the other enum's strings.</summary>
    public string SuspendStored => ActionChoice.Suspend.StoredFor(SuspendIndex);

    /// <summary>Audio/Video, which really is stored as the index it looks like.</summary>
    public int AudioVideoDisabled
    {
        get => audioVideoDisabled;
        set => Set(ref audioVideoDisabled, value);
    }

    public bool StreamerMode
    {
        get => streamerMode;
        set => Set(ref streamerMode, value);
    }

    /// <summary>
    /// Whether the stream menu has a shortcut at all. It is also the shortcut row's visibility -
    /// `visible: Chiaki.settings.streamMenuEnabled` - so unchecking it must repaint four combos
    /// and not just store a bool.
    /// </summary>
    public bool StreamMenuEnabled
    {
        get => streamMenuEnabled;
        set
        {
            Set(ref streamMenuEnabled, value);
            Raise(nameof(StreamMenuShortcutsVisible));
        }
    }

    public bool StreamMenuShortcutsVisible => StreamMenuEnabled;

    /// <summary>Where the logs are. Shown with an Open button and never typed into.</summary>
    public string LogDirectory
    {
        get => logDirectory;
        set => Set(ref logDirectory, value ?? "");
    }

    /// <summary>One of the four stream-menu shortcuts, as an index into <see cref="ShortcutLabels"/>.</summary>
    public int Shortcut(int slot) => shortcuts[slot];

    /// <summary>Sets one, and names it so the four bindings resolve.</summary>
    public void SetShortcut(int slot, int index)
    {
        if (shortcuts[slot] == index)
            return;

        shortcuts[slot] = index;
        Raise($"Shortcut{slot + 1}");
        Raise(nameof(StreamMenuShortcutsVisible));
    }

    public int Shortcut1 { get => Shortcut(0); set => SetShortcut(0, value); }
    public int Shortcut2 { get => Shortcut(1); set => SetShortcut(1, value); }
    public int Shortcut3 { get => Shortcut(2); set => SetShortcut(2, value); }
    public int Shortcut4 { get => Shortcut(3); set => SetShortcut(3, value); }

    /// <summary>
    /// The shortcut defaults spelled as the label beside the row spells them, which is the
    /// cross-check: "(L1+R1+L3+R3)" is printed by the screen and 9, 10, 11, 12 are stored by
    /// settings.cpp, and the two agree only if the list has not been reordered.
    /// </summary>
    public string DefaultShortcutHint()
        => "(" + string.Join('+', ShortcutKeys.Select(
            k => ShortcutLabels[(int)(uint)(Preferences.Find(k)!.Default ?? 0u)])) + ")";
}

/// <summary>
/// PP16: the General tab's rules where they are actually written down - two files, because the
/// combo's order is in the QML and the string it stores is in settings.cpp.
/// </summary>
public static class GeneralSettingsSource
{
    /// <summary>Where the enum orders live.</summary>
    public const string SettingsHeader = @"gui\include\settings.h";

    /// <summary>Where the strings and the fallback live.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>The settings screen, or null outside a checkout.</summary>
    public static string? LocateQml() => SettingsFieldSource.Locate();

    /// <summary>
    /// Whether an action is still stored as a string rather than as the combo's index. Both maps
    /// are checked entry by entry, because a single wrong pair is a preference that resets.
    /// </summary>
    public static bool StoredAsStrings(string cpp, ActionChoice choice, string mapName)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(mapName);

        return cpp.Contains($"settings.setValue(\"{choice.Key}\", {mapName}[action]);",
                StringComparison.Ordinal)
            && cpp.Contains($"return {mapName}.key(v, ", StringComparison.Ordinal);
    }

    /// <summary>Whether the enum order the index relies on is still the header's.</summary>
    public static bool EnumOrderIs(string header, string enumName, params string[] members)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(enumName);
        ArgumentNullException.ThrowIfNull(members);

        int at = header.IndexOf("enum class " + enumName, StringComparison.Ordinal);
        if (at < 0)
            return false;

        int close = header.IndexOf('}', at);
        if (close < 0)
            return false;

        string body = header[at..close];
        int cursor = 0;
        foreach (string member in members)
        {
            int found = body.IndexOf(member, cursor, StringComparison.Ordinal);
            if (found < 0)
                return false;

            cursor = found + member.Length;
        }

        return true;
    }

    /// <summary>Whether a combo on the tab still offers exactly these labels, in this order.</summary>
    public static bool ComboOffers(string qml, IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(labels);

        string model = "model: [" + string.Join(", ", labels.Select(l => $"qsTr(\"{l}\")")) + "]";
        return qml.Contains(model, StringComparison.Ordinal);
    }

    /// <summary>Whether the shortcut row is still shown only when the stream menu is enabled.</summary>
    public static bool TheShortcutRowFollowsTheStreamMenu(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("visible: Chiaki.settings.streamMenuEnabled", StringComparison.Ordinal);
    }

    /// <summary>The hint the screen prints beside the shortcut row, or null.</summary>
    public static string? ShortcutHint(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        const string marker = "qsTr(\"(L1+R1+L3+R3)\")";
        return qml.Contains(marker, StringComparison.Ordinal) ? "(L1+R1+L3+R3)" : null;
    }
}
