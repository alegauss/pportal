using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One "&lt;file&gt; is &lt;n&gt; lines" claim, and where it was written.</summary>
/// <param name="Document">The governed file carrying it, repository-relative with forward slashes.</param>
/// <param name="Line">1-based line in that document.</param>
/// <param name="Text">The matched text itself, which is what a correction replaces.</param>
/// <param name="Subject">The file or directory being sized.</param>
/// <param name="Stated">The number the backlog states.</param>
/// <param name="SizesADirectory">Whether the subject is a directory total rather than one file.</param>
public readonly record struct CountedClaim(
    string Document, int Line, string Text, string Subject, int Stated, bool SizesADirectory);

/// <summary>
/// PP280, PP285 and PP304: the sizes the backlog states, where they are stated, and how to correct
/// one.
///
/// The scanning and the counting were PP280's and lived in the test that asserts them, which was
/// right while a person only ever needed the verdict. PP304 is the other half: every commit that
/// adds a comment to a C file invalidates one to three of these, and the list of them arrives from
/// a red test AFTER the work is done - so each is then corrected by hand, with the old number
/// spelled exactly, into whichever governed command that document takes. Measured over one session:
/// three fixes, six corrections, one of them addressed to the wrong section on the first try.
///
/// So the reading moved here, where both the test and <c>ChiakiNg.exe --recount</c> reach it, and
/// what this adds over the test is <see cref="Remedy"/> - the exact roadkeep call that fixes one
/// claim, with the anchor resolved and both numbers filled in. It does not WRITE: the governed
/// files are roadkeep's, a hook denies a hand edit to them, and a tool that went around that would
/// be solving the transcription by removing the gate.
/// </summary>
public static partial class CountedClaims
{
    /// <summary>The two governed files that describe unshipped work, and so speak in the present.</summary>
    public static IReadOnlyList<string> Backlog { get; } = ["docs/ROADMAP.md", "docs/IMPROVEMENTS.md"];

    /// <summary>Where a file the backlog sizes is looked for. Not test\, which shares basenames.</summary>
    public static IReadOnlyList<string> ImplementationTrees { get; } = ["lib", "gui", "shim", "app"];

