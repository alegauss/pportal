using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One control message type the C declares, and where this side answers it.</summary>
/// <param name="CName">The C constant, without its CTRL_MESSAGE_TYPE_ prefix.</param>
/// <param name="Value">The number on the wire.</param>
/// <param name="AnsweredBy">The managed type that models what happens when it arrives or is sent.</param>
/// <param name="Because">What that type answers about it, in a phrase.</param>
public readonly record struct CtrlMessageRow(
    string CName, ushort Value, string AnsweredBy, string Because);

/// <summary>
/// PP440, under PP294: ctrl.c's message types, and where each is answered.
///
/// §PP294 says the message types are the work rather than the line count, and nothing said which of
/// them the port already answers. Finding out cost a session and three readers. Grepping each hex
/// value matched 0x1 inside 0x13 and 0x1fe and reported eleven files for DISPLAYA. Grepping the C
/// CONSTANT NAME reported nine types as unported - GOTO_BED, KEYBOARD_OPEN, GO_HOME, DISPLAYA,
/// DISPLAYB, MIC_CONNECT, MIC_TOGGLE, KEYBOARD_CLOSE_REMOTE and SWITCH_TO_STREAM_CONNECTION - and
/// every one of the nine is answered by a class that does not quote the C name. A hundred per cent
/// false negatives, from a reader that looked reasonable.
///
/// WHAT IT FOUND, ONCE THE READER WAS RIGHT: both enums already carry all 22, with the same values,
/// and NOTHING CHECKED THAT THEY AGREE. That is PP437's shape - two declarations of one wire
/// contract, correct today because of what two files happen to say.
///
/// THE COMPARISON IS BY VALUE, not by name. DISPLAYA maps to DisplayA and GOTO_BED to GotoBed, and a
/// snake-to-Pascal transform gets neither right - but more than that, the number is the contract. A
/// member renamed with its value intact is a refactor; a member whose value moved is a message sent
/// to the wrong handler, and only the second is a defect.
///
/// THE ANSWERED-BY COLUMN IS WHAT MAKES A --part CHOOSABLE. A file this size lands one recorded half
/// at a time, and a half cannot be picked without knowing which parts are already done. Each row is
/// checked by reflection, so a class named here and absent from the assembly is a red test rather
/// than prose that used to be true.
/// </summary>
public static partial class CtrlMessageCensus
{
    /// <summary>Where the C declares them.</summary>
    public const string CtrlRelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? LocateCtrl() => SanitizerSource.LocateRelative(CtrlRelativePath);

