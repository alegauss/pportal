using System.Text.RegularExpressions;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP297: redaction that names a field, which is what PP323's tap made possible and the log never
/// could.
///
/// THE LEAK THIS EXISTS FOR
/// ------------------------
/// The session request carries RP-Registkey and the answer carries RP-Nonce. Down the log path both
/// arrived as a chiaki_log_hexdump, and PP320 redacts a hexdump row WHOLE - so they were covered,
/// not by being understood but by being unreadable.
///
/// The tap hands the same bytes over structured, and structure removes that cover. Run
/// SessionLogSanitizer over the session request on its own and RP-Registkey survives only by
/// accident: it is 32 hex characters and LongHexPattern takes any run of 16 or more, so a shorter
/// key is missed. RP-Nonce does not survive at all. A nonce is base64 - '+', '/', '=' and mixed
/// case - and no rule in sessionlog.cpp matches a token of that shape. A recording is a file
/// somebody attaches to an issue, and it would have carried the nonce in the clear.
///
/// SEPARATE FROM SessionLogSanitizer, DELIBERATELY
/// -----------------------------------------------
/// The obvious fix is an eleventh pattern beside the other ten. It is not available: PP88 holds
/// SessionLogSanitizer.Patterns character-for-character against the QRegularExpression literals in
/// gui/src/sessionlog.cpp, so adding one there turns that check red - correctly, because the Qt
/// client's log path does not have this problem and does not need this rule.
///
/// The two answer different questions. That one asks what a line of text may not show; this one
/// asks what a header may not carry, which only something holding a whole message can ask. They
/// compose, and the recorder runs this one first.
///
/// BY NAME AND NOT BY SHAPE, which is the argument. A value is redacted because of the header it
/// sits under, so a key that happens to be short, or a nonce that happens to decode to something
/// hex-shaped, is covered for the same reason as every other value of that field.
///
/// Addresses are NOT here. Host: carries the console's address and SessionLogSanitizer already
/// redacts an IPv4 or IPv6 literal wherever it appears; duplicating that would be a second place
/// the same rule lives, which is what PP88 was filed about.
/// </summary>
public static class SessionHeaderSanitizer
{
    /// <summary>
    /// The headers whose value never reaches a recording.
    ///
    /// Matched case-insensitively because the two ends do not agree with each other: session.c
    /// formats the request with "RP-Registkey" and "Rp-Version" and reads the answer back with
    /// strcasecmp. A list matched exactly would cover the request and miss the reply.
    /// </summary>
    public static IReadOnlyList<string> Secret { get; } = ["RP-Registkey", "RP-Nonce"];

    /// <summary>What replaces the value, matching the marker the log sanitiser writes.</summary>
    public const string Marker = "<redacted>";

    /// <summary>
    /// Built FROM <see cref="Secret"/> rather than written out beside it.
    ///
    /// A [GeneratedRegex] needs a literal pattern, which would mean the names existing twice in this
    /// one file - and a header added to the list but not to the literal is a leak that looks like a
    /// fix. The compile is paid once at class init; this runs a few times per session.
    /// </summary>
    /// <remarks>
    /// The alternation is wrapped in its own (?:…) group. Without it the trailing "\s*:\s*" binds to
    /// the LAST name only, so every other header matched its name alone, the colon fell into the
    /// value group, and the line came back as "RP-Registkey&lt;redacted&gt;" - redacted, and no
    /// longer a header. The value went either way, which is why only an assertion on the surviving
    /// field found it.
    /// </remarks>
    private static readonly Regex SecretHeader = new(
        @"^((?:" + string.Join("|", Secret.Select(Regex.Escape)) + @")\s*:\s*)([^\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Every secret header's value replaced with the marker; the rest of the message untouched.
    ///
    /// The header NAME survives. A recording is read to find out what the exchange looked like, and
    /// "RP-Nonce: &lt;redacted&gt;" says a nonce was there where a blanked line says nothing - and a
    /// replay comparing two recordings needs the field to line up.
    /// </summary>
    public static string Sanitize(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return SecretHeader.Replace(message, "$1" + Marker);
    }
}
