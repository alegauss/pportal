using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP446: the documentation area at /pportal/docs, held against the site it is built beside.
///
/// The area is a second npm project with a second toolchain, and what joins it to the first is
/// three lines in three files. None of the three fails loudly: a build order that empties the area
/// away, a base that disagrees with the site's, and an output directory the deploy does not upload
/// all produce a green build and a wrong publish.
///
/// scripts/docs.test.mjs asserts what the build produced - the pages, the search index, the
/// sitemap robots names. This asserts the sources, so a reordered script is reported as a reordered
/// script rather than as a folder that went missing.
/// </summary>
public class SiteDocsAreaTests(ITestOutputHelper output)
{
    /// <summary>THE RULE. Every join between the site and the docs area still holds.</summary>
    [Fact]
    public void EveryJoinBetweenTheSiteAndTheDocsAreaHolds()
    {
        if (SiteDocsArea.Read() is not { } files)
            return;

        IReadOnlyList<string> unmet = SiteDocsArea.Unmet(files.Package, files.Vite, files.Astro);
        foreach (string sentence in unmet)
            output.WriteLine(sentence);

        Assert.True(
            unmet.Count == 0,
            "the documentation area and the site it ships inside no longer agree:\n  "
                + string.Join("\n  ", unmet));
    }

    /// <summary>
    /// And the values are the ones PP446 wired, so a change to any of them is a thing somebody
    /// looks at rather than a drift that keeps passing because the rule only compares two files.
    /// </summary>
    [Fact]
    public void TheAreaIsPublishedUnderTheSiteBasePlusOneSegment()
    {
        if (SiteDocsArea.Read() is not { } files)
            return;

        Assert.Equal("/pportal/", SiteDocsArea.SiteBase(files.Vite));
        Assert.Equal("/pportal/docs", SiteDocsArea.DocsBase(files.Astro));
        Assert.Equal("../dist/docs", SiteDocsArea.DocsOutDir(files.Astro));
    }

    /// <summary>
    /// The build order, in both directions.
    ///
    /// `vite build` empties dist/, so a docs build placed before the prerender is deleted by the
    /// step after it. The failure is one absent directory in an otherwise correct artefact, which
    /// is why the order is asserted rather than commented.
    /// </summary>
    [Fact]
    public void ADocsBuildBeforeThePrerenderIsReported()
    {
        const string Wrong = """{ "scripts": { "build": "npm run build:docs && node scripts/prerender.mjs" } }""";
        const string Right = """{ "scripts": { "build": "node scripts/prerender.mjs && npm run build:docs" } }""";

        Assert.False(SiteDocsArea.DocsBuildRunsLast(Wrong));
        Assert.True(SiteDocsArea.DocsBuildRunsLast(Right));

        string reported = Assert.Single(SiteDocsArea.Unmet(Wrong, """export const BASE = "/p/";""", Base("/p/docs")));
        Assert.Contains("empties dist", reported, StringComparison.Ordinal);
    }

    /// <summary>A build that never runs the docs at all is the same defect, not a passing one.</summary>
    [Fact]
    public void ABuildThatNeverRunsTheDocsIsReported()
    {
        const string Missing = """{ "scripts": { "build": "vite build && node scripts/prerender.mjs" } }""";

        Assert.False(SiteDocsArea.DocsBuildRunsLast(Missing));
    }

    /// <summary>
    /// PP446: "build:docs" is a key of its own in that file, and reading it as the "build" script
    /// would make the rule pass on a package.json whose build never mentions the docs.
    /// </summary>
    [Fact]
    public void TheScriptReadIsBuildAndNotBuildDocs()
    {
        const string Package = """
            { "scripts": {
                "build": "vite build && node scripts/prerender.mjs && npm run build:docs",
                "build:docs": "npm --prefix docs run build"
            } }
            """;

        Assert.Contains("prerender.mjs", SiteDocsArea.BuildScript(Package), StringComparison.Ordinal);
        Assert.DoesNotContain("--prefix", SiteDocsArea.BuildScript(Package), StringComparison.Ordinal);
    }

    /// <summary>A base that disagrees with the site's is reported, and it says what it should be.</summary>
    [Fact]
    public void ABaseThatDisagreesWithTheSiteIsReported()
    {
        const string Package = """{ "scripts": { "build": "node scripts/prerender.mjs && npm run build:docs" } }""";
        const string Vite = """export const BASE = "/pportal/";""";

        string reported = Assert.Single(SiteDocsArea.Unmet(Package, Vite, Base("/docs")));
        Assert.Contains("/pportal/docs", reported, StringComparison.Ordinal);

        // And the agreeing pair is not reported.
        Assert.Empty(SiteDocsArea.Unmet(Package, Vite, Base("/pportal/docs")));
    }

    /// <summary>The expected base is derived from the site's, whatever the repository is renamed to.</summary>
    [Fact]
    public void TheExpectedBaseFollowsARename()
    {
        Assert.Equal("/pportal/docs", SiteDocsArea.Expected("/pportal/"));
        Assert.Equal("/other/docs", SiteDocsArea.Expected("/other/"));
        Assert.Equal("/other/docs", SiteDocsArea.Expected("/other"));
    }

    /// <summary>An outDir outside the tree the deploy uploads is reported.</summary>
    [Fact]
    public void AnOutDirOutsideTheDeployedTreeIsReported()
    {
        Assert.True(SiteDocsArea.LandsInTheSiteDist("../dist/docs"));
        Assert.True(SiteDocsArea.LandsInTheSiteDist(@"..\dist\docs"));
        Assert.False(SiteDocsArea.LandsInTheSiteDist("dist"));
        Assert.False(SiteDocsArea.LandsInTheSiteDist("../../public/docs"));

        const string Package = """{ "scripts": { "build": "node scripts/prerender.mjs && npm run build:docs" } }""";
        string astro = """
            const BASE = "/pportal/docs";
            const OUT_DIR = "dist";
            """;

        string reported = Assert.Single(SiteDocsArea.Unmet(Package, """export const BASE = "/pportal/";""", astro));
        Assert.Contains("site/dist", reported, StringComparison.Ordinal);
    }

    /// <summary>A declaration renamed away reads as broken, never as absent.</summary>
    [Fact]
    public void AMissingDeclarationIsReportedRatherThanPassed()
    {
        const string Package = """{ "scripts": { "build": "node scripts/prerender.mjs && npm run build:docs" } }""";

        Assert.Contains(
            SiteDocsArea.Unmet(Package, "", Base("/pportal/docs")),
            s => s.Contains("no longer exports a BASE", StringComparison.Ordinal));

        Assert.Contains(
            SiteDocsArea.Unmet(Package, """export const BASE = "/pportal/";""", """const OUT_DIR = "../dist/docs";"""),
            s => s.Contains("no longer declares a BASE", StringComparison.Ordinal));
    }

    /// <summary>An astro config carrying the two declarations this reads, and nothing else.</summary>
    private static string Base(string value)
        => $"""
            const BASE = "{value}";
            const OUT_DIR = "../dist/docs";
            """;
}
