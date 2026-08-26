using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP358: every response parser in lib/src matches header names without regard to case.
///
/// PP296 established this for parse_session_response and gave the reason: an HTTP field name is
/// case-insensitive, and a console spelling one otherwise was the defect it was filed for. It
/// changed that function and did not reach parse_ctrl_response, thirty lines of the same kind of
/// code away, which went on matching RP-Server-Type and RP-Prohibit with strcmp.
///
/// The consequence there was not an error. server_type_valid stayed false, the log said "No valid
/// Server Type in ctrl response", and the connect carried on WITHOUT the two downgrades that branch
/// performs - so a regular PS4 was asked for 1080p and for H265, neither of which it supports. A
/// console that answered correctly, in a spelling nobody allowed, and a log line blaming it.
///
/// SO THE CHECK IS OVER EVERY PARSER, not the one that was wrong. PP296 asserted its own function
/// and the rule was broken next door for as long as anybody looked - which is the same lesson
/// PP348 learned about the quit reason, and the reason this one is written wider.
/// </summary>
public static partial class HeaderMatching
{
    /// <summary>The files that parse a response into named headers.</summary>
    public static IReadOnlyList<string> Parsers { get; } =
        [@"lib\src\session.c", @"lib\src\ctrl.c"];

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Every header-name comparison made case-sensitively.
    ///
    /// Found by shape: a strcmp whose second argument is a quoted string starting with a capital
    /// letter and containing a dash, which is what an HTTP field name looks like and what a protocol
    /// constant does not. RP-Version, RP-Nonce, RP-Server-Type all match; a strcmp against
    /// "Server shutting down" or a path does not.
    /// </summary>
    /// <returns>The comparison text of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> CaseSensitiveHeaderComparisons(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match comparison in SensitiveCompare().Matches(source))
            found.Add(comparison.Value.Trim());

        return found;
    }

    // strcmp, not strcasecmp, against something shaped like a field name: a capital, then letters
    // and a dash before the closing quote. The negative lookbehind keeps strcasecmp out.
    [GeneratedRegex(@"(?<!case)cmp\s*\([^,]*,\s*""[A-Z][A-Za-z0-9]*-[A-Za-z0-9-]+""\s*\)")]
    private static partial Regex SensitiveCompare();
}