    /// <summary>
    /// Every CTRL_MESSAGE_TYPE the C declares, read from the enum rather than listed here.
    ///
    /// Comments stripped first: ctrl.c names these constants in prose as well as in code, and
    /// PP400's rule is that a claim about what a file declares reads the code.
    /// </summary>
    public static IReadOnlyList<(string CName, ushort Value)> Declared(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        return
        [
            .. EnumMemberRegex().Matches(CCall.Code(ctrlSource))
                .Select(match => (
                    match.Groups["name"].Value,
                    ushort.Parse(match.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
        ];
    }

    /// <summary>
    /// The 22, each with the managed type that answers it.
    ///
    /// Every one was verified against a declaration rather than assigned by name similarity: the
    /// microphone rows are CtrlReactions because the burst is where both toggles happen, and the
    /// keyboard rows split between CtrlKeyboard for what this side SENDS and CtrlKeyboardArrivals
    /// for what the console sends us.
    /// </summary>
    public static IReadOnlyList<CtrlMessageRow> Rows { get; } =
    [
        new("SESSION_ID", 0x33, nameof(CtrlReactions),
            "the burst a session id triggers, and the second one being dropped"),
        new("HEARTBEAT_REQ", 0xfe, nameof(CtrlReactions),
            "answered whatever it carries, with nothing between arrival and reply"),
        new("HEARTBEAT_REP", 0x1fe, nameof(CtrlReactions), "the reply this side sends, empty"),
        new("LOGIN_PIN_REQ", 0x4, nameof(CtrlLoginPinRequest),
            "PP411: the prompt, and the refusal when it arrives after the session id"),
        new("LOGIN_PIN_REP", 0x8004, nameof(LoginPinHandover),
            "PP345: the handover, whose allocation failure used to reach the user as a wrong PIN"),
        new("LOGIN", 0x5, nameof(CtrlLoginResult),
            "PP408: the result, including an unsolicited PIN-incorrect raising no prompt"),
        new("GOTO_BED", 0x50, nameof(CtrlSendQueue), "queued with an empty payload"),
        new("KEYBOARD_ENABLE", 0xd, nameof(CtrlReactions), "part of the session-id burst"),
        new("KEYBOARD_ENABLE_TOGGLE", 0x20, nameof(CtrlExchangeParticipant),
            "its one payload byte, as the capture holds it"),
        new("KEYBOARD_OPEN", 0x21, nameof(CtrlKeyboardArrivals),
            "PP409: the open, and its 32-byte header"),
        new("KEYBOARD_CLOSE_REMOTE", 0x22, nameof(CtrlKeyboardArrivals),
            "PP409: indistinguishable from a close this side asked for"),
        new("KEYBOARD_TEXT_CHANGE_REQ", 0x23, nameof(CtrlKeyboard), "the text this side sends"),
        new("KEYBOARD_TEXT_CHANGE_RES", 0x24, nameof(CtrlKeyboardArrivals),
            "PP409: the response, where an empty text arrived as no text at all"),
        new("KEYBOARD_CLOSE_REQ", 0x25, nameof(CtrlKeyboard), "accept and reject, four bytes apart"),
        new("ENABLE_DUALSENSE_FEATURES", 0x13, nameof(CtrlReactions),
            "in the burst only when the session asked for it"),
        new("GO_HOME", 0x14, nameof(CtrlSendResults), "its 0x10 payload and what the send returns"),
        new("DISPLAYA", 0x1, nameof(CtrlDisplay), "the display report this side parses"),
        new("DISPLAYB", 0x16, nameof(CtrlDisplay), "the second shape of the same report"),
        new("MIC_CONNECT", 0x30, nameof(CtrlReactions), "the connect this side sends"),
        new("MIC_TOGGLE", 0x36, nameof(CtrlReactions),
            "sent TWICE in the burst, both times false, 108 microseconds apart in the capture"),
        new("DISPLAY_DEVICES", 0x910, nameof(CtrlDisplay), "the device list, and its empty payload"),
        new("SWITCH_TO_STREAM_CONNECTION", 0x34, nameof(CtrlReactions),
            "acknowledged once; a second arrival is idempotent"),
    ];

    /// <summary>
    /// Where the C enum and this census disagree, as sentences.
    ///
    /// BOTH DIRECTIONS. A type the C declares and this does not is the twenty-third type §PP294
    /// warns about, quietly absent from a rewrite; a row for a type the C no longer declares is a
    /// claim about a message that cannot arrive.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        var declared = Declared(ctrlSource).ToDictionary(t => t.CName, t => t.Value, StringComparer.Ordinal);
        var census = Rows.ToDictionary(r => r.CName, r => r.Value, StringComparer.Ordinal);

        var apart = new List<string>();

        foreach ((string name, ushort value) in declared)
        {
            if (!census.TryGetValue(name, out ushort stated))
                apart.Add($"ctrl.c declares {name} = 0x{value:x} and the census has no row for it");
            else if (stated != value)
                apart.Add($"{name} is 0x{value:x} in ctrl.c and 0x{stated:x} here");
        }

        foreach (string name in census.Keys.Where(name => !declared.ContainsKey(name)))
            apart.Add($"the census has a row for {name}, which ctrl.c no longer declares");

        return apart;
    }

    // CTRL_MESSAGE_TYPE_SESSION_ID = 0x33, - the enum's own shape. The last member carries no
    // trailing comma, so the comma is not part of the match.
    [GeneratedRegex(@"CTRL_MESSAGE_TYPE_(?<name>[A-Z0-9_]+)\s*=\s*0x(?<value>[0-9a-fA-F]+)")]
    private static partial Regex EnumMemberRegex();
}
