using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP436: the claims the site's hand-written prose makes about this application.
///
/// PP432 held the generated flag list and stopped, because a derived file is wrong only if the
/// generator is. features.ts and site-content.ts are typed by hand and name a tool and two flags -
/// all three right today, and checked by nothing.
/// </summary>
public class SiteProseClaimsTests(ITestOutputHelper output)
{
    private static string? Solution()
        => SanitizerSource.LocateRelative(SiteProseClaims.SolutionRelativePath);

    private static string? Host()
        => SanitizerSource.LocateRelative(SiteProseClaims.HostRelativePath);

    /// <summary>
    /// THE RULE. Every flag the copy types is one the host declares, and every tool it names is one
    /// the solution builds.
    /// </summary>
    [Fact]
    public void EveryClaimTheProseMakesHoldsInTheTree()
    {
        if (SiteProseClaims.ReadProse() is not { } prose)
            return;
        if (Host() is not { } host || Solution() is not { } solution)
            return;

        IReadOnlyList<ProseClaim> claims = SiteProseClaims.Claims(prose);
        foreach (ProseClaim claim in claims)
            output.WriteLine($"{claim.Code}: {claim.Kind}");

        // PP271: a reader that found no claims would pass about nothing.
        Assert.True(claims.Count >= 3, $"only {claims.Count} inline-code values read from the prose");

        IReadOnlyList<string> unmet = SiteProseClaims.Unmet(
            prose, File.ReadAllText(host), File.ReadAllText(solution));

        Assert.True(
            unmet.Count == 0,
            "the site states things about this application that the tree does not bear out:\n  "
                + string.Join("\n  ", unmet));
    }

    /// <summary>
    /// And the four values are the four, classified as PP436 found them - so a fifth appearing is a
    /// thing somebody looks at rather than a claim that slid in.
    /// </summary>
    [Fact]
    public void TheProseNamesATheToolTwoFlagsAndOneDomainTerm()
    {
        if (SiteProseClaims.ReadProse() is not { } prose)
            return;

        IReadOnlyList<ProseClaim> claims = SiteProseClaims.Claims(prose);

        Assert.Contains(claims, c => c.Code == "compare-baselines" && c.Kind == ProseClaimKind.ToolProject);
        Assert.Contains(claims, c => c.Code == "--controllers" && c.Kind == ProseClaimKind.HostFlag);
        Assert.Contains(claims, c => c.Code == "--capture-controller" && c.Kind == ProseClaimKind.HostFlag);
        Assert.Contains(claims, c => c.Code == "d3d11va" && c.Kind == ProseClaimKind.DomainTerm);
    }

    /// <summary>
    /// PP436's own remedy, asserted: the solution reaches the tool the front page promises.
    /// </summary>
    [Fact]
    public void TheSolutionBuildsTheToolTheCopyNames()
    {
        if (Solution() is not { } solution)
            return;

        Assert.True(
            SiteProseClaims.SolutionBuilds(File.ReadAllText(solution), "compare-baselines"),
            "compare-baselines is named on the site and compile.cmd builds ChiakiNg.slnx");
    }

    /// <summary>A flag the host does not declare is named, which is PP432's gap for typed prose.</summary>
    [Fact]
    public void AFlagTheHostDoesNotDeclareIsReported()
    {
        const string Prose = """{ code: "--invented" }""";
        const string Host = "\t\tnew(\"--recount\", \"\", \"check the sizes\"),";

        string unmet = Assert.Single(SiteProseClaims.Unmet(Prose, Host, "<Solution />"));

        Assert.Contains("--invented", unmet, StringComparison.Ordinal);
        Assert.Contains("no such flag", unmet, StringComparison.Ordinal);

        // And a declared one is not reported.
        Assert.Empty(SiteProseClaims.Unmet("""{ code: "--recount" }""", Host, "<Solution />"));
    }

    /// <summary>And a tool no project builds is named, which is the half PP436 found true.</summary>
    [Fact]
    public void AToolNoProjectBuildsIsReported()
    {
        const string Prose = """{ code: "measure-everything" }""";

        string unmet = Assert.Single(SiteProseClaims.Unmet(Prose, "", "<Solution />"));

        Assert.Contains("measure-everything", unmet, StringComparison.Ordinal);
        Assert.Contains("no project in the solution builds it", unmet, StringComparison.Ordinal);

        Assert.Empty(SiteProseClaims.Unmet(
            Prose, "", """<Project Path="tools/measure-everything/Thing.csproj" />"""));
    }

    /// <summary>
    /// PP400: a comment naming a project is not building it.
    ///
    /// The folder entry PP436 added to the solution explains itself at length and names the other
    /// four projects it deliberately leaves out - so a reader counting that prose would find every
    /// tool built, including the ones that are not.
    /// </summary>
    [Fact]
    public void ACommentNamingAProjectIsNotBuildingIt()
    {
        const string Prose = """{ code: "present-path" }""";
        const string Solution = """
            <Solution>
              <!-- spike/present-path stays out: it is a record of an experiment's answer. -->
              <Project Path="app/ChiakiNg.csproj" />
            </Solution>
            """;

        string unmet = Assert.Single(SiteProseClaims.Unmet(Prose, "", Solution));
        Assert.Contains("present-path", unmet, StringComparison.Ordinal);
    }

    /// <summary>
    /// A domain term is exempt and says why, so the bucket that lets a value through is a decision.
    /// </summary>
    [Fact]
    public void ADomainTermIsExemptAndCarriesItsReason()
    {
        Assert.Equal(ProseClaimKind.DomainTerm, SiteProseClaims.KindOf("d3d11va"));
        Assert.Empty(SiteProseClaims.Unmet("""{ code: "d3d11va" }""", "", "<Solution />"));

        foreach ((string term, string because) in SiteProseClaims.DomainTerms)
        {
            Assert.True(
                because.Length > 40,
                $"{term} is exempt and the reason beside it is not one a reader could act on");
        }
    }

    /// <summary>
    /// An unrecognised value defaults to a tool and so is REPORTED, not waved through.
    ///
    /// The direction matters: a false alarm is a thing somebody fixes in a minute, and a silent pass
    /// is the state PP436 was filed about.
    /// </summary>
    [Fact]
    public void AnUnknownValueIsTreatedAsAClaimAndNotIgnored()
    {
        Assert.Equal(ProseClaimKind.ToolProject, SiteProseClaims.KindOf("something-nobody-declared"));
    }

    /// <summary>PP272: and empty prose claims nothing.</summary>
    [Fact]
    public void EmptyProseClaimsNothing()
    {
        Assert.Empty(SiteProseClaims.Claims(""));
        Assert.Empty(SiteProseClaims.Unmet("", "", ""));
        Assert.False(SiteProseClaims.HostDeclares("", "--recount"));
        Assert.False(SiteProseClaims.SolutionBuilds("", "compare-baselines"));
    }

    /// <summary>The same value twice in the copy is one claim, not two.</summary>
    [Fact]
    public void ARepeatedValueIsOneClaim()
    {
        IReadOnlyList<ProseClaim> claims = SiteProseClaims.Claims(
            """{ code: "compare-baselines" } ... { code: "compare-baselines" }""");

        Assert.Single(claims);
    }
}
