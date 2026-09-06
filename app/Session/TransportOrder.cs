namespace ChiakiNg.Session;

/// <summary>
/// PP636: what PP295 waits on, which is not the whole of PP27.
///
/// PP27's four criteria are not one thing. THREE ARE THE TRANSPORT and are met: a shim entry point
/// into takion's receive loop (PP601, PP607), the timing against the C (PP610, and PP635 for the
/// half that has no ratio because takion's handlers are file-local), and PP44's budget (PP633, zero
/// allocated over payloads a PS5 sent).
///
/// THE FOURTH IS THE DELETION, and its own reason says so - "this is the end state and not a
/// progress bar - the same shape PP33's own last criterion has". takion.c cannot leave a build that
/// <see cref="StillCallTakion"/> names, and removing the last of those files IS PP295.
///
/// So the dep PP295 declared made it wait on work that waits on PP295, with PP28, PP31 and PP32
/// behind it - and PP31 is the port's largest unanswered question. The dep is gone; this is the
/// reason, and the assertion beside it is the premise the release rests on: if PP27's fourth
/// criterion stops being the end state, the release was made against a line that changed.
/// </summary>
public static class TransportOrder
{
    /// <summary>Where the criteria and the lines both live.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>Where a line goes when it ships, which is the other place PP295 can now be.</summary>
    public const string LedgerRelativePath = @"docs\CHANGELOG.md";

    /// <summary>The ledger, or null outside a checkout.</summary>
    public static string? LocateLedger() => SanitizerSource.LocateRelative(LedgerRelativePath);

    /// <summary>
    /// The files in lib/ that still call takion, which is what the fourth criterion waits on.
    ///
    /// Named rather than counted, for <see cref="Protocol.HolepunchConsumers"/>'s reason: a deletion
    /// needs which, not how many. streamconnection.c is PP295's own subject, which is the whole
    /// point - the criterion cannot land until PP295 does.
    /// </summary>
    public static IReadOnlyList<string> StillCallTakion { get; } =
    [
        @"lib\src\audioreceiver.c",
        @"lib\src\audiosender.c",
        @"lib\src\congestioncontrol.c",
        @"lib\src\feedbacksender.c",
        @"lib\src\senkusha.c",
        @"lib\src\streamconnection.c",
    ];

    /// <summary>PP295's own subject, and the last of the six a deletion would have to answer for.</summary>
    public const string StreamConnection = @"lib\src\streamconnection.c";

    /// <summary>The criterion the release turns on, as the roadmap spells its lead.</summary>
    public const string EndStateCriterion =
        "takion.c, takionsendbuffer.c and reorderqueue.c leave the build";

    /// <summary>
    /// The words that make it an end state rather than something PP295 could wait for.
    ///
    /// Read from the reason and not from the lead: a lead can be met early, and what says this one
    /// cannot is the sentence under it.
    ///
    /// PP666: TWO WORDS, NOT ONE SENTENCE. This was the literal "the end state and not a progress
    /// bar", which is one spelling of the thing rather than the thing - and PP666 rewrote the
    /// criterion to name PP295 outright, so the premise got STRONGER and the check went red. A check
    /// that a more precise sentence fails is reading its own wording back. These are the two words
    /// <see cref="DeletionEndState.EndStateWords"/> already reads across all three deletion lines.
    /// </summary>
    public static IReadOnlyList<string> EndStateSays { get; } = DeletionEndState.EndStateWords;

    /// <summary>
    /// Whether the roadmap still says what the release was made against.
    ///
    /// TWO HALVES, AND THE SECOND IS PP666'S. The criterion still reads as an end state, AND its
    /// prose still says it waits on PP295 - which is the actual premise, PP295 being the last of the
    /// six callers. The old version asserted only the first, so a criterion that kept the words and
    /// dropped the wait would have passed it while the release rested on nothing.
    ///
    /// Collapsed whitespace, because roadkeep reflows a criterion's reason to the prose width - so a
    /// check reading the file as written would be asserting about the wrapping.
    /// </summary>
    public static bool TheEndStateIsStillTheEndState(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        string flat = System.Text.RegularExpressions.Regex.Replace(roadmap, @"\s+", " ");

        if (!flat.Contains(EndStateCriterion, StringComparison.Ordinal))
            return false;

        if (DeletionEndState.CriteriaOf(roadmap, "PP27") is not { } criteria)
            return false;

        return EndStateSays.All(word => criteria.Contains(word, StringComparison.OrdinalIgnoreCase))
            && CriterionBlockers.WaitedOnIn(criteria).Contains("PP295", StringComparer.Ordinal);
    }

    /// <summary>
    /// PP295 having shipped, whether PP27's fourth criterion is still an end state.
    ///
    /// The premise above was the whole of it WHILE PP295 was open: releasing PP295 from a dep on
    /// PP27 rested on PP27's own deletion waiting on PP295, so a criterion that stopped saying so
    /// made the release unjustified. That order has now run, and PP690's rule takes over from the
    /// other side - a criterion naming a shipped id tells a planner work is left where none is.
    ///
    /// So this is the same question with the wait removed: the words that make it an end state, and
    /// nothing else. <see cref="TheEndStateIsStillTheEndState"/> keeps its meaning rather than being
    /// inverted, because the fixtures that prove it are about a criterion that waits.
    /// </summary>
    public static bool TheEndStateIsStillAnEndState(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        if (DeletionEndState.CriteriaOf(roadmap, "PP27") is not { } criteria)
            return false;

        return EndStateSays.All(word => criteria.Contains(word, StringComparison.OrdinalIgnoreCase))
            && !CriterionBlockers.WaitedOnIn(criteria).Contains("PP295", StringComparer.Ordinal);
    }

    /// <summary>Whether a line's own text names an id among its deps.</summary>
    public static bool DeclaresDep(string roadmapLine, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmapLine);
        ArgumentNullException.ThrowIfNull(id);

        int deps = roadmapLine.IndexOf("(deps:", StringComparison.Ordinal);
        if (deps < 0)
            return false;

        int closes = roadmapLine.IndexOf(')', deps);
        string inside = closes < 0 ? roadmapLine[deps..] : roadmapLine[deps..closes];

        return inside.Contains(id + " ", StringComparison.Ordinal)
            || inside.EndsWith(id, StringComparison.Ordinal);
    }

    /// <summary>One line of the roadmap, by id, or null.</summary>
    public static string? LineFor(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(id);

        return roadmap.Split('\n')
            .FirstOrDefault(one => one.Contains($"**{id}**", StringComparison.Ordinal));
    }
}
