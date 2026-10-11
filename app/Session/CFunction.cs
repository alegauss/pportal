namespace ChiakiNg.Session;

/// <summary>
/// PP343: one reader for a C function's body, which is the commonest thing the drift checks do.
///
/// THE FIRST MATCH IS NOT THE FUNCTION, and that is the whole reason this needs care. Every static
/// handler in this tree's C is forward-declared at the top of its file, so a search for the name
/// finds a prototype ending in a semicolon. A body taken from there runs to the next closing brace
/// somewhere else in the file, and the comparison that follows is between two positions in a
/// function neither call is in - which checks out green.
///
/// A match is therefore only the definition when what follows its parameter list is a brace. From
/// there the end is found by COUNTING braces rather than by matching the first one in column zero:
/// the crude version works for every function in this tree today and stops working on the first one
/// containing a brace at the start of a line, which is a thing no author would think to check.
///
/// IT LIVES HERE BECAUSE IT IS ABOUT C AND NOT ABOUT A SUBSYSTEM. Two copies of this existed, one
/// behind a class about the reorder queue and one private to the message tap, and a third was
/// written before either was found - twice caught by a failing test rather than by review. A reader
/// that looks general gets reused; one named after a subsystem gets copied, and the copy is the
/// version that walks into the trap.
///
/// PP847: AND IT HANDED BACK COMMENTS, WHICH IT NO LONGER DOES. Every reader beside this one strips
/// with <see cref="CCall.Code"/> - PP400 wrote that for exactly this, and PP437's census, PP540's
/// sweep and PP793's export sweep all read through it, because a symbol named in prose is not a
/// reference to it. This was the one that did not, and 160 call sites had the choice available.
///
/// PP30 PAID FOR IT. Its note where a deleted call had been named the function it had just removed,
/// the assertion that the call was gone read this body, found the note, and went red in the commit
/// that did the work.
///
/// AND IT REMOVES A SECOND CLASS OF HAZARD THIS TREE HAS NOT TRIPPED, which is worth stating as
/// exactly that. A commented-out function looks like a real one to a raw reader - signature,
/// parenthesis, brace - and holepunch.c carries forty lines of one at file scope. It never bit, and
/// the reason is the comment markers themselves: between the <c>)</c> and the <c>{</c> of a
/// <c>//</c> block sits the next line's <c>//</c>, so the definition test fails and the search moves
/// on. A <c>/* */</c> one would have no such gap and would be taken.
///
/// The same for braces: a comment inside a body holding an unbalanced one would end
/// <see cref="RawBody"/>'s count early or late.
///
/// PP852: BOTH PREMISES ARE SWEPT NOW, not measured once. <see cref="CommentedDefinitions"/> and
/// <see cref="UnbalancedCommentBraces"/> answer them for one file, and CFunctionTests runs them over
/// every file the drift checks read - so the C arriving with either is a red gate rather than a raw
/// reader quietly answering about a function nobody compiles.
/// </summary>
public static class CFunction
{
    /// <summary>PP852: one comment in a C file, as written.</summary>
    /// <param name="Line">One-based line the comment starts on.</param>
    /// <param name="Text">The comment with its markers.</param>
    /// <param name="Block">A <c>/* */</c> comment rather than a <c>//</c> one.</param>
    public readonly record struct Comment(int Line, string Text, bool Block);

    /// <summary>PP852: a body whose comments do not balance their braces.</summary>
    /// <param name="Line">The line its opening brace is on.</param>
    /// <param name="Net">Opening minus closing braces across the comments inside it.</param>
    public readonly record struct Unbalanced(int Line, int Net);

