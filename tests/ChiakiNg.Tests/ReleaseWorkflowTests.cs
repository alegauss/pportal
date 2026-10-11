using System.Text.RegularExpressions;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP856: the release workflow builds the installer through the scripts a maintainer runs.
///
/// WorkflowParseTests already holds that release.yml is a workflow. What it cannot see is whether
/// the workflow packages by the same path as the desk - compile.cmd then package.cmd - or by a
/// second spelling of them, which is the copy that drifts. And the installer it attaches is named
/// in two files: the .iss writes it and the workflow picks it up, so a rename on one side is a
/// release with nothing to upload.
/// </summary>
public class ReleaseWorkflowTests
{
    private const string RelativePath = @".github\workflows\release.yml";

    /// <summary>The workflow with its comment lines removed, or null outside a checkout.</summary>
    private static string? Code()
    {
        if (SanitizerSource.LocateRelative(RelativePath) is not { } path)
            return null;

        return Regex.Replace(File.ReadAllText(path), @"(?m)^[ \t]*#.*$", "");
    }

    /// <summary>
    /// PP856: the two scripts, in order, as commands rather than as words in the comment header -
    /// which names both of them at length.
    /// </summary>
    [Fact]
    public void ItRunsCompileThenPackage()
    {
        if (Code() is not { } code)
            return;

        int compile = code.IndexOf("call compile.cmd", StringComparison.Ordinal);
        int package = code.IndexOf("call package.cmd", StringComparison.Ordinal);

        Assert.True(compile >= 0, "release.yml does not run compile.cmd");
        Assert.True(package >= 0, "release.yml does not run package.cmd");
        Assert.True(compile < package, "package.cmd needs the native tree compile.cmd deploys");
    }

    /// <summary>
    /// PP856: the file it uploads is the file the installer script writes. OutputDir is ..\build
    /// and OutputBaseFilename the name, so the pair is build\&lt;name&gt;.exe.
    /// </summary>
    [Fact]
    public void ItPicksUpTheInstallerTheScriptWrites()
    {
        if (Code() is not { } code
            || SanitizerSource.LocateRelative(@"scripts\chiaki-ng.iss") is not { } iss)
            return;

        Match name = Regex.Match(File.ReadAllText(iss), @"(?m)^OutputBaseFilename=(\S+)\s*$");
        Assert.True(name.Success, "chiaki-ng.iss names no OutputBaseFilename");

        Assert.Contains($@"build\{name.Groups[1].Value}.exe", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP856: a draft, carrying its digest, and only for a tag that names the version built - the
    /// three things that make a release something a person reviews before anyone downloads it.
    /// </summary>
    [Fact]
    public void ItDraftsAReleaseWithItsDigestForTheTagItBuilt()
    {
        if (Code() is not { } code)
            return;

        Assert.Contains("gh release create", code, StringComparison.Ordinal);
        Assert.Contains("--draft", code, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", code, StringComparison.Ordinal);
        Assert.Contains("contents: write", code, StringComparison.Ordinal);
        Assert.Contains("VersionInfo.ProductVersion", code, StringComparison.Ordinal);
        Assert.Contains("GITHUB_REF_TYPE -ne 'tag'", code, StringComparison.Ordinal);
    }
}
