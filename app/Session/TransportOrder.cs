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
    /// </summary>
    public const string EndStateSays = "the end state and not a progress bar";

    /// <summary>
    /// Whether the roadmap still says what the release was made against.
    ///
    /// Collapsed whitespace, because roadkeep reflows a criterion's reason to the prose width - so a
    /// check reading the file as written would be asserting about the wrapping.
    /// </summary>
    public static bool TheEndStateIsStillTheEndState(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        string flat = System.Text.RegularExpressions.Regex.Replace(roadmap, @"\s+", " ");

        return flat.Contains(EndStateCriterion, StringComparison.Ordinal)
            && flat.Contains(EndStateSays, StringComparison.Ordinal);
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
