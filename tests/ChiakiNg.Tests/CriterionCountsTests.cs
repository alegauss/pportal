using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP728: a criterion's numbers, held against the program they were copied from.
///
/// One criterion of the run's host stated seven where the answer had become zero, and it got there
/// over four green commits - each one shortening the census the sentence was copied from. PP690
/// holds a criterion's BLOCKER claim against the ledger for the same reason; this is the count.
///
/// PP295's third criterion is the live case: three numbers out of PP669's census in one sentence,
/// and a row added to any of those groups makes it false with nothing to say so.
/// </summary>
public class CriterionCountsTests(ITestOutputHelper output)
{
    private static string? Roadmap()
    {
        string? path = CriterionCounts.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// EACH STATED NUMBER IS THE PROGRAM'S, which is the half that goes stale.
    ///
    /// Not a recount of the document - a comparison against the list the sentence describes. The
    /// commit that adds a consumer fails here, which is where it should fail.
    /// </summary>
    [Fact]
    public void EveryStatedCountIsWhatTheProgramHolds()
    {
        foreach (CriterionCount row in CriterionCounts.All)
        {
            int actual = row.Actual();

            output.WriteLine($"{row.About} \"{row.Phrase}\": states {row.Stated}, holds {actual}");

            Assert.True(
                row.Stated == actual,
                $"{row.About}'s criterion says \"{row.Phrase}\" and the program holds {actual}");
        }
    }

    /// <summary>
    /// AND THE PHRASE SPELLS THAT NUMBER, which is the half that would otherwise be a loophole.
    ///
    /// Without it a row can be corrected on its own: change Stated to fourteen, leave the document
    /// saying thirteen, and both assertions above pass over a sentence that is now wrong.
    /// </summary>
    [Fact]
    public void EveryPhraseSpellsTheNumberItsRowStates()
        => Assert.All(
            CriterionCounts.All,
            row => Assert.True(
                CriterionCounts.ThePhraseSpellsTheNumber(row),
                $"\"{row.Phrase}\" does not spell {row.Stated}"));

    /// <summary>
    /// And the phrase is really in that criterion, so a reworded sentence is news rather than silence.
    ///
    /// Addressed by task and lead rather than searched for in the file: a phrase that moved to a
    /// different criterion would still be found by a bare search, and would be answering about
    /// something else.
    /// </summary>
    [Fact]
    public void EveryPhraseIsStillInTheCriterionItNames()
    {
        if (Roadmap() is not { } roadmap)
            return;

        foreach (CriterionCount row in CriterionCounts.All)
        {
            string? text = CriterionBlockers.TextOf(roadmap, row.About, row.Lead);

            Assert.True(text is not null, $"{row.About} has no criterion \"{row.Lead}\"");

            Assert.Contains(row.Phrase, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The reader really reads one criterion, rather than answering for the whole file.
    ///
    /// PP271's shape: every assertion above passes if TextOf returns the roadmap entire. A lead
    /// that does not exist has to come back null, and a criterion's text must not carry its
    /// neighbour's.
    ///
    /// ON A FIXTURE AND NOT ON THE LIVE ROADMAP. It read PP295's third criterion until PP295
    /// shipped and took its list to the ledger, which made this test a question about which lines
    /// happen to be open. The reader is the subject; a document that answers it is something this
    /// file should own.
    /// </summary>
    [Fact]
    public void TheCriterionReaderReadsOneCriterion()
    {
        // BUILT rather than written, which is PP311's trap and the fifth time it has been stepped
        // in: an id spelled here is an id named in an assertion file, so a literal absent one makes
        // this file the answer to "where is it named" and turns that audit red.
        string about = "PP" + 9001.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string absent = "PP" + 9999.ToString(System.Globalization.CultureInfo.InvariantCulture);

        const string Lead = "Every consumer the linker named has a counterpart";

        string roadmap = $"""
            ## Done when — {about}

            - **The ordering is ported, not only the functions** Six orderings stated as checks on
              the C, reproduced in one trace.
            - **{Lead}** Met: the caller's five, the seam's thirteen and the
              suite's four each resolve to a class by reflection.
            """;

        Assert.Null(CriterionBlockers.TextOf(roadmap, about, "a lead no criterion has"));
        Assert.Null(CriterionBlockers.TextOf(roadmap, absent, Lead));

        string? text = CriterionBlockers.TextOf(roadmap, about, Lead);
        Assert.NotNull(text);

        output.WriteLine(text);

        // Its own words, and not the criterion above it in the same section.
        Assert.Contains("thirteen", text, StringComparison.Ordinal);
        Assert.DoesNotContain("The ordering is ported", text, StringComparison.Ordinal);
    }

    /// <summary>Numbers past the words this list has are refused rather than rendered as digits.</summary>
    [Fact]
    public void ACountPastTwentyIsRefused()
    {
        Assert.Equal("thirteen", CriterionCounts.InWords(13));
        Assert.Throws<ArgumentOutOfRangeException>(() => CriterionCounts.InWords(21));
    }

    /// <summary>Every row says which value it is, because a mapping with no reason is a table.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(
            CriterionCounts.All,
            row => Assert.False(string.IsNullOrWhiteSpace(row.Why)));
}
