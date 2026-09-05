namespace ChiakiNg.Session;

/// <summary>
/// What a more precise sentence would do to a check that matches a governed file's prose.
/// </summary>
public enum ProseReading
{
    /// <summary>Nothing: the file reads no governed document and the match is a coincidence.</summary>
    Incidental,

    /// <summary>
    /// Nothing: the text is an ADDRESS rather than a sentence - a non-goal's lead, a criterion's
    /// lead, a heading roadkeep writes. Those are how the tool itself names the thing, so matching
    /// one literally is the correct reading and a rewording of it is a rename.
    /// </summary>
    Address,

    /// <summary>
    /// It SHOULD break, and that is the point: the words are the claim, kept deliberately short so
    /// the sentence around them can be rewritten. A rewording that drops them is the finding.
    /// </summary>
    Held,

    /// <summary>
    /// Nothing visible, which is the quieter failure: the check stops noticing. A phrase list that
    /// misses a new spelling goes green about a case it never examined.
    /// </summary>
    FailsOpen,

    /// <summary>
    /// It breaks, wrongly. PP666's failure: a whole sentence, or one spelling of a claim, demanded
    /// of prose that is free to improve - so the better sentence turns the gate red.
    /// </summary>
    Fragile,
}

/// <summary>One string constant in app/ that also occurs in a governed file, and what it is.</summary>
/// <param name="Text">The literal, exactly as the source spells it.</param>
/// <param name="Where">A file it appears in. Informational: the judgement is about the text.</param>
public readonly record struct ProseReader(string Text, string Where, ProseReading Reading, string Why);

/// <summary>
/// PP691: every sentence of a governed file that a check matches literally, counted and judged.
///
/// Two readers went red inside PP666 for the same reason and neither was about a defect.
/// CriterionBlockers read a ship marker with a pattern that could not tell a partial entry from a
/// whole one; TransportOrder held PP27's fourth criterion against the literal string "the end state
/// and not a progress bar", and rewording that criterion to name PP295 outright - a STRONGER
/// statement of the premise the check exists to hold - failed it.
///
/// NOBODY HAD COUNTED, and that was the whole of this line. Both were found by tripping over them.
///
/// SO THE CANDIDATES ARE DERIVED AND THE VERDICTS ARE DECLARED. <see cref="Candidates"/> reads every
/// string literal in app/ and keeps the ones that occur in a governed file - a mechanical question
/// with no judgement in it - and <see cref="All"/> answers, per text, what a better sentence would
/// do. A literal the sweep finds and the list does not name fails the census, so the next one is
/// judged when it is written rather than when it goes red.
///
/// THE ANSWER IS FOUR, not two. Four checks demand a spelling of prose that is free to improve, and
/// they are not the two PP666 hit: those were repaired in the commit that found them.
/// <see cref="ProseReading.Fragile"/> names them and the rest of the taxonomy says why the other
/// twenty-eight are not the same thing - six address, four hold words on purpose, nine fail open,
/// and nine are coincidences of two files using the same English.
///
/// WHAT THIS IS NOT is a rule forbidding the four. A deletion line's subject has to be recognised
/// somehow, and "read the meaning" is not an option a string comparison has. The census is what
/// makes the choice visible: a fifth row arriving is a decision somebody takes, and the four here
/// are on record as costing a red gate the next time their sentence improves.
///
/// PP705: this file RECORDS the phrases it judges, so every sweep here skips it.
/// </summary>
public static class RoadmapProseReaders
{
    /// <summary>The assembly's source, which is what the sweep walks.</summary>
    public const string ManagedRelativeDirectory = "app";

    /// <summary>
    /// This file's own name, which two other censuses have to skip.
    ///
    /// A list of the phrases a check may not rest on is a file containing every one of those
    /// phrases. <see cref="ManagedBoundaryRule"/> and <see cref="LibRepairCensus"/> each sweep app/
    /// for one of them and each already skips its own source for the same reason; this is a third
    /// file with the same standing, and naming it here rather than spelling it twice is what keeps
    /// a rename from quietly turning both sweeps red.
    /// </summary>
    public const string CensusFileName = "RoadmapProseReaders.cs";

    /// <summary>
    /// The files roadkeep owns, which are the prose a check can be reading.
    ///
    /// All five, not just the roadmap: a ledger sentence and a rationale sentence are rewritten for
    /// the same reasons a task line is, and PP666's shape does not care which file it was in.
    /// </summary>
    public static IReadOnlyList<string> GovernedRelativePaths { get; } =
    [
        @"docs\ROADMAP.md",
        @"docs\CHANGELOG.md",
        @"docs\IMPROVEMENTS.md",
        @"docs\DECISIONS.md",
        @"docs\DEFERRED.md",
    ];

