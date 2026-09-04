namespace ChiakiNg.Session;

/// <summary>
/// PP705: which files RECORD a phrase in order to judge it, asked of the file rather than listed.
///
/// Five classes here sweep the assembly for something nothing may say - a COM signature without
/// PreserveSig, a reason resting on lib/ being untouched, a promise of a managed decoder, a capture
/// API outside the one capture, a check matching governed prose. Every one of them is a file
/// containing every phrase it forbids, so every one of them has to be skipped by its own sweep.
///
/// EACH WROTE THAT SKIP ITSELF, seven clauses of it, and PP691 is what made the cost visible: it
/// added a fifth such file and had to edit two of the four to say so. The exclusion is not a
/// property of the sweeper. It is a property of the SWEPT file, and asking it from the wrong end
/// makes every new census an edit to the ones already there.
///
/// SO THE FILE SAYS SO ITSELF. A recording file carries <see cref="Marker"/> in a comment and every
/// sweep asks. A sixth arriving needs no edit anywhere else, which is the difference between this
/// and a shared list: a list is the same four names in one place, and the failure it leaves is a
/// census added without touching it - which does not go red where it was written, but turns the
/// OTHER sweeps into false reports of an offender.
///
/// This file carries the marker too, because it spells it. Nothing here is forbidden by any of the
/// five, so being skipped costs nothing and saying why costs one line.
/// </summary>
public static class PhraseCensus
{
    /// <summary>
    /// The line a recording file carries, spelled so nothing writes it by accident.
    ///
    /// PP705: this file RECORDS the phrases it judges.
    /// </summary>
    public const string Marker = "PP705: this file RECORDS the phrases it judges";

    /// <summary>
    /// Whether a source declares itself a recording file.
    ///
    /// Read from the text and not from a path, so a file that moves keeps its answer and a file
    /// renamed into a census's place does not inherit one.
    /// </summary>
    public static bool Records(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains(Marker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a file on disk does. False where it cannot be read, which is a sweep's own problem.
    /// </summary>
    public static bool RecordsFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return Records(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The paths a sweep should read: not a build output, and not a file that records its phrases.
    ///
    /// The bin and obj arms are here for the same reason the marker is - four sweeps wrote those
    /// twice each as well, and a build output carrying a copy of a docstring is the same claim
    /// counted twice.
    /// </summary>
    public static IEnumerable<string> Sweepable(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        char slash = Path.DirectorySeparatorChar;

        return paths
            .Where(one => !one.Contains($"{slash}bin{slash}", StringComparison.Ordinal))
            .Where(one => !one.Contains($"{slash}obj{slash}", StringComparison.Ordinal))
            .Where(one => !RecordsFile(one));
    }
}
