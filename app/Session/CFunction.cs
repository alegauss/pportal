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
/// </summary>
public static class CFunction
{
    /// <summary>
    /// The body of <paramref name="name"/>, without its signature, or null where the file declares
    /// no definition for it.
    /// </summary>
    /// <param name="source">The whole translation unit.</param>
    /// <param name="name">
    /// The function's name, or any prefix of its definition ending before the parameter list - a
    /// full signature works and is what a caller passes to tell two overloads apart.
    /// </param>
    public static string? Body(string source, string name)
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
}
