using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP63: the Qt client built once WITH QtWebEngine, so PP46 has a "before" to measure.
///
/// scripts\reference-build-msvc.ps1 is that build: MSVC and clang-cl, a worktree of the commit the
/// WebEngine-absent row was measured on, and measure-startup at the end. Two things are held here.
/// The row it produced says Chromium was in the tree - measure-startup writes that only where it
/// found one, and returns 2 otherwise. And nothing in the gate calls the script, because a second
/// toolchain that ordinary work starts using is a build split in two that the port then keeps
/// green for ever.
/// </summary>
public class ReferenceBuildTests
{
    private const string Script = @"scripts\reference-build-msvc.ps1";
    private const string PresentRow = @"tools\measure-startup\qt-build-webengine-present.json";

    /// <summary>
    /// PP63: every file the gate runs, plus the workflows. A missing one is skipped rather than
    /// failed - the files that MUST exist are compile.cmd's own preflight's business.
    /// </summary>
    private static readonly string[] GateFiles =
    [
        "compile.cmd",
        "test.cmd",
        "package.cmd",
        @"scripts\build-windows.sh",
        @"scripts\test-windows.sh",
        @"scripts\deploy-windows-msys2.sh",
        @".github\workflows\build.yml",
        @".github\workflows\release.yml",
    ];

    /// <summary>
    /// PP63: the measured row carries Chromium. Without the reference build there is no such row
    /// at all, and a row taken on the MinGW build says false in both fields.
    /// </summary>
    [Fact]
    public void TheReferenceRowWasMeasuredWithWebEnginePresent()
    {
        if (SanitizerSource.LocateRelative(PresentRow) is not { } path)
            return;

        string row = File.ReadAllText(path);

        Assert.Contains("\"webengine_present\":true", row);
        Assert.Contains("\"is_before_baseline\":true", row);
        Assert.Contains("\"cold_measured\":true", row);
    }

    /// <summary>
    /// PP63: the script exists, so the check below is not vacuously green on a deleted file.
    /// </summary>
    [Fact]
    public void TheReferenceBuildScriptExists()
    {
        if (SanitizerSource.LocateRelative("compile.cmd") is null)
            return;

        Assert.NotNull(SanitizerSource.LocateRelative(Script));
    }

    /// <summary>
    /// PP63: no gate names the reference build. compile.cmd stays the tree's only build.
    /// </summary>
    [Fact]
    public void NoGateRunsTheReferenceBuild()
    {
        foreach (string relative in GateFiles)
        {
            if (SanitizerSource.LocateRelative(relative) is not { } path)
                continue;

            Assert.DoesNotContain(
                "reference-build-msvc", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
    }
}
