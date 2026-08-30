using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP532: no line of the gate credits a Qt client the run did not build.
///
/// <see cref="EveryLineThatClaimsTheClientChecksFirst"/> runs against this checkout. The rest
/// exercise the rule against text a test writes - including the two lines as they stood before
/// this shipped, because a sweep that cannot fail on those holds nothing.
/// </summary>
public class CompileMessagesTests
{
    private static string? Source()
    {
        string? path = CompileMessages.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>THE RULE, against the gate itself.</summary>
    [Fact]
    public void EveryLineThatClaimsTheClientChecksFirst()
    {
        if (Source() is not { } source)
            return;

        var claims = CompileMessages.Claims(source);

        Assert.NotEmpty(claims);
        Assert.All(claims, c => Assert.True(c.Guarded,
            $"compile.cmd:{c.Line} claims the Qt client with nothing checking whether one was "
            + $"built: {c.Text}"));
    }

    /// <summary>
    /// Both halves of the failure line are there, so the message tells the truth either way
    /// rather than by being vague enough to survive both.
    /// </summary>
    [Fact]
    public void TheFailureLineHasAHalfForEachKindOfBuild()
    {
        if (Source() is not { } source)
            return;

        Assert.Contains("the Qt client built, the .NET host did not", source, StringComparison.Ordinal);
        Assert.Contains("the native side built, the .NET host did not", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two lines as they were. A rule that passes over these is a rule that would have been
    /// green on the defect, which is the only way this check can be wrong in the direction that
    /// matters.
    /// </summary>
    [Fact]
    public void TheLinesThisReplacedAreReportedUnguarded()
    {
        const string before = """
            :app_done
            echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
            echo [compile] NOTE: this binary only starts inside an MSYS2 MinGW64 shell.
            exit /b 0
            """;

        var claims = CompileMessages.Claims(before);

        Assert.Single(claims);
        Assert.False(claims[0].Guarded);
    }

    /// <summary>A flag test above it in the same block is what makes the claim honest.</summary>
    [Fact]
    public void AFlagTestInTheSameBlockGuards()
    {
        const string after = """
            :app_done
            if /I "%CHIAKI_ENABLE_GUI%"=="ON" (
                echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
            )
            exit /b 0
            """;

        Assert.All(CompileMessages.Claims(after), c => Assert.True(c.Guarded));
    }

    /// <summary>
    /// And a test in a DIFFERENT block does not, which is how the defect would come back: the
    /// check jumps somewhere, and the block it lands in prints without ever having asked.
    /// </summary>
    [Fact]
    public void AFlagTestInAnotherBlockDoesNotGuard()
    {
        const string jumped = """
            :ok
            if /I "%CHIAKI_ENABLE_GUI%"=="ON" goto ok_gui
            exit /b 0
            :ok_gui
            echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
            exit /b 0
            """;

        var claims = CompileMessages.Claims(jumped);

        Assert.Single(claims);
        Assert.False(claims[0].Guarded);
    }

    /// <summary>
    /// An existence test is the other honest guard: a line saying the client "also exists" has
    /// asked whether it does, which is a different question from the flag and an equally good one.
    /// </summary>
    [Fact]
    public void AnExistenceTestOnThatPathAlsoGuards()
    {
        const string asked = """
            :ok_deploy
            if not exist "%~dp0%BUILD_DIR%\gui\chiaki.exe" goto ok_deploy_managed
            echo [compile] ^(%~dp0%BUILD_DIR%\gui\chiaki.exe also exists, but it needs the
            exit /b 0
            """;

        Assert.All(CompileMessages.Claims(asked), c => Assert.True(c.Guarded));
    }

    /// <summary>
    /// PP586: cmd's one-line `if exist X echo X` guards itself. The two-line block already passed,
    /// and a rule that only looked at the lines ABOVE called the tighter spelling the unguarded one.
    /// </summary>
    [Fact]
    public void AnExistenceTestOnTheSameLineGuardsIt()
    {
        const string oneLine = """
            :ok_deploy_managed
            if exist "%~dp0%BUILD_DIR%\gui\chiaki.exe" echo [compile]   ^(%~dp0%BUILD_DIR%\gui\chiaki.exe is an EARLIER run's.^)
            exit /b 0
            """;

        var claims = CompileMessages.Claims(oneLine);

        Assert.Single(claims);
        Assert.True(claims[0].Guarded);
    }

    /// <summary>
    /// PP586, PP532: the ending a plain compile.cmd reaches decides on the flag, against this
    /// checkout.
    /// </summary>
    [Fact]
    public void TheDeployEndingDecidesOnTheFlag()
    {
        if (Source() is not { } source)
            return;

        Assert.True(CompileMessages.TheDeployEndingAsksTheFlag(source),
            "compile.cmd's :ok_deploy branch recommends a binary off a file being on disk rather "
            + "than off CHIAKI_ENABLE_GUI, so a run that skipped the Qt deploy still names the Qt "
            + "client");
    }

    /// <summary>
    /// PP586: the branch as it stood. An existence test passes on a stale binary, which is the
    /// whole defect - so a rule that could not fail on this text would hold nothing.
    /// </summary>
    [Fact]
    public void TheExistenceBranchThisReplacedIsReported()
    {
        const string before = """
            :ok_deploy
            rem PP21: the Qt client is off by default.
            if not exist "%~dp0%BUILD_DIR%\gui\chiaki.exe" goto ok_deploy_managed
            echo [compile] OK - run this one:
            echo [compile]   %~dp0%DEPLOY_DISP%\chiaki.exe
            exit /b 0
            """;

        Assert.False(CompileMessages.TheDeployEndingAsksTheFlag(before));
    }

    /// <summary>
    /// And the block it falls through to is not read in its place. `:ok_deploy_managed` starts with
    /// its own `if exist`, so a label match by prefix would answer for the wrong block.
    /// </summary>
    [Fact]
    public void TheManagedEndingIsNotReadAsTheDeployOne()
    {
        const string both = """
            :ok_deploy
            if /I not "%CHIAKI_ENABLE_GUI%"=="ON" goto ok_deploy_managed
            echo [compile]   %~dp0%DEPLOY_DISP%\chiaki.exe
            exit /b 0
            :ok_deploy_managed
            if exist "%~dp0%BUILD_DIR%\gui\chiaki.exe" echo [compile]   an EARLIER run's Qt client.
            exit /b 0
            """;

        Assert.True(CompileMessages.TheDeployEndingAsksTheFlag(both));
    }

    /// <summary>
    /// Comments are not claims. The file's own header describes the client at length and none of
    /// it is printed, so a rule that read rem lines would demand guards on prose.
    /// </summary>
    [Fact]
    public void CommentsAreNotClaims()
    {
        const string commented = """
            :ok
            rem .\build\gui\chiaki.exe links against C:\msys64\mingw64\bin
            rem the Qt client is built here
            exit /b 0
            """;

        Assert.Empty(CompileMessages.Claims(commented));
    }
}
