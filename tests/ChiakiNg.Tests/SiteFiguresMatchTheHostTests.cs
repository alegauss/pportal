using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP432: the site's derived flag list, held against the host's own table.
///
/// The committed generated file listed fourteen flags while HostCommandLine declared sixteen, and
/// the two missing were added by PP417 and PP396 - in this session, without anybody noticing the
/// site had a list.
/// </summary>
public class SiteFiguresMatchTheHostTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE. The site states exactly the flags the host declares, with the same arguments.
    ///
    /// Both directions: a flag the host declares and the site omits is the staleness that started
    /// this, and one the site states that the host no longer declares is a figure about a flag that
    /// does not exist.
    /// </summary>
    [Fact]
    public void TheSiteStatesExactlyTheHostsFlags()
    {
        if (SiteFiguresMatchTheHost.LocateHost() is not { } host)
            return;
        if (SiteFiguresMatchTheHost.LocateSite() is not { } site)
            return;

        string hostSource = File.ReadAllText(host);
        string generated = File.ReadAllText(site);

        IReadOnlyList<(string Name, string Argument)> declared =
            SiteFiguresMatchTheHost.HostFlags(hostSource);

        output.WriteLine($"{declared.Count} declared, "
            + $"{SiteFiguresMatchTheHost.SiteFlags(generated).Count} stated");

        // A sweep that reads no flags passes about nothing - PP271's lesson.
        Assert.True(
            declared.Count >= 10,
            $"only {declared.Count} flags read from the host - the reader is not working");

        IReadOnlyList<string> apart =
            SiteFiguresMatchTheHost.Disagreements(hostSource, generated);

        Assert.True(
            apart.Count == 0,
            "the site's figures and the host's table disagree, so the copy states flags that are "
                + "not the application's:\n  " + string.Join("\n  ", apart)
                + "\n\nRun `npm run generate` in site/.");
    }

    /// <summary>
    /// And the two this session added are in both, named so a regression is legible.
    /// </summary>
    [Theory]
    [InlineData("--apply")]
    [InlineData("--select-corpus")]
    public void TheFlagsThisSessionAddedAreInBoth(string name)
    {
        if (SiteFiguresMatchTheHost.LocateHost() is not { } host)
            return;
        if (SiteFiguresMatchTheHost.LocateSite() is not { } site)
            return;

        Assert.Contains(
            SiteFiguresMatchTheHost.HostFlags(File.ReadAllText(host)),
            flag => flag.Name == name);

        Assert.Contains(
            SiteFiguresMatchTheHost.SiteFlags(File.ReadAllText(site)),
            flag => flag.Name == name);
    }

    /// <summary>
    /// Both readers find their own shape, and a disagreement in either direction is reported.
    ///
    /// On synthetic text, because the real files are required to agree and so cannot be the fixture
    /// for the case that matters.
    /// </summary>
    [Fact]
    public void ADisagreementIsReportedEitherWay()
    {
        const string Host = """
            		new("--recount", "", "check the sizes"),
            		new("--apply", "", "with --recount: run those"),
            """;

        const string Site = """
              {
                "name": "--recount",
                "argument": "",
                "summary": "check the sizes"
              },
            """;

        // The host declares two and the site states one.
        Assert.Equal(2, SiteFiguresMatchTheHost.HostFlags(Host).Count);
        Assert.Single(SiteFiguresMatchTheHost.SiteFlags(Site));

        string missing = Assert.Single(SiteFiguresMatchTheHost.Disagreements(Host, Site));
        Assert.Contains("--apply", missing, StringComparison.Ordinal);
        Assert.Contains("missing from the site", missing, StringComparison.Ordinal);

        // And the other way: a flag the site states that the host does not declare.
        const string HostOnlyOne = "\t\tnew(\"--recount\", \"\", \"check the sizes\"),";
        const string SiteTwo = """
              {
                "name": "--recount",
                "argument": "",
                "summary": "check the sizes"
              },
              {
                "name": "--gone",
                "argument": "",
                "summary": "a flag nothing declares"
              },
            """;

        string stale = Assert.Single(
            SiteFiguresMatchTheHost.Disagreements(HostOnlyOne, SiteTwo));
        Assert.Contains("--gone", stale, StringComparison.Ordinal);
        Assert.Contains("no longer declared", stale, StringComparison.Ordinal);
    }

    /// <summary>An argument that disagrees is a disagreement too, not just a missing name.</summary>
    [Fact]
    public void AnArgumentThatDisagreesIsReported()
    {
        const string Host = "\t\tnew(\"--select-corpus\", \"<in> <out>\", \"keep the entries\"),";
        const string Site = """
              {
                "name": "--select-corpus",
                "argument": "",
                "summary": "keep the entries"
              },
            """;

        string apart = Assert.Single(SiteFiguresMatchTheHost.Disagreements(Host, Site));
        Assert.Contains("<in> <out>", apart, StringComparison.Ordinal);
    }

    /// <summary>PP272: and neither reader invents a flag from nothing.</summary>
    [Fact]
    public void NeitherReaderFindsAnythingInAnEmptyFile()
    {
        Assert.Empty(SiteFiguresMatchTheHost.HostFlags(""));
        Assert.Empty(SiteFiguresMatchTheHost.SiteFlags(""));
        Assert.Empty(SiteFiguresMatchTheHost.Disagreements("", ""));
    }

    /// <summary>And a commented declaration is not one - PP400's rule.</summary>
    [Fact]
    public void ACommentedDeclarationIsNotAFlag()
    {
        Assert.Empty(SiteFiguresMatchTheHost.HostFlags(
            "\t\t// new(\"--gone\", \"\", \"a flag that was removed\"),"));
    }
}
