using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>One <c>value("key", default).toX()</c> call, as the Qt client spells it.</summary>
/// <param name="Key">The key literal.</param>
/// <param name="Default">The default expression verbatim, or null where the call passes none.</param>
/// <param name="Conversion">The <c>to*</c> without its "to" - "String", "Bool", "Float".</param>
public readonly record struct PreferenceRead(string Key, string? Default, string Conversion);

/// <summary>
/// PP82: the extraction that produced <see cref="Preferences"/>, run as a check instead of once.
///
/// That table asserts its own row count, its own scopes and its own kinds, and every one of those
/// passes if it is internally consistent and completely wrong - because the thing it is a
/// transcription OF is a C++ file nothing here reads. 148 being asserted makes it worse: a
/// reviewer reads a number that specific as verified, and it is verified against the file that
/// states it.
///
/// So the C++ is read. A key the Qt client gained and the table did not is a preference the port
/// reports as absent on a store where the user set it, and nothing about that is visible from
/// either side alone.
///
/// Scanned rather than matched. A default expression contains parentheses of its own - QRect(),
/// fps_values[fps_default] - so the call's closing paren is found by balancing, and a regex that
/// stopped at the first ')' would silently read half the calls in the file.
/// </summary>
public static partial class PreferenceSource
{
    /// <summary>The two files the Qt client reads its preferences in.</summary>
    public static IReadOnlyList<string> RelativePaths { get; } =
        [@"gui\src\settings.cpp", @"gui\include\settings.h"];

    /// <summary>The files, or null when this is not running out of a checkout.</summary>
    public static IReadOnlyList<string>? Locate()
    {
        List<string?> found = [.. RelativePaths.Select(SanitizerSource.LocateRelative)];
        return found.Any(f => f is null) ? null : [.. found.Cast<string>()];
    }

    /// <summary>
    /// Every <c>value(...)</c> call in the text, with the default expression it passes and the
    /// conversion applied to it.
    ///
    /// A call whose result is not immediately converted is skipped: those exist, and what they
    /// read is a QVariant the caller inspects itself rather than a typed preference, so there is
    /// no kind for the table to disagree with.
    /// </summary>
    public static IReadOnlyList<PreferenceRead> ReadsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var reads = new List<PreferenceRead>();
        foreach (Match m in CallRegex().Matches(text))
        {
            // The scan starts just past the key literal, at the comma or the closing paren.
            int i = m.Index + m.Length;
            int depth = 1;
            while (i < text.Length && depth > 0)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
                i++;
            }

            if (depth != 0)
                continue;

            string inner = text[(m.Index + m.Length)..(i - 1)].Trim();
            string? dflt = inner.StartsWith(',') ? inner[1..].Trim() : null;
            if (dflt?.Length == 0)
                dflt = null;

            Match conv = ConversionRegex().Match(text, i);
            if (!conv.Success || conv.Index != i)
                continue;

            reads.Add(new PreferenceRead(m.Groups[1].Value, dflt, conv.Groups[1].Value));
        }

        return reads;
    }

    /// <summary>
    /// The kind a Qt conversion lands on in <see cref="QSettingsKind"/>.
    ///
    /// Not 1:1, and the one place it is not is worth naming: Qt's toFloat and toDouble both come
    /// back as Double here, because the store holds a decimal string either way and the width the
    /// caller wants is the caller's business. Anything else returns null and is reported as a
    /// conversion this mapping does not know rather than guessed at.
    /// </summary>
    public static QSettingsKind? KindOf(string conversion) => conversion switch
    {
        "String" => QSettingsKind.String,
        "Bool" => QSettingsKind.Bool,
        "Int" => QSettingsKind.Int,
        "UInt" => QSettingsKind.UInt,
        "Float" or "Double" => QSettingsKind.Double,
        "Rect" => QSettingsKind.Rect,
        "ByteArray" => QSettingsKind.ByteArray,
        _ => null,
    };

    /// <summary>
    /// Whether a default expression is a literal this can compare, as opposed to an enum table
    /// index or a call whose value lives elsewhere in the C++.
    ///
    /// The distinction is reported rather than hidden. A check that quietly skipped every
    /// indirect default would be measuring its own reach again, which is the whole of PP82.
    /// </summary>
    public static bool IsLiteral(string? expression)
        => expression is not null && LiteralRegex().IsMatch(expression);

    /// <summary>
    /// A literal default as the table would hold it, or null where it is not a literal. Qt's
    /// trailing type suffixes are dropped - 1.0f is 1.0 - because the store has no width.
    /// </summary>
    public static object? LiteralValue(string? expression)
    {
        if (!IsLiteral(expression))
            return null;

        string e = expression!.Trim();
        if (e == "true") return true;
        if (e == "false") return false;
        if (e.StartsWith('"')) return e[1..^1];

        e = e.TrimEnd('f', 'u', 'U', 'l', 'L');
        if (e.Contains('.'))
            return double.Parse(e, System.Globalization.CultureInfo.InvariantCulture);
        return long.Parse(e, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Matches up to and including the opening paren's key literal and the following comma-or-end,
    // leaving the scan above to balance the rest.
    [GeneratedRegex(@"\.value\(\s*""([^""]*)""")]
    private static partial Regex CallRegex();

    [GeneratedRegex(@"\s*\.to([A-Za-z]+)\s*\(\s*\)")]
    private static partial Regex ConversionRegex();

    [GeneratedRegex(@"^(true|false|""[^""]*""|-?\d+(\.\d+)?[fuUlL]*)$")]
    private static partial Regex LiteralRegex();
}
