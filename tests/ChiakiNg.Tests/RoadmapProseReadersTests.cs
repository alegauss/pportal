using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP691: how many checks match a governed file's wording, and what a better sentence does to each.
///
/// PP666 hit two in one task and both were red about text that had IMPROVED. Nobody had counted the
/// rest, and that count is what this file is: the candidates are swept out of app/ mechanically, and
/// every one of them has a verdict a person wrote.
///
/// THE ANSWER IS FOUR. Four checks demand a spelling of prose that is free to be rewritten, and none
/// of them is one of PP666's - those were repaired in the commit that found them. The other
/// twenty-eight split into an address roadkeep itself uses, words held on purpose, a phrase list
/// that fails open, and two files happening to use the same English.
/// </summary>
public class RoadmapProseReadersTests(ITestOutputHelper output)
{
    private static IReadOnlyList<string>? Governed()
    {
        var texts = new List<string>();

        foreach (string relative in RoadmapProseReaders.GovernedRelativePaths)
        {
            if (RoadmapProseReaders.Locate(relative) is not { } path)
                return null;

            texts.Add(File.ReadAllText(path));
        }

        return texts;
    }

    /// <summary>
    /// THE COUNT: every literal the sweep finds is judged, and none is left over.
    ///
    /// This is the whole of the line. The candidates are derived - a string constant in app/ that
    /// occurs in one of the five files roadkeep owns - so the list cannot go quietly stale: a check
    /// written next month against a sentence in the ledger fails here until somebody says what a
    /// better sentence would do to it.
    /// </summary>
    [Fact]
    public void EveryLiteralThatMatchesGovernedProseIsJudged()
    {
        if (RoadmapProseReaders.LocateManaged() is not { } managed || Governed() is not { } governed)
            return;

        IReadOnlyList<(string Where, string Text)> candidates =
            RoadmapProseReaders.Candidates(managed, governed);

        output.WriteLine($"{candidates.Count} occurrence(s), {RoadmapProseReaders.All.Count} judged");

        IReadOnlyList<string> unjudged = RoadmapProseReaders.Unjudged(candidates);

        Assert.True(
            unjudged.Count == 0,
            "these string constants match governed prose and no row says what a better sentence "
                + $"would do to them: {string.Join(" | ", unjudged)}");

        // PP271: a sweep that found nothing would pass the assertion above without looking. Not
        // "not empty" but "finds every judged text": each row's words are a string constant in this
        // assembly - the census names them - so a reader that stopped finding literals, or stopped
        // matching a reflowed sentence, goes red here instead of reporting a clean census.
        var seen = candidates.Select(one => one.Text).ToHashSet(StringComparer.Ordinal);

        foreach (ProseReader row in RoadmapProseReaders.All)
        {
            Assert.True(
                seen.Contains(row.Text),
                $"the sweep did not find \"{row.Text}\", which this file spells out - so the census "
                    + "is reporting on less than it walked");
        }
    }

