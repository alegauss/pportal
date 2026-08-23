using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP5 and PP88: SanitizeLogMessage, which is what keeps a session log attachable to a bug report.
///
/// Nine rules, and the ORDER is the design. The address rules run first, so a labelled address is
/// already redacted by the time the label rule sees it - and the label rule then replaces the
/// marker too, which is why "console ip: 10.0.0.1" ends as "console ip: &lt;redacted&gt;" and not as
/// "console ip: &lt;redacted-ipv4&gt;". Reordering these is not a tidy-up; it changes what a user
/// pastes into a public issue.
///
/// One text, two clients
/// ---------------------
/// Every pattern below is character-identical to the one in gui/src/sessionlog.cpp, and
/// <see cref="Patterns"/> exists so the selftest can hold them against that file. Two clients that
/// redact differently produce logs that cannot be compared with the ones users already have, and a
/// divergence here is invisible until somebody reads a log looking for something that was supposed
/// to be gone.
///
/// The patterns are deliberately blunt - the last rule redacts any run of sixteen hex digits,
/// which catches console ids and device uids and also catches anything else that looks like one.
/// Over-redaction is the intended failure direction.
/// </summary>
public static partial class SessionLogSanitizer
{
    /// <summary>
    /// PP320: a hexdump row, redacted WHOLE, and it runs before everything else.
    ///
    /// The rules below this one read a log line. chiaki_log_hexdump does not write log lines: it
    /// writes an offset, sixteen bytes as space-separated pairs, and an ASCII gutter of every
    /// printable character. session.c dumps the session request at line 955 and the response header
    /// at 1019, so RP-Registkey and RP-Nonce go through here in two shapes at once - as hex, and as
    /// text in the gutter. Measured against a real row, the long-hex rule never fires because the
    /// pairs are separated, and the labelled-secret rule does not name either field.
    ///
    /// Whole, and not field by field, because a gutter is sixteen characters wide and a secret is
    /// longer than that. "RP-Registkey: 3e" ends one row and the rest of the value continues on the
    /// next with no label above it, where no per-line rule can reach it. PP88 is the precedent and
    /// it is exact: a partial redaction is worse than none, because a reader scanning for leaks
    /// skips a line that says redacted.
    ///
    /// First, so no later rule can mangle a row into something this no longer recognises. Eight
    /// groups rather than sixteen so a short final row is still caught, and the offset column is
    /// left wherever the match does not reach it - it names a position and carries nothing.
    /// </summary>
    private const string HexdumpRowPattern = @"(?:[0-9a-fA-F]{2} ){8,}[^\n]*";

    private const string Ipv4Pattern =
        @"\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b";

    /// <summary>
    /// PP88. The old first alternative was <c>(?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}</c>, which
    /// stops at the first <c>::</c> it meets: the engine takes the leftmost match, so
    /// <c>fd00:1234:5678:9abc::1</c> came out as <c>&lt;redacted-ipv6&gt;::1</c> and the marker made
    /// the line look handled. A partial redaction is worse than none, because a reader scanning for
    /// leaks skips a line that says redacted.
    ///
    /// This matches a whole token: an optional group, then three or more runs of one or two colons
    /// each followed by an optional group. Three is the floor that keeps a clock out of it -
    /// <c>20:10:38</c> is two runs and does not match, while <c>aa:bb:cc:dd</c> is four and does,
    /// exactly as the old pattern did. The second alternative stays for a token whose only
    /// separator is <c>::</c>, which the first cannot reach.
    /// </summary>
    private const string Ipv6Pattern =
        @"(?<![0-9A-Za-z])\[?(?:[0-9A-Fa-f]{0,4}(?::{1,2}[0-9A-Fa-f]{0,4}){3,}|[0-9A-Fa-f]{0,4}::[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4})*)\]?(?![0-9A-Za-z])";

    private const string LabeledSecretPattern =
        @"(((?:console|host|server|session|account|psn|public|remote)\s+(?:id|ip|address)|duid)\s*:\s*)([^\s,;]+)";

    private const string SessionIdTokenPattern = @"(session\s+id\s+)([A-Za-z0-9+/=_-]{8,})";

    private const string UuidPattern =
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b";

    private const string LongHexPattern = @"\b[a-fA-F0-9]{16,}\b";

    private const string AccountIdPattern = @"(account(?:_id)?\s*=\s*)([^\s,;]+)";

    private const string DuidPattern = @"(duid\s*=\s*)([^\s,;]+)";

    private const string SessionIdEqualsPattern = @"(session\s+id\s*=\s*)([^\s,;]+)";

    /// <summary>
    /// Every pattern, so the selftest can hold this file against gui/src/sessionlog.cpp. Order is
    /// the C++ file's declaration order, which is not the order they are applied in.
    /// </summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        HexdumpRowPattern,
        Ipv4Pattern,
        Ipv6Pattern,
        LabeledSecretPattern,
        SessionIdTokenPattern,
        UuidPattern,
        LongHexPattern,
        AccountIdPattern,
        DuidPattern,
        SessionIdEqualsPattern,
    ];

    /// <summary>Applies every rule in sessionlog.cpp's order.</summary>
    public static string Sanitize(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // PP320 first. A row that reaches the rules below arrives already redacted, so none of them
        // can turn it into something this one would no longer match.
        string s = HexdumpRow().Replace(message, "<redacted-hexdump>");

        s = Ipv4().Replace(s, "<redacted-ipv4>");
        s = Ipv6().Replace(s, "<redacted-ipv6>");
        s = LabeledSecret().Replace(s, "$1<redacted>");
        s = AccountId().Replace(s, "$1<redacted>");
        s = Duid().Replace(s, "$1<redacted>");
        s = SessionIdEquals().Replace(s, "$1<redacted>");
        s = SessionIdToken().Replace(s, "$1<redacted>");
        s = Uuid().Replace(s, "<redacted-uuid>");

        // Unlabelled long hex identifiers, which is what a console id looks like in the wild.
        return LongHex().Replace(s, "<redacted-hex>");
    }

    [GeneratedRegex(HexdumpRowPattern)]
    private static partial Regex HexdumpRow();

    [GeneratedRegex(Ipv4Pattern)]
    private static partial Regex Ipv4();

    [GeneratedRegex(Ipv6Pattern)]
    private static partial Regex Ipv6();

    [GeneratedRegex(LabeledSecretPattern, RegexOptions.IgnoreCase)]
    private static partial Regex LabeledSecret();

    [GeneratedRegex(SessionIdTokenPattern, RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdToken();

    [GeneratedRegex(UuidPattern)]
    private static partial Regex Uuid();

    [GeneratedRegex(LongHexPattern)]
    private static partial Regex LongHex();

    [GeneratedRegex(AccountIdPattern, RegexOptions.IgnoreCase)]
    private static partial Regex AccountId();

    [GeneratedRegex(DuidPattern, RegexOptions.IgnoreCase)]
    private static partial Regex Duid();

    [GeneratedRegex(SessionIdEqualsPattern, RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdEquals();
}
