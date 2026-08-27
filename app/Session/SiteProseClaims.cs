using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>What kind of thing an inline-code value in the site's prose is.</summary>
public enum ProseClaimKind
{
    /// <summary>A host flag, which the host has to declare.</summary>
    HostFlag,

    /// <summary>An executable this repo ships, which a build has to reach.</summary>
    ToolProject,

    /// <summary>A term of the domain, which names nothing in this tree.</summary>
    DomainTerm,
}

/// <summary>One inline-code value the site's prose states, and what it claims.</summary>
/// <param name="Code">The value as the copy spells it.</param>
/// <param name="Kind">What it is a claim about.</param>
public readonly record struct ProseClaim(string Code, ProseClaimKind Kind);

/// <summary>
/// PP436: the things the site's PROSE names, held against the tree.
///
/// PP432 joined the site's flag list to <see cref="HostCommandLine"/> and stopped there, and the
/// reason it stopped is that the list is generated - a derived file is wrong only if the generator
/// is. The prose is not generated. features.ts and site-content.ts are written by hand, and they
/// carry inline-code values that are claims about this application.
///
/// THERE ARE FOUR AND THREE ARE CLAIMS: `compare-baselines`, `--controllers`, `--capture-controller`,
/// and `d3d11va`. All three checkable ones are right today, and nothing checked them - which is
/// PP434's shape, where a green answer came from what the files happened to contain.
///
/// THE TOOL WAS IN NO BUILD. compile.cmd builds ChiakiNg.slnx; this tree has seven first-party csproj
/// files and the solution named two. So the front page promised what compare-baselines prints while
/// no gate here compiled it. PP436 put it in the solution, and this holds the join.
///
/// d3d11va IS THE FOURTH and it names nothing in this tree - it is a decoder the domain calls that.
/// It sits in <see cref="DomainTerms"/> with its reason, so the bucket that lets a value through is
/// a decision and not a hole.
/// </summary>
public static partial class SiteProseClaims
{
    /// <summary>The hand-written copy. Not the generated file, which PP432 already holds.</summary>
    public static IReadOnlyList<string> ProseRelativePaths { get; } =
        [@"site\src\lib\features.ts", @"site\src\lib\site-content.ts"];

    /// <summary>Where the host declares its flags.</summary>
    public const string HostRelativePath = @"app\Session\HostCommandLine.cs";

    /// <summary>And the solution compile.cmd builds.</summary>
    public const string SolutionRelativePath = "ChiakiNg.slnx";

    /// <summary>
    /// Inline-code values that name nothing in this tree, each with why it is exempt.
    ///
    /// Written down rather than inferred: a value that matches no flag and no project would
    /// otherwise be either a false alarm or a silent pass, and this is the third option.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DomainTerms { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["d3d11va"] = "the name ffmpeg gives the Direct3D 11 hardware decoder, so the copy is "
                + "quoting an ffmpeg identifier and not a file here",
        };

    /// <summary>The prose files, concatenated, or null where the site is not in this checkout.</summary>
    public static string? ReadProse()
    {
        string?[] found = [.. ProseRelativePaths.Select(SanitizerSource.LocateRelative)];

        if (found.Any(path => path is null))
            return null;

        return string.Concat(found.Select(path => File.ReadAllText(path!)));
    }

    /// <summary>Every inline-code value the prose states, classified, without duplicates.</summary>
    public static IReadOnlyList<ProseClaim> Claims(string prose)
    {
        ArgumentNullException.ThrowIfNull(prose);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var claims = new List<ProseClaim>();

        foreach (Match found in CodeValueRegex().Matches(prose))
        {
            string code = found.Groups["code"].Value;
            if (seen.Add(code))
                claims.Add(new ProseClaim(code, KindOf(code)));
        }

        return claims;
    }

    /// <summary>
    /// What a value claims, by its shape.
    ///
    /// A leading double dash is a flag; a value the domain list names is a term; anything else is
    /// taken to be a program this repo ships. That last default is deliberate - an unrecognised
    /// value reported as an unbuilt tool is a false alarm somebody fixes, and one waved through is a
    /// claim nobody checks.
    /// </summary>
    public static ProseClaimKind KindOf(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        if (code.StartsWith("--", StringComparison.Ordinal))
            return ProseClaimKind.HostFlag;

        return DomainTerms.ContainsKey(code) ? ProseClaimKind.DomainTerm : ProseClaimKind.ToolProject;
    }

    /// <summary>Whether the host declares a flag, read from its source.</summary>
    public static bool HostDeclares(string hostSource, string flag)
    {
        ArgumentNullException.ThrowIfNull(hostSource);
        ArgumentException.ThrowIfNullOrEmpty(flag);

        return CCall.Code(hostSource).Contains($"new(\"{flag}\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the solution names a project in a directory called <paramref name="tool"/>.
    ///
    /// The directory and not the assembly name: the site spells the executable, and this tree names
    /// the folder after it - tools/compare-baselines holds CompareBaselines.csproj.
    /// </summary>
    public static bool SolutionBuilds(string solution, string tool)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrEmpty(tool);

        return solution.Contains($"/{tool}/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every claim the tree does not bear out, as sentences.
    ///
    /// Comments stripped from the solution before it is read: the folder entry PP436 added explains
    /// itself at length, and a reader counting that prose would find every tool built.
    /// </summary>
    public static IReadOnlyList<string> Unmet(string prose, string hostSource, string solution)
    {
        ArgumentNullException.ThrowIfNull(prose);
        ArgumentNullException.ThrowIfNull(hostSource);
        ArgumentNullException.ThrowIfNull(solution);

        string projects = WithoutXmlComments(solution);
        var unmet = new List<string>();

        foreach (ProseClaim claim in Claims(prose))
        {
            switch (claim.Kind)
            {
                case ProseClaimKind.HostFlag when !HostDeclares(hostSource, claim.Code):
                    unmet.Add($"{claim.Code} is written in the copy and the host declares no such flag");
                    break;

                case ProseClaimKind.ToolProject when !SolutionBuilds(projects, claim.Code):
                    unmet.Add($"{claim.Code} is named by the copy and no project in the solution builds it");
                    break;

                default:
                    break;
            }
        }

        return unmet;
    }

    // { code: "compare-baselines" } - the site's own shape for an inline-code run.
    [GeneratedRegex(@"\{\s*code:\s*""(?<code>[^""]+)""")]
    private static partial Regex CodeValueRegex();

    // <!-- ... -->, so a comment naming a project does not count as building it. PP400's rule.
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlCommentRegex();

    private static string WithoutXmlComments(string solution)
        => XmlCommentRegex().Replace(solution, "");
}
