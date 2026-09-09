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
        //
        // The stated-count floor is 1 since PP340 shipped. It was 2 because two sections stated a
        // count, and one of the two was §PP340's own line about `remaining PP33` reading 420 - which
        // a ship deletes, because rationale for unshipped work is what IMPROVEMENTS.md holds. The
        // count fell for the right reason, so the floor follows it rather than the prose being
        // padded to keep a number up. What the floor is for is unchanged: a reader returning
        // nothing still fails here.
        // PP33: the floor is 1, and it fell for the same reason the stated-count floor did. §PP33
        // declared the second fence and a ship deletes the section, because rationale for unshipped
        // work is what IMPROVEMENTS.md holds. A reader returning nothing still fails here, which is
        // the whole of what the floor is for.
        //
        // PP30: BOTH FLOORS ARE 0 NOW, AND THAT IS THE END OF THE ROAD FOR THIS SHAPE. §PP30 held
        // the last fence and the last stated count, and its ship deleted the section - so there is
        // no live subject at all, and a floor of 1 would be a demand that somebody keep a
        // roadkeep-remaining query alive to satisfy a test.
        //
        // What the floor was for moves to the fixtures below, which is where it can stay: they run
        // the readers over documents and trees they build themselves, so a reader that returned
        // nothing is red there whether or not the backlog happens to declare a query. So the two
        // floors are GONE rather than lowered to zero - a zero floor is a line that passes on every
        // input, and asserting the emptiness instead would make the next migration's first fence
        // red in a test that has nothing to do with it. The rule below is unchanged, and the day a
        // fence arrives it is live again with no edit here.
        output.WriteLine($"{queries.Count} query fence(s), {stated.Count} stated count(s)");

        IReadOnlyList<string> apart = RemainingQueries.Disagreements(root, improvements);

        Assert.True(
            apart.Count == 0,
            "the backlog states a remaining count its own query does not answer:\n  "
                + string.Join("\n  ", apart));
    }

    /// <summary>
    /// No query is declared any more, and the two that were are named so the reason is readable.
    ///
    /// PP33: it was two. Its query counted curl and json-c across lib/src and went with §PP33 when
    /// the line shipped - the libraries have left the tree, and holepunch.c stays as unbuilt source
    /// the way gui/ did, so a query still counting its call sites would be measuring a file nothing
    /// compiles against a criterion nobody holds.
    ///
    /// PP30: and it is now one fewer for exactly that reason. Its fence counted jerasure and galois_
    /// across lib/src, the libraries have left the build, and fec.c and frameprocessor.c stay as
    /// unbuilt source - so the same sentence applies word for word. Both ids are named rather than
    /// the check being deleted: a fence reappearing under either would mean somebody re-declared a
    /// query for work that has shipped.
    /// </summary>
    [Fact]
    public void NeitherShippedQueryIsStillDeclared()
    {
        if (Improvements() is not { } improvements)
            return;

        IReadOnlyDictionary<string, RemainingQuery> queries = RemainingQueries.Declared(improvements);

        Assert.DoesNotContain("PP30", queries);
        Assert.DoesNotContain("PP33", queries);
    }

    /// <summary>
    /// A site is a matching LINE, which is roadkeep's unit and not a file or a call.
    ///
    /// PP30: DEMONSTRATED ON A TREE THIS TEST WRITES, and it was 13 read off the real lib/src -
    /// checked against `roadkeep remaining PP30` before it was trusted, which was the right check
    /// while the fence existed. The ship deletes the fence, so roadkeep can no longer answer for
    /// this pattern at all and the number became a literal nothing could re-derive: it moved
    /// whenever a comment in lib/src happened to name the library, including the comment this task
    /// left where the deleted call had been.
    ///
    /// Three facts, none of which the count could separate: two matches on one line are one site,
    /// a matching line in a SUBDIRECTORY is counted - lib/src has remote/, and a non-recursive
    /// glob silently misses four files there - and a file with no match contributes nothing.
    /// </summary>
    [Fact]
    public void ASiteIsAMatchingLine()
    {
        string root = Path.Combine(Path.GetTempPath(), "pportal-remaining-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib", "src", "remote"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "lib", "src", "one.c"),
                "int *m = galois_x(); /* and jerasure too, on the same line */\nint plain = 0;\n");
            File.WriteAllText(Path.Combine(root, "lib", "src", "none.c"), "int nothing = 0;\n");
            File.WriteAllText(
                Path.Combine(root, "lib", "src", "remote", "deep.c"), "#include <jerasure.h>\n");

            Assert.Equal(
                2,
                RemainingQueries.Run(root, new RemainingQuery("lib/src/**/*.c", "jerasure|galois_")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    /// <summary>
    /// A disagreement is reported, on a tree this test controls.
    ///
    /// PP30: the two numbers are DERIVED and were typed. It was PP30's real query against the real
    /// lib/src with a stated count one out - which is exactly the defect PP447 was filed for - and
    /// the "it reads 13" half was a literal about a tree, so any comment in lib/src naming the
    /// library moved it. The query stays real and the stated count stays one out; both numbers now
    /// come from the reader, so what the fixture demonstrates is the disagreement rather than a
    /// census of somebody else's file.
    /// </summary>
    [Fact]
    public void ADisagreementIsReported()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        var query = new RemainingQuery("lib/src/**/*.c", "jerasure|galois_");

        int reads = RemainingQueries.Run(root, query) ?? -1;
        Assert.True(reads >= 0, "lib/src answered null, so the fixture has no tree to disagree with");

        int claimed = reads + 1;

        string doc = $"""
            ### §PP30 Reed-Solomon

            ```roadkeep-remaining
            {query.Glob} :: {query.Pattern}
            ```

            `remaining PP30` reads {claimed}
            """;

        string apart = Assert.Single(RemainingQueries.Disagreements(root, doc));

        Assert.Contains($"says PP30 reads {claimed}", apart, StringComparison.Ordinal);
        Assert.Contains($"it reads {reads}", apart, StringComparison.Ordinal);
    }

    /// <summary>A count for a task that declares no query is reported as that, not compared.</summary>
    [Fact]
    public void ACountWithNoQueryIsReported()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string apart = Assert.Single(RemainingQueries.Disagreements(
            root, "`remaining PP9999` reads 7\n"));

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
