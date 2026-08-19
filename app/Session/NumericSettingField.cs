using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP16: a numeric settings field, which is PP37's third example made concrete - "a settings
/// property that writes on every keystroke rather than on commit".
///
/// The Qt client's custom-resolution fields write on onEditingFinished and not on text change,
/// which is a decision rather than an implementation detail: writing per keystroke means typing
/// "1920" stores 1, then 19, then 192, and every one of those is a settings write and a value
/// some other binding may act on.
///
/// Three more rules that are easy to get wrong and silent when they are:
///
///   an INVALID entry is not refused - it commits ZERO and clears the field. A port that simply
///   rejected the input would leave the old value in the setting and the bad text on screen,
///   which is a field that looks edited and is not;
///
///   the range is 0..9999 inclusive, and zero is legal - it is what "unset" means here, which is
///   why clearing the field and typing nonsense land in the same place;
///
///   and the parse is JAVASCRIPT's. parseInt("1920px") is 1920, so the Qt client commits 1920 for
///   that text. int.TryParse refuses it, and a port using that would clear the field instead -
///   the same keystrokes, a different setting, and nothing saying so.
/// </summary>
public sealed partial class NumericSettingField
{
    /// <summary>The value the setting holds. Only ever changed by <see cref="Commit"/>.</summary>
    public int Value { get; private set; }

    /// <summary>What is in the box, which is not the setting until editing finishes.</summary>
    public string Text { get; set; } = "";

    /// <summary>The largest accepted value. 9999 is the Qt client's own bound.</summary>
    public const int Max = 9999;

    /// <summary>
    /// Whether the current text would be accepted. This is what tints the field red, and the Qt
    /// rule tints only while the text is NON-EMPTY - an empty box is not an error, it is a box
    /// nobody has typed in yet.
    /// </summary>
    public bool ShowsError => Text.Length > 0 && !IsValid(Text);

    /// <summary>
    /// Editing finished. Commits the parsed value, or commits ZERO and clears the box.
    ///
    /// The clearing is the part a port drops. Leaving the text alone would show a value the
    /// setting does not hold, which is worse than either accepting or refusing it.
    /// </summary>
    public void Commit()
    {
        if (IsValid(Text))
        {
            Value = ParseInt(Text)!.Value;
            return;
        }

        Value = 0;
        Text = "";
    }

    /// <summary>Typing does not write. Named so a caller cannot mistake it for a commit.</summary>
    public void Type(string text) => Text = text ?? "";

    /// <summary>Whether a string parses to something inside the range.</summary>
    public static bool IsValid(string text)
    {
        int? parsed = ParseInt(text);
        return parsed is >= 0 and <= Max;
    }

    /// <summary>
    /// JavaScript's parseInt, as far as this field needs it: optional sign, then leading digits,
    /// and everything after them ignored. Null where there are no leading digits at all, which is
    /// what NaN means on the other side.
    ///
    /// Written out rather than delegated to int.TryParse because the two disagree on exactly the
    /// input a user produces by accident - "1920px", "1080 " - and the Qt client's answer is the
    /// lenient one.
    /// </summary>
    public static int? ParseInt(string text)
    {
        if (text is null)
            return null;

        Match m = LeadingIntRegex().Match(text.TrimStart());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^([+-]?[0-9]+)")]
    private static partial Regex LeadingIntRegex();
}

/// <summary>
/// PP16: the settings screen's commit rules, read out of the QML.
/// </summary>
public static partial class SettingsFieldSource
{
    /// <summary>The screen being replaced.</summary>
    public const string RelativePath = @"gui\src\qml\SettingsDialog.qml";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// How many fields commit on editing finished, and how many on text change.
    ///
    /// The second number is the one that matters: the screen writes on commit everywhere, and a
    /// port that bound a property straight to a text box would write on every keystroke without
    /// anything looking different.
    /// </summary>
    public static (int OnFinished, int OnChanged) CommitPoints(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return (FinishedRegex().Matches(qml).Count, TextChangedRegex().Matches(qml).Count);
    }

    /// <summary>Whether an invalid entry still commits zero and clears the box.</summary>
    public static bool InvalidCommitsZeroAndClears(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return ZeroAndClearRegex().IsMatch(qml);
    }

    /// <summary>The upper bound the QML validates against.</summary>
    public static int? UpperBound(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        Match m = BoundRegex().Match(qml);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"onEditingFinished:")]
    private static partial Regex FinishedRegex();

    [GeneratedRegex(@"onTextChanged:|onTextEdited:")]
    private static partial Regex TextChangedRegex();

    [GeneratedRegex(@"=\s*0;\s*\r?\n\s*text\s*=\s*"""";")]
    private static partial Regex ZeroAndClearRegex();

    [GeneratedRegex(@"num >= 0 && num <= (\d+)")]
    private static partial Regex BoundRegex();
}
