using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>A set-aside line whose reason names something the project declares.</summary>
/// <param name="Id">The line.</param>
/// <param name="Requirement">What its reason names.</param>
/// <param name="Phrase">The words in the reason that say so.</param>
public readonly record struct HiddenByReason(string Id, string Requirement, string Phrase);

/// <summary>
/// PP509: the reasons the deferred store keeps, read for absences the project already declares.
///
/// This project has two stores for work that is not happening. `defer` is "not now": a line whose
/// rationale would be lost by retiring it, which nothing is waiting on. `[requirements]` is "not
/// here": a line ready in every sense except that the room lacks a thing, and `--have` is how a
/// caller who has that thing asks for it.
///
/// A LINE IN THE FIRST STORE FOR A REASON BELONGING TO THE SECOND IS INVISIBLE TO THE ONE CALLER
/// WHO COULD FINISH IT. Three were: PP50 "needs a live session to measure the trade", PP72 "needs
/// real sessions", PP76 "needs a console" - all set aside for a console this project declares and
/// has, and none of them reachable by `pick --have console`, because pick reads the roadmap and the
/// deferred store is another file.
///
/// SO THIS IS PP486'S CHECK POINTING THE OTHER WAY. That one reads open ROADMAP lines for a prose
/// phrase naming an UNDECLARED requirement - a line that needs something and does not say so. This
/// reads set-aside REASONS for a phrase naming a DECLARED one - a line that says what it needs, in
/// the file where saying it achieves nothing.
///
/// The phrase list is <see cref="BacklogRequirements.ProseNames"/>, plus the plainer ways a reason
/// spells the same absence. A reason is one clause, written to be read by a person deciding
/// whether to un-defer, so it says "needs a console" where a task line says "no test can exercise
/// one without a live console".
/// </summary>
public static partial class DeferredReasons
{
    /// <summary>Where set-aside lines live.</summary>
    public const string DeferredRelativePath = @"docs\DEFERRED.md";

    /// <summary>The store, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(DeferredRelativePath);

    /// <summary>
    /// Phrases a set-aside REASON uses for an absence, and the requirement each means.
    ///
    /// Wider than <see cref="BacklogRequirements.ProseNames"/> on purpose, and it can afford to be:
    /// a reason is a short clause about why this line is not happening, so "needs a console" in one
    /// is a statement of need. The same three words in a task line's why would be about the port's
    /// subject and would flag everything.
    /// </summary>
    public static IReadOnlyList<(string Phrase, string Requirement)> ReasonNames { get; } =
    [
        ("live console", "console"),
        ("needs a console", "console"),
        ("needs real sessions", "console"),
        ("needs a live session", "console"),
        ("a person looking", "a-person-looking"),
    ];

    /// <summary>
    /// The reason one set-aside line carries - the parenthesised clause after "set aside".
    /// </summary>
    public static string? ReasonIn(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Match reason = ReasonRegex().Match(line);
        return reason.Success ? reason.Groups["reason"].Value : null;
    }

    /// <summary>
    /// Every set-aside line whose reason names a requirement the project declares.
    ///
    /// Only the REASON is read, not the whole line: a deferred line keeps its symptom and its why,
    /// and those are about the port's subject. "PP76 ... the decoder preference is measured on
    /// synthetic frames" would flag on any phrase list wide enough to be useful.
    /// </summary>
    public static IReadOnlyList<HiddenByReason> Hidden(string deferred, IReadOnlySet<string> declared)
    {
        ArgumentNullException.ThrowIfNull(deferred);
        ArgumentNullException.ThrowIfNull(declared);

        var hidden = new List<HiddenByReason>();

        foreach (string line in deferred.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Match id = LineIdRegex().Match(line);
            if (!id.Success || ReasonIn(line) is not { } reason)
                continue;

            foreach ((string phrase, string requirement) in ReasonNames)
            {
                if (!declared.Contains(requirement))
                    continue;

                if (reason.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    hidden.Add(new HiddenByReason(id.Groups["id"].Value, requirement, phrase));
                    break;
                }
            }
        }

        return hidden;
    }

    // - ⏸ **PP76** (deps: —) **symptom** — set aside (needs a console): why. → §PP76
    [GeneratedRegex(@"^-\s.*?\*\*(?<id>PP[0-9]+)\*\*")]
    private static partial Regex LineIdRegex();

    [GeneratedRegex(@"set aside\s*\((?<reason>[^)]*)\)")]
    private static partial Regex ReasonRegex();
}
