using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP5: SanitizeLogMessage, which is what keeps a session log attachable to a bug report.
///
/// Nine rules, and the ORDER is the design. The address rules run first, so a labelled address is
/// already redacted by the time the label rule sees it - and the label rule then replaces the
/// marker too, which is why "console ip: 10.0.0.1" ends as "console ip: &lt;redacted&gt;" and not as
/// "console ip: &lt;redacted-ipv4&gt;". Reordering these is not a tidy-up; it changes what a user
/// pastes into a public issue.
///
/// The patterns are gui/src/sessionlog.cpp's, character for character, with Qt's "\1" backreference
/// written as .NET's "$1". They are deliberately blunt - the last rule redacts any run of sixteen
/// hex digits, which catches console ids and device uids and also catches anything else that
/// happens to look like one. Over-redaction is the intended failure direction here.
/// </summary>
public static partial class SessionLogSanitizer
{
    /// <summary>Applies every rule in sessionlog.cpp's order.</summary>
    public static string Sanitize(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string s = Ipv4().Replace(message, "<redacted-ipv4>");
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

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"(?<![0-9A-Za-z])\[?(?:(?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{0,4}:){0,7}::(?:[0-9A-Fa-f]{0,4}:){0,7}[0-9A-Fa-f]{0,4})\]?(?![0-9A-Za-z])")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"(((?:console|host|server|session|account|psn|public|remote)\s+(?:id|ip|address)|duid)\s*:\s*)([^\s,;]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabeledSecret();

    [GeneratedRegex(@"(session\s+id\s+)([A-Za-z0-9+/=_-]{8,})", RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdToken();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\b[a-fA-F0-9]{16,}\b")]
    private static partial Regex LongHex();

    [GeneratedRegex(@"(account(?:_id)?\s*=\s*)([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountId();

    [GeneratedRegex(@"(duid\s*=\s*)([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Duid();

    [GeneratedRegex(@"(session\s+id\s*=\s*)([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdEquals();
}
