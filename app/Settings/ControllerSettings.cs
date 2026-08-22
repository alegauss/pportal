using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the Controllers tab's rumble combo, and the one label out of six that is not its own word.
///
/// settings.cpp keys a QMap by the C++ enum and stores the VALUE, so this is a
/// <see cref="StoredChoice"/> like the Video tab's. What makes it worth its own paragraph is that
/// exactly one pairing differs, and only in CASE: the combo says "Very Weak" and the store holds
/// "Very weak".
///
/// A port deriving the stored word from the label writes "Very Weak"; GetRumbleHapticsIntensity
/// looks that up with QMap::key(s, default), finds nothing, and answers Normal. So one of the six
/// choices silently does not stick, and only when the other client reads it back.
/// </summary>
public static class RumbleHapticsChoice
{
    /// <summary>The combo, its six words, and Normal as the default - which is index THREE.</summary>
    public static StoredChoice Intensity { get; } = new(
        "settings/rumble_haptics_intensity",
        new[] { "Off", "Very Weak", "Weak", "Normal", "Strong", "Very Strong" },
        // "Very weak" is settings.cpp's spelling and is deliberate here. The other five match
        // their labels exactly, which is what makes this one easy to miss.
        new[] { "Off", "Very weak", "Weak", "Normal", "Strong", "Very Strong" },
        defaultIndex: 3);
}

/// <summary>
/// PP16: the four dpad-touch shortcut combos, whose stored value IS the index.
///
/// Unlike the rumble combo beside them these store a number, and the number is a position in THIS
/// list rather than a controller button: the list is seventeen entries with "Not Used" at zero,
/// and it does NOT include L2, R2 or either analog stick, which the Keys tab's twenty-six do. So a
/// port storing a ChiakiControllerButton value writes something the Qt client reads as a different
/// button, or as none.
///
/// The four defaults spell the hint printed beside them, "(L1+R1+dpad Up)": 9, 10, 7 and 0.
/// </summary>
public static class DpadTouchShortcut
{
    /// <summary>The seventeen, in the QML's order. The index is the stored value.</summary>
    public static IReadOnlyList<string> Buttons { get; } =
    [
        "Not Used", "Cross", "Moon", "Box", "Pyramid",
        "Dpad Left", "Dpad Right", "Dpad Up", "Dpad Down",
        "L1", "R1", "L3", "R3", "Options", "Share", "Touchpad", "PS",
    ];

    /// <summary>Nothing bound. Index zero, and the fourth slot's default.</summary>
    public const int NotUsed = 0;

    /// <summary>The four keys, in the order the combos appear.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "settings/dpad_touch_shortcut1",
        "settings/dpad_touch_shortcut2",
        "settings/dpad_touch_shortcut3",
        "settings/dpad_touch_shortcut4",
    ];

    /// <summary>L1, R1, Dpad Up and nothing - which is the hint beside the row, read as numbers.</summary>
    public static IReadOnlyList<int> Defaults { get; } = [9, 10, 7, NotUsed];

    /// <summary>The label for a stored index, or "Not Used" for anything outside the list.</summary>
    public static string LabelFor(int index)
        => index >= 0 && index < Buttons.Count ? Buttons[index] : Buttons[NotUsed];
}

/// <summary>
/// PP16: the dpad touch increment, stored in HUNDREDTHS of a millimetre.
///
/// The slider runs 1 to 1079 and the label divides by a hundred, so the stored default of 30 is
/// the "(0.3 mm)" printed beside it. Nothing in the property's name says the unit, and a port that
/// stored millimetres would move the pointer a hundred times too far on every dpad press.
/// </summary>
public static class DpadTouchIncrementSetting
{
    public const string Key = "settings/dpad_touch_increment";

    public const int Minimum = 1;

    /// <summary>1079, which is 10.79 mm and not a round number in either unit.</summary>
    public const int Maximum = 1079;

    /// <summary>30 hundredths, printed as 0.3 mm.</summary>
    public const int Default = 30;

    /// <summary>The label: the stored value over a hundred, with no fixed decimal count.</summary>
    public static string Caption(int hundredths)
        => (hundredths / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + " mm";
}

/// <summary>
/// PP16: the true-haptics multiplier, whose middle is a BAND rather than a value.
///
/// The slider runs 0 to 2 in steps of 0.1 and the label reads "% console setting" - except between
/// 0.99 and 1.01, where it reads "console setting" with no number.
///
/// That band is not a display nicety. streamsession.cpp tests the same two numbers before it
/// decides whether to scale the haptic samples at all, so inside the band the multiplication is
/// SKIPPED rather than performed with a one. The label and the audio path share one threshold, and
/// a port that rounded in one place and compared exactly in the other would show "console setting"
/// while quietly rescaling, or the reverse.
///
/// Contrast the stream menu's zoom slider (PP10), whose named position is an EXACT -1. Two sliders,
/// two ways of naming a special value, and neither is the other's.
/// </summary>
public static class HapticOverrideSetting
{
    public const string Key = "settings/haptic_override";

