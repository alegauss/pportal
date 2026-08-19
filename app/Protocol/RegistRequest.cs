using System.Text;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the registration request head, formatted here instead of by regist.c's snprintf.
///
/// This is the request side of the two dependencies that leave. It is a small thing to translate
/// and an easy one to translate wrongly, because the bytes are not what a reader would write: the
/// request line carries "HTTP/1.1" TWICE.
///
/// <code>
///   POST /sie/ps5/rp/sess/rgst HTTP/1.1\r\n HTTP/1.1\r\n
/// </code>
///
/// The second copy makes the next line " HTTP/1.1", a header beginning with a space and with no
/// header before it to fold into. RFC 7230 says a recipient rejects that or replaces the fold
/// with spaces; the console does neither and registers. It has been there since 2020 and is
/// upstream, not this tree's, so it is REPRODUCED and not corrected. Registration is the step
/// between an installed application and a working one, and a request nobody has sent to a console
/// is not an improvement on one that has been sent for six years.
///
/// The formatting rules are read out of regist.c rather than copied here - see
/// <see cref="RegistRequestSource"/> - so this stays a translation rather than a second source.
/// </summary>
public static partial class RegistRequest
{
    /// <summary>The console generations, as regist.c's request_path switch distinguishes them.</summary>
    public enum Path { Ps5, Ps4, Ps4Pre10 }

    /// <summary>The three paths regist.c can POST to.</summary>
    public static string PathFor(Path path) => path switch
    {
        Path.Ps5 => "/sie/ps5/rp/sess/rgst",
        Path.Ps4 => "/sie/ps4/rp/sess/rgst",
        Path.Ps4Pre10 => "/sce/rp/regist",
        _ => throw new ArgumentOutOfRangeException(nameof(path)),
    };

    /// <summary>
    /// The request head, byte for byte as regist.c writes it.
    ///
    /// <paramref name="rpVersion"/> is the RP-Version header's value, or null for a target below
    /// PS4 9.0 - regist.c omits the whole header there rather than sending it empty, and an empty
    /// header is a different request.
    ///
    /// ASCII and not UTF-8: every field here is a path, an address or a version, the C writes
    /// bytes, and a multi-byte character would change a Content-Length that is counted in the
    /// payload rather than in this string.
    /// </summary>
    public static byte[] Head(Path path, string localAddress, ulong payloadSize, string? rpVersion)
    {
        ArgumentNullException.ThrowIfNull(localAddress);

        var sb = new StringBuilder();

        // The doubled version is deliberate. See the note above; RegistRequestSource asserts that
        // regist.c still spells it this way, so correcting it upstream shows up here as a failure
        // rather than as two clients that disagree.
        sb.Append("POST ").Append(PathFor(path)).Append(" HTTP/1.1\r\n HTTP/1.1\r\n");
        sb.Append("HOST: ").Append(localAddress).Append("\r\n");
        sb.Append("User-Agent: remoteplay Windows\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("Content-Length: ").Append(payloadSize).Append("\r\n");

        if (rpVersion is not null)
            sb.Append("RP-Version: ").Append(rpVersion).Append("\r\n");

        // The blank line that ends the head. regist.c memcpy's it with its NUL and then steps the
        // cursor back over it, so the byte is written into the buffer and not counted - which is
        // why the C's length is what it is and why nothing here appends one.
        sb.Append("\r\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}

/// <summary>
/// PP33: regist.c's own formatting strings, so <see cref="RegistRequest"/> is held against the
/// file it translates rather than against a memory of it.
///
/// Two of the things checked here are defects being reproduced on purpose, and that is exactly
/// why they are checked: a faithful copy of a defect is indistinguishable from a mistake unless
/// something says which it is.
/// </summary>
public static partial class RegistRequestSource
{
    /// <summary>The file being translated.</summary>
    public const string RelativePath = @"lib\src\regist.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the request line still carries HTTP/1.1 twice, the second time behind a space.
    ///
    /// If upstream ever fixes it, this fails - and it should, because at that moment the port's
    /// faithful copy becomes the divergence and the two clients start sending different requests
    /// to the same console.
    /// </summary>
    public static bool RequestLineIsStillDoubled(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return DoubledVersionRegex().IsMatch(text);
    }

    /// <summary>The three request paths, in the order regist.c declares them.</summary>
    public static IReadOnlyList<string> Paths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. PathRegex().Matches(text).Select(m => m.Groups[1].Value)];
    }

    /// <summary>Whether a header the port emits is spelled the same way in the C.</summary>
    public static bool Declares(string text, string headerLine)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(headerLine);
        return text.Contains('"' + headerLine + @"\r\n" + '"', StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether request_header_format still bounds its writes with payload_size, the size of the
    /// BODY, rather than with buf_size, the capacity of the buffer it is writing into.
    ///
    /// It does, in two places, and both are dead as written: the caller passes a 0x100 header
    /// buffer and a payload_size that is always 0x1e0 or more, so a guard that trips at ~500 can
    /// never fire for a buffer of 256. What actually catches a truncated header is the caller's
    /// own check against sizeof(request_header) - which runs after the memcpy those guards were
    /// meant to protect.
    ///
    /// Recorded rather than repaired, for the same reason as PP107: this port does not edit lib/.
    /// The managed side has no fixed buffer to overrun, so it inherits the bytes and not the bug -
    /// and asserting the C still reads this way is what stops that claim going stale.
    /// </summary>
    public static bool GuardsUseThePayloadSize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("if(cur < 0 || cur >= payload_size)", StringComparison.Ordinal)
            && text.Contains("if(cur + tail_size > payload_size)", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"""POST %s HTTP/1\.1\\r\\n HTTP/1\.1\\r\\n""")]
    private static partial Regex DoubledVersionRegex();

    [GeneratedRegex(@"request_path_[a-z0-9_]+\s*=\s*""([^""]+)""")]
    private static partial Regex PathRegex();
}
