using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the two logging switches, and the fact that neither of them stores a level.
///
/// The verbose checkbox is a bit SUBTRACTED from everything, not a level selected:
/// <c>GetLogLevelMask</c> starts at CHIAKI_LOG_ALL and clears CHIAKI_LOG_VERBOSE when the box is
/// off. So "off" is four levels and "on" is five, and a port that read it as a threshold - verbose
/// meaning "verbose and worse" - would turn Debug off along with it.
/// </summary>
public static class LogSwitches
{
    /// <summary>Whether the log is scrubbed before it reaches a file. ON by default.</summary>
    public const string SanitizeKey = "settings/log_sanitize";

    /// <summary>And whether the verbose level is included. OFF by default.</summary>
    public const string VerboseKey = "settings/log_verbose";

    /// <summary>Sanitising is the default, which is what this tab's own label says.</summary>
    public const bool SanitizeDefault = true;

    /// <summary>Verbose is not.</summary>
    public const bool VerboseDefault = false;

    /// <summary>
    /// The mask the session is given: everything, less the verbose bit when the box is off. Debug
    /// stays on either way, which is the part a threshold would get wrong.
    /// </summary>
    public static ChiakiLogLevel MaskFor(bool verbose)
        => verbose ? ChiakiLogLevel.All : ChiakiLogLevel.All & ~ChiakiLogLevel.Verbose;
}

/// <summary>
/// PP16: the profile the application is running under, which is not stored where the rest is.
///
/// <c>GetCurrentProfile</c> reads <c>settings/current_profile</c> out of DEFAULT_SETTINGS and not
/// out of the active settings object. It has to: the profile names the file everything else is
/// read from, so a copy inside that file could only be found after it had already been chosen.
///
/// The default profile is the EMPTY STRING and is shown as the word "default". A port storing the
/// word would create a profile actually called "default" beside the real one, and the two would
/// diverge silently - it is the same trick <see cref="SteamShortcut"/> plays with the shortcut's
/// name, read from the other side.
/// </summary>
public static class CurrentProfile
{
    /// <summary>The key, which lives in the default settings file whatever profile is loaded.</summary>
    public const string Key = "settings/current_profile";

    /// <summary>What the tab shows for the unnamed profile.</summary>
    public const string DefaultName = "default";

    /// <summary>The label: the profile's name, or the word for the one that has none.</summary>
    public static string Caption(string? profile)
        => "Current Profile: " + (string.IsNullOrEmpty(profile) ? DefaultName : profile);
}

/// <summary>
/// PP16: the settings screen's Config tab, the ninth and last of them.
///
/// Four buttons and two checkboxes, and the checkboxes are drawn differently from every other tab:
/// their default is INSIDE the control's own text - "Sanitize Logs (checked)" - where every other
/// tab puts the hint in a Label beside the control. A port that harmonised them would draw this
/// tab differently from upstream for no reason a user could name.
/// </summary>
public sealed class ConfigSettingsViewModel : DialogViewModel
{
    private string profile = "";
    private bool sanitizeLogs = LogSwitches.SanitizeDefault;
    private bool verboseLogs = LogSwitches.VerboseDefault;

    /// <summary>The tab with the defaults and the unnamed profile.</summary>
    public ConfigSettingsViewModel()
    {
    }