    public const double Minimum = 0.0;

    public const double Maximum = 2.0;

    public const double Step = 0.1;

    /// <summary>The console's own intensity, which is the middle of the band and its default.</summary>
    public const double Default = 1.0;

    /// <summary>The band's floor, exclusive - streamsession.cpp's own number.</summary>
    public const double BandLow = 0.99;

    /// <summary>And its ceiling, exclusive.</summary>
    public const double BandHigh = 1.01;

    /// <summary>
    /// Whether this value means "leave it to the console". Inside the band the session does not
    /// scale at all, so this answers a question about behaviour and not only about a label.
    /// </summary>
    public static bool IsConsoleSetting(double value) => value > BandLow && value < BandHigh;

    /// <summary>The label, which drops the number entirely inside the band.</summary>
    public static string Caption(double value)
        => IsConsoleSetting(value)
            ? "console setting"
            : (value * 100).ToString("F0", CultureInfo.InvariantCulture) + " % console setting";
}

/// <summary>
/// PP16: the settings screen's Controllers tab, the seventh of the nine.
///
/// Two checkboxes here are written in DIFFERENT idioms, and the difference is upstream's:
/// Background Controller Events assigns the checkbox's own new state
/// (<c>settings.allowJoystickBackgroundEvents = checked</c>) while Dpad Touchpad Emulation flips
/// the SETTING (<c>= !settings.dpadTouchEnabled</c>). They agree as long as the two are already in
/// step; they part company the moment a write is refused or clamped, and only one of them would
/// then recover. Reproduced as two methods rather than one, so the difference is visible.
///
/// The five rows below the second checkbox are hidden when it is off - which is four combos and a
/// slider appearing together, not one control changing.
/// </summary>
public sealed class ControllerSettingsViewModel : DialogViewModel
{
    private bool backgroundEvents = true;
    private bool dpadTouchEnabled = true;
    private int dpadTouchIncrement = DpadTouchIncrementSetting.Default;
    private readonly int[] shortcuts = [.. DpadTouchShortcut.Defaults];
    private bool buttonsByPosition;
    private int rumbleIndex = RumbleHapticsChoice.Intensity.DefaultIndex;
    private double hapticOverride = HapticOverrideSetting.Default;

    /// <summary>The tab with every default, which is what a fresh install shows.</summary>
    public ControllerSettingsViewModel()
    {
    }

    /// <summary>The tab as the store holds it.</summary>
    public ControllerSettingsViewModel(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        backgroundEvents = preferences.GetBool("settings/allow_joystick_background_events");
        dpadTouchEnabled = preferences.GetBool("settings/dpad_touch_enabled");
        dpadTouchIncrement = preferences.GetInt(DpadTouchIncrementSetting.Key);
        buttonsByPosition = preferences.GetBool("settings/buttons_by_pos");
    }

    protected override string ButtonProperty => nameof(DpadTouchRowsVisible);

    /// <summary>Whether controller input is processed while the window is not in front.</summary>
    public bool BackgroundEvents
    {
        get => backgroundEvents;
        set => Set(ref backgroundEvents, value);
    }

    /// <summary>Whether the dpad drives the touchpad, which is what the five rows below follow.</summary>
    public bool DpadTouchEnabled
    {
        get => dpadTouchEnabled;
        set { Set(ref dpadTouchEnabled, value); Raise(nameof(DpadTouchRowsVisible)); }
    }

    /// <summary>The increment in hundredths of a millimetre, as the store holds it.</summary>
    public int DpadTouchIncrement
    {
        get => dpadTouchIncrement;
        set { Set(ref dpadTouchIncrement, value); Raise(nameof(DpadTouchIncrementCaption)); }
    }

    /// <summary>Whether the buttons are read by position rather than by their printed label.</summary>
    public bool ButtonsByPosition
    {
        get => buttonsByPosition;
        set => Set(ref buttonsByPosition, value);
    }

    /// <summary>The rumble combo's position, which the store holds as a word.</summary>
    public int RumbleIndex
    {
        get => rumbleIndex;
        set { Set(ref rumbleIndex, value); Raise(nameof(RumbleStored)); }
    }

    /// <summary>The multiplier, whose middle is a band.</summary>
    public double HapticOverride
    {
        get => hapticOverride;
        set { Set(ref hapticOverride, value); Raise(nameof(HapticOverrideCaption)); }
    }

    /// <summary>Whether the five dpad-touch rows are on show, which is one checkbox for all of them.</summary>
    public bool DpadTouchRowsVisible => DpadTouchEnabled;

    /// <summary>The increment's label, in millimetres.</summary>
    public string DpadTouchIncrementCaption
        => DpadTouchIncrementSetting.Caption(DpadTouchIncrement);

