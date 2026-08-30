using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP22: the CI workflow, held against the tree it builds.
///
/// A workflow file is read by exactly one thing, and that thing is not on this machine. Nothing
/// here compiles it, nothing here runs it, and the first report that it names a file the tree no
/// longer has is a red push - which is the same shape of silence compile.cmd's preflight was
/// written for, one repository away. So the parts of it that are claims about THIS checkout are
/// asserted here instead: the paths it reads, the framework it installs, the toolchain it
/// configures through, and the platform it runs on.
///
/// What is deliberately NOT here is whether the build succeeds on a runner. That is a question
/// only a runner answers, and pretending otherwise would put a second build system in this file.
/// </summary>
public static partial class BuildWorkflow
{
    /// <summary>The workflow that builds and packages the application.</summary>
    public const string RelativePath = @".github\workflows\build.yml";

    /// <summary>The project whose framework the workflow has to install.</summary>
    public const string HostProjectRelativePath = @"app\ChiakiNg.csproj";

    /// <summary>The workflow, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The .NET host's project, or null outside a checkout.</summary>
    public static string? LocateHostProject() => SanitizerSource.LocateRelative(HostProjectRelativePath);

    /// <summary>
    /// File extensions that make a token in the workflow a claim about this checkout.
    ///
    /// A closed list rather than "anything with a dot in it": a runner image path, an action
    /// reference and a triplet name all look like paths to a regex, and a check that reported
    /// those as missing would be turned off within a week.
    /// </summary>
    public static IReadOnlySet<string> RepositoryFileExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".slnx", ".csproj", ".json", ".txt", ".cmd", ".sh", ".iss",
        };

    /// <summary>
    /// Every path in the workflow that names a file this repository is supposed to contain.
    ///
    /// Two exclusions, and both are the difference between a check and a nuisance. A token
    /// carrying a `$` or a `${{ }}` is rooted somewhere on the runner - the vcpkg toolchain under
    /// VCPKG_INSTALLATION_ROOT is the one this port actually uses - and nothing in a checkout
    /// answers for it. A token under the build directory is output: it does not exist before the
    /// step that writes it, and a test asserting otherwise would only ever be green by accident.
    /// </summary>
    public static IReadOnlyList<string> RepositoryPathsIn(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match token in PathTokenRegex().Matches(workflow))
        {
            string path = token.Value;

            if (path.Contains('$', StringComparison.Ordinal))
                continue;

            if (!RepositoryFileExtensions.Contains(Path.GetExtension(path)))
                continue;

            if (IsBuildOutput(path))
                continue;

            paths.Add(path);
        }

        return [.. paths];
    }

    /// <summary>Whether a path names something a build writes rather than something it reads.</summary>
    public static bool IsBuildOutput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalised = path.Replace('\\', '/');

        return normalised.StartsWith("build/", StringComparison.OrdinalIgnoreCase)
            || normalised.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalised.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The runner labels the workflow's jobs ask for.</summary>
    public static IReadOnlySet<string> RunnersIn(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var runners = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match runner in RunsOnRegex().Matches(workflow))
            runners.Add(runner.Groups[1].Value.Trim('"', '\''));

        return runners;
    }

    /// <summary>The version actions/setup-dotnet is asked to install, or null if it is not asked.</summary>
    public static string? DotnetVersionIn(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        Match version = DotnetVersionRegex().Match(workflow);
        return version.Success ? version.Groups[1].Value.Trim('"', '\'') : null;
    }

    /// <summary>The target framework a .csproj declares, or null if it declares none.</summary>
    public static string? TargetFrameworkOf(string csproj)
    {
        ArgumentNullException.ThrowIfNull(csproj);

        Match framework = TargetFrameworkRegex().Match(csproj);
        return framework.Success ? framework.Groups[1].Value : null;
    }

    /// <summary>
    /// The bare version out of a target framework moniker - net10.0-windows is 10.0 - which is the
    /// only part of it a runner installs. The platform half is the machine's, and asserting on it
    /// would be asking setup-dotnet a question it has no answer to.
    /// </summary>
    public static string? VersionOfFramework(string? targetFramework)
    {
        if (targetFramework is null)
            return null;

        Match version = FrameworkVersionRegex().Match(targetFramework);
        return version.Success ? version.Groups[1].Value : null;
    }

    /// <summary>
    /// Whether the configure runs through vcpkg's toolchain file.
    ///
    /// The one line that makes PP230's manifest check mean anything. vcpkg.json is compared against
    /// CMakeLists.txt on every commit precisely so that a runner with nothing installed can
    /// configure - and a workflow that configures without the toolchain installs none of it, which
    /// leaves that comparison guarding a file nobody reads.
    /// </summary>
    public static bool ConfiguresThroughVcpkg(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return workflow.Contains("buildsystems/vcpkg.cmake", StringComparison.OrdinalIgnoreCase)
            || workflow.Contains(@"buildsystems\vcpkg.cmake", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP567: the category the local gate excludes, which CI has to exclude for the same reason.
    ///
    /// PP227 put the interaction tests outside test.cmd because they open the real application and
    /// drive its window through UI Automation; `test.cmd interaction` runs them and only them. The
    /// workflow's dotnet test step carried no filter, so a runner - which has no desk - would have
    /// run all five and gone red, immediately after the configure fix let it get that far.
    ///
    /// Held as the trait expression rather than as "some filter", because a filter that excluded
    /// something else would satisfy a looser check and still leave the five running.
    /// </summary>
    public const string InteractionExclusion = @"--filter ""Category!=Interaction""";

    /// <summary>The name test.cmd knows the category by, which both sides must agree on.</summary>
    public const string InteractionCategory = "Interaction";

    /// <summary>
    /// Whether the workflow still runs the managed suite without the interaction tests.
    ///
    /// Both halves: that it runs dotnet test at all, and that the run carries the exclusion. A
    /// workflow that stopped testing would pass a check that only looked for the filter.
    /// </summary>
    public static bool ExcludesTheInteractionTests(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return workflow.Contains("dotnet test", StringComparison.Ordinal)
            && workflow.Contains(InteractionExclusion, StringComparison.Ordinal);
    }

    /// <summary>One $env: reference in a run: step, and whether anything will expand it.</summary>
    /// <param name="Line">Its 1-based line number.</param>
    /// <param name="Text">The line, trimmed.</param>
    /// <param name="Quoted">Whether it sits inside a double-quoted string.</param>
    public sealed record EnvReference(int Line, string Text, bool Quoted);

    /// <summary>
    /// PP535: every $env: in the workflow, with whether pwsh will actually expand it.
    ///
    /// A run: step on a windows runner is pwsh, and pwsh expands $env:X/path inside a string but
    /// NOT inside a bare argument to a native command - there the token reaches the program as
    /// text. That is how the configure step handed cmake the literal
    /// "$env:VCPKG_INSTALLATION_ROOT/..." for 39 runs, none of which was ever green.
    ///
    /// <see cref="ConfiguresThroughVcpkg"/> could not see it: that asks whether the text names the
    /// toolchain file, and the text did, unexpanded. A check over what a workflow SAYS is green on
    /// a workflow that cannot run, which is the whole reason this one asks a different question.
    ///
    /// The quote test is per line, because the token and its quotes are on one line in a folded
    /// `run: >` block. A value split across two lines would be read as unquoted here - which fails
    /// toward complaining, and is the direction a gate should be wrong in.
    /// </summary>
    public static IReadOnlyList<EnvReference> EnvReferencesIn(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        string[] lines = workflow.ReplaceLineEndings("\n").Split('\n');
        var found = new List<EnvReference>();

        for (int i = 0; i < lines.Length; i++)
        {
            // A comment is not a run step. The note above this very step explains the rule and
            // quotes the broken form to do it, so a sweep that read comments would fail on its own
            // explanation - which is what CompileMessages found one task earlier about rem lines.
            if (lines[i].TrimStart().StartsWith('#'))
                continue;

            int at = lines[i].IndexOf("$env:", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;

            // Inside a double-quoted string when an odd number of quotes stands before it.
            int quotes = lines[i][..at].Count(c => c == '"');
            found.Add(new EnvReference(i + 1, lines[i].Trim(), quotes % 2 == 1));
        }

        return found;
    }

    /// <summary>
    /// PP36: whether both suites run in the workflow that builds.
    ///
    /// The C one and the managed one, because they are two suites and a workflow that runs one is a
    /// branch half of whose assertions cannot turn red. Named by the commands rather than by step
    /// titles: a step can be renamed and still be the thing, and a title cannot fail a job.
    /// </summary>
    public static bool RunsBothSuites(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return workflow.Contains("ctest ", StringComparison.Ordinal)
            && workflow.Contains("dotnet test ", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether their results are kept from the run that needs them.
    ///
    /// Both halves, and the second is the one that is easy to lose. A results file is only ever
    /// wanted after a red push, and a step with no condition is skipped as soon as anything above
    /// it fails - so an upload without `if: always()` writes the files and throws them away on
    /// exactly the runs they exist for.
    /// </summary>
    public static bool PublishesTestResults(string workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return workflow.Contains("upload-artifact", StringComparison.Ordinal)
            && workflow.Contains("if: always()", StringComparison.Ordinal)
            && (workflow.Contains("--output-junit", StringComparison.Ordinal)
                || workflow.Contains(".trx", StringComparison.Ordinal));
    }

    // A path-shaped token: a segment, then at least one separator, or a bare name with an
    // extension. Quotes and YAML punctuation end it; `$` is kept so the exclusion above can see it.
    [GeneratedRegex(@"[A-Za-z0-9_$.\-]+(?:[/\\][A-Za-z0-9_$.\-]+)*\.[A-Za-z0-9]+")]
    private static partial Regex PathTokenRegex();

    // runs-on: windows-2022
    [GeneratedRegex(@"runs-on:\s*(\S+)")]
    private static partial Regex RunsOnRegex();

    // dotnet-version: 10.0.x
    [GeneratedRegex(@"dotnet-version:\s*(\S+)")]
    private static partial Regex DotnetVersionRegex();

    // <TargetFramework>net10.0-windows</TargetFramework>
    [GeneratedRegex(@"<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>")]
    private static partial Regex TargetFrameworkRegex();

    // net10.0-windows -> 10.0
    [GeneratedRegex(@"^net([0-9]+\.[0-9]+)")]
    private static partial Regex FrameworkVersionRegex();
}
