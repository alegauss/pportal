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
