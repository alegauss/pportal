using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP682: every step the gate runs is judged for a crash as well as for a failure.
///
/// `if errorlevel 1` is true for one sign only, and a crashing process exits with the other. The
/// selftest died on every default build from PP663 until PP681 and this gate printed OK over it,
/// which is PP56's stale green arriving through a shell comparison.
/// </summary>
public class GateVerdictsTests(ITestOutputHelper output)
{
    private static string? Launcher(string relative)
        => GateVerdicts.Locate(relative) is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE CHECK: each named step carries both tests, with the same consequent.
    ///
    /// One case over every step rather than one per launcher, so the failure names which step lost
    /// its second half rather than which file did.
    /// </summary>
    [Fact]
    public void EveryStepIsJudgedForBothSigns()
    {
        var blind = new List<string>();

        foreach (GateStep step in GateVerdicts.Steps)
        {
            if (Launcher(step.Where) is not { } launcher)
                return;

            GateVerdict verdict = GateVerdicts.Judgement(launcher, step);
            output.WriteLine(
                $"{step.Where}: {step.What} -> +{verdict.Positive ?? "none"} / -{verdict.Negative ?? "none"}");

            if (!GateVerdicts.CatchesEverySign(verdict))
            {
                blind.Add(
                    $"{step.Where}: {step.What} is judged by "
                        + $"'{verdict.Positive ?? "nothing"}' on a failure and "
                        + $"'{verdict.Negative ?? "nothing"}' on a crash");
            }
        }

        Assert.True(
            blind.Count == 0,
            "these steps are judged for one sign of exit code only, so a crash in them reads as a "
                + "pass:\n  " + string.Join("\n  ", blind));
    }

    /// <summary>
    /// And every step is still a line in its launcher, so a renamed call is a failure here rather
    /// than a row that quietly stops checking anything.
    /// </summary>
    [Fact]
    public void EveryStepIsStillRun()
    {
        foreach (GateStep step in GateVerdicts.Steps)
        {
            if (Launcher(step.Where) is not { } launcher)
                return;

            Assert.True(
                GateVerdicts.Judgement(launcher, step).Ran,
                $"{step.Where} no longer runs {step.What} as `{step.Runs}`, so nothing here is "
                    + "checking how its exit code is read");
        }
    }

    /// <summary>Both launchers are covered, and the list did not quietly become one file's.</summary>
    [Fact]
    public void BothLaunchersAreCovered()
    {
        Assert.Contains(GateVerdicts.Steps, s => s.Where == GateAndCiAgree.LocalRelativePaths[0]);
        Assert.Contains(GateVerdicts.Steps, s => s.Where == CompileMessages.RelativePath);
        Assert.Equal(7, GateVerdicts.Steps.Count);
    }

    /// <summary>
    /// The reader tells a whole verdict from half of one, and from a test over another number.
    ///
    /// Written against text rather than against the launchers, because what has to be right is the
    /// reader: a check that called every shape acceptable would pass over the file that broke.
    /// </summary>
    [Theory]
    [InlineData("if errorlevel 1 set \"CRC=1\"\nif not errorlevel 0 set \"CRC=1\"", true)]
    [InlineData("if errorlevel 1 set \"CRC=1\"", false)]
    [InlineData("if not errorlevel 0 set \"CRC=1\"", false)]
    [InlineData("if errorlevel 1 set \"CRC=1\"\nif not errorlevel 0 echo oops", false)]
    [InlineData("if errorlevel 10 set \"CRC=1\"\nif not errorlevel 0 set \"CRC=1\"", false)]
    [InlineData("", false)]
    public void TheReaderTellsAWholeVerdictFromHalfOfOne(string verdict, bool whole)
    {
        var step = new GateStep("test.cmd", "a-step.exe", "a step");
        string launcher = "a-step.exe\n" + verdict + "\n";

        Assert.Equal(whole, GateVerdicts.CatchesEverySign(GateVerdicts.Judgement(launcher, step)));
    }

    /// <summary>
    /// A comment between the call and its verdict does not hide either half - which matters, since
    /// the reason for the pair is exactly what belongs there.
    /// </summary>
    [Fact]
    public void ACommentBetweenTheCallAndItsVerdictIsNotAWall()
    {
        var step = new GateStep("test.cmd", "a-step.exe", "a step");
        const string launcher = """
            a-step.exe
            rem why the pair is here
            REM and a second line of it
            if errorlevel 1 set "CRC=1"
            if not errorlevel 0 set "CRC=1"
            """;

        Assert.True(GateVerdicts.CatchesEverySign(GateVerdicts.Judgement(launcher, step)));
    }

    /// <summary>
    /// And the walk stops once the step has been judged, so a LATER step's tests are not read as
    /// this one's - the failure that would make every row pass on the strength of one.
    /// </summary>
    [Fact]
    public void ALaterStepsVerdictIsNotReadAsThisOnes()
    {
        var step = new GateStep("test.cmd", "a-step.exe", "a step");
        const string launcher = """
            a-step.exe
            if errorlevel 1 set "CRC=1"
            echo something else happens here
            another-step.exe
            if not errorlevel 0 set "CRC=1"
            """;

        Assert.False(GateVerdicts.CatchesEverySign(GateVerdicts.Judgement(launcher, step)));
    }

    /// <summary>A step that is not in the file is reported as absent, not as judged.</summary>
    [Fact]
    public void AStepThatIsNotThereIsSaidRatherThanPassed()
    {
        GateVerdict verdict = GateVerdicts.Judgement(
            "echo nothing to see", new GateStep("test.cmd", "a-step.exe", "a step"));

        Assert.False(verdict.Ran);
        Assert.False(GateVerdicts.CatchesEverySign(verdict));
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.False(
            GateVerdicts.Judgement("", new GateStep("test.cmd", "a-step.exe", "a step")).Ran);
    }
}