    private static readonly System.Text.RegularExpressions.Regex Definition =
        new(@"[A-Za-z_]\w*\s*\([^;{}()]*\)\s*\{", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// PP852: every comment in <paramref name="source"/>, in order.
    ///
    /// String and character literals are stepped over, so <c>"/*"</c> and <c>'"'</c> start nothing
    /// - the second is a case <see cref="CCall.Code"/> does not handle, and a sweep that misreads
    /// a quote character would report the rest of the file as a string.
    /// </summary>
    public static IReadOnlyList<Comment> Comments(string source) => Walk(source, null);

    /// <summary>
    /// PP852: the block comments that hold a definition's shape - a name, a parameter list and a
    /// brace - which <see cref="RawBody"/> would take for the function itself.
    /// </summary>
    public static IReadOnlyList<Comment> CommentedDefinitions(string source)
        => [.. Comments(source).Where(comment => comment.Block && Definition.IsMatch(comment.Text))];

    /// <summary>
    /// PP852: the top-level bodies whose comments add up to an unbalanced brace, which would move
    /// where <see cref="RawBody"/> stops counting. A comment holding <c>{</c> and another holding
    /// <c>}</c> in the same body cancel, which is the state the C was found in and is no hazard.
    /// </summary>
    public static IReadOnlyList<Unbalanced> UnbalancedCommentBraces(string source)
    {
        var found = new List<Unbalanced>();
        Walk(source, found);
        return found;
    }

    /// <summary>
    /// One pass: comments collected, code braces counted, and each top-level body's comment braces
    /// summed. Where <paramref name="bodies"/> is null only the comments are wanted.
    /// </summary>
    private static List<Comment> Walk(string source, List<Unbalanced>? bodies)
    {
        ArgumentNullException.ThrowIfNull(source);

        var comments = new List<Comment>();
        var line = 1;
        var depth = 0;
        var bodyLine = 0;
        var bodyNet = 0;

        for (var at = 0; at < source.Length; at++)
        {
            char c = source[at];

            if (c == '\n')
            {
                line++;
                continue;
            }

            if (c is '"' or '\'')
            {
                int close = at + 1;
                while (close < source.Length && source[close] != c && source[close] != '\n')
                    close += source[close] == '\\' ? 2 : 1;

                at = Math.Min(close, source.Length - 1);
                if (source[at] == '\n')
                    line++;
                continue;
            }

            if (c == '/' && at + 1 < source.Length && source[at + 1] is '/' or '*')
            {
                bool block = source[at + 1] == '*';
                int end = block ? source.IndexOf("*/", at + 2, StringComparison.Ordinal) : source.IndexOf('\n', at);
                end = end < 0 ? source.Length : end + (block ? 2 : 0);

                string text = source[at..end];
                comments.Add(new Comment(line, text, block));

                if (depth > 0)
                    bodyNet += text.Count(ch => ch == '{') - text.Count(ch => ch == '}');

                line += text.Count(ch => ch == '\n');
                at = end - 1;
                continue;
            }

            if (c == '{' && depth++ == 0)
            {
                bodyLine = line;
                bodyNet = 0;
            }
            else if (c == '}' && depth > 0 && --depth == 0 && bodyNet != 0)
            {
                bodies?.Add(new Unbalanced(bodyLine, bodyNet));
            }
        }

        return comments;
    }
    /// <summary>
    /// The body of <paramref name="name"/>, without its signature, or null where the file declares
    /// no definition for it.
    ///
    /// PP847: COMMENTS ARE GONE BEFORE THE SEARCH STARTS. A caller that wants the text as written
    /// asks <see cref="RawBody"/> for it, which is the whole of the difference and is deliberate:
    /// this name is what 160 call sites already use, so the safe answer is the one they get by
    /// default and the other is the one somebody has to name.
    /// </summary>
    /// <param name="source">The whole translation unit.</param>
    /// <param name="name">
    /// The function's name, or any prefix of its definition ending before the parameter list - a
    /// full signature works and is what a caller passes to tell two overloads apart.
    ///
    /// A signature spanning a comment does not match, which is a consequence rather than a rule: the
    /// stripper leaves a newline where a line comment was and a space where a block one was, so the
    /// text a caller names has to be the code.
    /// </param>
    public static string? Body(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);

        return RawBody(CCall.Code(source), name);
    }

    /// <summary>
    /// The same, over the text exactly as written - comments included.
    ///
    /// FOR A CALLER THAT IS ABOUT A COMMENT, and there are two. PP847's design said there were none
    /// and the gate found them the moment <see cref="Body"/> started stripping: TakionHandshake asks
    /// whether the wire field the tag was chosen over is still commented out beside it, and
    /// CtrlRudpSubtypes whether the switch still admits its own shape is wrong. Both are claims
    /// about prose in the C, and both are the reason this door is named rather than absent.
    ///
    /// What it cannot do is tell a commented-out function from a real one where no comment marker
    /// sits between the parenthesis and the brace - see the note above.
    /// </summary>
    public static string? RawBody(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        for (int start = source.IndexOf(name, StringComparison.Ordinal);
             start >= 0;
             start = source.IndexOf(name, start + name.Length, StringComparison.Ordinal))
        {
            int close = source.IndexOf(')', start);
            if (close < 0)
                return null;

            int open = close + 1;
            while (open < source.Length && char.IsWhiteSpace(source[open]))
                open++;

            // A prototype, or a call. Keep looking; the definition is further down.
            if (open >= source.Length || source[open] != '{')
                continue;

            var depth = 0;
            for (int at = open; at < source.Length; at++)
            {
                if (source[at] == '{')
                {
                    depth++;
                }
                else if (source[at] == '}' && --depth == 0)
                {
                    // Between the braces, exclusive - so a caller reading for a statement cannot
                    // match the signature it came from.
                    return source[(open + 1)..at];
                }
            }

            return null;
        }

        return null;
    }

    /// <summary>The same, read out of a file. Null where the file cannot be read.</summary>
    public static string? BodyIn(string filePath, string name)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        return File.Exists(filePath) ? Body(File.ReadAllText(filePath), name) : null;
    }

    /// <summary>
    /// PP847: and the raw form read out of a file, which is <see cref="RawBody"/>'s door.
    ///
    /// Here because <see cref="BodyIn"/> is what a drift check calls, so a check about a comment
    /// would otherwise have to open the file itself - and a caller opening a file to get around a
    /// reader is how a second copy of that reader gets written, which is PP343's own finding.
    /// </summary>
    public static string? RawBodyIn(string filePath, string name)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        return File.Exists(filePath) ? RawBody(File.ReadAllText(filePath), name) : null;
    }
}
