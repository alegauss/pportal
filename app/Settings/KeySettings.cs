using System.Collections.ObjectModel;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>One row of the Keys tab: a console button, its display name and the key bound to it.</summary>
public readonly record struct KeyBinding(int ButtonValue, string ButtonName, string KeyName);

/// <summary>
/// PP16: the keyboard mapping, whose settings key is derived from a TRANSLATABLE string.
///
/// Three things here are not what they look like.
///
/// The property driving this tab is called `controllerMapping`, and it is the KEYBOARD map: a
/// console button to a keyboard key. The tab beside it, Controllers, is about SDL gamepads. So the
/// name belongs to the other screen and a port following it would wire the wrong tab.
///
/// The stored key is built from the display name - `name.replace(' ', '_').toLower()`, prefixed
/// `keymap/`. That display name comes from `tr()`, so the SETTINGS KEY depends on the interface
/// language: run the Qt client in a locale where "Cross" is translated and it reads and writes a
/// different key. Reproduced with the English names, because those are the keys the settings file
/// this port shares actually contains - but it is a defect rather than a design, and worth knowing
/// before anyone adds a translation.
///
/// And two of the display names contradict the enum they come from. ANALOG_STICK_LEFT_X_UP is
/// called "Left Stick Right" and X_DOWN "Left Stick Left", while Y_UP is "Left Stick Up". PP5 found
/// why - the horizontal half-axes are positive up and the vertical ones negative up - and this is
/// where that shows on screen. A port naming the rows from the enum would label two of them
/// backwards.
/// </summary>
public static class KeyMap
{
    /// <summary>The prefix every binding is stored under.</summary>
    public const string KeyPrefix = "keymap/";

    /// <summary>
    /// Every bindable button, in the order a QMap yields them - which is BY NUMERIC VALUE and not
    /// the order this table is written in. The screen's rows are therefore ordered by the button's
    /// id, and a port listing buttons in a hand-chosen order would draw a different grid.
    /// </summary>
    /// <remarks>
    /// A property over a field declared BELOW the two dictionaries, not an initialiser here. Static
    /// field initialisers run in declaration order, so `= Build()` at this position ran before the
    /// tables it reads existed and threw a TypeInitializationException on first touch - which
    /// surfaces as every member of the class failing at once, with the real cause two frames down.
    /// </remarks>
    public static IReadOnlyList<KeyBinding> Defaults => defaultRows;

    /// <summary>The display name for a button value, or the empty string.</summary>
    public static string NameOf(int buttonValue)
        => names.TryGetValue(buttonValue, out string? name) ? name : "";

    /// <summary>
    /// The settings key a button is stored under: its display name, spaces to underscores, lowered.
    /// So "D-Pad Left" is `keymap/d-pad_left` - the hyphen survives and the space does not.
    /// </summary>
    public static string StorageKeyFor(int buttonValue)
        => KeyPrefix + NameOf(buttonValue).Replace(' ', '_').ToLowerInvariant();

    /// <summary>The default key name for a button, as a QKeySequence would spell it.</summary>
    public static string DefaultKeyNameFor(int buttonValue)
        => defaults.TryGetValue(buttonValue, out string? key) ? key : "";

    /// <summary>
    /// The whole map as the screen sees it: the defaults, with anything the store holds substituted
    /// in. A key the store does not have falls back rather than disappearing, which is why the tab
    /// always shows every row.
    /// </summary>
    /// <param name="lookup">
    /// Reads one raw settings value, or null. NOT <see cref="IPreferences"/>, and that is the point:
    /// PP2's table declares every `settings/` key this port knows and refuses an undeclared one,
    /// because an undeclared key is a typo or a preference nobody transcribed. These twenty-six
    /// cannot be in it - their names are COMPUTED from a translatable display name, so the set of
    /// keys is not knowable from the source at all. So the keymap is read through a plain lookup and
    /// the table's guarantee is left intact rather than widened to cover something it cannot check.
    /// </param>
    public static IReadOnlyList<KeyBinding> Read(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var rows = new List<KeyBinding>(Defaults.Count);
        foreach (KeyBinding row in Defaults)
        {
            string? stored = lookup(StorageKeyFor(row.ButtonValue));
            rows.Add(row with { KeyName = string.IsNullOrEmpty(stored) ? row.KeyName : stored });
        }

        return rows;
    }

