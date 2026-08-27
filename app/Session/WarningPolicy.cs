using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP316: what the build does with a warning, held against the projects the gate builds.
///
/// A warning is a message with no recipient. compile.cmd prints hundreds of lines, test.cmd prints
/// hundreds more, and nothing between them stops on one - so xUnit2000 named an assertion that
/// compared two literals and could not fail, in every run, from the commit that wrote it until a
/// session read the log for an unrelated reason. Three others were keeping it company: xUnit2029 on
/// an assertion that could not name what broke it, CS0108 on a property hiding the one it inherited,
/// CS8123 on a tuple element name the compiler dropped.
///
/// The policy is therefore <c>TreatWarningsAsErrors</c> and not a list of codes. PP22 named IL3000
/// through IL3002 for a reason that was specific and correct, and the list still could not have
/// caught any of the four - which is PP279's finding about the root-file list arriving one file out.
///
/// PP438: WHAT THE GATE BUILDS IS THE SOLUTION, and this used to be a hardcoded pair. Its reason was
/// written down and was true - "spike\, gate\ and tools\ carry csproj files of their own and no gate
/// compiles them" - and PP436 made it false in the same commit that relied on it, by adding
/// tools/compare-baselines to ChiakiNg.slnx so compile.cmd would build the tool the site names. That
/// project declared no TreatWarningsAsErrors, so the gate compiled code whose warnings had no
/// recipient: PP316's own defect, reintroduced by a task that was widening coverage.
///
/// So the list is derived. compile.cmd runs `dotnet build ChiakiNg.slnx`, which makes the solution the
/// thing that decides, and a project added to it tomorrow obeys this policy without anybody
/// remembering this file exists. It is PP434's lesson: a list standing for a graph is green because
/// of what a file happened to contain.
/// </summary>
public static partial class WarningPolicy
{
    /// <summary>The test assembly. The host is <see cref="BuildWorkflow.HostProjectRelativePath"/>.</summary>
    public const string TestProjectRelativePath = @"tests\ChiakiNg.Tests\ChiakiNg.Tests.csproj";

    /// <summary>
    /// The solution compile.cmd builds, which is what decides who this policy binds.
    /// </summary>
    public const string SolutionRelativePath = SiteProseClaims.SolutionRelativePath;

