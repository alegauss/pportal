using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP447: the counts a roadkeep-remaining query answers, held against the prose that states them.
///
/// §PP30 said 14, dated, and its query reads 13. PP443's guard reads "N lines" and could not see
/// "N sites"; CountedClaims needs a filename beside the number and a query count has none.
/// </summary>
public class RemainingQueriesTests(ITestOutputHelper output)
{
    private static string? Improvements()
    {
        string? path = RemainingQueries.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE RULE. Every stated count is what its query actually answers.
    /// </summary>
    [Fact]
    public void EveryStatedCountIsWhatTheQueryReads()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;
        if (Improvements() is not { } improvements)
            return;

        IReadOnlyDictionary<string, RemainingQuery> queries = RemainingQueries.Declared(improvements);
        IReadOnlyList<StatedCount> stated = RemainingQueries.Stated(improvements);

        foreach ((string task, RemainingQuery query) in queries)
            output.WriteLine($"{task}: {query.Glob} :: {query.Pattern} -> {RemainingQueries.Run(root, query)}");

        // PP271: a reader that found no queries or no counts would agree with any prose at all.
        Assert.True(queries.Count >= 2, $"only {queries.Count} query fence(s) read");
        Assert.True(stated.Count >= 2, $"only {stated.Count} stated count(s) read");

        IReadOnlyList<string> apart = RemainingQueries.Disagreements(root, improvements);

        Assert.True(
            apart.Count == 0,
            "the backlog states a remaining count its own query does not answer:\n  "
                + string.Join("\n  ", apart));
    }

    /// <summary>
    /// The two the backlog declares, named - so a third appearing is something somebody reads.
    /// </summary>
    [Fact]
    public void TheTwoQueriesAreTheTwo()
    {
        if (Improvements() is not { } improvements)
            return;

        IReadOnlyDictionary<string, RemainingQuery> queries = RemainingQueries.Declared(improvements);

        Assert.Contains("PP30", queries);
        Assert.Contains("PP33", queries);
    }

    /// <summary>
    /// PP30's query counts matching LINES, which is roadkeep's unit and not files or calls.
    ///
    /// 13 across common.c, fec.c and frameprocessor.c, which is what `roadkeep remaining PP30`
    /// answers - the number this reader was checked against before it was trusted.
    /// </summary>
    [Fact]
    public void ASiteIsAMatchingLine()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        int? sites = RemainingQueries.Run(root, new RemainingQuery("lib/src/**/*.c", "jerasure|galois_"));

        Assert.Equal(13, sites);
    }

    /// <summary>
    /// The count may live in a DIFFERENT section from the fence. PP33's query is declared in §PP33
    /// and its count stated in §PP340, which is why the whole document is scanned.
    /// </summary>
    [Fact]
    public void ACountOutsideItsOwnSectionIsStillRead()
    {
        const string Doc = """
            ### §PP33 Two dependencies

            ```roadkeep-remaining
            lib/src/**/*.c :: curl_easy_
            ```

            ### §PP340 What has to be true first

            Until then PP33 is correctly blocked, and `remaining PP33` reads 420.
            """;

        Assert.Contains("PP33", RemainingQueries.Declared(Doc));

        StatedCount said = Assert.Single(RemainingQueries.Stated(Doc));
        Assert.Equal("PP33", said.Task);
        Assert.Equal(420, said.Stated);
    }

    /// <summary>A disagreement is reported, on a tree this test controls.</summary>
    [Fact]
    public void ADisagreementIsReported()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        // PP30's real query, with a count that is one out - which is exactly what it said.
        const string Doc = """
            ### §PP30 Reed-Solomon

            ```roadkeep-remaining
            lib/src/**/*.c :: jerasure|galois_
            ```

            `remaining PP30` reads 14
            """;

        string apart = Assert.Single(RemainingQueries.Disagreements(root, Doc));

        Assert.Contains("says PP30 reads 14", apart, StringComparison.Ordinal);
        Assert.Contains("it reads 13", apart, StringComparison.Ordinal);
    }

    /// <summary>A count for a task that declares no query is reported as that, not compared.</summary>
    [Fact]
    public void ACountWithNoQueryIsReported()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string apart = Assert.Single(RemainingQueries.Disagreements(
            root, "`remaining PP999` reads 7\n"));

        Assert.Contains("no section declares a query", apart, StringComparison.Ordinal);
    }

    /// <summary>
    /// An absent tree is not zero. A query over a directory this checkout lacks answers null, so the
    /// prose is left alone rather than agreed with.
    /// </summary>
    [Fact]
    public void AnAbsentDirectoryIsNotZero()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.Null(RemainingQueries.Run(
            root, new RemainingQuery("no/such/place/**/*.c", "anything")));

        // And so nothing is reported about it.
        Assert.Empty(RemainingQueries.Disagreements(root, """
            ### §PP1 A section

            ```roadkeep-remaining
            no/such/place/**/*.c :: anything
            ```

            `remaining PP1` reads 99
            """));
    }

    /// <summary>PP272: and an empty document declares and states nothing.</summary>
    [Fact]
    public void AnEmptyDocumentSaysNothing()
    {
        Assert.Empty(RemainingQueries.Declared(""));
        Assert.Empty(RemainingQueries.Stated(""));

        if (SanitizerSource.RepositoryRoot() is { } root)
            Assert.Empty(RemainingQueries.Disagreements(root, ""));
    }

    /// <summary>The fence's own shape is required: prose naming a query is not one.</summary>
    [Fact]
    public void ProseNamingAQueryIsNotAFence()
    {
        Assert.Empty(RemainingQueries.Declared("""
            ### §PP30 Reed-Solomon

            The query is lib/src/**/*.c :: jerasure|galois_ and it counts call sites.
            """));
    }
}
