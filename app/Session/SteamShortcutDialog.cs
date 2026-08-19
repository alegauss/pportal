using System.Text.RegularExpressions;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP19: the non-Steam shortcut dialog - the last of the tail, and the one that writes outside the
/// application.
///
/// It asks for a game name, optional launch options and an optional Steam base path, then hands them
/// to createSteamShortcut. Four things about it are behaviour rather than layout:
///
///   the button rule is the trimmed-non-empty-and-not-in-flight shape PP145 found on the paste form,
///   which is the third screen to use it - so it is the same rule and not a coincidence;
///
///   the defaults are built from the CURRENT PROFILE by truthiness. `currentProfile ? a : b` treats
///   the empty string as absent, which is the same "" against "default" distinction PP14 found in
///   the profile dialog - and the name gets a trailing space before the profile where the launch
///   option gets an equals sign;
///
///   "stop asking" is written only on SUCCESS, from inside the progress callback. A failed creation
///   leaves the prompt to come back, which is careful and is the opposite of what a port writing the
///   preference on accept would do;
///
///   and closing chains into the PSN prompt - the SAME chain RemindDialog.qml has, with the same
///   four-token check, written out a second time. The port keeps one copy: the follow-up is
///   <see cref="RemindFollowUp"/> and the token test is
///   <see cref="RemindDialogViewModel.PsnIsLinked"/>, because two files holding one answer is how
///   PP93 got three.
/// </summary>
public sealed class SteamShortcutViewModel : DialogViewModel
{
    /// <summary>What the name field starts as when no profile is in use.</summary>
    public const string BaseName = "chiaki-ng";

    private string name;
    private string options;
    private string steamBasePath = "";
    private bool opening;
    private bool succeeded;

    /// <summary>A dialog for a profile, or for none when it is the empty string.</summary>
    /// <param name="currentProfile">
    /// The profile as SETTINGS spell it - so "" for the default one, not "default". The QML tests it
    /// for truthiness, so an empty name takes the no-profile branch.
    /// </param>
    /// <param name="fromReminder">
    /// Whether this was opened by the remind prompt, which is the only case where closing chains.
    /// </param>
    public SteamShortcutViewModel(string currentProfile = "", bool fromReminder = false)
    {
        CurrentProfile = currentProfile ?? "";
        FromReminder = fromReminder;

        name = DefaultName(CurrentProfile);
        options = DefaultOptions(CurrentProfile);
    }

    /// <summary>The profile the defaults were built from.</summary>
    public string CurrentProfile { get; }

    /// <summary>Whether the remind prompt opened this.</summary>
    public bool FromReminder { get; }

    protected override string ButtonProperty => nameof(CanCreate);

    /// <summary>
    /// The name field's default: the application name, and the profile after a SPACE where there is
    /// one. A profile named "couch" gives "chiaki-ng couch".
    /// </summary>
    public static string DefaultName(string? currentProfile)
        => string.IsNullOrEmpty(currentProfile) ? BaseName : BaseName + " " + currentProfile;

    /// <summary>
    /// The launch options' default: `--profile=` and the profile, or NOTHING - not a flag with an
    /// empty value, which is what a port filling in a template would produce.
    /// </summary>
    public static string DefaultOptions(string? currentProfile)
        => string.IsNullOrEmpty(currentProfile) ? "" : "--profile=" + currentProfile;

    /// <summary>The Steam game name.</summary>
    public string Name
    {
        get => name;
        set => Set(ref name, value ?? "");
    }

    /// <summary>The launch options, which are optional and are not validated.</summary>
    public string Options
    {
        get => options;
        set => Set(ref options, value ?? "");
    }

    /// <summary>
    /// A Steam base path the user picked, or the empty string. Optional, and passed through as-is -
    /// the QML shows whatever the chooser returned and does not check it.
    /// </summary>
    public string SteamBasePath
    {
        get => steamBasePath;
        set => Set(ref steamBasePath, value ?? "");
    }

    /// <summary>Whether a creation is in flight. Cleared only when the callback reports done.</summary>
    public bool Opening
    {
        get => opening;
        set => Set(ref opening, value);
    }

    /// <summary>Whether a creation succeeded, which is the only thing that stops the prompt.</summary>
    public bool Succeeded => succeeded;

    /// <summary>The log the progress callback appends to.</summary>
    public IList<string> Log { get; } = [];

