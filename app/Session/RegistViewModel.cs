using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP37: the registration dialog's logic, in a view model rather than behind a window.
///
/// The QML being replaced keeps its logic in C++ behind a property surface, which is why
/// qmlsettings and qmlbackend are testable objects and the markup is thin. A WPF port can
/// reproduce that with view models, or it can put the same logic in code-behind and lose it.
///
/// This is the first one, and it is the registration dialog because PP37's own example is its
/// PIN field - "a PIN field that enables the button one character early". That is not a
/// hypothetical: RegistDialog.qml validates the Remote Play PIN as [0-9]{8}, and a port that
/// enabled the button at seven produces a registration the console refuses, with the client
/// having said the input was fine.
///
/// Three rules, all of them from the QML rather than from taste:
///
///   the Remote Play PIN is exactly eight digits and is required;
///
///   the console PIN is empty OR exactly four - `^$|[0-9]{4}` - which is the one a naive port
///   gets wrong in the safe-looking direction, refusing an optional field that was left blank;
///
///   and the host is trimmed before it counts, so whitespace is not a host.
///
/// None of this needs a window, and that is the whole point: opening one to find out whether a
/// button is enabled is what makes eight screens untestable by construction.
/// </summary>
public sealed partial class RegistViewModel : INotifyPropertyChanged
{
    private string host = "";
    private string remotePlayPin = "";
    private string consolePin = "";
    private string onlineId = "";
    private string accountId = "";
    private bool onlineIdVisible;
    private bool accountIdVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The console's address. Trimmed before it counts - whitespace is not a host.</summary>
    public string Host
    {
        get => host;
        set => Set(ref host, value);
    }

    /// <summary>The eight-digit Remote Play PIN, as the console displays it.</summary>
    public string RemotePlayPin
    {
        get => remotePlayPin;
        set => Set(ref remotePlayPin, value);
    }

    /// <summary>The optional four-digit console PIN, for a console that has one set.</summary>
    public string ConsolePin
    {
        get => consolePin;
        set => Set(ref consolePin, value);
    }

    /// <summary>The PSN online id, required only while <see cref="OnlineIdVisible"/>.</summary>
    public string OnlineId
    {
        get => onlineId;
        set => Set(ref onlineId, value);
    }

    /// <summary>The PSN account id, required only while <see cref="AccountIdVisible"/>.</summary>
    public string AccountId
    {
        get => accountId;
        set => Set(ref accountId, value);
    }

    /// <summary>
    /// Which of the two PSN identifiers the chosen console generation asks for. Visibility is
    /// part of the rule and not a detail of the layout: the QML reads `!onlineId.visible ||
    /// onlineId.text.trim()`, so a hidden field does not block the button however empty it is.
    /// </summary>
    ///
    /// It raises CanRegister for the same reason every field does, and it has to: the console
    /// generation is a choice on this dialog, so changing it moves a required field out of the
    /// rule while the user is looking at the button. A silent property here leaves the button
    /// disabled for a field that is no longer on screen.
    /// </summary>
    public bool OnlineIdVisible
    {
        get => onlineIdVisible;
        set => SetVisible(ref onlineIdVisible, value);
    }

    /// <summary>The other one. Both hidden is legal - a PS4 before firmware 8 needs neither.</summary>
    public bool AccountIdVisible
    {
        get => accountIdVisible;
        set => SetVisible(ref accountIdVisible, value);
    }

    /// <summary>Whether the Remote Play PIN is acceptable: exactly eight digits.</summary>
    public bool RemotePlayPinValid => RemotePlayPinRegex().IsMatch(RemotePlayPin);

    /// <summary>
    /// Whether the console PIN is acceptable: empty, or exactly four digits.
    ///
    /// The empty alternative is the whole of it. A port that validated four digits and nothing
    /// else would refuse every console that has no PIN set, which is most of them - and it would
    /// do so by disabling a button, with nothing saying which field was at fault.
    /// </summary>
    public bool ConsolePinValid => ConsolePinRegex().IsMatch(ConsolePin);

    /// <summary>
    /// Whether the register button is enabled, which is the whole of what this class exists to
    /// make assertable.
    /// </summary>
    public bool CanRegister =>
        Host.Trim().Length > 0
        && RemotePlayPinValid
        && ConsolePinValid
        && (!OnlineIdVisible || OnlineId.Trim().Length > 0)
        && (!AccountIdVisible || AccountId.Trim().Length > 0);

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        value ??= "";
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // The button's state depends on every field, so every field raises it. A view model that
        // raised only its own property would leave a button that is correct and never repainted.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRegister)));
    }

    private void SetVisible(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRegister)));
    }

    [GeneratedRegex(@"^[0-9]{8}$")]
    private static partial Regex RemotePlayPinRegex();

    [GeneratedRegex(@"^$|^[0-9]{4}$")]
    private static partial Regex ConsolePinRegex();
}

/// <summary>
/// PP37: the dialog's rules as the QML states them, so the view model cannot drift from it.
/// </summary>
public static partial class RegistDialogSource
{
    /// <summary>The QML being replaced.</summary>
    public const string RelativePath = @"gui\src\qml\RegistDialog.qml";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Every validator pattern the dialog declares, in the order it declares them.</summary>
    public static IReadOnlyList<string> Validators(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return [.. ValidatorRegex().Matches(qml).Select(m => m.Groups[1].Value)];
    }

    /// <summary>
    /// Whether the button's enabling still trims the host and still treats a hidden identifier as
    /// satisfied. Both halves are in one expression there, and both are easy to lose.
    /// </summary>
    public static bool ButtonRuleIsUnchanged(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("hostField.text.trim() && pin.acceptableInput && cpin.acceptableInput",
                StringComparison.Ordinal)
            && qml.Contains("(!onlineId.visible || onlineId.text.trim())", StringComparison.Ordinal)
            && qml.Contains("(!accountId.visible || accountId.text.trim())", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"regularExpression:\s*/([^/]+)/")]
    private static partial Regex ValidatorRegex();
}
