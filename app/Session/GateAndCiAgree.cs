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
/// The local gate runs three: ctest over the C suite, the .NET host's <c>--selftest</c>, and the
/// xUnit vectors. build.yml runs the first and the third. The selftest is 454 checks and CI does not
/// mention it.
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

    /// <summary>And the workflow that gates a branch.</summary>
    public const string CiRelativePath = @".github\workflows\build.yml";

    /// <summary>
    /// The local gate's files, concatenated, or null outside a checkout.
    ///
    /// Concatenated rather than asked one at a time: which of the two runs a given pass is their
    /// business, and the question here is whether the gate runs it at all.
    /// </summary>
    public static string? ReadLocal()
    {
        string?[] found =
            [.. LocalRelativePaths.Select(SanitizerSource.LocateRelative)];

        if (found.Any(path => path is null))
            return null;

        return string.Concat(found.Select(path => File.ReadAllText(path!)));
    }

    /// <summary>build.yml, or null outside a checkout.</summary>
    public static string? LocateCi() => SanitizerSource.LocateRelative(CiRelativePath);

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
            "the C suite (ctest)" => code.Contains("ctest", StringComparison.Ordinal),
            "the xUnit vectors" => code.Contains("dotnet test", StringComparison.Ordinal)
                || code.Contains("vstest", StringComparison.Ordinal),
            "the host's --selftest" => code.Contains("--selftest", StringComparison.Ordinal),
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
