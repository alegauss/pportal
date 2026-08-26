using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP88: the assertion that keeps the two clients redacting the same way.
///
/// The sanitiser's patterns exist twice - once as QRegularExpression literals in
/// gui/src/sessionlog.cpp, once as constants in <see cref="SessionLogSanitizer"/> - because
/// libchiaki has no regex engine, so there is no C translation unit both halves could share
/// without hand-rolling ten matchers. Duplication was therefore the choice; going unchecked was
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
    public static string? Locate() => LocateRelative(RelativePath);

    /// <summary>
    /// The file that marks the top of this checkout. Chosen because roadkeep owns it, so it is
    /// present in every clone and absent from every directory below the root.
    /// </summary>
    public const string RootAnchor = "roadkeep.toml";

    /// <summary>
    /// The repository root, or null when this is not running out of a checkout.
    ///
    /// The walk is upward rather than a fixed depth: the host builds into
    /// app\bin\&lt;config&gt;\&lt;tfm&gt;\&lt;rid&gt;, and a count of ".." is the kind of thing that
    /// survives exactly one layout change.
    ///
    /// PP22: from AppContext.BaseDirectory, not Assembly.Location, which is the empty string once
    /// the assembly is embedded in a single-file publish. Every drift check in this port starts
    /// here, so what that returned was not one broken check but all of them at once - and quietly,
    /// because a source file that cannot be found is reported as "could not run" by design.
    /// </summary>
    public static string? RepositoryRoot() => RepositoryRootFrom(AppContext.BaseDirectory);

    /// <summary>
    /// The same walk from a directory named by the caller, which is what makes it testable: a test
    /// cannot move AppContext.BaseDirectory, so a resolver that only ever reads it can be asserted
    /// about only from wherever the runner happens to sit.
    /// </summary>
    public static string? RepositoryRootFrom(string startDirectory)
    {
        ArgumentNullException.ThrowIfNull(startDirectory);

        for (string? dir = startDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, RootAnchor)))
                return dir;
        }

        return null;
    }

    /// <summary>
    /// Any repository file, resolved from the root above.
    ///
    /// PP276: from the ROOT, and not from the first directory going up that happens to hold a
    /// matching name. Those are the same answer for every multi-segment path this port asks for -
    /// gui\src\sessionlog.cpp cannot exist twice above the executable - and a different answer the
    /// moment a name is short. PP275 asked for .gitignore, and app\.gitignore sits between the
    /// host's output directory and the root, so the walk returned three rules about bin and obj and
    /// the check was green on a file it had never opened.
    ///
    /// One walk finds the root; everything else is a path built from it. A name that is not there
    /// answers null exactly as before, which is what a published host relies on.
    /// </summary>
    public static string? LocateRelative(string relativePath)
        => LocateRelative(relativePath, AppContext.BaseDirectory);

    /// <summary>
    /// The same resolution from a directory named by the caller. See
    /// <see cref="RepositoryRootFrom"/> for why the overload exists.
    /// </summary>
    public static string? LocateRelative(string relativePath, string startDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string? root = RepositoryRootFrom(startDirectory);
        if (root is null)
            return null;

        string candidate = Path.Combine(root, relativePath);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// A repository DIRECTORY, resolved the same way.
    ///
    /// PP382: separate from <see cref="LocateRelative(string)"/> rather than folded into it, and
    /// that is the whole point. That one ends in <c>File.Exists</c>, so every caller that handed it
    /// a directory got null and every rule built on one silently did not run - which is exactly
    /// what a locator returning null is supposed to mean and exactly why nobody noticed.
    ///
    /// The two sweeps over lib/src were the callers: PP374's byte-order width rule and PP378's
    /// unaligned-access rule. Both early-returned on null by design, both reported green, and
    /// neither had ever opened a file. Widening the file locator instead would make a path that is
    /// meant to be a file answer for a directory of the same name, so the kinds stay apart and a
    /// caller says which it wants.
    /// </summary>
    public static string? LocateDirectory(string relativePath)
        => LocateDirectory(relativePath, AppContext.BaseDirectory);

    /// <summary>The same, from a directory named by the caller.</summary>
    public static string? LocateDirectory(string relativePath, string startDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string? root = RepositoryRootFrom(startDirectory);
        if (root is null)
            return null;

        string candidate = Path.Combine(root, relativePath);
        return Directory.Exists(candidate) ? candidate : null;
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
