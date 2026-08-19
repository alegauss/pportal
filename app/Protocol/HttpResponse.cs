namespace ChiakiNg.Protocol;

/// <summary>One header, as the parser found it.</summary>
public readonly record struct HttpHeader(string Key, string Value);

/// <summary>
/// PP33: the HTTP response parser, in managed code, replacing the vendored one rather than calling
/// it.
///
/// This is the first piece of the core that gets SMALLER instead of merely moving - curl and
/// json-c were vendored for what the runtime already does. It is also therefore the first place
/// two implementations exist at once, which is what PP23's harness was built for: the same bytes
/// go through this and through libchiaki's, and the answers are compared header for header.
///
/// Being a replacement, it has to match a parser nobody wrote down, so three of its rules are
/// transcribed rather than chosen:
///
///   - The headers come out in REVERSE order. libchiaki builds the list by prepending, and the
///     one caller that reads it by position would see a different response if this sorted them.
///   - Exactly ONE space is skipped after the colon, not all whitespace. "Key:  two" keeps a
///     leading space in its value.
///   - A header is only recorded when a line ending follows it. A response whose last header has
///     no trailing newline loses that header, with no error - which is a trap and not a rule, and
///     is asserted so the two implementations at least lose it together.
/// </summary>
public static class HttpResponse
{
    private const string VersionPrefix = "HTTP/1.1 ";

    /// <summary>
    /// Parses a response, or returns null when it is not one.
    ///
    /// Null covers what libchiaki refuses: a buffer shorter than the version prefix, one that does
    /// not start with it, a status line with no line ending, a status code that reads as zero, a
    /// header line with no colon, and an empty key. NOT an empty value - libchiaki has a branch
    /// for that which cannot be reached, and this agrees with it rather than with its intent.
    /// </summary>
    public static (int Code, IReadOnlyList<HttpHeader> Headers)? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length < VersionPrefix.Length || !text.StartsWith(VersionPrefix, StringComparison.Ordinal))
            return null;

        int at = VersionPrefix.Length;
        int lineEnd = text.IndexOf('\n', at);
        if (lineEnd < 0)
            return null;

        // The status line must be followed by something. libchiaki checks buf_size <= line_length,
        // so a response that ends with its status line is refused rather than returned bare.
        if (lineEnd + 1 >= text.Length)
            return null;

        // strtol stops at the first non-digit, and a code of zero is how libchiaki spells "this
        // was not a status line" - so "HTTP/1.1 OK" and "HTTP/1.1 0 x" are both refused.
        int code = 0;
        int digits = 0;
        while (at < lineEnd && char.IsAsciiDigit(text[at]))
        {
            code = code * 10 + (text[at] - '0');
            at++;
            digits++;
        }

        if (digits == 0 || code == 0)
            return null;

        return ParseHeaders(text[(lineEnd + 1)..], code);
    }

    private static (int, IReadOnlyList<HttpHeader>)? ParseHeaders(string rest, int code)
    {
        var headers = new List<HttpHeader>();
        int keyStart = 0;
        int valueStart = -1;

        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '\0')
                break;

            if (valueStart < 0)
            {
                if (c == ':')
                {
                    if (keyStart == i)
                        return null;

                    int afterColon = i + 1;
                    if (afterColon >= rest.Length)
                        return null;

                    // One space, not a run of them.
                    if (rest[afterColon] == ' ')
                        afterColon++;
                    if (afterColon >= rest.Length)
                        return null;

                    headers.Add(new HttpHeader(rest[keyStart..i], string.Empty));
                    valueStart = afterColon;

                    // Resume PAST the first character of the value, not at it. libchiaki advances
                    // its cursor when it takes the colon and then the loop advances it again, so
                    // the byte the value starts on is never examined - which is why "Key:\n"
                    // parses as a success with no headers rather than as the empty value its own
                    // FAIL branch was written for. That branch cannot fire, and reproducing the
                    // skip is what makes this parser agree.
                    i = afterColon;
                }
                else if (c is '\r' or '\n')
                {
                    // A blank line is skipped; a line with content and no colon is refused.
                    if (keyStart + 1 < i)
                        return null;
                    keyStart = i + 1;
                }
            }
            else if (c is '\r' or '\n')
            {
                headers[^1] = headers[^1] with { Value = rest[valueStart..i] };
                keyStart = i + 1;
                valueStart = -1;
            }
        }

        // A header whose value never met a line ending was never recorded by libchiaki either -
        // the entry is only created at the terminator. Dropping the half-built one here is what
        // reproduces that.
        if (valueStart >= 0)
            headers.RemoveAt(headers.Count - 1);

        // Prepended by libchiaki, so the last header parsed is the first one out.
        headers.Reverse();
        return (code, headers);
    }
}
