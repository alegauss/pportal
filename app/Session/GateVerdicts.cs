namespace ChiakiNg.Session;

/// <summary>One step a launcher runs and judges, named by the line that runs it.</summary>
/// <param name="Where">The launcher, repository-relative.</param>
/// <param name="Runs">The command line, exactly as the file spells it.</param>
/// <param name="What">What the step is, for a failure message that names it.</param>
public readonly record struct GateStep(string Where, string Runs, string What);

/// <summary>The two tests that follow a step, or what was found instead.</summary>
/// <param name="Ran">Whether the line that runs the step is still in the file.</param>
/// <param name="Positive">The consequent of `if errorlevel 1`, or null where there is none.</param>
/// <param name="Negative">The consequent of `if not errorlevel 0`, or null where there is none.</param>
public readonly record struct GateVerdict(bool Ran, string? Positive, string? Negative);

/// <summary>
/// PP682: whether each step the gate runs is judged for EVERY non-zero exit code, or only for the
/// positive ones.
///
/// cmd's `if errorlevel N` is true when the errorlevel is N or above, so `if errorlevel 1` is the
/// idiom for "failed" only while failures are positive. A process that crashes rather than failing
/// exits with a negative code - 0xE0434352 is an unhandled .NET exception - and a negative number is
/// below one. Every verdict written that way is blind to exactly the failure a developer most needs
/// told about.
///
/// IT WAS NOT HYPOTHETICAL. From PP663 until PP681 the host's selftest died mid-run on every default
/// build, and this gate printed OK over it for weeks: the exit code was the runtime's, the
/// comparison could not see it, and CRC stayed zero. That is the lie PP56, PP74 and PP75 are each
/// about - a green over assertions nobody ran - arriving through a shell comparison rather than
/// through a binary nobody built.
///
/// THE PAIR IS THE FIX, and both halves are needed: `if errorlevel 1` catches the positive codes and
/// `if not errorlevel 0` is true only below zero. Measured rather than reasoned about - at
/// -532462766 the first misses and the second catches, at 3 the first catches and the second is
/// quiet, at 0 both are quiet - so together they cover every non-zero code and nothing else.
///
/// THE STEPS ARE NAMED, not inferred. `if errorlevel 1` also appears over probes - `where dotnet`,
/// `where roadkeep`, a tasklist filter - where the number means found or not found and a negative is
/// impossible. Deciding which is which by reading the consequent would be a guess about intent; this
/// is a list, so a step added to a launcher and left unjudged is a row somebody has to write rather
/// than a case the reader silently skipped.
/// </summary>
public static class GateVerdicts
{
    /// <summary>`if errorlevel 1`, which catches one sign.</summary>
    public const string PositiveTest = "if errorlevel 1";

    /// <summary>`if not errorlevel 0`, which catches the other.</summary>
    public const string NegativeTest = "if not errorlevel 0";

    /// <summary>
    /// Every step whose exit code is a verdict, in both launchers.
    ///
    /// Not every run: `test.cmd interaction` ends in `exit /b %errorlevel%`, which passes whatever
    /// it was given straight out and so is blind to nothing.
    /// </summary>
    public static IReadOnlyList<GateStep> Steps { get; } =
    [
        new(GateAndCiAgree.LocalRelativePaths[0], "\"%APP_EXE%\" --selftest", "the host's selftest"),
        new(GateAndCiAgree.LocalRelativePaths[0], "\"%CB_EXE%\" --self-test", "compare-baselines"),
        new(GateAndCiAgree.LocalRelativePaths[0], "\"%MS_EXE%\" --self-test", "measure-startup"),
        new(GateAndCiAgree.LocalRelativePaths[0], "roadkeep lint", "the governed files"),
        new(
            GateAndCiAgree.LocalRelativePaths[0],
            "dotnet test \"%~dp0ChiakiNg.slnx\" --nologo -v quiet --filter \"Category!=Interaction\"",
            "the xUnit suite"),
        new(
            CompileMessages.RelativePath,
            "\"%BASH%\" -l \"%REPO%/scripts/build-windows.sh\" %SH_ARGS%",
            "the native side"),
        new(
            CompileMessages.RelativePath,
            "dotnet build \"%~dp0ChiakiNg.slnx\" -c Debug --nologo -v quiet",
            "the .NET host"),
    ];

    /// <summary>A launcher, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// How a step is judged: the first two tests after the line that runs it, comments skipped.
    ///
    /// Bounded at the tests themselves rather than at a line count, so a comment between the call
    /// and its verdict - which is where the reason for the pair belongs - does not hide either half.
    /// The walk stops at the first line that is neither a comment nor an errorlevel test, because
    /// past that the step has been judged and what follows belongs to something else.
    /// </summary>
    public static GateVerdict Judgement(string launcher, GateStep step)
    {
        ArgumentNullException.ThrowIfNull(launcher);

        string[] lines = launcher.ReplaceLineEndings("\n").Split('\n');

        int at = Array.FindIndex(lines, line => line.Trim() == step.Runs);
        if (at < 0)
            return new GateVerdict(false, null, null);

        string? positive = null;
        string? negative = null;

        for (int i = at + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)
                || line.Equals("rem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Consequent(line, NegativeTest) is { } negated)
                negative ??= negated;
            else if (Consequent(line, PositiveTest) is { } caught)
                positive ??= caught;
            else
                break;
        }

        return new GateVerdict(true, positive, negative);
    }

    /// <summary>
    /// Whether a step is judged for both signs, and by the same consequent.
    ///
    /// The consequents have to match: a verdict that sets the failure flag on one sign and merely
    /// logs on the other is half a check, and reads at a glance as a whole one.
    /// </summary>
    public static bool CatchesEverySign(GateVerdict verdict)
        => verdict is { Ran: true, Positive: not null, Negative: not null }
            && string.Equals(verdict.Positive, verdict.Negative, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a test does, where the line is that test; null where it is not.</summary>
    private static string? Consequent(string line, string test)
    {
        if (!line.StartsWith(test, StringComparison.OrdinalIgnoreCase))
            return null;

        string rest = line[test.Length..];

        // `if errorlevel 10 ...` starts with `if errorlevel 1` and is a different test.
        return rest.Length > 0 && char.IsWhiteSpace(rest[0]) ? rest.Trim() : null;
    }
}
