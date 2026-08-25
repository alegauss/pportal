using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP326: the control messages whose payload never reaches a recording, named by message type.
///
/// PP325 settled the shape of this for the session channel: a value goes because of the FIELD it
/// sits in, not because of what the bytes happen to look like. The control channel needs the same
/// answer keyed differently, because a ctrl message has no fields to name from outside - it is a
/// decrypted struct. What it has is a type, and the type is enough: <c>SESSION_ID</c>'s payload IS
/// the session id, and <c>LOGIN_PIN_REP</c>'s payload is the PIN the user just typed.
///
/// THE KEYBOARD PAIR IS THE ONE THAT MATTERS MOST and is the least obvious. A
/// <c>KEYBOARD_TEXT_CHANGE_REQ</c> carries text the person typed on the console, which is a
/// password as often as it is a search. Nothing about those bytes looks like a secret; only the
/// type says so.
///
/// THE SIZE GOES TOO, and that is a real cost stated rather than hidden. Recording
/// "&lt;redacted&gt;" without a length means the framing of these six types cannot be checked from
/// a recording, which is work PP297 wanted. Keeping the length would leak how long a password was.
/// The framing is checkable from the other sixteen types, and a password length is not recoverable
/// once written, so the trade goes this way.
///
/// EVERY NUMBER HERE IS A COPY of one in lib/src/ctrl.c, which is why <see cref="DeclaredIn"/>
/// exists: a type renumbered upstream would leave this list redacting a message that no longer
/// carries the secret, and recording the one that now does. That is a leak that reads as a passing
/// test, so it is asserted rather than trusted.
/// </summary>
public static partial class CtrlMessageSecrets
{
    /// <summary>Where the enum this list copies lives, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>What replaces a secret payload. No length: see the note on the class.</summary>
    public const string Marker = "<redacted>";

    /// <summary>
    /// The six, by the name ctrl.c gives them and the value it gives them today.
    ///
    /// Names as well as numbers so <see cref="DeclaredIn"/> can join the two: a check that only had
    /// numbers could say the set was unchanged while every one of them had moved to a different
    /// message.
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> Secret { get; } = new Dictionary<string, ushort>
    {
        // The payload is the session id.
        ["CTRL_MESSAGE_TYPE_SESSION_ID"] = 0x33,

        // The login exchange, and the PIN the user typed into it.
        ["CTRL_MESSAGE_TYPE_LOGIN"] = 0x5,
        ["CTRL_MESSAGE_TYPE_LOGIN_PIN_REQ"] = 0x4,
        ["CTRL_MESSAGE_TYPE_LOGIN_PIN_REP"] = 0x8004,

        // Text typed on the console's keyboard, which is a password as often as it is a search.
        ["CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_REQ"] = 0x23,
        ["CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_RES"] = 0x24,
    };

    /// <summary>The same set as bare values, which is what a recorder holding a type asks with.</summary>
    public static IReadOnlySet<ushort> SecretTypes { get; } = Secret.Values.ToHashSet();

    /// <summary>Whether a message of this type may have its payload recorded.</summary>
    public static bool MayRecord(ushort type) => !SecretTypes.Contains(type);

    /// <summary>
    /// Every <c>CTRL_MESSAGE_TYPE_*</c> ctrl.c declares, name to value, read out of the enum.
    ///
    /// Not a parser for C so much as for the one shape this enum is written in - a name, '=', a
    /// literal, a comma - which is every line of it. A line it cannot read is left out rather than
    /// guessed at, and a name missing from the result is what the assertion above fails on.
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> DeclaredIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var declared = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (Match match in TypeDeclaration().Matches(source))
        {
            string literal = match.Groups[2].Value;

            bool hex = literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (ushort.TryParse(
                    hex ? literal[2..] : literal,
                    hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort value))
            {
                declared[match.Groups[1].Value] = value;
            }
        }

        return declared;
    }

    [GeneratedRegex(@"\b(CTRL_MESSAGE_TYPE_[A-Z0-9_]+)\s*=\s*(0[xX][0-9a-fA-F]+|\d+)")]
    private static partial Regex TypeDeclaration();
}
