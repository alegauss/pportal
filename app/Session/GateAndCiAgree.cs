using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One pass over the tree, and where it is run.</summary>
/// <param name="Name">What it is called here.</param>
/// <param name="Locally">Whether the local gate runs it - test.cmd or the script it launches.</param>
/// <param name="InCi">Whether build.yml runs it.</param>
/// <param name="Because">Why the two differ, where they do. Empty where they agree.</param>
public readonly record struct TestPass(string Name, bool Locally, bool InCi, string Because = "");

/// <summary>
/// PP433: the passes test.cmd runs and the passes CI runs, held against each other.
///
/// The local gate runs five: ctest over the C suite, the .NET host's <c>--selftest</c>, the xUnit
/// vectors, and the two tools' own self-tests (PP569, PP570). build.yml runs all but the host's,
/// which is 454 checks CI does not mention.
///
/// IT IS THE SHAPE PP75 FIXED ONE LEVEL DOWN. PP2 put assertions in app/SelfTest.cs and nothing ran
/// them; PP75 gave them a runner. That runner is local and CI is where a branch turns red, so the
/// argument now applies to the pass.
///
/// THE GAP IS RECORDED RATHER THAN CLOSED, and PP117 is why. It found that SDL2 failing to resolve a
/// dependency inside its own initialisation presents as a HANG with a modal dialog, because the
/// process has no window to click. Whether SDL2 resolves on a runner cannot be answered from a
/// developer machine, and a step that hangs every push is worse than the omission - so the step is
/// the user's to land, after one run says which it is.
///
/// WHAT THIS DOES IS STOP THE DIVERGENCE GROWING. One known member, named with its reason. A pass
/// added to test.cmd and not to CI, or dropped from CI, turns the suite red rather than widening a
/// difference nobody is counting.
/// </summary>
public static partial class GateAndCiAgree
{
    /// <summary>
    /// The local gate, which is TWO files and not one.
    ///
    /// test.cmd is a thin launcher around scripts/test-windows.sh, and the split is deliberate: the
    /// shell script runs ctest because ctest lives in /mingw64/bin and not on a plain Windows PATH,
    /// while PP75's selftest runs from test.cmd "rather than from here", as that script says. A
    /// reader that stopped at the launcher would find comments about ctest and no ctest - which is
    /// what the first version of this did.
    /// </summary>
    public static IReadOnlyList<string> LocalRelativePaths { get; } =
        ["test.cmd", @"scripts\test-windows.sh"];

    /// <summary>
    /// And the workflows that gate a branch, which are THREE and not one.
    ///
    /// PP587: this was build.yml alone, and it was right while every pass lived there. `roadkeep
    /// lint` does not - PP36 gave it a workflow of its own - so a table reading build.yml would have
    /// reported CI as not running a pass CI runs, and the disagreement would have been in the reader.
    /// site.yml is here for the same reason rather than because it runs one: a list of "the
    /// workflows CI has" is a fact, and a list of "the ones that happen to matter today" is a
    /// judgement that goes stale silently.
    /// </summary>
    public static IReadOnlyList<string> CiRelativePaths { get; } =
    [
        @".github\workflows\build.yml",
        @".github\workflows\roadkeep.yml",
        @".github\workflows\site.yml",
    ];

    /// <summary>
    /// The local gate's files, concatenated, or null outside a checkout.
    ///
    /// Concatenated rather than asked one at a time: which of the two runs a given pass is their
    /// business, and the question here is whether the gate runs it at all.
    /// </summary>
    public static string? ReadLocal() => Concatenate(LocalRelativePaths);

    /// <summary>The workflows, concatenated, or null outside a checkout - as <see cref="ReadLocal"/>.</summary>
    public static string? ReadCi() => Concatenate(CiRelativePaths);

    private static string? Concatenate(IReadOnlyList<string> relativePaths)
    {
        string?[] found = [.. relativePaths.Select(SanitizerSource.LocateRelative)];

        if (found.Any(path => path is null))
            return null;

        return string.Concat(found.Select(path => File.ReadAllText(path!)));
    }

