using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>A view model that raises its own property and the button's, for the reason in PP37.</summary>
public abstract class DialogViewModel : INotifyPropertyChanged
{
    /// <summary>The name of the property the button binds to. Raised on every change.</summary>
    protected abstract string ButtonProperty { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets a field and raises both it and the button.
    ///
    /// Both, always. A view model that raised only the property that changed leaves a button
    /// which is correct and never repainted - and that looks exactly like validation that is
    /// wrong, which is the worst kind of bug to be told about second-hand.
    /// </summary>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ButtonProperty));
    }

    /// <summary>
    /// Raises one more property, for a computed one that is not the button.
    ///
    /// <see cref="Set{T}"/> covers the field and the button because those are the two every
    /// dialog has. A screen whose mode also decides what is VISIBLE has a third, and an
    /// unraised visibility is the same stuck-repaint bug one step further out.
    /// </summary>
    protected void Raise(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// PP14: adding a console by address, when discovery cannot find it.
///
/// Two conditions, and the second is the one a port invents wrongly. The Qt combo represents
/// "no console chosen" as an INDEX OF -1 rather than as a null or an empty selection, so the
/// rule is `consoleCombo.model[currentIndex].index != -1`. A port that treated the absence as a
/// null reference would enable the button on a fresh dialog, where the combo has a selection and
/// that selection means nothing.
/// </summary>
public sealed class ManualHostViewModel : DialogViewModel
{
    private string host = "";
    private int registeredIndex = NoConsole;

    /// <summary>What the combo carries when nothing has been chosen.</summary>
    public const int NoConsole = -1;

    protected override string ButtonProperty => nameof(CanAdd);

    /// <summary>The address typed in. Trimmed before it counts, as everywhere else.</summary>
    public string Host
    {
        get => host;
        set => Set(ref host, value ?? "");
    }

    /// <summary>The chosen registered console, or <see cref="NoConsole"/>.</summary>
    public int RegisteredIndex
    {
        get => registeredIndex;
        set => Set(ref registeredIndex, value);
    }

    public bool CanAdd => Host.Trim().Length > 0 && RegisteredIndex != NoConsole;
}

/// <summary>
/// PP14: the console PIN on its own, which is NOT the same rule as the one in registration.
///
/// RegistDialog validates the console PIN as `^$|[0-9]{4}` - empty or four digits, because the
/// field is optional there. ConsolePinDialog validates it as `[0-9]{4}`, with no empty
/// alternative, because the dialog exists only when a PIN is being asked for.
///
/// The same value, required in one place and optional in another. A port that shared one
/// validator between the two dialogs would break whichever it did not write it for - and the
/// symptom is a button, so nothing says which rule was applied.
/// </summary>
public sealed partial class ConsolePinViewModel : DialogViewModel
{
    private string pin = "";

    protected override string ButtonProperty => nameof(CanSubmit);

    public string Pin
    {
        get => pin;
        set => Set(ref pin, value ?? "");
    }

    public bool CanSubmit => PinRegex().IsMatch(Pin);

    [GeneratedRegex(@"^[0-9]{4}$")]
    private static partial Regex PinRegex();
}

/// <summary>Which of the profile dialog's three jobs it is doing.</summary>
public enum ProfileDialogMode
{
    /// <summary>Switching to an existing profile.</summary>
    Switch,
    /// <summary>Creating one, so the name field is showing.</summary>
    Create,
    /// <summary>Deleting one, so the delete box is showing.</summary>
    Delete,
}

/// <summary>
/// PP14: profiles, which is one dialog doing three jobs and therefore three button rules.
///
/// The third is the subtle one. The combo offers "default" for the profile whose stored name is
/// the EMPTY STRING, so selecting "default" while the current profile is "" is a no-op the button
/// has to refuse - and the two spellings are not equal, which is what makes it easy to miss.
///
/// Deleting needs nothing: a profile is chosen in the combo and the box is ticked, so the button
/// is enabled the moment the box is. That is not laxity, it is the dialog having no second thing
/// to ask for.
/// </summary>
public sealed class ProfileViewModel : DialogViewModel
{
    private ProfileDialogMode mode = ProfileDialogMode.Switch;
    private bool deleteChecked;
    private string newName = "";
    private string selected = "";
    private string current = "";

    /// <summary>What the combo shows for the profile stored as the empty string.</summary>
    public const string DefaultProfile = "default";

    protected override string ButtonProperty => nameof(CanAccept);

    public ProfileDialogMode Mode
    {
        get => mode;
        set => Set(ref mode, value);
    }

    /// <summary>Whether the delete box is ticked. Only meaningful in <see cref="ProfileDialogMode.Delete"/>.</summary>
    public bool DeleteChecked
    {
        get => deleteChecked;
        set => Set(ref deleteChecked, value);
    }

    /// <summary>The name typed for a new profile.</summary>
    public string NewName
    {
        get => newName;
        set => Set(ref newName, value ?? "");
    }

    /// <summary>The profile chosen in the combo, as the combo spells it - so "default", not "".</summary>
    public string Selected
    {
        get => selected;
        set => Set(ref selected, value ?? "");
    }

    /// <summary>The profile in use, as SETTINGS spell it - so "", not "default".</summary>
    public string Current
    {
        get => current;
        set => Set(ref current, value ?? "");
    }

    public bool CanAccept => Mode switch
    {
        ProfileDialogMode.Delete => DeleteChecked,
        ProfileDialogMode.Create => NewName.Trim().Length > 0,

        // Switching to the profile already in use is refused, and "default" against "" is that
        // same case wearing the combo's spelling rather than the setting's.
        _ => !(Selected == DefaultProfile && Current.Length == 0) && Selected != Current,
    };
}

/// <summary>
/// PP14: the three dialogs' rules as their QML states them.
/// </summary>
public static partial class DialogSource
{
    /// <summary>The QML file for a dialog, or null outside a checkout.</summary>
    public static string? Locate(string dialog)
        => SanitizerSource.LocateRelative(Path.Combine("gui", "src", "qml", dialog + ".qml"));

    /// <summary>Whether the manual host dialog still represents "nothing chosen" as index -1.</summary>
    public static bool ManualHostUsesMinusOne(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "hostField.text.trim() && (consoleCombo.model[consoleCombo.currentIndex].index != -1)",
            StringComparison.Ordinal);
    }

    /// <summary>The validator pattern a dialog declares, or null where it declares none.</summary>
    public static string? Validator(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        Match m = ValidatorRegex().Match(qml);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Whether the profile dialog still refuses "default" against an empty current profile.</summary>
    public static bool ProfileRefusesDefaultAgainstEmpty(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            @"!(profileComboBox.model[profileComboBox.currentIndex] == ""default"" && Chiaki.settings.currentProfile == """")",
            StringComparison.Ordinal);
    }

    [GeneratedRegex(@"regularExpression:\s*/([^/]+)/")]
    private static partial Regex ValidatorRegex();
}