    /// <summary>
    /// Every project the solution names, as a repository-relative path with Windows separators.
    ///
    /// Comments stripped first: the folder entry PP436 added names the four projects it deliberately
    /// LEAVES OUT, and a reader counting those would bind a policy to projects no gate compiles -
    /// which is the mirror of the defect this fixes.
    /// </summary>
    public static IReadOnlyList<string> ProjectsIn(string solutionText)
    {
        ArgumentNullException.ThrowIfNull(solutionText);

        return
        [
            .. ProjectPathRegex().Matches(WithoutComments(solutionText))
                .Select(match => match.Groups[1].Value.Replace('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// The projects a gate in this tree compiles, and therefore the ones this policy binds.
    ///
    /// Empty outside a checkout, where there is no solution to read. A caller that would otherwise
    /// assert over an empty set has to say so - which is what the test does.
    /// </summary>
    public static IReadOnlyList<string> GatedProjects()
    {
        if (SanitizerSource.LocateRelative(SolutionRelativePath) is not { } solution)
            return [];

        return ProjectsIn(File.ReadAllText(solution));
    }

    /// <summary>
    /// The only warning code this port suppresses outright, and the project it is suppressed in.
    ///
    /// Named here so that <see cref="SuppressedIn"/> has something to be held against. NoWarn is
    /// the door this whole policy can be walked back out of one code at a time, and a suppression
    /// nobody has to justify is the state before PP316 with an extra step.
    /// </summary>
    public static IReadOnlySet<string> AllowedSuppressions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WPF0001" };

    /// <summary>The gated projects, resolved against this checkout; empty outside one.</summary>
    public static IReadOnlyList<string> LocateGatedProjects() =>
        [.. GatedProjects().Select(SanitizerSource.LocateRelative).OfType<string>()];

    /// <summary>
    /// PP317: a project text with its XML comments removed, which is what every reader here reads.
    ///
    /// Found by this file's own check going red on this file's own prose. The csproj comment that
    /// explains why the two UIAutomation references were DELETED spells one out to say what it is
    /// talking about, and a reader matching flat text counted it as a live item.
    ///
    /// Both directions matter and the other one is worse. A commented-out
    /// <c>&lt;TreatWarningsAsErrors&gt;true&lt;/TreatWarningsAsErrors&gt;</c> would read as a
    /// project refusing warnings while the build printed them - a gate reporting on prose is a
    /// gate that is not there, which is the whole of what PP316 was filed about.
    /// </summary>
    public static string WithoutComments(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        return XmlCommentRegex().Replace(projectText, "");
    }

    /// <summary>
    /// Whether a project text turns every compiler warning into an error.
    ///
    /// The value is read rather than assumed present: <c>&lt;TreatWarningsAsErrors&gt;false&lt;/&gt;</c>
    /// is the shape a future disabling would take, and a check testing only for the element's
    /// presence would call that a pass.
    /// </summary>
    public static bool RefusesEveryWarning(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        Match match = TreatWarningsAsErrorsRegex().Match(WithoutComments(projectText));
        return match.Success
            && string.Equals(match.Groups[1].Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every warning code a project text silences, in declaration order and without duplicates.
    ///
    /// <c>$(NoWarn)</c> and any other MSBuild reference is dropped: it names whatever the SDK
    /// already put there, which is not this port's decision and not a code.
    /// </summary>
    public static IReadOnlyList<string> SuppressedIn(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        var codes = new List<string>();

        foreach (Match element in NoWarnRegex().Matches(WithoutComments(projectText)))
        {
            foreach (string token in element.Groups[1].Value.Split(
                [';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Contains('$', StringComparison.Ordinal))
                    continue;

                if (!codes.Contains(token, StringComparer.OrdinalIgnoreCase))
                    codes.Add(token);
            }
        }

        return codes;
    }

    /// <summary>
    /// PP317: every assembly a project asks for by bare name, which is the pre-SDK resolution path.
    ///
    /// The four warnings PP316's policy could not reach were all this: two
    /// <c>&lt;Reference Include="UIAutomation…" /&gt;</c> items naming assemblies the WindowsDesktop
    /// framework reference already supplied. MSB3245 could not find them down that path, MSB3243
    /// then resolved the conflict between the two candidates "arbitrarily", and naming what was
    /// already there is what created the second candidate to choose between.
    ///
    /// A Reference carrying a <c>HintPath</c> is not this: it names a file on disk, which is a
    /// deliberate answer to where an assembly comes from and not a guess at one.
    /// </summary>
    public static IReadOnlyList<string> BareAssemblyReferencesIn(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        var names = new List<string>();

        foreach (Match item in BareReferenceRegex().Matches(WithoutComments(projectText)))
        {
            if (item.Value.Contains("HintPath", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = item.Groups[1].Value;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names;
    }

    [GeneratedRegex(@"<TreatWarningsAsErrors>([^<]*)</TreatWarningsAsErrors>", RegexOptions.IgnoreCase)]
    private static partial Regex TreatWarningsAsErrorsRegex();

    [GeneratedRegex(@"<Reference\s[^>]*?Include=""([^""]+)""[^>]*?/>", RegexOptions.IgnoreCase)]
    private static partial Regex BareReferenceRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlCommentRegex();

    [GeneratedRegex(@"<NoWarn>([^<]*)</NoWarn>", RegexOptions.IgnoreCase)]
    private static partial Regex NoWarnRegex();

    // <Project Path="tools/compare-baselines/CompareBaselines.csproj" /> - the slnx spells its paths
    // with forward slashes, and the rest of this port's constants use backslashes.
    [GeneratedRegex(@"<Project\s+Path=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectPathRegex();
}