    /// <summary>
    /// The three passes, and where each is run today.
    ///
    /// The selftest's <see cref="TestPass.Because"/> is the whole of what this class is for: a
    /// difference with a reason beside it is a decision, and one without is an oversight.
    /// </summary>
    public static IReadOnlyList<TestPass> Passes { get; } =
    [
        new("the C suite (ctest)", Locally: true, InCi: true),

        new("the xUnit vectors", Locally: true, InCi: true),

        new("the host's --selftest", Locally: true, InCi: false,
            "454 runtime checks over the native seam. PP117 recorded that an SDL2 resolution "
                + "failure presents as a hang with a modal dialog, so the step needs a timeout and "
                + "one CI run to say whether SDL2 resolves on a runner - which a developer machine "
                + "cannot answer."),

        // PP569: run by both, which is the state this table exists to keep. It is here because it
        // was run by NEITHER - the tool is in the solution, so both sides built it and neither
        // executed the assertion its own README calls the one it ships with. Safe in CI where the
        // host's selftest is not: pure fixtures, no native seam, so PP117's argument does not reach
        // it and it needs no timeout.
        new("the tool's --self-test", Locally: true, InCi: true),

        // PP570: the same flag on the other tool, which was not even in the solution - so no gate
        // built it, let alone ran it. Kept as its own pass rather than folded into the one above:
        // they are two binaries, and a table that said "the tools" would be green with one of them
        // wired, which is the state PP569 left and this found.
        new("measure-startup's self-test", Locally: true, InCi: true),

        // PP587: the only pass that arrived from the CI side. PP36 gave `roadkeep lint` a workflow
        // and the local gate never gained it, so a governed-file drift was first reported by a push -
        // the mirror image of the host's selftest above, and the one direction this table had no
        // member for. The plugin's hook validates every WRITE, which is why it was easy to leave out;
        // what a write cannot catch is a hand-edit that went round it, a merge, or a rule the engine
        // gained since the file was last touched.
        new("roadkeep lint", Locally: true, InCi: true),
    ];

    /// <summary>How many passes run locally and not in CI. One, and it is named.</summary>
    public const int LocalOnlyCeiling = 1;

    /// <summary>
    /// Whether a file runs a pass, read by the command each one is.
    ///
    /// Comments stripped for the CI file too: build.yml's comments discuss test.cmd and ctest at
    /// length, and a reader counting those would find every pass in both places.
    /// </summary>
    public static bool Runs(string script, string pass)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrEmpty(pass);

        string code = WithoutComments(script);

