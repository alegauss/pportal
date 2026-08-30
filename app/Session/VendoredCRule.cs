namespace ChiakiNg.Session;

/// <summary>
/// PP568: the rule about patching the C this repository vendors, held where a verb cannot drop it.
///
/// §PP107 decided it and argued it: the two broken reorder-queue functions are accepted and NOT
/// patched, because every drift check in this port asserts the managed side matches lib/, and a
/// local patch would leave them asserting agreement with a libchiaki nobody else runs. That is why
/// five drift checks exist there instead of a two-line fix.
///
/// IT LIVED IN A DEFERRED SECTION, and a deferral is not terminal - DEFERRED.md exists precisely so
/// a line can come back. Resolve PP107 and §PP107 is dropped with it, leaving one ledger sentence
/// for a rule that binds every task touching lib/.
///
/// SO IT IS A NON-GOAL NOW, which is durable, refused at input like a task line, and read out on
/// every `brief`. This holds that it is still there: a non-goal nothing asserts is prose again, and
/// prose does not go red - which is §PP107's own argument for writing those five.
/// </summary>
public static class VendoredCRule
{
    /// <summary>Where the non-goals live.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? LocateRoadmap() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>
    /// The rule's lead, as the roadmap spells it.
    ///
    /// Held as the lead alone rather than the whole sentence: the reason beside it is prose that may
    /// be reworded, and pinning it would turn a better explanation into a failing test.
    /// </summary>
    public const string Lead = "No local patch to the vendored C";

    /// <summary>The section it has to be in, so a bullet moved elsewhere does not satisfy this.</summary>
    public const string Heading = "Non-goals";

    /// <summary>Whether the roadmap still carries the rule, under the non-goals.</summary>
    public static bool IsStillANonGoal(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        int heading = roadmap.IndexOf(Heading, StringComparison.OrdinalIgnoreCase);
        if (heading < 0)
            return false;

        return roadmap.IndexOf(Lead, heading, StringComparison.Ordinal) > heading;
    }

    /// <summary>
    /// And the deferral that argues it is still the one to read, which is what the rule points at.
    ///
    /// The non-goal carries a lead and a sentence; the argument is longer than a sentence, so the
    /// two are joined by name rather than duplicated. A pointer to a section nobody kept would be
    /// worse than no pointer.
    /// </summary>
    public const string ArguedIn = "PP107";

    /// <summary>
    /// PP571: the line the rule does NOT reach, named in the rule itself.
    ///
    /// PP568 wrote the rule and left it reading as a ban on PP33 - which is an open, ready line
    /// whose whole content is deleting holepunch.c and editing session.c and ctrl.c to stop calling
    /// it. That is a local change to vendored C by any plain reading.
    ///
    /// The reason is what settles it and was already in the rule: drift checks would be left
    /// asserting agreement with a libchiaki nobody else runs. A deletion removes the thing they
    /// agree with, so there is nothing left to diverge from - the rule's own argument exempts it.
    /// Saying so is the difference between a session reading that and a session stopping.
    /// </summary>
    public const string DoesNotReach = "PP33";

    /// <summary>
    /// Whether the rule still names what it does not reach.
    ///
    /// Held against the roadmap's own text rather than this constant alone: a rule that forbids a
    /// line the same file lists as ready is a contradiction, and the exemption is the only thing
    /// keeping the two consistent.
    /// </summary>
    public static bool NamesWhatItDoesNotReach(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        int lead = roadmap.IndexOf(Lead, StringComparison.Ordinal);
        if (lead < 0)
            return false;

        // Within the rule's own paragraph, which ends at the next bullet.
        int next = roadmap.IndexOf("\n- ", lead, StringComparison.Ordinal);
        string rule = next < 0 ? roadmap[lead..] : roadmap[lead..next];

        return rule.Contains(DoesNotReach, StringComparison.Ordinal);
    }
}
