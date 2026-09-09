namespace ChiakiNg.Session;

/// <summary>One control: the file it names, and which way the build graph has to answer about it.</summary>
/// <param name="Where">The class that declares it, for the failure message.</param>
/// <param name="RelativePath">The file, repository-relative.</param>
/// <param name="MustBeCompiled">
/// True for a positive control - a file some target compiles, which is what says the reader is
/// reading. False for a negative one, where the finding IS that nothing compiles it.
/// </param>
public readonly record struct ControlFile(string Where, string RelativePath, bool MustBeCompiled);

/// <summary>
/// PP844: a control's chosen file, and the rule PP27's two failures wrote down in words.
///
/// PP271 makes every sweep carry a control, because a reader that found nothing would satisfy its
/// own assertion. What nothing checked is WHICH file the control names, and twice in one commit the
/// answer was a file the deletion was aiming at. SuiteFreshnessTests asserted takion.c was in the
/// build graph; StreamConnectionConsumers proved a list was lib's by finding src/takion.c in it,
/// picked as what streamconnection.c sat beside - a neighbour, not a survivor. Both went red the day
/// takion.c left, and neither failure was about what its check is for.
///
/// EACH FIX WAS ONE LINE AND EACH WROTE THE RULE AS PROSE: "a file chosen for a deletion to aim at
/// is the one thing a control must never be". That sentence is checkable, and this is it.
///
/// TWO QUESTIONS, AND THE FIRST IS THE POINT. <see cref="Aimed()"/> asks whether any control names a
/// file an arranged deletion has declared - answerable while the deletion is still arranged, which is
/// before the red tail rather than inside it. <see cref="Disagreeing"/> asks whether the build graph
/// agrees with each control's polarity today, which is the same question one commit too late: it
/// turns a mysterious failure wearing the reader's name into a sentence saying the fixture moved.
///
/// THE FIRST IS INERT ON THIS TREE and that is the shape FlipTrial already argues for: both flips
/// landed under PP27, so no deletion is arranged and nothing is aimed at. The readers are exercised
/// on supplied lists, so an empty <see cref="FlipTrial.Flips"/> costs no coverage - only the live
/// subject, which is what landing is.
/// </summary>
public static class ControlFiles
{
    /// <summary>
    /// The controls this tree declares.
    ///
    /// A LIST AND NOT A SWEEP, for the reason FecConsumers gives about its three callers: telling a
    /// control from a subject cannot be done lexically. 220 filename mentions across 101 test files
    /// name one of the departed C files, and almost all of them are subjects - a differential reading
    /// the C it replaces. What makes these three controls is that each asserts MEMBERSHIP in a list
    /// the build produces, and the file is furniture rather than the thing under test.
    ///
    /// So a fourth control needs a row, and that is the cost. What it buys is that the rows can be
    /// checked at all: the alternative is a sweep whose answer is 220 lines of which two matter.
    /// </summary>
    public static IReadOnlyList<ControlFile> All { get; } =
    [
        // SuiteFreshnessTests' positive control: session.c, and takion.c until PP27 took it out.
        new(nameof(SuiteFreshness), @"lib\src\session.c", MustBeCompiled: true),
        new(nameof(SuiteFreshness), @"test\main.c", MustBeCompiled: true),

        // And its negative one, which is the case PP720 is about rather than an exception to it.
        new(nameof(SuiteFreshness), SuiteFreshness.OutOfTheGraph, MustBeCompiled: false),

        // StreamConnectionConsumers.SurvivesTheFlip, spelled as lib's list spells it. Both are
        // positive: a list naming neither is not lib's list, which is what the reader is for.
        new("StreamConnectionConsumers", @"lib\src\session.c", MustBeCompiled: true),
        new("StreamConnectionConsumers", @"lib\src\ctrl.c", MustBeCompiled: true),
    ];

    /// <summary>
    /// Every file an arranged deletion has declared, both halves, repository-relative.
    ///
    /// The suite's half is qualified as it is stored - <see cref="ArrangedFlip.Removes"/> is spelled
    /// the way <c>test/CMakeLists.txt</c> spells it, which is bare - and the library's half already
    /// is. Both are needed and PP844 is the proof: the two controls that broke named library files.
    /// </summary>
    public static IReadOnlyList<string> Claimed { get; } =
    [
        .. FlipTrial.Flips.SelectMany(one => one.Removes).Select(one => $@"test\{one}"),
        .. FlipTrial.Flips.SelectMany(one => one.RemovesFromLibrary),
    ];

    /// <summary>
    /// Whether two spellings of a path name the same file.
    ///
    /// Separators are normalised and the comparison is on the whole relative path, not the basename:
    /// <c>test\fec.c</c> and <c>lib\src\fec.c</c> are two different files with one name, and that
    /// coincidence is exactly the confusion a control gets lost in.
    /// </summary>
    public static bool SameFile(string one, string other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Normalise(one), Normalise(other), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string path)
        => path.Replace('\\', '/').TrimStart('.', '/');

    /// <summary>
    /// Controls a deletion is aiming at, as sentences - the question asked BEFORE it lands.
    ///
    /// Only positive controls can be aimed at. A negative control naming a file a deletion will
    /// remove is not a defect: it is a control that is about to become correct, and holepunch.c is
    /// the one this tree already has.
    /// </summary>
    public static IReadOnlyList<string> Aimed(IReadOnlyList<string> claimed)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        return
        [
            .. All.Where(control => control.MustBeCompiled
                    && claimed.Any(one => SameFile(one, control.RelativePath)))
                .Select(control =>
                    $"{control.Where}'s control names {control.RelativePath}, which an arranged "
                        + "deletion claims")
        ];
    }

    /// <summary>The same, against what this tree has arranged - which is nothing.</summary>
    public static IReadOnlyList<string> Aimed() => Aimed(Claimed);

    /// <summary>
    /// Controls the build graph disagrees with, as sentences - the question asked after.
    ///
    /// Both directions. A positive control whose file no target compiles is the failure PP27 hit
    /// twice; a negative one whose file came back is PP720's case being undone, which is worth
    /// hearing from here rather than only from the test that owns it.
    /// </summary>
    public static IReadOnlyList<string> Disagreeing(string buildGraph)
    {
        ArgumentNullException.ThrowIfNull(buildGraph);

        var apart = new List<string>();

        foreach (ControlFile control in All)
        {
            bool compiled = SuiteFreshness.IsInTheBuildGraph(buildGraph, control.RelativePath);

            if (compiled == control.MustBeCompiled)
                continue;

            apart.Add(compiled
                ? $"{control.Where}'s negative control names {control.RelativePath} and the build "
                    + "graph compiles it"
                : $"{control.Where}'s control names {control.RelativePath} and no target compiles "
                    + "it, so the fixture moved rather than the code");
        }

        return apart;
    }
}