        return pass switch
        {
            // PP589: the same join as the two tools below, for a command that takes no flag to pair
            // with. A bare Contains was answered by nine lines that run nothing - the assignment, a
            // temp file called `ctest_out`, the five lines that cat, grep and rm it, and a warning
            // echo - so the call could be deleted and this stayed green. Measured, not read.
            "the C suite (ctest)" => RunsCommand(code, "ctest"),
            "the xUnit vectors" => code.Contains("dotnet test", StringComparison.Ordinal)
                || code.Contains("vstest", StringComparison.Ordinal),
            "the host's --selftest" => code.Contains("--selftest", StringComparison.Ordinal),

            // PP569: the hyphen is the whole difference from the pass above, and it is enough -
            // "--selftest" is not a substring of "--self-test".
            //
            // PP588: and the two halves have to reach each other. They were two Contains over the
            // whole file, so the banner supplied the binary and the OTHER tool's call supplied the
            // flag - either invocation could be deleted and its pass stayed green.
            "the tool's --self-test" => RunsTool(code, "compare-baselines", "--self-test"),

            // PP570: named by its own binary, because both tools take the same flag. Matching on the
            // flag alone made one wiring satisfy both - the green that hid this for a whole task.
            "measure-startup's self-test" => RunsTool(code, "measure-startup", "--self-test"),

            // PP587: two spellings, because the two sides invoke it differently and neither is the
            // other's substring. The gate calls the verb; CI uses the action roadkeep publishes,
            // which roadkeep.yml's own comment says is deliberate - a copied `run:` block drifts per
            // repository. Matching the verb alone would report CI as not running it.
            //
            // AND THE VERB HAS TO BE THE COMMAND. `echo [test] roadkeep lint` announces the step one
            // line above it, so a Contains over the whole file is satisfied by the label alone - the
            // check was green with the call deleted. This is the same failure as counting a comment,
            // one step along: a comment is not the only line that can name a command without running
            // it. Hence Invokes, which asks whether a LINE starts with the verb.
            "roadkeep lint" => Invokes(code, "roadkeep lint")
                || code.Contains("alegauss/roadkeep", StringComparison.Ordinal),

            _ => false,
        };
    }

    /// <summary>
    /// Where the table and the two files disagree, as sentences.
    ///
    /// BOTH FILES, because either can move. A pass added to test.cmd is a pass CI should gain, and a
    /// pass CI stops running is a pass that turned local-only without anybody choosing that.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string local, string ci)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(ci);

        var apart = new List<string>();

        foreach (TestPass pass in Passes)
        {
            bool locally = Runs(local, pass.Name);
            bool inCi = Runs(ci, pass.Name);

            if (locally != pass.Locally)
                apart.Add($"{pass.Name}: the local gate {(locally ? "runs" : "does not run")} it, table says otherwise");

            if (inCi != pass.InCi)
                apart.Add($"{pass.Name}: build.yml {(inCi ? "runs" : "does not run")} it, table says otherwise");
        }

        return apart;
    }

    /// <summary>
    /// Whether any line RUNS the command, rather than merely containing it.
    ///
    /// PP587: the line has to begin with it. `echo [test] roadkeep lint` contains the verb and runs
    /// nothing, and a gate that printed its own banner and skipped the step would read as wired.
    /// </summary>
    public static bool Invokes(string code, string command)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(command);

        return code.Split('\n')
            .Any(line => line.Trim().StartsWith(command, StringComparison.Ordinal));
    }

    /// <summary>
    /// PP588: whether a tool is run WITH a flag, rather than both words appearing somewhere.
    ///
    /// The two sides spell the same call two ways and neither puts the binary at the start of the
    /// line, so <see cref="Invokes"/> does not reach this - which is why PP587's fix stopped at the
    /// one row it was written for.
    ///
    /// CI PUTS BOTH HALVES ON ONE LINE: `dotnet run --project tools/compare-baselines/... --
    /// --self-test`. THE GATE SPLITS THEM OVER A VARIABLE: `set "CB_EXE=...compare-baselines.exe"`
    /// and then `"%CB_EXE%" --self-test` four lines later. Both are real invocations and a rule that
    /// took only the first would report the gate as not running what it runs.
    ///
    /// AND AN ECHO IS NEVER ONE. `echo [test] compare-baselines selftest` names the binary and runs
    /// nothing; leaving it in is what let the neighbour's flag finish the pair.
    /// </summary>
    public static bool RunsTool(string code, string tool, string flag)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentException.ThrowIfNullOrEmpty(flag);

        string[] lines = [.. code.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("echo", StringComparison.OrdinalIgnoreCase))];

        if (lines.Any(line => line.Contains(tool, StringComparison.Ordinal)
                && line.Contains(flag, StringComparison.Ordinal)))
        {
            return true;
        }

        return VariablesSetTo(lines, tool).Any(
            name => lines.Any(line => line.Contains($"%{name}%", StringComparison.Ordinal)
                && line.Contains(flag, StringComparison.Ordinal)));
    }

    /// <summary>
    /// PP589: whether a command is RUN, for one that carries no flag to pair it with.
    ///
    /// <see cref="RunsTool"/> needs a binary and a flag, and the C suite has only the binary - so
    /// the ambiguity has to be resolved by where the word sits rather than by a second word.
    ///
    /// CI RUNS IT BY NAME: `run: ctest --test-dir build -C Release`. THE GATE RUNS IT THROUGH A
    /// VARIABLE, because ctest lives in /mingw64/bin and not on a plain Windows PATH (PP67):
    /// `CTEST="${CTEST:-/mingw64/bin/ctest}"` and then `"$CTEST"` on the call. The literal never
    /// appears on the line that runs it.
    ///
    /// AN ASSIGNMENT IS NOT A CALL, which is the whole of the fix. `ctest_out=$(mktemp)` names a
    /// temp file and `CTEST=...` names a path; both were answering for the invocation. And the word
    /// has to stand alone - `$ctest_out` carries the letters and is a different thing.
    /// </summary>
    public static bool RunsCommand(string code, string command)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(command);

        string[] lines = [.. code.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("echo", StringComparison.OrdinalIgnoreCase))
            .Where(line => !IsAssignment(line))];

        if (lines.Any(line => NamesAWord(line, command)))
            return true;

        // The variable form. Read from the whole text, since the assignment was filtered out above:
        // it is the one line that has to be an assignment for this to mean anything.
        return ShellVariablesSetTo(code, command)
            .Any(name => lines.Any(line => NamesAWord(line, $"\"${name}\"")
                || NamesAWord(line, $"${name}")));
    }

    /// <summary>Whether a line assigns rather than runs - shell `NAME=value` or batch `set …`.</summary>
    private static bool IsAssignment(string line)
    {
        if (line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            return true;

        int at = 0;
        while (at < line.Length && (char.IsLetterOrDigit(line[at]) || line[at] == '_'))
            at++;

        return at > 0 && at < line.Length && line[at] == '=';
    }

    /// <summary>
    /// Whether the word stands on its own, bounded by whitespace or the ends of the line.
    ///
    /// `$ctest_out` carries the letters and is a temp file; `/mingw64/bin/ctest` carries them and is
    /// a path inside an assignment. Neither is the command being run.
    /// </summary>
    private static bool NamesAWord(string line, string word)
    {
        for (int at = line.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = line.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            bool leftClear = at == 0 || char.IsWhiteSpace(line[at - 1]);
            int after = at + word.Length;
            bool rightClear = after >= line.Length || char.IsWhiteSpace(line[after]);

            if (leftClear && rightClear)
                return true;
        }

        return false;
    }

    /// <summary>The shell variables whose value names the command, as `NAME=value` writes them.</summary>
    private static IEnumerable<string> ShellVariablesSetTo(string code, string command)
    {
        foreach (string raw in code.Split('\n'))
        {
            string line = raw.Trim();
            if (!IsAssignment(line) || line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                continue;

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (NamesAToken(line[(equals + 1)..], command))
                yield return line[..equals];
        }
    }

    /// <summary>
    /// Whether a value names the command as a token, with neither neighbour an identifier character.
    ///
    /// A plain Contains here is what made the first version of PP589's fix pass on a tree with the
    /// call deleted: `cases=$(sed … "$ctest_out" …)` holds the letters of ctest inside `$ctest_out`,
    /// so `cases` read as a variable holding the tool and `"$cases"` two lines later read as running
    /// it. `/mingw64/bin/ctest}` is the real one - `/` on the left, `}` on the right.
    /// </summary>
    private static bool NamesAToken(string value, string command)
    {
        for (int at = value.IndexOf(command, StringComparison.Ordinal); at >= 0;
             at = value.IndexOf(command, at + 1, StringComparison.Ordinal))
        {
            bool leftClear = at == 0 || !IsIdentifier(value[at - 1]);
            int after = at + command.Length;
            bool rightClear = after >= value.Length || !IsIdentifier(value[after]);

            if (leftClear && rightClear)
                return true;
        }

        return false;
    }

    private static bool IsIdentifier(char one) => char.IsLetterOrDigit(one) || one == '_';

    /// <summary>
    /// The batch variables whose value names a tool, as `set "NAME=value"` writes them.
    ///
    /// The quoted form is the only one this gate uses and the only one accepted: `set NAME=value`
    /// unquoted keeps trailing spaces in the value, which is why the file does not write it.
    /// </summary>
    private static IEnumerable<string> VariablesSetTo(IEnumerable<string> lines, string tool)
    {
        foreach (string line in lines)
        {
            if (!line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                continue;

            string assignment = line[4..].Trim().Trim('"');
            int equals = assignment.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
                continue;

            // PP589: as a token, for the reason NamesAToken gives - a value holding the tool's
            // letters inside a longer identifier is not a variable holding the tool.
            if (NamesAToken(assignment[(equals + 1)..], tool))
                yield return assignment[..equals];
        }
    }

    /// <summary>Every pass that runs locally and not in CI.</summary>
    public static IReadOnlyList<TestPass> LocalOnly()
        => [.. Passes.Where(pass => pass.Locally && !pass.InCi)];

    // Batch rem/:: lines and YAML # lines. Not a parser: what it has to not do is let a comment
    // mentioning a command count as running it.
    private static string WithoutComments(string script)
        => CommentRegex().Replace(script, "");

    [GeneratedRegex(@"(?im)^[ \t]*(?:rem\b|::|#).*$")]
    private static partial Regex CommentRegex();
}
