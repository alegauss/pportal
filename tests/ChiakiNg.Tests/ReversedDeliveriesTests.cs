using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP848: the one place a delivery that did not hold has to say so.
///
/// 51 files credit PP696 and PP763 reverted it whole. The line asked whether the answer was a check
/// over those 51 or an end to naming commits in prose; reading it settled that neither is - 49 of
/// the 51 are correct history, and provenance is what makes the prose worth reading. What is wrong
/// is reachable from one place, which is the ledger entry a reader follows the id to.
/// </summary>
public class ReversedDeliveriesTests(ITestOutputHelper output)
{
    private static string? Changelog()
        => ReversedDeliveries.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE RULE. Every reverted delivery's entry names what undid it and what put it back.
    /// </summary>
    [Fact]
    public void EveryReversedDeliverySaysSoInItsOwnEntry()
    {
        if (Changelog() is not { } changelog)
            return;

        IReadOnlyList<string> breaches = ReversedDeliveries.Breaches(changelog);

        Assert.True(
            breaches.Count == 0,
            "a delivery the tree took back does not say so:\n  " + string.Join("\n  ", breaches));
    }

    /// <summary>
    /// And the subject is live: PP696's entry is there and carries all four ids.
    ///
    /// PP630's rule. Breaches over a table nothing matched would pass while saying nothing, and the
    /// whole of this line is that ONE entry carries the correction for 92 mentions.
    /// </summary>
    [Fact]
    public void ThePP696EntryCarriesTheWholeStory()
    {
        if (Changelog() is not { } changelog)
            return;

        string? entry = ReversedDeliveries.EntryFor(changelog, "PP696");

        Assert.NotNull(entry);
        output.WriteLine(entry);

        foreach (string id in (string[])["PP763", "PP27", "PP783", "PP784"])
            Assert.Contains(id, entry, StringComparison.Ordinal);

        // And it is still the SHIPPED entry rather than a retraction: PP696 did deliver, and the
        // ledger records what landed. What the sentence adds is what happened to it afterwards.
        Assert.Contains("✅", entry, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP272: the reader sees each edit it guards against, on a ledger this test supplies.
    /// </summary>
    [Fact]
    public void TheReaderSeesTheEditsItGuardsAgainst()
    {
        ReversedDelivery one = Assert.Single(ReversedDeliveries.All);

        // An entry naming nothing: the reverter and every restorer are reported.
        IReadOnlyList<string> silent = ReversedDeliveries.Breaches(
            $"- ✅ **{one.Id}** **something** — it landed and that is all this says.\n");

        Assert.Equal(1 + one.Restored.Count, silent.Count);
        Assert.Contains(silent, each => each.Contains(one.Reverter, StringComparison.Ordinal));

        // An entry naming the reverter and not what put the work back is the OPPOSITE wrong idea -
        // a reader who stops there believes the frame path is still out of the build.
        IReadOnlyList<string> half = ReversedDeliveries.Breaches(
            $"- ✅ **{one.Id}** **something** — {one.Reverter} reverted it.\n");

        Assert.Equal(one.Restored.Count, half.Count);
        Assert.All(half, each => Assert.Contains("re-established", each, StringComparison.Ordinal));

        // And a ledger with no such entry at all is its own finding, not a pass.
        string missing = Assert.Single(ReversedDeliveries.Breaches("- ✅ **PP1** **other** — no.\n"));
        Assert.Contains("not in the ledger at all", missing, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row is a judgement and not a shape, which is why it is written down.
    ///
    /// roadkeep files a revert as its own delivery, so PP763's entry says the four files are back
    /// and nothing structurally marks PP696 as the entry it undid - `reversals` reports none for
    /// this project. A reader deriving the pair from the ledger would have to understand both
    /// sentences, which is the judgement this row records.
    /// </summary>
    [Fact]
    public void TheReverterIsItsOwnDeliveryAndSaysNothingAboutWhatItUndid()
    {
        if (Changelog() is not { } changelog)
            return;

        string? reverter = ReversedDeliveries.EntryFor(changelog, "PP763");

        Assert.NotNull(reverter);
        output.WriteLine(reverter);

        // PP763's own entry is a delivery like any other. It does not name PP696, which is the
        // whole reason the pairing is a row here rather than something a sweep could find.
        Assert.DoesNotContain("PP696", reverter, StringComparison.Ordinal);
    }
}
