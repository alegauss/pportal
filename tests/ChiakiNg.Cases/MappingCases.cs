using System.Diagnostics;
using System.Text.RegularExpressions;

using Winwright.Asserting;
using Winwright.Processes;
using Winwright.Projects;
using Winwright.Scenarios;
using Winwright.Verdicts;
using Winwright.Windowing;

using Xunit;

namespace ChiakiNg.Cases;

/// <summary>
/// WW88: the mapping screen's cases, run.
/// <para>
/// This is what is left of <c>Interaction.cs</c> — 285 lines in which every case decided for itself
/// how long to wait for a control, how many attempts an act gets, what a missing read-back does to
/// the exit code and how to say that something could not be checked. The engine owns all of those,
/// so what is left here is naming the files, measuring what only this repository can measure, and
/// reading the verdict.
/// </para>
/// <para>
/// One test and not one per case, deliberately: the engine runs the selection and answers a verdict
/// over all of it, including which cases it left alone and why. Splitting that into a fact per case
/// would relaunch the application for each and throw away what the fixtures say about sharing.
/// </para>
/// <para>
/// Still <c>Category=Interaction</c>, which is what keeps it out of the gate: these open a window and
/// need a pad, and a suite that does either fails for reasons about the desk it ran on rather than
/// about the code. <c>test.cmd interaction</c> is the request to run them.
/// </para>
/// </summary>
[Trait("Category", "Interaction")]
public sealed class MappingCases
{
    /// <summary>
    /// The one precondition no engine could measure, because it is about this desk's hardware rather
    /// than about the window: a pad SDL can map.
    /// <para>
    /// This is the reading the file it replaces could not act on. xUnit 2 has no word for "should
    /// have been checked, wasn't", so a desk with no controller had to be a red about the code — and
    /// the run said so in a sentence nobody could act on either. Declared as a need, the engine
    /// leaves the case alone and names the absence, which is neither a pass nor a failure.
    /// </para>
    /// </summary>
    private const string Pad = "a controller SDL can map";

