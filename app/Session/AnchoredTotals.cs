using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One "N lines" phrase in the backlog that no claim covers.</summary>
/// <param name="Document">The backlog file it is in.</param>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Stated">The number nobody can check.</param>
/// <param name="Text">The phrase, for the message.</param>
public readonly record struct UnanchoredTotal(string Document, int Line, int Stated, string Text);

/// <summary>
/// PP443: a line total the recount cannot see, because no filename sits beside it.
///
/// <see cref="CountedClaims"/> checks two shapes - a claim naming a FILE and a number, and one naming
/// a DIRECTORY. A claim naming neither is about nothing the reader can find, and the backlog held
/// three:
///
///   PP27  "takion is 1868 lines"                        takion.c is 1888                out by 20
///   PP28  "session, ctrl and streamconnection are 3977"  1244 + 1763 + 1531 = 4538      out by 561
///   PP29  "registration and discovery are 1775"          918 + 492 + 384 = 1794         out by 19
///
/// THE CONTROL WAS IN THE SAME BACKLOG. PP294 said "ctrl.c is 1763 lines" - a filename beside a
/// number - and it was exact. One anchored and right, three unanchored and wrong.
///
/// THE FIX WAS THE CLAIM, NOT THE READER, and it had to be: a sum cannot be anchored, because
/// "streamconnection.c 4538" would be read as that file's own count and would be false. So each of
/// the three now states its files separately with one number each, and the recount went from 16
/// claims to 24 and checks them all.
///
/// THIS IS THE THIRD TIME. PP280 found two of seven stale, PP410 found three in a shape the reader
/// could not see and one stale by 139, and this is three more. What is new is the guard: not the
/// arithmetic, which the recount already does, but whether a number is checkABLE at all.
///
/// ITS LIMIT IS STATED RATHER THAN SILENT. It reads the phrase "N lines", which is the shape all
/// three took and the shape PP280 and PP410 took. A future total spelled without the word - "the
/// three are 4000" - is not caught, and broadening to any bare number would report ids, dates and
/// byte counts instead.
/// </summary>
public static partial class AnchoredTotals
{
    /// <summary>The files this reads. The same two the recount reads.</summary>
    public static IReadOnlyList<string> Backlog => CountedClaims.Backlog;

    /// <summary>
    /// Every "N lines" phrase in the given lines that falls outside every claim
    /// <see cref="CountedClaims.In"/> recognised.
    ///
    /// By POSITION, not by value: two numbers on one line can be equal, and a phrase is anchored
    /// only if it is the one the claim actually matched.
    /// </summary>
    public static IReadOnlyList<UnanchoredTotal> In(IEnumerable<string> lines, string document)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(document);

        var loose = new List<UnanchoredTotal>();
        int number = 0;

        foreach (string line in lines)
        {
            number++;

            MatchCollection totals = TotalRegex().Matches(line);
            if (totals.Count == 0)
                continue;

            IReadOnlyList<CountedClaim> claims = CountedClaims.In([line], document);

            foreach (Match total in totals)
            {
                int at = total.Groups["lines"].Index;

                // PP412's rule, which this check owes: a number inside quotes is ILLUSTRATION and
                // not a claim. §PP443's own prose is the proof - written with the real numbers, it
                // reintroduced the shape it describes and turned this red along with PP412's check.
                if (CountedClaims.IsInsideQuotes(line, at))
                    continue;

                bool anchored = claims.Any(claim =>
                    at >= claim.Column && at < claim.Column + claim.Text.Length);

                if (!anchored)
                {
                    loose.Add(new UnanchoredTotal(
                        document, number, int.Parse(total.Groups["lines"].Value,
                            System.Globalization.CultureInfo.InvariantCulture),
                        total.Value.Trim()));
                }
            }
        }

        return loose;
    }

    /// <summary>Every unanchored total in the backlog, or empty outside a checkout.</summary>
    public static IReadOnlyList<UnanchoredTotal> All()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return [];

        var loose = new List<UnanchoredTotal>();

        foreach (string document in Backlog)
        {
            string path = Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                loose.AddRange(In(File.ReadAllLines(path), document));
        }

        return loose;
    }

    /// <summary>
    /// How many "N lines" phrases the backlog holds at all, anchored or not.
    ///
    /// PP271: a reader that stopped matching would report no unanchored totals and be believed, so
    /// the count it examined is available to be asserted about.
    /// </summary>
    public static int Examined(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Sum(line => TotalRegex().Matches(line).Count);
    }

    // "1888 lines" and "3977 lines of state machine" - the number and the word, with the number
    // captured so its position can be compared against a claim's span.
    [GeneratedRegex(@"(?<lines>\d{2,5})\s+lines\b")]
    private static partial Regex TotalRegex();
}
