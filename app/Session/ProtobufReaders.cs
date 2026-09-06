namespace ChiakiNg.Session;

/// <summary>What a managed reader of a takion message does about proto2's required set.</summary>
public enum ProtobufReading
{
    /// <summary>
    /// It checks the required set before deciding anything, which is what nanopb does.
    ///
    /// PP730's finding: the managed parser accepts a message with every required field absent and
    /// the console's own decoder refuses the same bytes. A reader that DECIDES on a message has to
    /// ask, or it is answering about bytes no console sent.
    /// </summary>
    ChecksRequired,

    /// <summary>
    /// It reads back bytes this port has just written, to compare them.
    ///
    /// A round trip decides nothing about a console's message - it asserts that two generators
    /// agree - so there is no leniency to guard against. A verdict all the same, because "it does
    /// not need to" is the answer, and an unjudged site is the silence PP733 was filed about.
    /// </summary>
    RoundTrip,
}

/// <summary>One file that parses a takion message, and what its calls are.</summary>
/// <param name="File">The file, relative to the repository root.</param>
/// <param name="Sites">How many times it parses one.</param>
/// <param name="Reading">What those calls are.</param>
/// <param name="Why">The reason a person wrote, because a table with no reasons is a table.</param>
public readonly record struct ProtobufReader(
    string File, int Sites, ProtobufReading Reading, string Why);

/// <summary>
/// PP733: every managed reader of a takion message, and what each does about the required set.
///
/// PP730 measured that the managed parser takes messages nanopb refuses and put the check in the
/// bang; PP732 carried it to the disconnect and the streaminfo. Three sites, found by GREPPING -
/// so the list was a thing somebody remembered rather than a thing something reads.
///
/// WHICH IS THE SHAPE THIS PORT KEEPS PAYING FOR. PP279 found it in the root-file list, PP718 in
/// the wait census, PP720 in a staleness warning, PP724 in a check whose own commit reworded two
/// rows out of its reach, and PP735 in a sweep that reads a census string as a caller. A hand-kept
/// list guards what its author thought of, and the next reader is by definition the one nobody
/// thought of.
///
/// AND ONE OF THE THREE HAD ALREADY BEEN WRONG UNDER A PASSING TEST. StreamInfoMessage read an
/// empty buffer as "not a streaminfo" and a test asserted it, because a message with no type at all
/// reads perfectly well through protoc and is refused by nanopb. The correction came from applying
/// the check, not from anybody noticing.
///
/// SO THE ROWS ARE HELD BOTH WAYS. The sweep finds the call sites, this names them, and a file
/// arriving or a site appearing in a file already listed fails by name. The verdict is not taken on
/// trust either: a row claiming to check the required set is read for the call that does it.
/// </summary>
public static class ProtobufReaders
{
    /// <summary>The managed half, which is where a reader could be.</summary>
    public const string ManagedRelativeDirectory = "app";

    /// <summary>What a parse looks like, and what the sweep counts.</summary>
    public const string ParseCall = "Parser.ParseFrom(";

    /// <summary>And what a row claiming to check the required set has to contain.</summary>
    public const string RequiredCall = "RequiredFields.AllPresentIn(";

    /// <summary>
    /// This file, excluded from its own sweep.
    ///
    /// It spells the call it looks for, so a census reading its own declaration would report itself
    /// as a reader. The same answer PP716's locking census needed and the one this tree gives every
    /// check that finds its own fixture.
    /// </summary>
    public const string CensusFileName = "ProtobufReaders.cs";

    /// <summary>Every file in app that parses a takion message, and what its calls are.</summary>
    public static IReadOnlyList<ProtobufReader> All { get; } =
    [
        new(
            @"app\Protocol\BangHandler.cs",
            1,
            ProtobufReading.ChecksRequired,
            "PP730: the bang decides whether a session is keyed, which is where the leniency was found."),
        new(
            @"app\Protocol\DisconnectMessage.cs",
            2,
            ProtobufReading.ChecksRequired,
            "PP732: the reason is required, so an absent one is a failed decode and not an empty string."),
        new(
            @"app\Protocol\StreamInfoMessage.cs",
            1,
            ProtobufReading.ChecksRequired,
            "PP732: the audio header is required, and a stream configured without one was never valid."),
        new(
            @"app\Protocol\StreamArrivals.cs",
            1,
            ProtobufReading.ChecksRequired,
            "PP773: the idle arm switches on the type, and the C reaches that switch only past pb_decode."),
        new(
            @"app\SelfTest.cs",
            2,
            ProtobufReading.RoundTrip,
            "PP25's pair: bytes this port wrote, read back to show the two generators agree."),
    ];

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativeDirectory);

    /// <summary>One of the listed files, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// Every file under app that parses a takion message, with how many times it does.
    ///
    /// Keyed by the path as the rows spell it, so a sweep and a row can be compared by name. The
    /// build's own output is skipped: obj holds a generated Takion.cs whose parsers are the
    /// generator's, not a reader's.
    /// </summary>
    public static IReadOnlyDictionary<string, int> SitesUnder(string managed)
    {
        ArgumentNullException.ThrowIfNull(managed);

        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string root = Path.GetDirectoryName(managed.TrimEnd(Path.DirectorySeparatorChar)) ?? managed;

        foreach (string file in Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file) || string.Equals(
                    Path.GetFileName(file), CensusFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int sites = Count(File.ReadAllText(file), ParseCall);
            if (sites > 0)
                found[Path.GetRelativePath(root, file)] = sites;
        }

        return found;
    }

    /// <summary>Whether a file's text makes the call a ChecksRequired row claims.</summary>
    public static bool ChecksTheRequiredSet(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains(RequiredCall, StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string file)
    {
        char separator = Path.DirectorySeparatorChar;

        return file.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string needle)
    {
        var found = 0;

        for (int at = source.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
