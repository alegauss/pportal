using System.Reflection;

namespace ChiakiNg.Session;

/// <summary>
/// PP278: every repository file a drift check reads, found rather than listed.
///
/// PP21 asserted this corpus for the first time, and filtered it to paths starting <c>gui\</c>
/// because Qt had just stopped being a build dependency and that was the tree at risk. The filter
/// outlived the reason: checks now read lib\, test\, shim\, scripts\ and app\, and none of those
/// was swept. What held them instead was a seventeen-entry array in SelfTest.cs, written by hand,
/// which is the shape PP271 names in its own comment - a list covers what exists the day it is
/// written.
///
/// Why not a naming convention
/// ---------------------------
/// The obvious sweep is over constants whose NAME says path - RelativePath, Anchor, Qml, Cpp. That
/// was tried and abandoned on contact with the tree: PsnTokenSource.AccountHeader is a fifth
/// spelling, and finding it meant the list of suffixes had the same defect as the list of paths.
/// A convention only guards what everyone remembered to spell that way.
///
/// So the test is on the VALUE, against the checkout itself: a constant is a repository path when
/// its first segment names a directory at the root, or when it names a file there. That excludes
/// what a name-based rule could not - SOFTWARE\Chiaki\Chiaki is a registry key, C:\msys64 is
/// absolute, and mingw64\include\SDL2\SDL_gamecontroller.h is relative to MSYS2 and not to this
/// repository, which is exactly why it must not be asserted as one.
/// </summary>
public static class DriftCorpus
{
    /// <summary>
    /// Where the FEC vectors are read from. Declared here rather than at the call site because
    /// nothing else models the file, and a raw literal in a locator call is unreachable by any
    /// sweep - which is the hole this class closes.
    /// </summary>
    public const string BitstreamRelativePath = @"test\bitstream.c";

    /// <summary>Where the key-exchange vectors live.</summary>
    public const string GkCryptRelativePath = @"test\gkcrypt.c";

    /// <summary>Where the registration vectors live.</summary>
    public const string RegistRelativePath = @"test\regist.c";

    /// <summary>Where the takion vectors and the allocation budget live.</summary>
    public const string TakionTestRelativePath = @"test\takion.c";

    /// <summary>
    /// PP279: the repository files that sit at the root, named because they cannot be recognised
    /// any other way.
    ///
    /// Everything else in the corpus is judged on SHAPE - a path of two segments or more is a
    /// candidate when its first segment is a directory at the root, so a file that has been deleted
    /// is still a candidate and is then reported missing. A one-segment path has no first directory
    /// to test, and nothing separates roadkeep.toml from any other constant carrying a dot except
    /// whether a file of that name is there. PP278 asked exactly that, which meant the predicate was
    /// asking the question the assertion existed to answer: a deleted root file stopped being
    /// recognised instead of being reported, and the count floor was too loose to notice one going.
    ///
    /// Five names is a list, and PP278 deleted a list of seventeen. The difference is what the list
    /// is OF: the root of a repository changes on the order of once a year, where its drift sources
    /// changed three times in three tasks. And it is not the only guard - a constant naming a root
    /// file that is missing from here is itself reported, so the list cannot quietly fall behind.
    /// </summary>
    public static IReadOnlyList<string> RootFiles { get; } =
    [
        ".gitignore", "CMakeLists.txt", "package.cmd", "roadkeep.toml", "vcpkg.json",
    ];

    /// <summary>
    /// Constants that name a file at the root which <see cref="RootFiles"/> does not list. Empty is
    /// the only acceptable answer: each one is a path the corpus is silently not guarding.
    ///
    /// This is the half that keeps the list honest, and it is deliberately the weaker direction -
    /// it can only see a root file that EXISTS, so it catches "declared and forgotten" rather than
    /// "declared and already gone". The latter fails the declaring type's own check instead.
    /// </summary>
    public static IReadOnlyList<string> UndeclaredRootFiles()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
            return [];

        return
        [
            .. Constants()
                .Where(value =>
                    !value.Contains('\\', StringComparison.Ordinal)
                    && !value.Contains('/', StringComparison.Ordinal)
                    && value.Contains('.', StringComparison.Ordinal)
                    && !Path.IsPathRooted(value)
                    && File.Exists(Path.Combine(root, value))
                    && !RootFiles.Contains(value, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Every repository path declared as a constant anywhere in this assembly, deduplicated and
    /// sorted. Empty outside a checkout, where there is no root to test a path against and no
    /// corpus to speak of.
    /// </summary>
    public static IReadOnlyList<string> Declared()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
            return [];

        var paths = new SortedSet<string>(
            Constants().Where(value => IsRepositoryPath(value, root)),
            StringComparer.OrdinalIgnoreCase);

        return [.. paths];
    }

    /// <summary>Every public constant string in this assembly, paths and everything else.</summary>
    private static IEnumerable<string> Constants()
    {
        foreach (Type type in typeof(SanitizerSource).Assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                if (field.GetRawConstantValue() is string value)
                    yield return value;
            }
        }
    }

    /// <summary>
    /// The declared paths that are not on disk. Empty is the only acceptable answer in a checkout,
    /// and outside one there is no corpus, so it is empty for that reason instead.
    ///
    /// A DIRECTORY counts as present, which is not a detail. CurlSemanticsSource.CoreGlob is
    /// <c>lib\src</c>, and PP271 records what happened when it was handed to a resolver that tests
    /// File.Exists: null on every run, and the three checks beneath it returned early and asserted
    /// nothing. The port's answer was to pair a directory with a file anchor, and both spellings are
    /// worth guarding - a directory that moved breaks the glob exactly as a file that moved breaks
    /// the read.
    /// </summary>
    public static IReadOnlyList<string> Missing()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
            return [];

        return
        [
            .. Declared().Where(relative =>
            {
                string full = Path.Combine(root, relative);
                return !File.Exists(full) && !Directory.Exists(full);
            }),
        ];
    }

    /// <summary>
    /// Whether a constant names a file or directory in this repository.
    ///
    /// Two segments or more: the first must be a directory at the root, which is a test on the
    /// SHAPE - a missing file under gui\ is still a candidate, and is then reported missing.
    ///
    /// One segment: it must be in <see cref="RootFiles"/>. PP279 - this used to ask whether the file
    /// was there, which is the question the assertion exists to answer, so a deleted root file left
    /// the corpus rather than failing it. Membership is a shape test like the one above, and the
    /// list is kept honest by <see cref="UndeclaredRootFiles"/> from the other side.
    /// </summary>
    public static bool IsRepositoryPath(string value, string root)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(root);

        // An absolute path is somewhere else by construction, and this catches "\" and "C:\msys64"
        // as well as the obvious cases.
        if (value.Length == 0 || Path.IsPathRooted(value))
            return false;

        // The two-argument overload, explicitly: Split('\\', '/') binds to the params array but
        // reads like the (separator, count) overload, and both analyzers here object to whichever
        // of the short forms is written.
        string[] segments = value.Split(['\\', '/'], StringSplitOptions.None);
        if (Array.Exists(segments, s => s.Length == 0 || s == ".." || s == "."))
            return false;

        return segments.Length > 1
            ? Directory.Exists(Path.Combine(root, segments[0]))
            : RootFiles.Contains(value, StringComparer.OrdinalIgnoreCase);
    }
}