    /// <summary>
    /// And every row is still real: the file it names still carries the text.
    ///
    /// The other direction, and the one that rots. A row whose check was deleted or reworded is a
    /// judgement about nothing, and leaving it here would make the census read as larger than the
    /// problem it describes.
    /// </summary>
    [Fact]
    public void EveryRowIsStillInTheFileItNames()
    {
        if (RoadmapProseReaders.LocateManaged() is null)
            return;

        foreach (ProseReader row in RoadmapProseReaders.All)
        {
            if (RoadmapProseReaders.Locate(row.Where) is not { } path)
                continue;

            Assert.Contains(row.Text, File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// THE FOUR, named. A fifth arriving is a decision somebody takes rather than a gate going red.
    ///
    /// Each demands one spelling of a sentence that is free to improve: two deletion-line subjects,
    /// a count written in words, and the zero case of a caller census. None can be repaired the way
    /// PP666 repaired its two - a subject has to be recognised somehow, and a string comparison has
    /// no way to read meaning - so what this asserts is that the set is closed and on record.
    /// </summary>
    [Fact]
    public void TheFragileFourAreTheseAndNoOthers()
    {
        string[] fragile =
        [
            .. RoadmapProseReaders.All
                .Where(one => one.Reading == ProseReading.Fragile)
                .Select(one => one.Text)
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine(string.Join(", ", fragile));

        Assert.Equal(
            ["no file calls it", "the FEC decode", "the video receiver", "three callers"],
            fragile);
    }

    /// <summary>
    /// The verdicts print, because the number is what a reader of this line came for.
    ///
    /// Always passes and always prints, the same way the oracle census does: the point is the
    /// output, and a count nobody can see is the state PP691 was filed about.
    /// </summary>
    [Fact]
    public void TheVerdictsAreCountedAndPrinted()
    {
        foreach (ProseReading reading in Enum.GetValues<ProseReading>())
            output.WriteLine($"{RoadmapProseReaders.Count(reading),3}  {reading}");

        Assert.Equal(
            RoadmapProseReaders.All.Count,
            Enum.GetValues<ProseReading>().Sum(RoadmapProseReaders.Count));

        // Every verdict is used, or the taxonomy has a value that describes nothing here.
        foreach (ProseReading reading in Enum.GetValues<ProseReading>())
            Assert.True(RoadmapProseReaders.Count(reading) > 0, $"{reading} names no row");
    }

    /// <summary>No text is judged twice, which is what makes a row a decision about the words.</summary>
    [Fact]
    public void EachTextIsJudgedOnce()
        => Assert.Equal(
            RoadmapProseReaders.All.Count,
            RoadmapProseReaders.All.Select(one => one.Text).Distinct(StringComparer.Ordinal).Count());

    /// <summary>Every row says why, because a verdict with no reason is a label.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(RoadmapProseReaders.All, row => Assert.False(string.IsNullOrWhiteSpace(row.Why)));

    /// <summary>
    /// The reader finds an ordinary literal and a verbatim one, and neither a comment nor a char.
    ///
    /// The sweep's completeness rests on this, so it is exercised rather than assumed: a reader that
    /// silently skipped verbatim strings would make the census look finished while missing every
    /// path-shaped constant in the assembly.
    /// </summary>
    [Fact]
    public void TheReaderFindsLiteralsAndSkipsCommentsAndChars()
    {
        const string Source = """
            // "a comment's string"
            var a = "ordinary";
            /* "a block comment's" */
            var b = @"verbatim ""quoted"" here";
            var c = '"';
            var d = "after the char";
            """;

        Assert.Equal(
            ["ordinary", @"verbatim ""quoted"" here", "after the char"],
            RoadmapProseReaders.LiteralsIn(Source));
    }

    /// <summary>And the candidate rule keeps prose and drops paths, markup and fragments.</summary>
    [Theory]
    [InlineData("the video receiver", true)]
    [InlineData("no file calls it", true)]
    [InlineData("short", false)]
    [InlineData("onelongwordonly", false)]
    [InlineData(@"docs\ROADMAP.md is here", false)]
    [InlineData(" - ** a bullet", false)]
    [InlineData("trailing space ", false)]
    public void TheCandidateRuleKeepsProseAndDropsTheRest(string literal, bool kept)
        => Assert.Equal(kept, RoadmapProseReaders.IsCandidate(literal));

    /// <summary>
    /// Flatten collapses a wrap, which is why a sweep can see a sentence roadkeep reflowed.
    ///
    /// Without it every literal longer than the prose width would be invisible to the census, and
    /// the longest ones are exactly the sentences most likely to be rewritten.
    /// </summary>
    [Fact]
    public void FlattenCollapsesAWrappedSentence()
    {
        const string Wrapped = "takion.c, takionsendbuffer.c and\n  reorderqueue.c leave the build";

        Assert.Contains(
            "takion.c, takionsendbuffer.c and reorderqueue.c leave the build",
            RoadmapProseReaders.Flatten(Wrapped),
            StringComparison.Ordinal);
    }
}
