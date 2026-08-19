using System.Reflection;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP88: the assertion that keeps the two clients redacting the same way.
///
/// The sanitiser's patterns exist twice - once as QRegularExpression literals in
/// gui/src/sessionlog.cpp, once as constants in <see cref="SessionLogSanitizer"/> - because
/// libchiaki has no regex engine, so there is no C translation unit both halves could share
/// without hand-rolling nine matchers. Duplication was therefore the choice; going unchecked was
/// not. This reads the C++ file and hands back the raw-string literals in it, so the selftest can
/// say the two texts are identical.
///
/// It only works in a checkout, which is honest rather than convenient: a published executable has
/// no gui/src beside it, and a check that cannot run says so instead of passing. Every commit is
/// made in a checkout, which is where the drift would be introduced.
/// </summary>
public static partial class SanitizerSource
{
    /// <summary>Where the C++ patterns live, relative to the repository root.</summary>
    public const string RelativePath = @"gui\src\sessionlog.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate()
    {
        string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        // Upwards rather than a fixed depth: the host builds into app\bin\<config>\<tfm>\<rid>,
        // and a count of ".." is the kind of thing that survives exactly one layout change.
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, RelativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// Every R"(...)" literal in the file, in the order it declares them. Raw strings are what the
    /// C++ side uses for these patterns precisely because a backslash means a backslash in them,
    /// which is what makes a character-for-character comparison meaningful at all.
    /// </summary>
    public static IReadOnlyList<string> PatternsIn(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        return RawStringRegex()
            .Matches(File.ReadAllText(filePath))
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    // The delimiter-less form, which is the only one this file uses. A pattern containing `)"`
    // would need a delimiter and would not be found here - which would fail the comparison rather
    // than pass it quietly, and that is the right direction for this check to break in.
    [GeneratedRegex(@"R""\((.*?)\)""", RegexOptions.Singleline)]
    private static partial Regex RawStringRegex();
}
