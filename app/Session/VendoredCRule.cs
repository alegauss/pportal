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
    /// PP593: and PP30, which the rule was silent about while `roadkeep lint` flagged it the same
    /// way it flags PP33.
    ///
    /// Both lines name "vendored", so the note fires on both and says the same thing about each: a
    /// constraint may bound a line without forbidding it, and nothing in the file decides which.
    /// PP571 decided that for PP33 and left PP30 reading as forbidden by a rule that does not mean
    /// to forbid it - the same defect one line down, and the note is not what makes it one. A
    /// session picking PP30 up reads the rule and stops.
    ///
    /// WHY IT IS BOUNDED AND NOT FORBIDDEN, and it takes both of PP30's outcomes to say it. §PP30
    /// declares the surface to port as the call sites - common.c, fec.c, frameprocessor.c - and
    /// names keeping the C as a legitimate outcome, because the arithmetic is self-contained and
    /// has no OS surface. Deleting the call sites removes what the drift checks agree with, which is
    /// PP33's argument exactly; keeping the C changes nothing in lib/ at all. Neither is a patch,
    /// which is the only thing the rule forbids.
    ///
    /// The note itself does not clear - it is lexical, and PP33 has been flagged since PP571 named
    /// it. That is roadkeep's, not this repository's; what is this repository's is whether the
    /// answer is written down where the next session reads it.
    ///
    /// PP637: and PP295, which nothing flagged at all.
    ///
    /// The third of three and the first the lint note is silent about. PP33 and PP30 both carry the
    /// word "vendored" in their own text, so the note fires on them lexically; PP295 does not, and
    /// was named by nothing. That is what makes it worth writing down rather than less: the note was
    /// never what made the other two a defect - the reading was, and a reading is available to
    /// anybody who picks the line up.
    ///
    /// §PP295 is explicit that deleting IS the deliverable - "the C video receiver leaving the build
    /// is what makes the five ports beneath it real" - so a session reads a rule that appears to
    /// forbid what the line asks for, and stops. The rule's own argument exempts it: a deletion
    /// removes what the drift checks agree with, so there is nothing left to diverge from.
    ///
    /// What the rule DOES still forbid, and PP295 is not asking for, is editing streamconnection.c
    /// to call something else while it stays in the build. That is a patch by any reading.
    /// </summary>
    public static IReadOnlyList<string> LinesItDoesNotReach { get; } =
        [DoesNotReach, "PP30", "PP295"];

    /// <summary>
    /// Whether the rule still names every line it does not reach.
    ///
    /// Held against the roadmap's own text rather than these constants alone: a rule that forbids a
    /// line the same file lists as ready is a contradiction, and the exemption is the only thing
    /// keeping the two consistent.
    ///
    /// PP593: ALL of them, not any. Named one at a time this would go green on PP33 alone, which is
    /// the state PP571 left and this is fixing - and it is the same shape as PP573's finding about
    /// PP33's own line, where a claim stayed wrong through three tasks that each falsified it.
    /// </summary>
    public static bool NamesWhatItDoesNotReach(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        return MissingExemptions(roadmap).Count == 0;
    }

    /// <summary>
    /// The lines the rule does not reach that its own paragraph fails to name, so a failure says
    /// which rather than that something is wrong.
    /// </summary>
    public static IReadOnlyList<string> MissingExemptions(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        int lead = roadmap.IndexOf(Lead, StringComparison.Ordinal);
        if (lead < 0)
            return LinesItDoesNotReach;

        // Within the rule's own paragraph, which ends at the next bullet.
        int next = roadmap.IndexOf("\n- ", lead, StringComparison.Ordinal);
        string rule = next < 0 ? roadmap[lead..] : roadmap[lead..next];

        return [.. LinesItDoesNotReach.Where(id => !rule.Contains(id, StringComparison.Ordinal))];
    }
}
