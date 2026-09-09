namespace ChiakiNg.Session;

/// <summary>One shipped task that did not hold, and what became of it.</summary>
/// <param name="Id">The delivery.</param>
/// <param name="Reverter">The task that undid it.</param>
/// <param name="Restored">
/// What re-established each half afterwards, or empty where nothing did. Several, normally: a
/// revert takes back one commit and the work returns in the order the tree can take it.
/// </param>
public readonly record struct ReversedDelivery(
    string Id, string Reverter, IReadOnlyList<string> Restored);

/// <summary>
/// PP848: a delivery the tree took back, said once where a reader following the id will land.
///
/// 92 mentions across 51 files name PP696. It took the frame path's four files out of the build,
/// put the shim's fourteen behind an #ifdef and replaced session.c's run with a callback - and
/// PP763 reverted all three, because it shipped a client that could not stream. Then PP27, PP783
/// and PP784 re-established each half, separately.
///
/// SO EVERY ONE OF THOSE SENTENCES IS TRUE AS HISTORY AND MISLEADING AS EXPLANATION. "PP696 took
/// the four files out" describes a state that holds, for reasons that are three other tasks, and a
/// reader looking up why the tree is as it is follows the id to a commit that was undone.
///
/// THE ANSWER IS NOT A SWEEP, and that is what reading the question settled. A check asking whether
/// a docstring credits a reverted id would fire on all 51, and 49 of them are correct history - a
/// report demanding the rewording of true sentences is not a guard. The other candidate, that
/// attributions stop naming commits, throws away the provenance that makes the prose worth reading.
///
/// WHAT IS WRONG IS REACHABLE FROM ONE PLACE. A reader who follows PP696 lands on its ledger entry,
/// so the correction belongs there: one write, and all 92 mentions resolve correctly. This is the
/// check that the write is still there.
///
/// THE LEDGER HAS AN `undone_by` FIELD AND NOTHING EXPOSED SETS IT. `reversals` reads a forward
/// pointer back and reports none for this project; `supersede` is the decisions role's door and
/// takes decisions only. So the fact lives in the entry's own sentence, which is text - and this
/// class is what makes text a thing the gate holds.
/// </summary>
public static class ReversedDeliveries
{
    /// <summary>The ledger.</summary>
    public const string RelativePath = @"docs\CHANGELOG.md";

    /// <summary>The ledger, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The deliveries this tree took back.
    ///
    /// A LIST, because a revert is a judgement somebody made and not a shape a reader can derive:
    /// roadkeep files a revert as its own delivery, so PP763's entry says "the four files are back"
    /// and nothing marks PP696 as the entry it undid. A row here is a person saying the two are
    /// about one thing.
    /// </summary>
    public static IReadOnlyList<ReversedDelivery> All { get; } =
    [
        new("PP696", "PP763", ["PP27", "PP783", "PP784"]),
    ];

    /// <summary>One ledger entry, or null where the file does not carry it.</summary>
    public static string? EntryFor(string changelog, string id)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        ArgumentException.ThrowIfNullOrEmpty(id);

        return changelog.Split('\n')
            .FirstOrDefault(one => one.Contains($"**{id}**", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every reverted delivery whose entry does not say so, as sentences.
    ///
    /// The reverter by name, because that is what a reader follows next. What re-established the
    /// work is asserted too: without it the entry says the work was undone and stops, and a reader
    /// then has the opposite wrong idea - that the frame path is still in the build.
    /// </summary>
    public static IReadOnlyList<string> Breaches(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);

        var found = new List<string>();

        foreach (ReversedDelivery one in All)
        {
            if (EntryFor(changelog, one.Id) is not { } entry)
            {
                found.Add($"{one.Id} is not in the ledger at all");
                continue;
            }

            if (!entry.Contains(one.Reverter, StringComparison.Ordinal))
                found.Add($"{one.Id}'s entry does not name {one.Reverter}, which reverted it");

            foreach (string restored in one.Restored.Where(
                each => !entry.Contains(each, StringComparison.Ordinal)))
            {
                found.Add($"{one.Id}'s entry does not name {restored}, which re-established a half");
            }
        }

        return found;
    }
}
