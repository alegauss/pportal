namespace ChiakiNg.Session;

/// <summary>
/// PP386: whether a C call HAPPENS, asked without reference to how it is punctuated.
///
/// The drift checks in this port quote C statements to assert things about them, and 101 of them
/// quote the terminating semicolon too. For fifty-four that is right: NatProbe's writes into
/// confirm_buf at 0x50, 0x52 and 0x54 are the wire format, and a check that accepted them written
/// any other way would have given the whole thing away.
///
/// FOR THE OTHER FORTY-SEVEN THE SEMICOLON IS INCIDENTAL. What is being claimed is that a call
/// happens, or that two happen in an order, and the punctuation around it is the C author's
/// business. Four of those went red in one block of work on edits that moved no behaviour - three
/// on sends being wrapped in a guard that reads their result, one on a call passed as an argument
/// across two lines.
///
/// NONE OF THE FOUR WAS A FALSE NEGATIVE. They were false ALARMS, which is the harm: a check that
/// cries on a refactor teaches the next reader to edit the check rather than read it, and that is
/// how a real failure later gets waved through.
///
/// SO THE RULE IS: ask <see cref="Happens"/> when the claim is that a call is made, and quote the
/// statement whole when the claim is what the bytes are. The two are different questions and were
/// written identically.
///
/// Whitespace is removed from both sides rather than normalised, so a call split across lines, one
/// indented differently, and one written <c>f(a,b)</c> against <c>f(a, b)</c> are all the same
/// call - which they are. The closing parenthesis is what keeps <c>free(notif)</c> from matching
/// <c>free(notif-&gt;json_buf)</c>, and an identifier-boundary test in front is what keeps it from
/// matching <c>xfree(notif)</c>.
/// </summary>
public static class CCall
{
    /// <summary>
    /// The text with layout removed but tokens still separated.
    ///
    /// NOT every whitespace character. The first version of this deleted them all, and welded
    /// <c>#endif</c> to the <c>xor_bytes(md, md + 0x10, 0x10);</c> on the line below it - so the
    /// call began mid-identifier and the boundary test correctly refused to see it. Deleting layout
    /// must not create adjacencies the C never had.
    ///
    /// So: runs of whitespace become one space, and a space goes only where at least one side of it
    /// is punctuation. <c>f(a, b)</c> and <c>f(a,b)</c> become the same thing; <c>md + 0x10</c> and
    /// <c>md+0x10</c> do too; <c>#endif</c> and <c>xor_bytes</c> stay two words.
    /// </summary>
    public static string Compact(string text)
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
                // Only between two identifier characters is the space carrying anything.
                if (IsWord(built[^1]) && IsWord(c))
                    built.Append(' ');

                pendingSpace = false;
            }

            built.Append(c);
        }

        return built.ToString();
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Whether <paramref name="call"/> appears in <paramref name="source"/> as a call.
    /// </summary>
    /// <param name="source">The C, or any part of it.</param>
    /// <param name="call">
    /// The call as it would be written, with or without a terminator - <c>free(notif)</c> and
    /// <c>free(notif);</c> ask the same question.
    /// </param>
    public static bool Happens(string source, string call) => Count(source, call) > 0;

    /// <summary>How many times it appears. Zero where it does not.</summary>
    public static int Count(string source, string call)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(call);

        string needle = Compact(call).TrimEnd(';');
        if (needle.Length == 0)
            return 0;

        string haystack = Compact(source);

        var found = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            if (IsCallStart(haystack, at))
                found++;
        }

        return found;
    }

    /// <summary>
    /// Where it appears, in the compacted text, or -1. Positions are comparable to each other and
    /// to nothing else - which is all an ordering check needs.
    /// </summary>
    public static int At(string source, string call, int from = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(call);
        ArgumentOutOfRangeException.ThrowIfNegative(from);

        string needle = Compact(call).TrimEnd(';');
        if (needle.Length == 0)
            return -1;

        string haystack = Compact(source);

        for (int at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            if (IsCallStart(haystack, at))
                return at;
        }

        return -1;
    }

    /// <summary>
    /// Whether these calls appear in this order, each after the last.
    ///
    /// The commonest ordering claim in this port - a teardown that must release one thing before
    /// another, a burst whose messages go out in a fixed sequence - and the one most often written
    /// as a chain of IndexOf calls that each had to quote a semicolon.
    /// </summary>
    public static bool InOrder(string source, params string[] calls)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(calls);

        var cursor = 0;
        foreach (string call in calls)
        {
            int at = At(source, call, cursor);
            if (at < 0)
                return false;

            cursor = at + 1;
        }

        return calls.Length > 0;
    }

    /// <summary>
    /// Whether the match at <paramref name="at"/> begins a name rather than ending one.
    ///
    /// <c>free(notif)</c> must not be found inside <c>xfree(notif)</c>, and removing the whitespace
    /// is what makes that reachable - the two are adjacent once the space is gone.
    /// </summary>
    private static bool IsCallStart(string haystack, int at)
    {
        if (at == 0)
            return true;

        return !IsWord(haystack[at - 1]);
    }
}