    /// <summary>
    /// The shortest literal the sweep will consider, in characters.
    ///
    /// A heuristic and stated as one. Below it the matches are punctuation and markup fragments -
    /// a bullet's dashes, a run of asterisks - which carry no prose to be more precise about. It is
    /// a floor on the CANDIDATES and not on the verdicts: everything above it is judged, including
    /// the things that turn out to be commands and labels.
    /// </summary>
    public const int MinimumLength = 8;

    /// <summary>Two words, because one word is a name and a name is what a census is for.</summary>
    public const int MinimumWords = 2;

    /// <summary>
    /// Every candidate the sweep finds, with what a better sentence would do to it.
    ///
    /// Keyed by TEXT rather than by file: two files spelling the same phrase are one reader's
    /// spelling, and the judgement is about the words.
    /// </summary>
    public static IReadOnlyList<ProseReader> All { get; } =
    [
        // ---- Fragile: a spelling demanded of prose that is free to improve. --------------------
        new(
            "the video receiver",
            @"app\Protocol\DeletionCallerClaims.cs",
            ProseReading.Fragile,
            "PP295's line is searched for its own subject, so a line naming videoreceiver.c instead reads as one that stopped claiming a caller."),
        new(
            "the FEC decode",
            @"app\Protocol\DeletionCallerClaims.cs",
            ProseReading.Fragile,
            "The same for PP30, and the same repair would be the same: a subject a line may spell more than one way."),
        new(
            "three callers",
            @"app\Protocol\FecConsumers.cs",
            ProseReading.Fragile,
            "The count has to be written in words and in the plural; a line saying three consumers, or naming them, fails a check about the number."),
        new(
            "no file calls it",
            @"app\Protocol\HolepunchConsumers.cs",
            ProseReading.Fragile,
            "PP622 narrowed this from a count word plus a fixed plural and left the zero case a single sentence the line must be spelled with."),

        // ---- Held: the words ARE the claim, kept short so the sentence can move. ---------------
        new(
            "end state",
            @"app\Session\DeletionEndState.cs",
            ProseReading.Held,
            "PP666's own repair: two words instead of the sentence that failed, which is the shape this census is arguing for."),
        new(
            "progress bar",
            @"app\Session\DeletionEndState.cs",
            ProseReading.Held,
            "The other half of the same pair, and what a deletion criterion is refusing to be."),
        new(
            "100% Windows",
            @"app\Session\ManagedBoundaryRule.cs",
            ProseReading.Held,
            "The goal the non-goal offers instead of the one it refuses; its own note says a constraint should be re-wordable around them."),
        new(
            "100% managed",
            @"app\Session\ManagedBoundaryRule.cs",
            ProseReading.Held,
            "And the promise it refuses, which is also the first of five spellings the same file will not let the port make."),

        // ---- Address: how roadkeep itself names the thing. -------------------------------------
        new(
            "takion.c, takionsendbuffer.c and reorderqueue.c leave the build",
            @"app\Session\TransportOrder.cs",
            ProseReading.Address,
            "A criterion's lead, which is the argument criterion-amend takes; changing it is a rename and a check that noticed is right to."),
        new(
            "No managed video decoder",
            @"app\Session\ManagedBoundaryRule.cs",
            ProseReading.Address,
            "A non-goal's lead, addressed by lead everywhere the tool touches one."),
        new(
            "No local patch to the vendored C",
            @"app\Session\VendoredCRule.cs",
            ProseReading.Address,
            "The same, for the rule that keeps a repair out of lib/."),
        new(
            "No GPU vendor feature for the network path",
            @"app\Session\EchoCancellation.cs",
            ProseReading.Address,
            "One of the two leads PP52 records as bounding it, quoted to say which non-goals were read."),
        new(
            "No vendor path whose absence is visible to the user",
            @"app\Session\EchoCancellation.cs",
            ProseReading.Address,
            "The other, and the one that decides what a machine without the card still gets."),
        new(
            "Done when",
            @"app\Session\DeletionEndState.cs",
            ProseReading.Address,
            "The heading roadkeep writes for a criteria list, so this is structure rather than prose."),

        // ---- FailsOpen: a phrase list, which a new spelling walks past. ------------------------
        new(
            "one caller left",
            @"app\Protocol\FecConsumers.cs",
            ProseReading.FailsOpen,
            "The old claim refused by name; a line that abandons the wording instead of correcting it is not caught."),
        new(
            "only caller",
            @"app\Protocol\HolepunchConsumers.cs",
            ProseReading.FailsOpen,
            "The same shape one file over, and the claim PP544, PP563 and PP564 each falsified without it ever changing."),
        new(
            "live console",
            @"app\Session\BacklogRequirements.cs",
            ProseReading.FailsOpen,
            "A prose name for a declared requirement; a line needing hardware and saying so differently is missed rather than flagged."),
        new(
            "a person looking",
            @"app\Session\BacklogRequirements.cs",
            ProseReading.FailsOpen,
            "The second of those names, and the narrower absence: somebody puts a window on a screen and says what is in it."),
        new(
            "needs a console",
            @"app\Session\DeferredReasons.cs",
            ProseReading.FailsOpen,
            "A set-aside reason's spelling of the same absence; the list is wider here because a reason is one clause written to be read."),
        new(
            "waits on",
            @"app\Session\CriterionBlockers.cs",
            ProseReading.FailsOpen,
            "A phrase that makes a named id a blocker rather than a citation; a criterion phrasing the wait otherwise is not read as one."),
        new(
            "waits for",
            @"app\Session\CriterionBlockers.cs",
            ProseReading.FailsOpen,
            "The same list's second spelling, which is what says the list is a guess at English rather than a rule."),
        new(
            "cannot land until",
            @"app\Session\CriterionBlockers.cs",
            ProseReading.FailsOpen,
            "And its longest, added because a criterion actually used it."),
        new(
            "does not edit lib/",
            @"app\Session\LibRepairCensus.cs",
            ProseReading.FailsOpen,
            "One of four spellings of a premise the census refuses; a fifth way of saying it would be a reason nothing falsifies."),

        // ---- Incidental: the file reads no governed document at all. ---------------------------
        new(
            "dotnet test",
            @"app\Session\GateAndCiAgree.cs",
            ProseReading.Incidental,
            "A command read out of a launcher and a workflow; the ledger quotes the same command because that is what shipped."),
        new(
            "roadkeep lint",
            @"app\Session\GateVerdicts.cs",
            ProseReading.Incidental,
            "Likewise a gate step, matched in .cmd and never in a document."),
        new(
            "if errorlevel 1",
            @"app\Session\GateVerdicts.cs",
            ProseReading.Incidental,
            "A batch token. Its appearance in the ledger is an entry quoting the thing it fixed."),
        new(
            "the host's selftest",
            @"app\Session\GateVerdicts.cs",
            ProseReading.Incidental,
            "A label for a row of that table, which the ledger happens to use the same English for."),
        new(
            "the .NET host",
            @"app\Session\GateVerdicts.cs",
            ProseReading.Incidental,
            "The same, and this port names that binary the same way wherever it is mentioned."),
        new(
            "the Qt client",
            @"app\Session\CompileMessages.cs",
            ProseReading.Incidental,
            "A claim read out of compile.cmd, for a line that names the client without naming the file."),
        new(
            ", one monotonic stream",
            @"app\Session\DatagramReplayReport.cs",
            ProseReading.Incidental,
            "A report's own OUTPUT. The ledger quotes it because PP527 wrote it down, which is the coincidence running the other way."),
        new(
            "all block-aligned",
            @"app\Session\DatagramReplayReport.cs",
            ProseReading.Incidental,
            "The same report, the same direction: the document copied the program rather than the program reading the document."),
        new(
            "cannot answer.",
            @"app\Session\GateAndCiAgree.cs",
            ProseReading.Incidental,
            "The tail of a note in that table, long enough to match and about a runner rather than about a backlog."),

        // PP712: names for the work PP707's host still owes. They occur in that line's own criterion
        // because it was written FROM this list - which is the coincidence running the way PP527's
        // does, the document copying the program - and nothing here reads a document. PP714 wrote
        // one of the four and its row left with it, which is what a row falling off looks like;
        // PP719 and PP723 took two more, and this is the last of them.
        new(
            "a BIG message",
            @"app\Protocol\StreamRunHostConsumers.cs",
            ProseReading.Incidental,
            "The third, which PP712 found reported as answered by a builder that has no BIG."),

        // PP718: this one IS matched, and never against a document. NativeWaits.Unclaimed looks for
        // it in its own rows' notes - program data - and the backlog carries the phrase only because
        // PP718's design quotes the note it was filed about.
        new(
            "is unported",
            @"app\Native\NativeWaits.cs",
            ProseReading.Incidental,
            "A phrase matched against this census's OWN note strings, which no document supplies."),
    ];