    /// <summary>The preference a success writes, and only a success.</summary>
    public const string AskKey = RemindDialogViewModel.SteamShortcutAskKey;

    /// <summary>The key that has been written, or null while nothing has succeeded.</summary>
    public string? StoppedAskingKey { get; private set; }

    /// <summary>Whatever the closure asked for, after <see cref="Closed"/>.</summary>
    public RemindFollowUp FollowUp { get; private set; }

    /// <summary>Trimmed non-empty, and no creation already running. The options are not part of it.</summary>
    public bool CanCreate => Name.Trim().Length > 0 && !Opening;

    /// <summary>What the shortcut is created with: both fields trimmed, the base path as-is.</summary>
    public (string Name, string Options, string BasePath) Request()
        => (Name.Trim(), Options.Trim(), SteamBasePath);

    /// <summary>Create pressed. The button goes out until the callback says done.</summary>
    public void Accept()
    {
        Opening = true;
        Raise(nameof(CanCreate));
    }

    /// <summary>
    /// One progress report. `ok` is what writes the preference - and it is checked before `done`, so
    /// a run that reports success and then keeps logging has already stopped the prompt.
    /// </summary>
    public void Progress(string message, bool ok, bool done)
    {
        if (ok)
        {
            succeeded = true;
            StoppedAskingKey = AskKey;
            Raise(nameof(Succeeded));
        }

        Log.Add(message ?? "");

        if (done)
            Opening = false;
    }

    /// <summary>
    /// Closing, which chains into the PSN prompt exactly as the remind dialog's closure does - and
    /// only when the remind dialog is what opened this.
    /// </summary>
    public void Closed(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!FromReminder || !preferences.GetBool(RemindDialogViewModel.RemotePlayAskKey))
            return;

        FollowUp = RemindDialogViewModel.PsnIsLinked(preferences)
            ? RemindFollowUp.ClearRemotePlayAsk
            : RemindFollowUp.ShowPsnPrompt;
    }
}

/// <summary>
/// PP19: the shortcut dialog's rules where the QML states them.
/// </summary>
public static partial class SteamShortcutSource
{
    /// <summary>The dialog, or null outside a checkout.</summary>
    public static string? Locate() => DialogSource.Locate("SteamShortcutDialog");

    /// <summary>Whether the button still needs a trimmed name and no creation in flight.</summary>
    public static bool TheButtonNeedsATrimmedNameAndNoRun(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("buttonEnabled: name.text.trim() && !opening", StringComparison.Ordinal);
    }

    /// <summary>Whether the two defaults are still built from the profile by truthiness.</summary>
    public static bool TheDefaultsComeFromTheProfile(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return NameDefaultRegex().IsMatch(qml) && OptionsDefaultRegex().IsMatch(qml);
    }

    /// <summary>Whether "stop asking" is still written only inside the success branch.</summary>
    public static bool OnlySuccessStopsAsking(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return SuccessRegex().IsMatch(qml);
    }

    /// <summary>
    /// Whether the PSN chain is still duplicated here as well as in the remind dialog. Asserted as
    /// STILL TRUE: the port holds one copy, and that is a decision rather than an oversight only
    /// while the second copy is known to exist.
    /// </summary>
    public static bool ThePsnChainIsDuplicatedHere(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                "if(dialog.fromReminder && Chiaki.settings.remotePlayAsk)", StringComparison.Ordinal)
            && qml.Contains("root.showRemindDialog(", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.remotePlayAsk = false;", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"text: Chiaki\.settings\.currentProfile \? qsTr\(""chiaki-ng ""\) \+ "
        + @"Chiaki\.settings\.currentProfile\s*: qsTr\(""chiaki-ng""\)")]
    private static partial Regex NameDefaultRegex();

    [GeneratedRegex(
        @"text: Chiaki\.settings\.currentProfile \? qsTr\(""--profile=""\) \+ "
        + @"Chiaki\.settings\.currentProfile : """"")]
    private static partial Regex OptionsDefaultRegex();

    [GeneratedRegex(
        @"if\(ok\)\s*\r?\n\s*\{\s*\r?\n\s*succeeded = true;\s*\r?\n\s*"
        + @"Chiaki\.settings\.addSteamShortcutAsk = false;")]
    private static partial Regex SuccessRegex();
}
