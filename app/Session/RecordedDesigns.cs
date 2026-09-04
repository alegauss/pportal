using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One ledger entry that says where its deleted design went.</summary>
/// <param name="Id">The task.</param>
/// <param name="Where">The path it names, as the ledger spells it: forward slashes, from the root.</param>
public readonly record struct RecordedDesign(string Id, string Where);

/// <summary>A pair the id join cannot reach, and why not.</summary>
/// <param name="Id">The task.</param>
/// <param name="Where">The file it recorded into.</param>
/// <param name="Reason">Why that file cannot name it. Required: no reason, no exemption.</param>
public readonly record struct UnnameableRecording(string Id, string Where, string Reason);

/// <summary>
/// PP642: the ledger says a deleted design went into a file, and until now nothing read the file.
///
/// `ship --recorded-in` takes a path, requires it to RESOLVE, and writes "(design recorded in `x`)"
/// into the entry. That is the whole of the check. Nothing distinguishes an entry whose paragraph
/// moved from one whose paragraph was never written - and the second is the easy mistake, because
/// the flag is passed in the same call that deletes the section it claims to have moved.
///
/// TWO HAND-WRITTEN CHECKS HAD ALREADY APPEARED, which is the decay this line predicted: PP11's in
/// RenderProbeTests and PP647's in VendorNeutralPresentTests, each asserting its own entry and each
/// saying in its docstring that it stands in for a general one. Both stay - they read phrases of the
/// design, which is stronger than anything general can be - and this is the floor beneath them.
///
/// THE JOIN IS THE ID, and it is exactly as strong as the assertion ratchet's: it cannot tell a
/// recording from a mention, and it can tell a recording from nothing at all. That is the whole
/// claim, and it is worth making because "nothing at all" is what a forgotten paragraph looks like.
///
/// A FILE THAT CANNOT CARRY AN ID IS EXEMPTED BY NAME. PP396, PP422 and PP423 all recorded into
/// PP396's capture, which is a tab-separated recording opened by a version line: a comment would
/// corrupt the format, and the design there IS the file's content rather than a paragraph in it.
/// The reason is written out for the same purpose the ratchet's exemptions serve - it is a record
/// somebody read in a diff, not a way of not looking.
/// </summary>
public static partial class RecordedDesigns
{
    /// <summary>Where the entries are.</summary>
    public const string LedgerRelativePath = @"docs\CHANGELOG.md";

    /// <summary>The ledger, or null outside a checkout.</summary>
    public static string? LocateLedger() => SanitizerSource.LocateRelative(LedgerRelativePath);

    /// <summary>The clause, as `ship --recorded-in` writes it.</summary>
    [GeneratedRegex(@"\*\*(?<id>PP[0-9]+)\*\*.*?\(design recorded in `(?<where>[^`]+)`\)")]
    private static partial Regex ClauseRegex();

    /// <summary>Every entry carrying the clause, in ledger order.</summary>
    public static IReadOnlyList<RecordedDesign> In(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return
        [
            .. ClauseRegex().Matches(ledger)
                .Select(one => new RecordedDesign(
                    one.Groups["id"].Value, one.Groups["where"].Value))
        ];
    }

    /// <summary>
    /// The recordings whose file cannot name the id, each with the reason it cannot.
    ///
    /// One file and three entries: PP396 captured it and PP422 and PP423 are the two findings that
    /// came out of the same run, so all three point at the recording rather than at prose about it.
    /// </summary>
    public static IReadOnlyList<UnnameableRecording> Exempt { get; } =
    [
        new("PP396", "tests/corpus/exchange-ps5-four-channels.txt",
            "a tab-separated recording opened by a version line: a comment corrupts the format, and "
                + "the design is the file's content rather than a paragraph in it"),
        new("PP422", "tests/corpus/exchange-ps5-four-channels.txt",
            "the same recording, whose fresh capture carrying 16 channels is the finding itself"),
        new("PP423", "tests/corpus/exchange-ps5-four-channels.txt",
            "the same recording, whose surviving BANG verdict is the finding itself"),
    ];

    /// <summary>Whether a recording is one the id join is not asked of.</summary>
    public static bool IsExempt(RecordedDesign recording)
        => Exempt.Any(one => one.Id == recording.Id
            && string.Equals(one.Where, recording.Where, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a file's text names the task, which is the join.</summary>
    public static bool NamesTheId(string source, string id)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(id);

        return Regex.IsMatch(source, $@"\b{Regex.Escape(id)}\b");
    }

    /// <summary>
    /// Every recording whose file is missing, or is there and does not name the id.
    /// </summary>
    /// <param name="ledger">The changelog's text.</param>
    /// <param name="read">
    /// Reads one repository-relative path, or returns null where it does not resolve. Injected so
    /// the reader is testable without a checkout on disk shaped for each case.
    /// </param>
    /// <returns>One sentence per failure, naming the entry and what was wrong.</returns>
    public static IReadOnlyList<string> NotRecorded(string ledger, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(read);

        var missing = new List<string>();

        foreach (RecordedDesign recording in In(ledger))
        {
            if (read(recording.Where) is not { } source)
            {
                missing.Add($"{recording.Id}: {recording.Where} does not resolve");
                continue;
            }

            if (IsExempt(recording) || NamesTheId(source, recording.Id))
                continue;

            missing.Add($"{recording.Id}: {recording.Where} does not name it");
        }

        return missing;
    }

    /// <summary>
    /// An exemption naming a pair the ledger does not carry, which is a row that has stopped
    /// standing for anything.
    ///
    /// The same shape the ratchet's exemptions have: a list that outlives its subject is a list
    /// nobody notices is wrong, and the cost of it here is a recording nothing checks.
    /// </summary>
    public static IReadOnlyList<string> ExemptionsWithNoEntry(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        IReadOnlyList<RecordedDesign> recordings = In(ledger);

        return
        [
            .. Exempt
                .Where(one => !recordings.Any(
                    r => r.Id == one.Id
                        && string.Equals(r.Where, one.Where, StringComparison.OrdinalIgnoreCase)))
                .Select(one => $"{one.Id} -> {one.Where}")
        ];
    }

    /// <summary>An exemption with no reason, which is a loophole rather than a record.</summary>
    public static IReadOnlyList<string> ExemptionsWithNoReason()
        => [.. Exempt.Where(one => string.IsNullOrWhiteSpace(one.Reason)).Select(one => one.Id)];
}