    /// <summary>The multiplier's label, which is a word inside the band and a percentage outside it.</summary>
    public string HapticOverrideCaption => HapticOverrideSetting.Caption(HapticOverride);

    /// <summary>What the store would hold for the rumble combo's current position.</summary>
    public string RumbleStored => RumbleHapticsChoice.Intensity.StoredFor(RumbleIndex);

    /// <summary>The four shortcut combos' positions, in the order they appear.</summary>
    public IReadOnlyList<int> Shortcuts => shortcuts;

    /// <summary>Sets one of the four. Out-of-range slots are ignored rather than throwing.</summary>
    public void SetShortcut(int slot, int buttonIndex)
    {
        if (slot < 0 || slot >= shortcuts.Length)
            return;

        shortcuts[slot] = buttonIndex;
        Raise(nameof(Shortcuts));
    }

    /// <summary>
    /// The background checkbox's write: the CONTROL's new state, assigned.
    /// </summary>
    public void SetBackgroundEventsFromCheckbox(bool isChecked) => BackgroundEvents = isChecked;

    /// <summary>
    /// And the dpad checkbox's: the SETTING, flipped. The argument is deliberately absent - the
    /// QML ignores the checkbox's own state here, and a port taking it would be writing the other
    /// idiom under this name.
    /// </summary>
    public void ToggleDpadTouch() => DpadTouchEnabled = !DpadTouchEnabled;
}

/// <summary>
/// PP16: the Controllers tab's rules where the Qt client states them.
/// </summary>
public static class ControllerSettingsSource
{
    /// <summary>The settings screen.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Where the rumble words and the dpad defaults live.</summary>
    public static string? LocateSettingsCpp()
        => GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);

    /// <summary>Where the haptic band is applied. Named, so PP278's sweep can see it.</summary>
    public const string StreamSessionRelativePath = @"gui\src\streamsession.cpp";

    /// <summary>Where the haptic band is applied rather than printed.</summary>
    public static string? LocateStreamSession()
        => SanitizerSource.LocateRelative(StreamSessionRelativePath);

    /// <summary>Whether the rumble intensity is still stored as a word rather than an index.</summary>
    public static bool TheRumbleIntensityIsStoredAsAWord(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
                "settings.setValue(\"settings/rumble_haptics_intensity\", intensities[intensity]);",
                StringComparison.Ordinal)
            && cpp.Contains("return intensities.key(s, intensity_default);", StringComparison.Ordinal);
    }

    /// <summary>Whether that word for Very Weak is still the lower-case one.</summary>
    public static bool VeryWeakIsStillSpeltWithASmallW(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("{ RumbleHapticsIntensity::VeryWeak, \"Very weak\"}", StringComparison.Ordinal);
    }

    /// <summary>And whether the combo beside it still shows the capitalised one.</summary>
    public static bool TheComboStillShowsTheCapitalisedOne(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("qsTr(\"Very Weak\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the four shortcut defaults still spell L1, R1, Dpad Up and nothing.</summary>
    public static bool TheFourShortcutDefaultsAreStillThese(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("\"settings/dpad_touch_shortcut1\", 9)", StringComparison.Ordinal)
            && cpp.Contains("\"settings/dpad_touch_shortcut2\", 10)", StringComparison.Ordinal)
            && cpp.Contains("\"settings/dpad_touch_shortcut3\", 7)", StringComparison.Ordinal)
            && cpp.Contains("\"settings/dpad_touch_shortcut4\", 0)", StringComparison.Ordinal);
    }

    /// <summary>Whether the increment is still stored in hundredths and divided at the label.</summary>
    public static bool TheIncrementIsStoredInHundredths(string cpp, string qml)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(qml);

        return cpp.Contains(
                $"\"settings/dpad_touch_increment\", {DpadTouchIncrementSetting.Default})",
                StringComparison.Ordinal)
            && qml.Contains("qsTr(\"%1 mm\").arg(parent.value / 100)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the haptic band is still the same two numbers in the label and in the audio path.
    /// Both are pinned, because the finding is that they are one threshold and not two.
    /// </summary>
    public static bool TheHapticBandIsSharedWithTheSession(string qml, string session)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(session);

        return qml.Contains("if(parent.value > 0.99 && parent.value < 1.01)", StringComparison.Ordinal)
            && session.Contains(
                "if(haptic_override > 0.99 && haptic_override < 1.01)", StringComparison.Ordinal);
    }

    /// <summary>Whether the two checkboxes on this tab are still written in different idioms.</summary>
    public static bool TheTwoCheckboxesAreWrittenDifferently(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                "onToggled: Chiaki.settings.allowJoystickBackgroundEvents = checked",
                StringComparison.Ordinal)
            && qml.Contains(
                "onToggled: Chiaki.settings.dpadTouchEnabled = !Chiaki.settings.dpadTouchEnabled",
                StringComparison.Ordinal);
    }
}