    /// <summary>
    /// The tab as the store holds it.
    /// </summary>
    /// <param name="preferences">The active profile's settings, which is where the two switches are.</param>
    /// <param name="defaultProfileName">
    /// The current profile, read from the DEFAULT settings rather than from
    /// <paramref name="preferences"/> - see <see cref="CurrentProfile"/>. Passed in rather than
    /// looked up here, because a view model that reached for a second store would hide exactly the
    /// distinction this parameter exists to make.
    /// </param>
    public ConfigSettingsViewModel(IPreferences preferences, string? defaultProfileName)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        profile = defaultProfileName ?? "";
        sanitizeLogs = preferences.GetBool(LogSwitches.SanitizeKey);
        verboseLogs = preferences.GetBool(LogSwitches.VerboseKey);
    }

    protected override string ButtonProperty => nameof(ProfileCaption);

    /// <summary>The profile's name, empty for the unnamed one.</summary>
    public string Profile
    {
        get => profile;
        set { Set(ref profile, value ?? ""); Raise(nameof(ProfileCaption)); }
    }

    /// <summary>Whether logs are scrubbed on the way to a file.</summary>
    public bool SanitizeLogs
    {
        get => sanitizeLogs;
        set => Set(ref sanitizeLogs, value);
    }

    /// <summary>Whether the verbose bit is in the mask.</summary>
    public bool VerboseLogs
    {
        get => verboseLogs;
        set { Set(ref verboseLogs, value); Raise(nameof(LogMask)); }
    }

    /// <summary>The heading, which names the unnamed profile rather than leaving a blank.</summary>
    public string ProfileCaption => CurrentProfile.Caption(Profile);

    /// <summary>The mask the session gets, which is everything less one bit.</summary>
    public ChiakiLogLevel LogMask => LogSwitches.MaskFor(VerboseLogs);

    /// <summary>The About button's text, which appends -ng to the application's own name.</summary>
    public static string AboutCaption(string applicationName)
    {
        ArgumentNullException.ThrowIfNull(applicationName);
        return $"About {applicationName}-ng";
    }
}

/// <summary>
/// PP16: the Config tab's rules where the Qt client states them.
/// </summary>
public static class ConfigSettingsSource
{
    /// <summary>The settings screen.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Where the two defaults are declared. Named, so PP278's sweep can see it.</summary>
    public const string SettingsHeaderRelativePath = @"gui\include\settings.h";

    /// <summary>Where the two defaults are declared.</summary>
    public static string? LocateSettingsHeader()
        => SanitizerSource.LocateRelative(SettingsHeaderRelativePath);

    /// <summary>Where the profile is read from the other store, and the mask is built.</summary>
    public static string? LocateSettingsCpp()
        => GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);

    /// <summary>Whether the current profile is still read out of the DEFAULT settings.</summary>
    public static bool TheProfileComesFromTheDefaultStore(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            $"default_settings.value(\"{CurrentProfile.Key}\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the empty profile is still shown as a word rather than stored as one.</summary>
    public static bool TheUnnamedProfileIsShownAsAWord(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            $"qsTr(\"Current Profile: {CurrentProfile.DefaultName}\")", StringComparison.Ordinal);
    }

    /// <summary>Whether verbose is still a bit cleared from ALL rather than a level chosen.</summary>
    public static bool VerboseIsABitClearedFromAll(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("uint32_t mask = CHIAKI_LOG_ALL;", StringComparison.Ordinal)
            && cpp.Contains("mask &= ~CHIAKI_LOG_VERBOSE;", StringComparison.Ordinal);
    }

    /// <summary>Whether the two defaults are still on and off respectively.</summary>
    public static bool TheTwoLogDefaultsAreStillThese(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains($"\"{LogSwitches.SanitizeKey}\", true", StringComparison.Ordinal)
            && header.Contains($"\"{LogSwitches.VerboseKey}\", false", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether this tab still puts each default INSIDE its checkbox's text, which is what makes it
    /// the one tab drawn differently from the other eight.
    /// </summary>
    public static bool TheDefaultsAreInsideTheCheckboxText(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("qsTr(\"Sanitize Logs (checked)\")", StringComparison.Ordinal)
            && qml.Contains("qsTr(\"Verbose Logging (unchecked)\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the About button still builds its text from the application's own name.</summary>
    public static bool TheAboutButtonAppendsNg(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("qsTr(\"About %1-ng\").arg(Qt.application.name)", StringComparison.Ordinal);
    }
}
