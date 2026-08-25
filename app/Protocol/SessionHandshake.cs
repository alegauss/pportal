using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP293: the session request and the answer's three headers - the outermost thing session.c does,
/// and the first slice of it the recording can judge.
///
/// THE ORACLE IS THE CAPTURE. PP297 recorded what the C actually sent to a real PS5, so this is not
/// a reading of session_request_fmt reproduced and hoped over: what this builds, redacted the way
/// the recording was redacted, is compared against the bytes that went out. That is the comparison
/// §PP293 says the connect exchange has and takion's timing does not - it can be replayed offline,
/// with no console answering.
///
/// EVERY ODDITY IS UPSTREAM'S AND IS KEPT. The header block mixes "RP-Registkey" with "Rp-Version"
/// in two different casings, sends Content-Length: 0 on a GET, and names the client "remoteplay
/// Windows" with a lowercase r. session.c reads the answer back with strcasecmp and the console
/// does not care, but the port's rule is that behaviour is reproduced and improvements are filed
/// apart - and a request that tidied any of it would be a request the official client never sends.
///
/// THE PATH IS THE ONLY THING THE TARGET DECIDES here. A PS4 on target 8 or 9 gets the old
/// /sce/rp/session; every other PS4 gets /sie/ps4/rp/sess/init; a PS5 gets /sie/ps5/rp/sess/init.
/// </summary>
public static class SessionHandshake
{
    /// <summary>The port session.c connects to, which is not the discovery port.</summary>
    public const int SessionPort = 9295;

    /// <summary>The path for a target, exactly as session.c's three branches choose it.</summary>
    public static string PathFor(ChiakiTarget target) => target switch
    {
        ChiakiTarget.Ps4_8 or ChiakiTarget.Ps4_9 => "/sce/rp/session",
        _ when RpVersion.IsPs5(target) => "/sie/ps5/rp/sess/init",
        _ => "/sie/ps4/rp/sess/init",
    };

    /// <summary>
    /// The registration key as session.c formats it into the header: truncated at the first NUL,
    /// then each remaining byte as two lowercase hex digits.
    ///
    /// Not the same reading as the wake credential, which takes those same bytes as ASCII text and
    /// parses them as a number. One key, two readings, and using either in the other's place
    /// produces a request that is refused for a reason naming neither.
    /// </summary>
    public static string RegistKeyHex(ReadOnlySpan<byte> registKey)
    {
        int end = registKey.IndexOf((byte)0);
        ReadOnlySpan<byte> used = end < 0 ? registKey : registKey[..end];

        var hex = new StringBuilder(used.Length * 2);
        foreach (byte b in used)
            hex.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));

        return hex.ToString();
    }

    /// <summary>
    /// The request head, byte for byte as session_request_fmt renders it.
    /// </summary>
    /// <param name="host">The console's address, which goes into Host untouched.</param>
    /// <param name="port">Normally <see cref="SessionPort"/>; a holepunched session uses another.</param>
    public static string Request(
        ChiakiTarget target, string host, ReadOnlySpan<byte> registKey, int port = SessionPort)
    {
        ArgumentNullException.ThrowIfNull(host);

        string version = RpVersion.StringFor(target)
            ?? throw new ArgumentOutOfRangeException(nameof(target), target, "no RP version for this target.");

        return $"GET {PathFor(target)} HTTP/1.1\r\n"
            + $"Host: {host}:{port}\r\n"
            + "User-Agent: remoteplay Windows\r\n"
            + "Connection: close\r\n"
            + "Content-Length: 0\r\n"
            + $"RP-Registkey: {RegistKeyHex(registKey)}\r\n"
            + $"Rp-Version: {version}\r\n"
            + "\r\n";
    }

    /// <summary>
    /// PP333: the answer, read by the parser the rest of the port reads one with.
    ///
    /// This used to be a reader of its own, written because PP332 needed the recorded answer's
    /// headers to assert against. It was the third in the tree and the worst: HttpResponse.Parse
    /// is PP33's managed replacement for chiaki_http_response_parse and transcribes three rules
    /// from a parser nobody wrote down - reverse order, exactly one space skipped after the colon,
    /// a header with no trailing newline dropped - and the reader here reproduced none of them.
    ///
    /// They agree on every answer in the corpus, which is why it went in green. They part on a
    /// value with two spaces, on a duplicate header and on a reply with no final newline, and the
    /// port would then hold two answers about one response with nothing comparing them.
    ///
    /// So this is a call, not a parser. What it adds is the last step SessionResponse expects: the
    /// three fields a session turns on, out of the headers that parse produced.
    /// </summary>
    /// <returns>Null where the text is not a response libchiaki would have accepted either.</returns>
    public static SessionResponseFields? ReadAnswer(string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (HttpResponse.Parse(response) is not var (code, headers))
            return null;

        return SessionResponse.Parse(code, headers);
    }
}

/// <summary>
/// PP293: the request format held against session.c, so the copy above cannot drift from it.
///
/// Every literal in <see cref="SessionHandshake.Request"/> is one from session_request_fmt, and a
/// header reworded upstream would leave this port sending a request the console has stopped
/// expecting - with the failure arriving as a refusal that names none of it.
/// </summary>
public static class SessionHandshakeSource
{
    /// <summary>Where the format lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Every line of the request format, as session.c writes them.</summary>
    public static IReadOnlyList<string> FormatLines { get; } =
    [
        "\"GET %s HTTP/1.1\\r\\n\"",
        "\"Host: %s:%d\\r\\n\"",
        "\"User-Agent: remoteplay Windows\\r\\n\"",
        "\"Connection: close\\r\\n\"",
        "\"Content-Length: 0\\r\\n\"",
        "\"RP-Registkey: %s\\r\\n\"",
        "\"Rp-Version: %s\\r\\n\"",
    ];

    /// <summary>Whether session.c still declares the format this reproduces.</summary>
    public static bool TheFormatIsStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return FormatLines.All(line => core.Contains(line, StringComparison.Ordinal));
    }

    /// <summary>Whether the three paths are still the ones the target picks between.</summary>
    public static bool ThePathsAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("\"/sce/rp/session\"", StringComparison.Ordinal)
            && core.Contains("\"/sie/ps5/rp/sess/init\"", StringComparison.Ordinal)
            && core.Contains("\"/sie/ps4/rp/sess/init\"", StringComparison.Ordinal);
    }
}
