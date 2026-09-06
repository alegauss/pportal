using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>A line whose subject is deleting C, and the module it deletes.</summary>
/// <param name="Id">The task.</param>
/// <param name="Module">What it removes, for the failure message.</param>
public readonly record struct DeletionLine(string Id, string Module);

/// <summary>
/// PP584: a deletion line's caller claim names this port's own shim.
///
/// Three lines have now stated how many callers their target has, and three have been short by the
/// same one. PP33's said session.c was the only caller of holepunch and there were four (PP573).
/// PP30's said the FEC decode had one caller left and there were three (PP574). PP295's said
/// streamconnection.c was the last C caller of the video receiver, and the shim wrapped five of its
/// exports - including one streamconnection never used.
///
/// PP697: WRAPPED, because PP696 put those five behind an #ifdef that is off. The claim they
/// falsified is unchanged - a line counting callers was short by this port's own seam, and it was
/// short at the moment somebody was deciding what the deletion cost.
///
/// IT IS THE SAME OMISSION EVERY TIME AND IT IS NOT A COINCIDENCE. PP574 counted it: the shim wraps
/// 130 chiaki_ entry points across every module this port has. It is therefore a caller of
/// everything any deletion removes, and a line counting callers is short unless it counted the
/// seam this port wrote itself.
///
/// SO THE INVARIANT IS THE LINE, NOT THE COUNT. Counts move as work lands and a check on one would
/// be wrong by the next commit; what must hold is that a line about deleting C says the shim is
/// among what calls it. That is a sentence a planner reads before deciding what the deletion costs,
/// and getting it wrong understates the job at exactly that moment.
/// </summary>
public static class DeletionCallerClaims
{
    /// <summary>Where the lines are.</summary>
    public const string RelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The lines whose subject is removing C from lib.</summary>
    public static IReadOnlyList<DeletionLine> All { get; } =
    [
        // PP33 stood here and has shipped. Its claim was the one this rule was written from -
        // "session.c is its only caller", falsified three times by PP544, PP563 and PP564 without
        // ever being changed - and a shipped line's prose is in the ledger, where nothing is
        // deciding what work costs any more.
        // PP295 stood here and has shipped too, for the same reason and on the same day its
        // deliverable landed: the four files left the build. Its claim - "streamconnection.c was
        // the last C caller of the video receiver" - was short by the shim's five wrappers, which
        // is the third instance this rule was written from, and it is recorded in the paragraph
        // above rather than in a row about an open line.
        new("PP30", "the FEC decode"),
    ];

    /// <summary>What the port's own seam is called where a line names it.</summary>
    public const string Seam = "shim";

    /// <summary>One line's full text, or null where the roadmap does not carry it.</summary>
    public static string? LineFor(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(id);

        return roadmap.Split('\n')
            .FirstOrDefault(one => one.Contains($"**{id}**", StringComparison.Ordinal));
    }

    /// <summary>
    /// Deletion lines that do not name the seam.
    ///
    /// A line already shipped leaves the roadmap, so a missing line is not a failure here - it is
    /// answered by the id no longer being open, and this reports only what it can read.
    /// </summary>
    public static IReadOnlyList<string> NotNamingTheSeam(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        return
        [
            .. All.Where(one => LineFor(roadmap, one.Id) is { } line
                    && !line.Contains(Seam, StringComparison.OrdinalIgnoreCase))
                .Select(one => $"{one.Id} ({one.Module})")
        ];
    }
}
