using System.Text;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP318: assertions that compare two constants, which no analyzer names.
///
/// PP316 fixed one of these and made the compiler refuse the next. It cannot: xUnit2000 fires on a
/// literal in the WRONG ARGUMENT, not on two of them, and <c>Assert.Equal(0, 0)</c> is well-formed
/// by every rule the analyzers carry. It passes, it can never fail, and it sits under a comment
/// describing a claim about the code that nothing is checking.
///
/// A SHAPE AND NOT A MEANING
/// -------------------------
/// Two literals either side of an <c>Assert.Equal</c> or <c>Assert.NotEqual</c>, an
/// <c>Assert.True(true)</c>, an <c>Assert.False(false)</c>. Each is decidable by reading the call
/// and none can be true about the code under test. What this does NOT find is the larger family -
/// constants reaching the call through a local, or a subject the test also wrote - because that
/// needs a reader that resolves names, and one that tried would be turned off within a week for
/// what it reported wrongly. That is the argument PP278 already made about guarding by convention.
///
/// It also reads one line at a time, so an assertion wrapped across lines is not seen. Stated
/// rather than hidden: the shape is short by nature, and a wrapped one is rare enough that
/// widening the reader would cost more in false reports than it collects.
///
/// WHY THE SCANNER
/// ---------------
/// PP317's check went red on its own project's prose, because it read XML as flat text. The same
/// trap is worse here and it is set twice: this port writes long doc comments that quote the code
/// they are about, and the test that holds THIS check has to spell the shapes out to have anything
/// to assert against. So the sweep reads code only - comments removed, string and char contents
/// blanked - and a sample living in a literal is invisible to it by construction rather than by an
/// exemption list somebody has to maintain.
/// </summary>
public static partial class DeadAssertions
{
    /// <summary>
    /// One dead assertion: where it is, and the line as the scanner saw it.
    /// </summary>
    /// <param name="File">The file, as the caller named it.</param>
    /// <param name="Line">The 1-based line number.</param>
    /// <param name="Text">The trimmed line, with strings blanked.</param>
    public readonly record struct DeadAssertion(string File, int Line, string Text)
    {
        /// <summary>The one-line form a failing assertion prints.</summary>
        public override string ToString() =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{File}:{Line}  {Text}");
    }

    /// <summary>
    /// Every dead assertion in one body of source, in the order they appear.
    /// </summary>
    public static IReadOnlyList<DeadAssertion> In(string source, string file = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(file);

        var found = new List<DeadAssertion>();
        string[] lines = CodeOnly(source).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (TwoConstantsRegex().IsMatch(line) || ConstantSubjectRegex().IsMatch(line))
                found.Add(new DeadAssertion(file, i + 1, line.Trim()));
        }

        return found;
    }

    /// <summary>
    /// Every dead assertion under this checkout, across the files an assertion can live in.
    ///
    /// The corpus is <see cref="AssertionRatchet.AssertionFiles"/>, which PP38 already resolved and
    /// PP308 already argued about: tests\, test\ and app\SelfTest.cs, which is the one that is easy
    /// to leave out. Reusing it means this check and the ratchet cannot disagree about where an
    /// assertion is allowed to be.
    /// </summary>
    public static IReadOnlyList<DeadAssertion> Sweep(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var found = new List<DeadAssertion>();

        foreach (string path in AssertionRatchet.AssertionFiles(root))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            found.AddRange(In(File.ReadAllText(path), relative));
        }

        return found;
    }

    /// <summary>
    /// One source with its comments removed and its string and char contents blanked, keeping every
    /// newline so a line number still means what it said.
    ///
    /// Handles the four spellings this tree uses: line and block comments, ordinary strings with
    /// escapes, verbatim <c>@"…"</c> strings with their doubled-quote escape, and raw <c>"""…"""</c>
    /// strings, whose terminator is however many quotes opened them.
    /// </summary>
    public static string CodeOnly(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var code = new StringBuilder(source.Length);
        int at = 0;

        while (at < source.Length)
        {
            char c = source[at];

            if (c == '/' && Next(source, at) == '/')
            {
                // To the end of the line, and the newline itself is left for the loop to emit.
                while (at < source.Length && source[at] != '\n')
                    at++;
                continue;
            }

            if (c == '/' && Next(source, at) == '*')
            {
                at += 2;
                while (at < source.Length && !(source[at] == '*' && Next(source, at) == '/'))
                    KeepNewline(code, source[at++]);

                at = Math.Min(at + 2, source.Length);
                continue;
            }

            if (c == '"' && Next(source, at) == '"' && Next(source, at + 1) == '"')
            {
                int quotes = 0;
                while (at < source.Length && source[at] == '"')
                {
                    quotes++;
                    at++;
                }

                string terminator = new('"', quotes);
                int end = source.IndexOf(terminator, at, StringComparison.Ordinal);
                int stop = end < 0 ? source.Length : end;

                while (at < stop)
                    KeepNewline(code, source[at++]);

                at = end < 0 ? source.Length : end + quotes;
                code.Append("\"\"");
                continue;
            }

            if (c == '@' && Next(source, at) == '"')
            {
                at += 2;
                while (at < source.Length)
                {
                    if (source[at] == '"')
                    {
                        // A doubled quote is an escaped one and the string keeps going.
                        if (Next(source, at) == '"')
                        {
                            at += 2;
                            continue;
                        }

                        at++;
                        break;
                    }

                    KeepNewline(code, source[at++]);
                }

                code.Append("\"\"");
                continue;
            }

            if (c is '"' or '\'')
            {
                char quote = c;
                at++;
                while (at < source.Length && source[at] != quote)
                {
                    if (source[at] == '\\')
                        at++;

                    at++;
                }

                at++;
                code.Append(quote).Append(quote);
                continue;
            }

            code.Append(c);
            at++;
        }

        return code.ToString();
    }

    /// <summary>The character after this one, or NUL where there is none.</summary>
    private static char Next(string source, int at) =>
        at + 1 < source.Length ? source[at + 1] : '\0';

    /// <summary>
    /// Newlines survive what is skipped, and nothing else does. A line number that drifted would
    /// send a reader to the wrong line, which is worse than not reporting one.
    /// </summary>
    private static void KeepNewline(StringBuilder code, char c)
    {
        if (c == '\n')
            code.Append(c);
    }

    /// <summary>A literal: a number, a blanked string or char, or one of the three keywords.</summary>
    private const string Literal =
        @"(?:-?\d[\d_]*(?:\.\d[\d_]*)?[fFdDmMuUlL]*|0[xX][0-9a-fA-F_]+|""""|''|true|false|null)";

    [GeneratedRegex($@"Assert\.(?:Equal|NotEqual|Same|NotSame)\(\s*{Literal}\s*,\s*{Literal}\s*[,)]")]
    private static partial Regex TwoConstantsRegex();

    [GeneratedRegex(@"Assert\.(?:True\(\s*true\s*[,)]|False\(\s*false\s*[,)])")]
    private static partial Regex ConstantSubjectRegex();
}
