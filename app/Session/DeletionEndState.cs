using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP639: a deletion's last criterion is an end state, and nothing open may wait on the line.
///
/// Three lines were found separately with one shape. PP33's deletion waits on the shim PP481 made
/// the oracle; PP27's waits on six files that call takion, one of them PP295; PP295's waits on
/// session.c, which is PP28's subject and which declared a dep on PP295. In every one the PORT is
/// doable and the DELETION comes after the line's own dependents.
///
/// PP636 untied the second by hand and PP638 found the third. A third by hand is a habit; this is
/// the rule, so a fourth arrives answered.
///
/// THE WORDS WERE ALREADY THERE. PP33's last criterion says "It is an end state, not a progress
/// bar", PP27's says the same, and PP295's now does. What was missing is that they BIND: a line
/// whose last criterion is an end state cannot be what another open line waits on, because what
/// that criterion waits for is the dependent.
///
/// The rule is about the DEP and not about the work. Nothing here says a dependent may start - that
/// is a reading of the criteria above the end state, and those are the line's real deliverable.
/// </summary>
public static partial class DeletionEndState
{
    /// <summary>Where the lines and their criteria both live.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>
    /// The lines whose last criterion is an end state, and what each end state waits on.
    ///
    /// Named rather than derived from the marker: a line is in this set because somebody decided its
    /// deletion comes last, and that decision is what the words in the criterion record. Deriving it
    /// would make the rule true by construction.
    ///
    /// THE SECOND HALF IS THE ONE THE RULE'S FIRST RUN ASKED FOR. Written as "no open line may
    /// depend on an end-state line", it reported PP30's dep on PP27 - and that is broader than the
    /// justification. PP27's end state waits on six files that call takion, and fec.c is not among
    /// them; §PP30 does not mention takion at all. The rule's reason is that what the end state
    /// waits FOR is the dependent, so the check has to know what each one waits for.
    ///
    /// PP33's waits on the shim, which is this port's own seam and not a line - so it constrains no
    /// dep, and an empty list says that rather than leaving PP33 out.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> WaitsOn { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // The shim wraps ten holepunch exports as PP481's oracle, counted at the linker by
            // PP653 and put behind an option by PP663. Not a line either way.
            ["PP33"] = [],

            // PP638: six files in lib/ call takion, and streamconnection.c is PP295's subject.
            ["PP27"] = ["PP295"],

            // PP690: it used to read ["PP28"], on the grounds that session.c drives the stream
            // connection and session.c is PP28's subject. PP28 shipped, and what it shipped was
            // three modelled joins - none of which stops session.c asking. What PP295's end state
            // waits on is an edit no open line owns, so the honest entry is none.
            ["PP295"] = [],
        };

    /// <summary>The lines the rule is about.</summary>
    public static IReadOnlyList<string> Lines { get; } = [.. WaitsOn.Keys];

    /// <summary>The two things an end-state criterion says, in the spellings the three use.</summary>
    public static IReadOnlyList<string> EndStateWords { get; } = ["end state", "progress bar"];

    /// <summary>
    /// Whether a line's criteria list carries one.
    ///
    /// The heading roadkeep opens for a task's list is "Done when — &lt;id&gt;", so the list is found
    /// by the id and read to the next heading. Both words in one list rather than one anywhere: a
    /// roadmap that said "progress bar" in some other line's prose would otherwise satisfy this.
    /// </summary>
    public static bool CarriesAnEndState(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(id);

        if (CriteriaOf(roadmap, id) is not { } list)
            return false;

        return EndStateWords.All(word => list.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One task's criteria list, or null where it has none.</summary>
    public static string? CriteriaOf(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(id);

        string flat = Whitespace().Replace(roadmap, " ");

        int at = flat.IndexOf($"Done when — {id} ", StringComparison.Ordinal);
        if (at < 0)
            at = flat.IndexOf($"Done when - {id} ", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int next = flat.IndexOf("Done when", at + 10, StringComparison.Ordinal);
        return next < 0 ? flat[at..] : flat[at..next];
    }

    /// <summary>
    /// The open lines that declare a dep on <paramref name="id"/>.
    ///
    /// Read from inside the deps parenthesis only. An id is named all over a roadmap - in a why, in
    /// a criterion, in another line's prose - and a check that asked the whole line would report a
    /// dependency wherever one was mentioned.
    /// </summary>
    public static IReadOnlyList<string> OpenLinesDependingOn(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(id);

        var found = new List<string>();

        foreach (Match line in OpenLine().Matches(roadmap))
        {
            string deps = line.Groups["deps"].Value;
            if (!DepsName(deps, id))
                continue;

            found.Add(line.Groups["id"].Value);
        }

        return found;
    }

    /// <summary>Whether a deps list names an id, as a whole token.</summary>
    public static bool DepsName(string deps, string id)
    {
        ArgumentNullException.ThrowIfNull(deps);
        ArgumentNullException.ThrowIfNull(id);

        return deps.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(one => string.Equals(one, id, StringComparison.Ordinal));
    }

    /// <summary>Every breach, named so a failure says which rather than that something is wrong.</summary>
    public static IReadOnlyList<string> Breaches(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        var found = new List<string>();

        foreach (string id in Lines)
        {
            if (!CarriesAnEndState(roadmap, id))
                found.Add($"{id} has no end-state criterion");

            // Only what the end state WAITS ON, which is the rule's own justification. A line that
            // merely depends on this one may be waiting for the port above the end state, and that
            // is an ordinary dependency - PP30 on PP27 is one, and the first run of this check
            // reported it because the check was broader than its reason.
            foreach (string waiting in OpenLinesDependingOn(roadmap, id)
                .Where(one => WaitsOn[id].Contains(one, StringComparer.Ordinal)))
            {
                found.Add($"{waiting} declares a dep on {id}, whose end state waits on {waiting}");
            }
        }

        return found;
    }

    // An open task line: the marker, the id, and the deps parenthesis where it has one.
    [GeneratedRegex(@"^- .{1,4} \*\*(?<id>PP[0-9]+)\*\*(?: \(deps: (?<deps>[^)]*)\))?", RegexOptions.Multiline)]
    private static partial Regex OpenLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
