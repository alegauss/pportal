using System.Text.RegularExpressions;

namespace ChiakiNg.Native;

/// <summary>
/// PP274: what the Inno Setup script says it packages, read back out of the script.
///
/// Three files have to agree about one payload and none of them can read the others.
/// <c>scripts\chiaki-ng.iss</c> defines the directory it compresses and the executable it makes
/// shortcuts for; <c>package.cmd</c> decides where that directory is written; and the csproj names
/// the executable that lands in it. Upstream's version of this file disagreed with all three - it
/// named <c>..\chiaki-ng-Win\chiaki.exe</c>, a directory no build creates and a client PP21 turned
/// off - and nothing said so, because an Inno Setup script is only read by ISCC and ISCC was never
/// run here.
///
/// It sits beside <see cref="PayloadScript"/> rather than in a packaging namespace of its own
/// because it asserts the same thing from the other end: that one is what goes INTO the payload,
/// this is what the payload is then handed to.
///
/// A field it cannot find yields the empty string, which fails every comparison the selftest makes
/// with it - PP272's rule, that a drift reader answers no to a file it stopped understanding rather
/// than finding nothing to disagree with.
/// </summary>
public static partial class InstallerScript
{
    /// <summary>The Inno Setup script, relative to the repository root.</summary>
    public const string RelativePath = @"scripts\chiaki-ng.iss";

    /// <summary>The project that names the executable, relative to the repository root.</summary>
    public const string ProjectRelativePath = @"app\ChiakiNg.csproj";

    /// <summary>The command that writes the payload, relative to the repository root.</summary>
    public const string CommandRelativePath = "package.cmd";

    /// <summary>
    /// The ignore list PP275 is about, which the packaging files must not appear in. Named rather
    /// than written at the call site so PP278's sweep can see it - it was the last literal in the
    /// tree, and the scan that forbids them found it.
    /// </summary>
    public const string IgnoreListRelativePath = ".gitignore";

    /// <summary>
    /// The executable the installer makes its shortcuts and its [Run] entry point at.
    /// </summary>
    public static string PackagedExecutable(string script)
        => Group(ExeNameRegex(), script);

    /// <summary>
    /// The directory the installer compresses, as the script spells it - relative to
    /// <c>scripts\</c>, which is where ISCC resolves it from.
    /// </summary>
    public static string PackagedDirectory(string script)
        => Group(AppPathRegex(), script);

    /// <summary>
    /// Whether the installer's version is read off the packaged executable rather than written into
    /// the script. The csproj keeps its informational version free of a commit suffix for exactly
    /// this, and a literal here would be the second place to update on every release.
    /// </summary>
    public static bool VersionComesFromThePayload(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return script.Contains("GetVersionComponents", StringComparison.Ordinal);
    }

    /// <summary>The executable the csproj produces, without its extension.</summary>
    public static string AssemblyName(string project)
        => Group(AssemblyNameRegex(), project);

    /// <summary>
    /// Where <c>package.cmd</c> writes the payload, repository-relative, with its own
    /// <c>%BUILD_DIR%</c> resolved. Both defaults are read rather than one assumed: the point of
    /// this comparison is that neither file is the authority, so neither may be guessed at.
    /// </summary>
    public static string PayloadDirectory(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        string payload = Group(PackageDirRegex(), command);
        string build = Group(BuildDirRegex(), command);
        if (payload.Length == 0 || build.Length == 0)
            return "";

        return payload.Replace("%BUILD_DIR%", build, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path in the script, made repository-relative. ISCC resolves a relative SourcePath from
    /// the directory holding the script, which is <c>scripts\</c>, so one leading <c>..\</c> is the
    /// repository root. A path that does not start there is left alone and will not compare equal,
    /// which is the answer wanted: an installer reaching somewhere else is the defect.
    /// </summary>
    public static string FromRepositoryRoot(string scriptRelative)
    {
        ArgumentNullException.ThrowIfNull(scriptRelative);
        return scriptRelative.StartsWith(@"..\", StringComparison.Ordinal)
            ? scriptRelative[3..]
            : scriptRelative;
    }

    private static string Group(Regex regex, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Match match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    [GeneratedRegex(@"#define\s+MyAppExeName\s+""([^""]*)""")]
    private static partial Regex ExeNameRegex();

    [GeneratedRegex(@"#define\s+MyAppPath\s+""([^""]*)""")]
    private static partial Regex AppPathRegex();

    [GeneratedRegex(@"<AssemblyName>([^<]*)</AssemblyName>")]
    private static partial Regex AssemblyNameRegex();

    [GeneratedRegex(@"set\s+""PACKAGE_DIR=([^""]*)""")]
    private static partial Regex PackageDirRegex();

    [GeneratedRegex(@"set\s+""BUILD_DIR=([^""]*)""")]
    private static partial Regex BuildDirRegex();
}