    [Fact]
    public void The_mapping_cases_run()
    {
        var desk = Desk.Read();
        if (!desk.CanObserve)
        {
            // Said and not asserted: a run that could observe nothing is a third verdict, and an
            // assertion over two constants is one `DeadAssertionsTests` refuses — rightly, since it
            // would pass whatever the code did. The sentence is the whole of what this can report.
            Console.WriteLine($"nothing ran: this desk lacks {desk.FirstAbsent!.Name}.");
            return;
        }

        var project = ProjectDeclaration.Find(Repository());
        var declared = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));

        // A case file that loaded nothing is a run with no failure and no hole in it, which reads as
        // a pass about nothing — the one shape this whole migration exists to refuse.
        Assert.NotEmpty(declared);

        using var register = new ProcessRegister();
        var verdict = Suite.Launch(declared, Selection.All, register, project, measured: Measured(project));

        Console.WriteLine(string.Join(Environment.NewLine, verdict.Render()));

        // A desk with no pad is the state this machine and CI are both in, and it is not a failure:
        // the cases declare the pad as a need, the engine leaves them alone and names the absence.
        // Said out loud rather than passed over, because a green that covers nothing is the defect
        // the file this replaces existed to stop — it just had to spell it as a red.
        if (OnlyTheMissingPad(verdict))
        {
            var left = verdict.Unhappy.Count;
            Console.WriteLine(
                $"{verdict.Assertions} assertion(s) ran and {left} case(s) were left alone: "
                + $"this desk lacks {Pad}.");

            // A pass that says what it did not check, which is the closest honest thing a framework
            // with two words for three verdicts has. The sentence goes in the message so a green run
            // carries it, rather than only a failing one.
            Assert.True(verdict.Assertions > 0, "no assertion ran at all, which is not a pad's absence");
            return;
        }

        // The whole reading and not just the outcome, because a red step carries its diagnosis is
        // this repository's rule about assertions and no different one layer up.
        Assert.True(verdict.Outcome == RunOutcome.Passed, Read(verdict));
        Assert.Equal(0, verdict.ExitCode);

        // And nothing was left on the desk. `Interaction.cs` produced this with a finally in every
        // case: a leftover ChiakiNg holds its own .exe open, the next build cannot overwrite it, and
        // the run after that reports on code that is not in the tree.
        Assert.Empty(register.StopAll());
    }

    /// <summary>
    /// What this desk turned out to have, measured once for the run by asking the application.
    /// <para>
    /// <c>--controllers</c> is the read-out this host already ships — "print what SDL sees, for a pad
    /// that is plugged in now" — so the count comes from the same SDL the mapping screen opens
    /// rather than from a second enumeration written here, which would agree with it until somebody
    /// changed one. The first line carries both numbers and the second is the one that matters: a pad
    /// SDL sees and cannot map is a pad this screen has nothing to show for.
    /// </para>
    /// </summary>
    /// <param name="project">The declaration, for the executable to ask.</param>
    private static PreconditionSet Measured(ProjectDeclaration project)
    {
        var (mappable, because) = Pads(project.Executable);

        return PreconditionSet.Of(
            mappable > 0 ? Precondition.Met(Pad) : Precondition.Absent(Pad, because));
    }

    /// <summary>How many pads SDL can map, and why the answer is not usable where it is not.</summary>
    /// <param name="executable">The application to ask.</param>
    private static (int Mappable, string Because) Pads(string executable)
    {
        try
        {
            using var asking = Process.Start(new ProcessStartInfo(executable, "--controllers")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });

            if (asking is null)
                return (0, $"{executable} would not start to be asked what SDL sees");

            var said = asking.StandardOutput.ReadToEnd();
            asking.WaitForExit(20_000);

            // `0 device(s), 0 mappable` is the line, and the second number is the one a case needs.
            var counted = Regex.Match(said, @"(\d+)\s+mappable");

            return counted.Success && int.TryParse(counted.Groups[1].Value, out var mappable)
                ? (mappable, $"--controllers reports {mappable} mappable pad(s)")
                : (0, $"--controllers answered nothing this could count: {said.Trim()}");
        }
        catch (Exception refused) when (refused is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (0, $"{executable} could not be asked what SDL sees: {refused.Message}");
        }
    }

    /// <summary>
    /// Whether the only thing this run has to report is the pad nobody plugged in.
    /// <para>
    /// Every unhappy case has to be unhappy for that one reason: nothing failed, nothing broke, and
    /// every assertion that did not run names this precondition. A hole of any other kind — a window
    /// that would not come, a desk that would not give up the foreground — is a reading this must not
    /// swallow, because the whole point of the third verdict is that it says which absence it met.
    /// </para>
    /// </summary>
    /// <param name="verdict">What the run concluded.</param>
    private static bool OnlyTheMissingPad(SuiteVerdict verdict) =>
        verdict.Outcome == RunOutcome.Degraded
        && verdict.Unhappy.All(one =>
            one.Verdict.Failures.Count == 0
            && one.Verdict.Broke.Count == 0
            && one.Verdict.Unchecked.Count > 0
            && one.Verdict.Unchecked.All(said => said.Missing?.Name == Pad));

    /// <summary>The whole reading, down to the assertion that went red.</summary>
    private static string Read(SuiteVerdict verdict)
    {
        var lines = new List<string>(verdict.Render());

        foreach (var unhappy in verdict.Unhappy)
        {
            lines.Add("");
            lines.Add($"{unhappy.Declared.Name}:");
            lines.AddRange(unhappy.Verdict.Failures.Select(one => $"  failed    {one}"));
            lines.AddRange(unhappy.Verdict.Unchecked.Select(one => $"  unchecked {one}"));
            lines.AddRange(unhappy.Verdict.Broke.Select(one => $"  broke     {one}"));
            lines.AddRange(unhappy.Trace.Select(one => $"  trace     {one}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// This repository's root: the first directory at or above this assembly that declares the
    /// project.
    /// <para>
    /// Walked rather than counted, which is what <c>Interaction.cs</c> already said about locating
    /// the executable and the count it got wrong the first time — five <c>..</c> where the right
    /// number was six, landing inside the test tree. The declaration is the marker because it is the
    /// thing being looked for anyway, so there is no second answer to keep in step.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Where no directory above this one declares it.</exception>
    private static string Repository()
    {
        var walking = new DirectoryInfo(AppContext.BaseDirectory);

        while (walking is not null)
        {
            if (File.Exists(Path.Combine(walking.FullName, "winwright.json")))
                return walking.FullName;

            walking = walking.Parent;
        }

        throw new InvalidOperationException(
            $"no winwright.json at or above {AppContext.BaseDirectory}, so the cases have no project");
    }
}