    /// <summary>How many rows carry one verdict.</summary>
    public static int Count(ProseReading reading) => All.Count(one => one.Reading == reading);

    /// <summary>
    /// A governed file, or null outside a checkout.
    /// </summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativeDirectory);

    /// <summary>
    /// Whitespace collapsed to single spaces.
    ///
    /// Both sides go through it, because roadkeep reflows prose to a width - so a check reading a
    /// document as written would be asserting about where the line broke, and a sweep comparing
    /// against it would miss every literal a wrap fell inside.
    /// </summary>
    public static string Flatten(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var built = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = built.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                built.Append(' ');
                pendingSpace = false;
            }

            built.Append(c);
        }

        return built.ToString();
    }

    /// <summary>
    /// Every string literal in one C# file, comments and char literals skipped.
    ///
    /// NOT A PARSER, and the bounds are worth stating because a sweep that quietly missed things
    /// would make the census look complete. It handles line and block comments, verbatim strings
    /// with their doubled quote, and the backslash escapes of an ordinary one. It does not
    /// interpret escapes - a literal carrying one keeps its backslash and is dropped below, on the
    /// grounds that a path is not a sentence - and it knows nothing of raw string literals, which
    /// this assembly does not use.
    /// </summary>
    public static IReadOnlyList<string> LiteralsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();
        var at = 0;

        while (at < source.Length)
        {
            char c = source[at];

            if (c == '/' && at + 1 < source.Length && source[at + 1] == '/')
            {
                int end = source.IndexOf('\n', at);
                at = end < 0 ? source.Length : end + 1;
                continue;
            }

            if (c == '/' && at + 1 < source.Length && source[at + 1] == '*')
            {
                int end = source.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? source.Length : end + 2;
                continue;
            }

            if (c == '@' && at + 1 < source.Length && source[at + 1] == '"')
            {
                at = ReadVerbatim(source, at + 2, found);
                continue;
            }

            if (c == '"')
            {
                at = ReadOrdinary(source, at + 1, found);
                continue;
            }

            if (c == '\'')
            {
                at = SkipChar(source, at + 1);
                continue;
            }

            at++;
        }

        return found;
    }

    /// <summary>Whether a literal is worth judging: prose-shaped, and not a path or a fragment.</summary>
    public static bool IsCandidate(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);

        if (literal.Length < MinimumLength)
            return false;

        // A path or an escape. Neither is a sentence somebody could write more precisely.
        if (literal.Contains('\\', StringComparison.Ordinal))
            return false;

        // Markup, and leading or trailing space, which is a joining fragment rather than prose.
        if (literal.Contains('*', StringComparison.Ordinal) || literal.Trim() != literal)
            return false;

        return literal.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinimumWords;
    }

    /// <summary>
    /// Every literal under app/ that occurs in one of the governed files, with where it was found.
    /// </summary>
    /// <param name="managedDirectory">app/, as <see cref="LocateManaged"/> gives it.</param>
    /// <param name="governed">The governed files' text, already read.</param>
    public static IReadOnlyList<(string Where, string Text)> Candidates(
        string managedDirectory, IReadOnlyList<string> governed)
    {
        ArgumentNullException.ThrowIfNull(managedDirectory);
        ArgumentNullException.ThrowIfNull(governed);

        string[] flattened = [.. governed.Select(Flatten)];
        var found = new List<(string, string)>();

        foreach (string path in Directory.EnumerateFiles(
            managedDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(@"\obj\", StringComparison.Ordinal)
                || path.Contains(@"\bin\", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string literal in LiteralsIn(File.ReadAllText(path)))
            {
                if (!IsCandidate(literal))
                    continue;

                string flat = Flatten(literal);
                if (flattened.Any(one => one.Contains(flat, StringComparison.Ordinal)))
                    found.Add((path, literal));
            }
        }

        return found;
    }

    /// <summary>The candidate texts no row in <see cref="All"/> judges, in order and without repeats.</summary>
    public static IReadOnlyList<string> Unjudged(IReadOnlyList<(string Where, string Text)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var judged = All.Select(one => one.Text).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. candidates
                .Select(one => one.Text)
                .Where(text => !judged.Contains(text))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static int ReadVerbatim(string source, int at, List<string> found)
    {
        var built = new System.Text.StringBuilder();

        while (at < source.Length)
        {
            if (source[at] == '"')
            {
                // A doubled quote is one quote, which is the only escape a verbatim string has.
                if (at + 1 < source.Length && source[at + 1] == '"')
                {
                    built.Append('"');
                    at += 2;
                    continue;
                }

                break;
            }

            built.Append(source[at]);
            at++;
        }

        found.Add(built.ToString());
        return at + 1;
    }

    private static int ReadOrdinary(string source, int at, List<string> found)
    {
        var built = new System.Text.StringBuilder();

        while (at < source.Length && source[at] != '"')
        {
            if (source[at] == '\\' && at + 1 < source.Length)
            {
                // Kept as written rather than interpreted: what the backslash means is a question
                // the candidate rule answers by dropping the literal.
                built.Append(source[at]).Append(source[at + 1]);
                at += 2;
                continue;
            }

            // An unterminated literal, which is a lexer state this reader does not have. Stop at the
            // line rather than swallowing the rest of the file.
            if (source[at] == '\n')
                break;

            built.Append(source[at]);
            at++;
        }

        found.Add(built.ToString());
        return at + 1;
    }

    private static int SkipChar(string source, int at)
    {
        while (at < source.Length && source[at] != '\'')
            at += source[at] == '\\' ? 2 : 1;

        return at + 1;
    }
}