    /// <summary>Every claim in the backlog, file-sized and directory-sized together.</summary>
    public static IReadOnlyList<CountedClaim> All(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var claims = new List<CountedClaim>();

        foreach (string document in Backlog)
        {
            string path = Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in FileClaimRegex().Matches(lines[i]))
                {
                    claims.Add(new CountedClaim(
                        document, i + 1, match.Value, match.Groups["file"].Value,
                        int.Parse(match.Groups["lines"].Value, CultureInfo.InvariantCulture),
                        SizesADirectory: false));
                }

                foreach (Match match in TreeClaimRegex().Matches(lines[i]))
                {
                    claims.Add(new CountedClaim(
                        document, i + 1, match.Value, match.Groups["dir"].Value,
                        int.Parse(match.Groups["lines"].Value, CultureInfo.InvariantCulture),
                        SizesADirectory: true));
                }
            }
        }

        return claims;
    }

    /// <summary>
    /// What the tree says the claim's subject is, or -1 where the subject cannot be resolved to
    /// exactly one thing - which is a broken claim rather than a stale one and is reported apart.
    /// </summary>
    public static int Actual(string root, CountedClaim claim)
    {
        ArgumentNullException.ThrowIfNull(root);

        return claim.SizesADirectory ? LinesOfCIn(root, claim.Subject) : LinesOfFile(root, claim.Subject);
    }

    /// <summary>Every .c line under a directory, which is what "lines of C in" is defined to mean.</summary>
    public static int LinesOfCIn(string root, string relative)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);

        string directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
            return -1;

        return Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories)
            .Where(NotVendoredOrBuilt)
            .Sum(p => File.ReadAllLines(p).Length);
    }

    /// <summary>
    /// One named file's line count, searched in the implementation trees only.
    ///
    /// Six of these basenames exist twice - lib\src\takion.c is the transport and test\takion.c is
    /// the C suite's vectors for it - and a claim that takion is 1868 lines of C over raw sockets is
    /// plainly about the first. Searching the whole checkout made all six unresolvable, which reads
    /// as a broken scan rather than as the answer being obvious.
    /// </summary>
    public static int LinesOfFile(string root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);

        string[] found = [.. Locate(root, name)];
        return found.Length == 1 ? File.ReadAllLines(found[0]).Length : -1;
    }

    /// <summary>Every path in the implementation trees matching a basename.</summary>
    public static IEnumerable<string> Locate(string root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);

        return ImplementationTrees
            .Select(tree => Path.Combine(root, tree))
            .Where(Directory.Exists)
            .SelectMany(tree => Directory.EnumerateFiles(tree, name, SearchOption.AllDirectories))
            .Where(NotVendoredOrBuilt);
    }

    /// <summary>
    /// The roadkeep call that corrects one claim, or null where the document is not one this knows
    /// how to address.
    ///
    /// Two shapes, because the two documents are two things. A claim in IMPROVEMENTS.md sits in a
    /// rationale section, so it is `section amend &lt;anchor&gt; --replace ... --with ...`, and the
    /// anchor is the nearest heading above it - which is the part a person gets wrong, because a
    /// claim about session.c can as easily be in §PP28 as in §PP293. A claim in ROADMAP.md is in a
    /// task line, where the symptom and the why are separate fields with separate verbs, so which
    /// half the number is in decides between `restate` and `amend`.
    /// </summary>
    public static string? Remedy(string root, CountedClaim claim, int actual)
        => RemedyArguments(root, claim, actual) is { } argv ? Render(argv) : null;

    /// <summary>
    /// PP417: the same call as the ARGUMENTS to pass roadkeep, rather than a line to paste.
    ///
    /// <see cref="Remedy"/> is now this rendered, which makes the argument list the one source of
    /// truth: what `--apply` runs is what `--recount` showed, and neither is parsed back out of the
    /// other. Handing a rendered line to a shell is where quoting goes wrong - prose fields here
    /// carry apostrophes, quotes and backticks - and it went wrong twice in the session that asked
    /// for this.
    ///
    /// The leading "roadkeep" is NOT in the list. It is the program, not an argument.
    /// </summary>
    public static IReadOnlyList<string>? RemedyArguments(string root, CountedClaim claim, int actual)
    {
        ArgumentNullException.ThrowIfNull(root);

        string path = Path.Combine(root, claim.Document.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return null;

        string[] lines = File.ReadAllLines(path);
        if (claim.Line < 1 || claim.Line > lines.Length)
            return null;

        string source = lines[claim.Line - 1];
        string corrected = Corrected(claim, actual);

        if (claim.Document.EndsWith("ROADMAP.md", StringComparison.OrdinalIgnoreCase))
            return TaskLineRemedyArguments(source, claim, corrected);

        string? anchor = AnchorAbove(lines, claim.Line);
        if (anchor is null)
            return null;

        // The matched fragment where it is unique in the file, the whole line where it is not.
        // `section amend --replace` refuses anything occurring more than once, deliberately, so
        // handing it an ambiguous fragment would only move the failure one command along.
        string replace = Occurrences(lines, claim.Text) == 1 ? claim.Text : source.Trim();
        string with = replace == claim.Text ? corrected : source.Trim().Replace(claim.Text, corrected, StringComparison.Ordinal);

        return ["section", "amend", anchor, "--replace", replace, "--with", with];
    }

    /// <summary>
    /// PP417: an argument list as the line it used to be built as.
    ///
    /// A value is quoted where it follows an option, which is what the printed form always did - the
    /// verbs, the id and the flags bare, the prose in quotes. Deliberately NOT a general shell
    /// quoter: nothing consumes this but a reader, and a value carrying a quote of its own is shown
    /// as it is rather than escaped into something that is no longer what will run.
    /// </summary>
    public static string Render(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);

        var rendered = new List<string>(argv.Count + 1) { "roadkeep" };

        for (var at = 0; at < argv.Count; at++)
        {
            bool isValue = at > 0 && argv[at - 1].StartsWith("--", StringComparison.Ordinal);
            rendered.Add(isValue ? $"\"{argv[at]}\"" : argv[at]);
        }

        return string.Join(" ", rendered);
    }

    /// <summary>The claim's own text with the tree's number in place of the stated one.</summary>
    public static string Corrected(CountedClaim claim, int actual)
        => claim.Text.Replace(
            claim.Stated.ToString(CultureInfo.InvariantCulture),
            actual.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>The anchor of the nearest section heading at or above a line, or null if none.</summary>
    public static string? AnchorAbove(IReadOnlyList<string> lines, int line)
    {
        ArgumentNullException.ThrowIfNull(lines);

        for (int i = Math.Min(line, lines.Count) - 1; i >= 0; i--)
        {
            Match heading = HeadingRegex().Match(lines[i]);
            if (heading.Success)
                return heading.Groups["anchor"].Value;
        }

        return null;
    }

    /// <summary>
    /// Which of a task line's two prose fields carries the number, as the verb that rewrites it.
    ///
    /// The line is `- MARKER **ID** (deps: …) **symptom** — why → §ref`. The symptom is bold and
    /// the why is not, so the second `**` pair is the whole of the distinction - and it matters,
    /// because `restate` and `amend` write different fields and each refuses the other's text.
    /// </summary>
    public static string? TaskLineRemedy(string source, CountedClaim claim, string corrected)
        => TaskLineRemedyArguments(source, claim, corrected) is { } argv ? Render(argv) : null;

    /// <summary>PP417: the same, as the arguments to pass roadkeep.</summary>
    public static IReadOnlyList<string>? TaskLineRemedyArguments(
        string source, CountedClaim claim, string corrected)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(corrected);

        Match line = TaskLineRegex().Match(source);
        if (!line.Success)
            return null;

        string id = line.Groups["id"].Value;
        Group symptom = line.Groups["symptom"];
        Group why = line.Groups["why"];

        if (symptom.Value.Contains(claim.Text, StringComparison.Ordinal))
        {
            string fixedText = symptom.Value.Replace(claim.Text, corrected, StringComparison.Ordinal);
            return ["restate", id, "--symptom", fixedText];
        }

        if (why.Value.Contains(claim.Text, StringComparison.Ordinal))
        {
            string fixedText = why.Value.Replace(claim.Text, corrected, StringComparison.Ordinal).Trim();
            return ["amend", id, "--why", fixedText];
        }

        return null;
    }

    private static int Occurrences(IReadOnlyList<string> lines, string text)
    {
        int count = 0;
        foreach (string line in lines)
        {
            int at = 0;
            while ((at = line.IndexOf(text, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += text.Length;
            }
        }

        return count;
    }

    private static bool NotVendoredOrBuilt(string path)
    {
        char sep = Path.DirectorySeparatorChar;
        return !path.Contains($"{sep}third-party{sep}", StringComparison.Ordinal)
            && !path.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
            && !path.Contains($"{sep}obj{sep}", StringComparison.Ordinal);
    }

    // "<name>.c is 1845 lines" and "<name>.c 406" - the second is how a list continues one "is"
    // across several files. Deliberately not matching a bare number near a filename with nothing
    // between them, which is most of a sentence.
    //
    // PP410: up to six lowercase words may stand between the two. The pattern used to allow "is"
    // and nothing else, which left "ctrl.c is the longest at 1574 lines" and two "<name>.c at
    // <n>" list continuations unscanned - and unscanned meant unchecked, so the first of them was
    // 139 lines stale while this gate reported every claim holding.
    //
    // THE BOUND IS WHAT KEEPS IT A READER RATHER THAN A GUESSER. Lowercase excludes a sentence
    // start, and the word run cannot cross a full stop because a word ending in one is not
    // [a-z]+ followed by whitespace. So "http.c is not among them. It is 262 lines" stays out:
    // that number belongs to http.c and only a person reading the prose knows it. Diffed over
    // both governed documents before this changed - three claims gained, none lost, nothing else
    // matched - which is the check to repeat if the bound is ever widened again.
    [GeneratedRegex(@"(?<file>[A-Za-z0-9_.-]+\.(?:c|h|cpp|cs|qml))\s+(?:(?![0-9])[a-z]+\s+){0,6}(?<lines>\d{2,5})\b(?=\s*(?:lines|and|,|:|\.|$))")]
    private static partial Regex FileClaimRegex();

    // "24527 lines of C in lib/src". The "of C in" is required and is the point: it names the
    // counting rule, which is what PP23's "the 16935 lines in lib/src" did not.
    [GeneratedRegex(@"(?<lines>\d{3,6})\s+lines of C in\s+(?<dir>[A-Za-z0-9_./-]+)")]
    private static partial Regex TreeClaimRegex();

    // ### §PP293 The heading text
    [GeneratedRegex(@"^#{2,6}\s+§(?<anchor>[A-Za-z]+[0-9]+)\b")]
    private static partial Regex HeadingRegex();

    // - ⏳ **PP293** (deps: PP297 ⏳) **symptom** — why → §PP293
    [GeneratedRegex(@"^-\s+\S+\s+\*\*(?<id>[A-Za-z]+[0-9]+)\*\*[^*]*\*\*(?<symptom>.+?)\*\*\s+—\s+(?<why>.*?)(?:\s+→.*)?$")]
    private static partial Regex TaskLineRegex();
}