    // The display names, from Settings::GetChiakiControllerButtonName. The two X-axis entries are
    // the ones that contradict their enum names; they are transcribed rather than derived.
    private static readonly Dictionary<int, string> names = new()
    {
        [(int)ChiakiControllerButton.Cross] = "Cross",
        [(int)ChiakiControllerButton.Moon] = "Moon",
        [(int)ChiakiControllerButton.Box] = "Box",
        [(int)ChiakiControllerButton.Pyramid] = "Pyramid",
        [(int)ChiakiControllerButton.DpadLeft] = "D-Pad Left",
        [(int)ChiakiControllerButton.DpadRight] = "D-Pad Right",
        [(int)ChiakiControllerButton.DpadUp] = "D-Pad Up",
        [(int)ChiakiControllerButton.DpadDown] = "D-Pad Down",
        [(int)ChiakiControllerButton.L1] = "L1",
        [(int)ChiakiControllerButton.R1] = "R1",
        [(int)ChiakiControllerButton.L3] = "L3",
        [(int)ChiakiControllerButton.R3] = "R3",
        [(int)ChiakiControllerButton.Options] = "Options",
        [(int)ChiakiControllerButton.Share] = "Share",
        [(int)ChiakiControllerButton.Touchpad] = "Touchpad",
        [(int)ChiakiControllerButton.Ps] = "PS",
        [(int)ChiakiControllerButton.L2] = "L2",
        [(int)ChiakiControllerButton.R2] = "R2",

        // X_UP is "Right" and X_DOWN is "Left". Not a typo here: PP5's sign asymmetry, named.
        [(int)ControllerButtonExt.AnalogStickLeftXUp] = "Left Stick Right",
        [(int)ControllerButtonExt.AnalogStickLeftXDown] = "Left Stick Left",
        [(int)ControllerButtonExt.AnalogStickLeftYUp] = "Left Stick Up",
        [(int)ControllerButtonExt.AnalogStickLeftYDown] = "Left Stick Down",
        [(int)ControllerButtonExt.AnalogStickRightXUp] = "Right Stick Right",
        [(int)ControllerButtonExt.AnalogStickRightXDown] = "Right Stick Left",
        [(int)ControllerButtonExt.AnalogStickRightYUp] = "Right Stick Up",
        [(int)ControllerButtonExt.AnalogStickRightYDown] = "Right Stick Down",
    };

    // The default bindings, as QKeySequence spells each key. Note the abbreviations: Escape is
    // "Esc" and the bracket keys are the characters rather than their names - a port writing
    // "Escape" would store a value the Qt client reads back as nothing.
    private static readonly Dictionary<int, string> defaults = new()
    {
        [(int)ChiakiControllerButton.Cross] = "Return",
        [(int)ChiakiControllerButton.Moon] = "Backspace",
        [(int)ChiakiControllerButton.Box] = "\\",
        [(int)ChiakiControllerButton.Pyramid] = "C",
        [(int)ChiakiControllerButton.DpadLeft] = "Left",
        [(int)ChiakiControllerButton.DpadRight] = "Right",
        [(int)ChiakiControllerButton.DpadUp] = "Up",
        [(int)ChiakiControllerButton.DpadDown] = "Down",
        [(int)ChiakiControllerButton.L1] = "2",
        [(int)ChiakiControllerButton.R1] = "3",
        [(int)ChiakiControllerButton.L3] = "5",
        [(int)ChiakiControllerButton.R3] = "6",
        [(int)ChiakiControllerButton.Options] = "O",
        [(int)ChiakiControllerButton.Share] = "F",
        [(int)ChiakiControllerButton.Touchpad] = "T",
        [(int)ChiakiControllerButton.Ps] = "Esc",
        [(int)ChiakiControllerButton.L2] = "1",
        [(int)ChiakiControllerButton.R2] = "4",
        [(int)ControllerButtonExt.AnalogStickLeftXUp] = "]",
        [(int)ControllerButtonExt.AnalogStickLeftXDown] = "[",
        [(int)ControllerButtonExt.AnalogStickLeftYUp] = "Ins",
        [(int)ControllerButtonExt.AnalogStickLeftYDown] = "Del",
        [(int)ControllerButtonExt.AnalogStickRightXUp] = "=",
        [(int)ControllerButtonExt.AnalogStickRightXDown] = "-",
        [(int)ControllerButtonExt.AnalogStickRightYUp] = "PgUp",
        [(int)ControllerButtonExt.AnalogStickRightYDown] = "PgDown",
    };

    /// <summary>
    /// Built here, BELOW the two dictionaries, because static field initialisers run in declaration
    /// order and this one reads them.
    /// </summary>
    private static readonly IReadOnlyList<KeyBinding> defaultRows = Build();

    private static IReadOnlyList<KeyBinding> Build()
        => defaults.Keys
            .OrderBy(value => value)
            .Select(value => new KeyBinding(value, NameOf(value), DefaultKeyNameFor(value)))
            .ToList();
}

/// <summary>
/// PP16: the settings screen's Keys tab.
///
/// A grid of bindings, a Clear button and two checkboxes. The grid's rows come from
/// <see cref="KeyMap"/>, ordered by button value because a QMap is.
///
/// One thing the QML does that a port would not: the key dialog's callback writes the BUTTON'S TEXT
/// directly - `callback: (name) =&gt; text = name` - rather than letting the row re-read the mapping.
/// So the label changes because the dialog said so, not because the setting did. If the write failed
/// the screen would still show the new key. Reproduced as <see cref="Rebind"/> setting the row and
/// leaving the store to the caller, so the same order of events is available - and named, so nobody
/// mistakes the label for evidence.
/// </summary>
public sealed class KeySettingsViewModel : DialogViewModel
{
    private readonly ObservableCollection<KeyBinding> bindings = [];

