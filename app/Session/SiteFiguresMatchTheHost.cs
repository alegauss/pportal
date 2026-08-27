using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP432: the site's derived figures, held against the host they are derived from.
///
/// site/src/lib/product.generated.ts says what it is for: "The figures the copy states, read out of
/// the application's own source rather than typed into a sentence." The derivation is right, and so
/// is the discipline round it - the site's own lint test refuses a version literal in the copy.
///
/// WHAT NOTHING CHECKED WAS THAT IT IS CURRENT. The committed file listed fourteen host flags while
/// <see cref="HostCommandLine"/> declared sixteen, and the two missing were --apply and
/// --select-corpus, added by PP417 and PP396. So the gap has evidence, and the evidence is that both
/// were added without anybody noticing the site had a list.
///
/// REGENERATING AT BUILD TIME IS NOT BEING RIGHT. `npm run build` runs the generator first, so a
/// deployed site carries whatever the generator finds. That covers staleness and not correctness: a
/// generator that stopped finding a flag produces a shorter list, the build regenerates happily, and
/// the site ships it.
///
/// SO THE CHECK IS HERE RATHER THAN IN THE SITE'S OWN TESTS. test.cmd runs this suite and not npm,
/// and this suite already reads gui/ and lib/ across the seam to hold the port against what it was
/// ported from. Reading one directory further out is the same act.
///
/// FLAGS ONLY. The version and framework come off a csproj this suite has no quarrel with; the flags
/// are the part that moved twice in one session.
/// </summary>
public static partial class SiteFiguresMatchTheHost
{
    /// <summary>Where the host declares its flags.</summary>
    public const string HostRelativePath = @"app\Session\HostCommandLine.cs";

    /// <summary>And where the site states them.</summary>
    public const string SiteRelativePath = @"site\src\lib\product.generated.ts";

    /// <summary>The host's file, or null outside a checkout.</summary>
    public static string? LocateHost() => SanitizerSource.LocateRelative(HostRelativePath);

    /// <summary>The site's, or null where the site is not in this checkout.</summary>
    public static string? LocateSite() => SanitizerSource.LocateRelative(SiteRelativePath);

    /// <summary>
    /// Every flag the host declares, as name and argument.
    ///
    /// Read from the source rather than from the loaded type, so the check answers about what is
    /// committed - which is what the generator reads too.
    /// </summary>
    public static IReadOnlyList<(string Name, string Argument)> HostFlags(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. HostFlagRegex().Matches(CCall.Code(source))
                .Select(m => (m.Groups["name"].Value, m.Groups["argument"].Value))
        ];
    }

    /// <summary>Every flag the site's generated file states, in the same shape.</summary>
    public static IReadOnlyList<(string Name, string Argument)> SiteFlags(string generated)
    {
        ArgumentNullException.ThrowIfNull(generated);

        return
        [
            .. SiteFlagRegex().Matches(generated)
                .Select(m => (m.Groups["name"].Value, m.Groups["argument"].Value))
        ];
    }

    /// <summary>
    /// The flags the two disagree about, as sentences.
    ///
    /// BOTH DIRECTIONS. A flag the host declares and the site omits is the staleness that started
    /// this; a flag the site states and the host no longer declares is a figure about a flag that
    /// does not exist, which is the same defect facing the other way.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string hostSource, string generated)
    {
        ArgumentNullException.ThrowIfNull(hostSource);
        ArgumentNullException.ThrowIfNull(generated);

        var host = HostFlags(hostSource).ToDictionary(f => f.Name, f => f.Argument, StringComparer.Ordinal);
        var site = SiteFlags(generated).ToDictionary(f => f.Name, f => f.Argument, StringComparer.Ordinal);

        var apart = new List<string>();

        foreach ((string name, string argument) in host)
        {
            if (!site.TryGetValue(name, out string? stated))
                apart.Add($"{name} is declared by the host and missing from the site");
            else if (stated != argument)
                apart.Add($"{name} takes \"{argument}\" and the site says \"{stated}\"");
        }

        foreach (string name in site.Keys.Where(name => !host.ContainsKey(name)))
            apart.Add($"{name} is stated by the site and no longer declared by the host");

        return apart;
    }

    // new("--recount", "", "check the sizes ...") - the table's own shape. The summary is not read:
    // it is prose that a JSON encoder in between would re-escape, and the names are what carry.
    [GeneratedRegex(@"new\(""(?<name>--[a-z-]+)"",\s*""(?<argument>[^""]*)""")]
    private static partial Regex HostFlagRegex();

    // "name": "--recount", "argument": "", - as scripts/product.mjs writes it.
    [GeneratedRegex(@"""name"":\s*""(?<name>--[a-z-]+)"",\s*""argument"":\s*""(?<argument>[^""]*)""")]
    private static partial Regex SiteFlagRegex();
}
