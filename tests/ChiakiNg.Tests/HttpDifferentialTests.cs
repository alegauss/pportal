using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the same responses through both HTTP parsers, header by header.
///
/// <see cref="HttpResponse"/> is a translation of <c>chiaki_http_response_parse</c> and the two
/// have the same signature, so they can be run over one corpus and compared - which is what PP33
/// asks for and what found the JSON null (PP183).
///
/// The rows are chosen for where a hand-written parser diverges rather than for looking like
/// traffic: a header with no space after its colon, one with several, a repeated name, an empty
/// value, a line with no colon at all, and the two line endings. A console's own replies are the
/// easy case; these are the ones that only appear when something upstream is not what anybody
/// tested against.
/// </summary>
public class HttpDifferentialTests
{
    public static TheoryData<string> Responses()
    {
        var data = new TheoryData<string>();

        void Add(string text) => data.Add(text);

        // The ordinary shape, so a divergence in the rows below is about the row.
        Add("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        // Spacing around the colon.
        Add("HTTP/1.1 200 OK\r\nA:1\r\n\r\n");
        Add("HTTP/1.1 200 OK\r\nA:   1\r\n\r\n");
        Add("HTTP/1.1 200 OK\r\nA :1\r\n\r\n");
        Add("HTTP/1.1 200 OK\r\nA: 1 \r\n\r\n");

        // An empty value, and a name with nothing after it.
        Add("HTTP/1.1 200 OK\r\nA:\r\n\r\n");
        Add("HTTP/1.1 200 OK\r\nA: \r\n\r\n");

        // A repeated name, which HTTP allows and a dictionary does not.
        Add("HTTP/1.1 200 OK\r\nA: 1\r\nA: 2\r\n\r\n");

        // Case, which HTTP says is insignificant and a parser may or may not fold.
        Add("HTTP/1.1 200 OK\r\ncontent-length: 5\r\nContent-Length: 6\r\n\r\n");

        // A line that is not a header at all.
        Add("HTTP/1.1 200 OK\r\nnot a header\r\nA: 1\r\n\r\n");

        // Status lines: the other version, no reason phrase, a longer one, and other codes.
        Add("HTTP/1.0 200 OK\r\nA: 1\r\n\r\n");
        Add("HTTP/1.1 200\r\nA: 1\r\n\r\n");
        Add("HTTP/1.1 404 Not Found\r\nA: 1\r\n\r\n");
        Add("HTTP/1.1 204 No Content\r\n\r\n");
        Add("HTTP/1.1 500 Internal Server Error\r\n\r\n");

        // Line endings, and a body after the blank line.
        Add("HTTP/1.1 200 OK\nA: 1\n\n");
        Add("HTTP/1.1 200 OK\r\nA: 1\r\n\r\nbody bytes here");

        // No headers at all, and no terminator.
        Add("HTTP/1.1 200 OK\r\n\r\n");
        Add("HTTP/1.1 200 OK\r\nA: 1\r\n");

        // Rubbish, where the question is whether both refuse.
        Add("");
        Add("HTTP/1.1\r\n\r\n");
        Add("nonsense\r\n\r\n");
        Add("HTTP/1.1 abc OK\r\n\r\n");

        return data;
    }

    /// <summary>
    /// Both parsers, one response. Compared as accepted-or-not first, then the code, then the
    /// headers in order - because a parser that kept the right pairs in a different order is a
    /// parser that answers differently to "which one won".
    /// </summary>
    [Theory]
    [MemberData(nameof(Responses))]
    public void BothParsersAnswerAlike(string text)
    {
        (int Code, IReadOnlyList<HttpHeader> Headers)? native = NativeHttp.Parse(text);
        (int Code, IReadOnlyList<HttpHeader> Headers)? managed = HttpResponse.Parse(text);

        Assert.Equal(native is null, managed is null);

        if (native is null || managed is null)
            return;

        Assert.Equal(native.Value.Code, managed.Value.Code);

        Assert.Equal(
            native.Value.Headers.Select(h => $"{h.Key}={h.Value}"),
            managed.Value.Headers.Select(h => $"{h.Key}={h.Value}"));
    }

    /// <summary>
    /// The comparison can tell two responses apart, so the rows above are not passing by asking
    /// nothing of either side.
    /// </summary>
    [Fact]
    public void TheComparisonCanTellTwoResponsesApart()
    {
        (int Code, IReadOnlyList<HttpHeader> Headers)? ok =
            NativeHttp.Parse("HTTP/1.1 200 OK\r\nA: 1\r\n\r\n");
        (int Code, IReadOnlyList<HttpHeader> Headers)? notFound =
            NativeHttp.Parse("HTTP/1.1 404 Not Found\r\nA: 1\r\n\r\n");

        Assert.NotNull(ok);
        Assert.NotNull(notFound);
        Assert.NotEqual(ok.Value.Code, notFound.Value.Code);
    }
}