    private bool keyboardEnabled = true;
    private bool mouseTouchEnabled = true;

    /// <summary>A tab with the default bindings.</summary>
    public KeySettingsViewModel()
    {
        foreach (KeyBinding row in KeyMap.Defaults)
            bindings.Add(row);
    }

    /// <summary>
    /// The tab as the store holds it. The two checkboxes come from PP2's declared table; the
    /// bindings come from a raw lookup, for the reason <see cref="KeyMap.Read"/> gives.
    /// </summary>
    public KeySettingsViewModel(IPreferences preferences, Func<string, string?>? keymapLookup = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        foreach (KeyBinding row in KeyMap.Read(keymapLookup ?? (_ => null)))
            bindings.Add(row);

        keyboardEnabled = preferences.GetBool("settings/keyboard_enabled");
        mouseTouchEnabled = preferences.GetBool("settings/mouse_touch_enabled");
    }

    protected override string ButtonProperty => nameof(KeyboardEnabled);

    /// <summary>The grid's rows, refilled in place for PP159's reason.</summary>
    public ObservableCollection<KeyBinding> Bindings => bindings;

    /// <summary>How many columns the grid's key navigation assumes.</summary>
    public const int Columns = 3;

    public bool KeyboardEnabled
    {
        get => keyboardEnabled;
        set => Set(ref keyboardEnabled, value);
    }

    public bool MouseTouchEnabled
    {
        get => mouseTouchEnabled;
        set => Set(ref mouseTouchEnabled, value);
    }

    /// <summary>
    /// A row rebound. Sets the row's key name and returns the settings key and value the store
    /// should receive - so the screen changes because this said so, which is the QML's own order and
    /// not an improvement on it.
    /// </summary>
    public (string Key, string Value) Rebind(int index, string keyName)
    {
        ArgumentNullException.ThrowIfNull(keyName);

        if (index < 0 || index >= bindings.Count)
            return ("", "");

        KeyBinding row = bindings[index];
        bindings[index] = row with { KeyName = keyName };

        return (KeyMap.StorageKeyFor(row.ButtonValue), keyName);
    }

    /// <summary>
    /// Clear, which puts every row back to its default rather than emptying the grid - the map is
    /// initialised from defaults and then overridden, so clearing the store restores them.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < bindings.Count && i < KeyMap.Defaults.Count; i++)
            bindings[i] = KeyMap.Defaults[i];
    }
}

/// <summary>
/// PP16: the Keys tab's rules where the Qt client states them.
/// </summary>
public static class KeySettingsSource
{
    /// <summary>The settings screen, or null outside a checkout.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Where the mapping and its storage key live.</summary>
    public static string? LocateSettingsCpp()
        => GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);

    /// <summary>Whether the tab is still driven by the misleadingly named property.</summary>
    public static bool TheGridComesFromControllerMapping(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("model: Chiaki.settings.controllerMapping", StringComparison.Ordinal);
    }

    /// <summary>Whether the storage key is still derived from the translatable display name.</summary>
    public static bool TheStorageKeyComesFromTheDisplayName(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                "auto button_name = GetChiakiControllerButtonName(chiaki_button).replace(' ', '_').toLower();",
                StringComparison.Ordinal)
            && cpp.Contains(
                "settings.setValue(\"keymap/\" + button_name, QKeySequence(key).toString());",
                StringComparison.Ordinal);
    }

    /// <summary>Whether that display name still comes from tr(), which is what makes it a defect.</summary>
    public static bool TheDisplayNameIsTranslated(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("return tr(\"Cross\");", StringComparison.Ordinal);
    }

    /// <summary>Whether the two X-axis names still contradict their enum names.</summary>
    public static bool TheHorizontalAxisNamesAreInverted(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                "ANALOG_STICK_LEFT_X_UP)    : return tr(\"Left Stick Right\");", StringComparison.Ordinal)
            && cpp.Contains(
                "ANALOG_STICK_LEFT_X_DOWN)  : return tr(\"Left Stick Left\");", StringComparison.Ordinal)
            && cpp.Contains(
                "ANALOG_STICK_LEFT_Y_UP)    : return tr(\"Left Stick Up\");", StringComparison.Ordinal);
    }

    /// <summary>Whether the mapping is still defaults-then-overrides rather than store-only.</summary>
    public static bool TheMapStartsFromDefaults(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("// Initialize with default values", StringComparison.Ordinal)
            && cpp.Contains("if(settings.contains(\"keymap/\" + button_name))", StringComparison.Ordinal);
    }

    /// <summary>Whether the key dialog still writes the button's text directly.</summary>
    public static bool TheCallbackWritesTheLabel(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("callback: (name) => text = name,", StringComparison.Ordinal);
    }

    /// <summary>Whether the grid's navigation still assumes three columns.</summary>
    public static bool TheGridIsThreeColumns(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains($"((index % {KeySettingsViewModel.Columns}) != 0)", StringComparison.Ordinal);
    }
}

