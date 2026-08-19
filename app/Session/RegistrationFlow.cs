using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// The four consoles the registration dialog offers, by the number its radio buttons carry.
///
/// The names are the trap. `CHIAKI_TARGET_PS4_8` is 800 and its radio is labelled "PS4 Firmware
/// &lt; 7.0"; `CHIAKI_TARGET_PS4_9` is 900 and reads ">= 7.0, &lt; 8.0"; `CHIAKI_TARGET_PS4_10` is
/// 1000 and reads ">= 8.0". Every constant is one version ahead of the firmware it is for, so a
/// port that picked CHIAKI_TARGET_PS4_8 for a console running firmware 8 would be two rows out.
///
/// Named here for the firmware rather than for the constant, because the firmware is what the user
/// is looking at.
/// </summary>
public enum ConsoleTarget
{
    /// <summary>PS4 below firmware 7.0 - the constant called CHIAKI_TARGET_PS4_8.</summary>
    Ps4Below7 = 800,

    /// <summary>PS4 from 7.0 to below 8.0 - the constant called CHIAKI_TARGET_PS4_9.</summary>
    Ps4From7 = 900,

    /// <summary>PS4 from 8.0 - the constant called CHIAKI_TARGET_PS4_10.</summary>
    Ps4From8 = 1000,

    /// <summary>PS5 - CHIAKI_TARGET_PS5_1.</summary>
    Ps5 = 1000100,
}

/// <summary>Why a registration was refused before it started.</summary>
public enum RegistrationRefusal
{
    /// <summary>It was not.</summary>
    None,

    /// <summary>The account id did not decode to eight bytes.</summary>
    InvalidAccountId,
}

/// <summary>
/// What the dialog hands to the registration, once it is going to happen at all.
/// </summary>
public sealed record RegistrationRequest(
    string Host,
    ConsoleTarget Target,
    bool Broadcast,
    uint Pin,
    uint ConsolePin,
    string? OnlineId,
    byte[]? AccountId);

/// <summary>
/// PP14: the registration flow - what is asked for, what is refused, and what is left behind.
///
/// The four dialogs are not four steps, which is the first thing reading them settles. Only the
/// registration dialog is on this path: the manual host dialog adds a console to the list, the
/// console PIN dialog sets a PIN on one that is ALREADY registered, and the profile dialog is
/// reached from settings. A port that chained them would ask a user for a console PIN in the
/// middle of registering, which is a question they cannot answer yet.
///
/// What is on the path is one branch and one conversion:
///
///   a console the backend knows a DUID for is offered automatic registration first, and only a
///   refusal opens the dialog. Without a DUID there is nothing to offer and the dialog opens;
///
///   and the identifier the dialog collects is a different KIND of thing depending on the console.
///   For a PS4 below firmware 7.0 it is a PSN online id, sent as text. For everything else it is a
///   PSN account id, sent as eight bytes decoded from base64 - and a string that does not decode
///   to exactly eight is refused before anything opens.
///
/// The decode is Qt's and not a strict one - see <see cref="LenientBase64"/>. That is the part
/// worth reading twice, because a strict decoder here refuses account ids that work.
/// </summary>
public static partial class Registration
{
    /// <summary>The account id's length in bytes, and the only length accepted.</summary>
    public const int AccountIdSize = 8;

    /// <summary>The address that means "ask every console on the network".</summary>
    public const string BroadcastHost = "255.255.255.255";

    /// <summary>
    /// Whether the online id field is showing - which is the same question as whether the
    /// identifier is text rather than bytes, and the dialog asks it exactly once.
    /// </summary>
    public static bool WantsOnlineId(ConsoleTarget target) => target == ConsoleTarget.Ps4Below7;

    /// <summary>The other one. Never both, and never neither.</summary>
    public static bool WantsAccountId(ConsoleTarget target) => !WantsOnlineId(target);

    /// <summary>
    /// Whether a console offers automatic registration before the dialog. A DUID is what makes it
    /// possible, and the user still gets to say no.
    /// </summary>
    public static bool OffersAutomatic(string? duid) => !string.IsNullOrEmpty(duid);

    /// <summary>
    /// Builds the request, or refuses it.
    ///
    /// A refusal is not an error state on a dialog that is open - nothing has opened yet. The Qt
    /// call returns false before any progress dialog exists, which is the case PP141's
    /// `Start(accepted: false)` is for.
    /// </summary>
    public static RegistrationRequest? Prepare(
        string host,
        ConsoleTarget target,
        string identifier,
        string pin,
        string consolePin,
        out RegistrationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(consolePin);

        refusal = RegistrationRefusal.None;

        string trimmedHost = host.Trim();
        string trimmedId = identifier.Trim();

        if (WantsOnlineId(target))
        {
            return new RegistrationRequest(
                trimmedHost, target, trimmedHost == BroadcastHost,
                Digits(pin), Digits(consolePin), trimmedId, null);
        }

        byte[] account = LenientBase64.Decode(trimmedId);
        if (account.Length != AccountIdSize)
        {
            refusal = RegistrationRefusal.InvalidAccountId;
            return null;
        }

        return new RegistrationRequest(
            trimmedHost, target, trimmedHost == BroadcastHost,
            Digits(pin), Digits(consolePin), null, account);
    }

    /// <summary>
    /// What a successful registration leaves behind, beyond the registered host itself.
    ///
    /// A console discovery found needs nothing else - it is on the list already, and it will be
    /// there again next time. A console that was typed in has to be written down as a manual host
    /// too, or it registers successfully and then vanishes from the list the moment the dialog
    /// closes. That is the whole of this rule, and it is easy to leave out because the
    /// registration itself succeeded.
    ///
    /// The address kept is the manual host's OWN if it has one, and the typed one only when it has
    /// none - so registering an existing manual host again does not move it.
    /// </summary>
    public static string? SettleManualHost(
        RegistrationRequest request, bool discovered, string? existingManualHost)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (discovered)
            return null;

        return string.IsNullOrEmpty(existingManualHost) ? request.Host : existingManualHost;
    }

    /// <summary>
    /// A PIN as a number. Qt reads it with toULong, which answers 0 for anything it cannot read -
    /// and the console PIN is optional, so 0 is the ordinary answer for an empty field rather than
    /// a failure worth reporting.
    /// </summary>
    public static uint Digits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value)
            ? value
            : 0;
    }
}

/// <summary>
/// PP14: base64 the way QByteArray::fromBase64 does it by default, which is not the way
/// Convert.FromBase64String does it.
///
/// Qt's default decoding SKIPS every character outside the alphabet instead of failing on one, and
/// that includes the padding. Six bits go in per character it recognises, a byte comes out per
/// eight, and whatever is left over at the end is dropped.
///
/// The difference is not academic on this screen. A PSN account id is copied out of a browser or a
/// help page, and it arrives with a newline in the middle or a stray space at a line break. Qt
/// decodes it; a strict decoder answers "Invalid Account-ID" for an id that is perfectly good, and
/// the user has no way to see what is wrong with it.
/// </summary>
public static class LenientBase64
{
    /// <summary>Decodes, skipping anything that is not a base64 digit - padding included.</summary>
    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new List<byte>(text.Length * 3 / 4 + 1);
        int buffer = 0;
        int bits = 0;

        foreach (char c in text)
        {
            int digit = DigitOf(c);
            if (digit < 0)
                continue;

            buffer = (buffer << 6) | digit;
            bits += 6;

            if (bits < 8)
                continue;

            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        // The leftover bits are discarded rather than padded out, which is why the twelve
        // characters an eight-byte id is normally written as - eleven digits and one '=' - come
        // back as eight bytes and not nine.
        return [.. output];
    }

    private static int DigitOf(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a' + 26,
        >= '0' and <= '9' => c - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => -1,
    };

    /// <summary>Standard base64, for writing an id back out. Only the reading side is lenient.</summary>
    public static string Encode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>
/// PP14: the flow's rules as the QML and the backend state them.
/// </summary>
public static partial class RegistrationFlowSource
{
    /// <summary>The QML that decides what opens.</summary>
    public const string MainQml = @"gui\src\qml\Main.qml";

    /// <summary>The list, which is where the console PIN dialog is actually reached from.</summary>
    public const string MainViewQml = @"gui\src\qml\MainView.qml";

    /// <summary>The backend, which decides what the identifier is.</summary>
    public const string Backend = @"gui\src\qmlbackend.cpp";

    /// <summary>One of the three, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether a DUID still means the automatic-registration question comes first.</summary>
    public static bool ADuidOffersAutomaticFirst(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return DuidBranchRegex().IsMatch(qml);
    }

    /// <summary>
    /// Whether the console PIN dialog is still opened from the console list rather than from
    /// registration - the reason the four dialogs are not four steps.
    /// </summary>
    public static bool TheConsolePinDialogIsOpenedFromTheList(string mainViewQml)
    {
        ArgumentNullException.ThrowIfNull(mainViewQml);
        return mainViewQml.Contains("root.showConsolePinDialog(index);", StringComparison.Ordinal);
    }

    /// <summary>Whether the identifier is still text for exactly one target and bytes otherwise.</summary>
    public static bool TheOnlineIdIsForOneTargetOnly(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("if (target == CHIAKI_TARGET_PS4_8) {", StringComparison.Ordinal);
    }

    /// <summary>Whether the eight-byte account id is still checked before anything opens.</summary>
    public static bool TheAccountIdSizeIsCheckedBeforeRegistering(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return AccountSizeRegex().IsMatch(cpp);
    }

    /// <summary>
    /// Whether a console that was not discovered is still written down as a manual host, and
    /// whether the typed address is still only a fallback for one that has none.
    /// </summary>
    public static bool AnUndiscoveredConsoleBecomesAManualHost(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return ManualHostRegex().IsMatch(cpp);
    }

    /// <summary>Whether the broadcast address is still what turns broadcast on.</summary>
    public static bool TheBroadcastAddressIsTheFlag(string registQml)
    {
        ArgumentNullException.ThrowIfNull(registQml);
        return registQml.Contains(
            @"hostField.text.trim() == ""255.255.255.255""", StringComparison.Ordinal);
    }

    /// <summary>
    /// The target each radio button carries, by the label it shows - which is how the constants
    /// being one version ahead of their firmware becomes visible rather than remembered.
    /// </summary>
    public static IReadOnlyList<int> TargetsInDialogOrder(string registQml)
    {
        ArgumentNullException.ThrowIfNull(registQml);
        return [.. TargetRegex().Matches(registQml)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))];
    }

    [GeneratedRegex(@"if\(!duid\)\s*\r?\n\s*showRegistDialog\(host, ps5\);")]
    private static partial Regex DuidBranchRegex();

    [GeneratedRegex(
        @"if \(account_id\.size\(\) != CHIAKI_PSN_ACCOUNT_ID_SIZE\) \{\s*\r?\n\s*emit error")]
    private static partial Regex AccountSizeRegex();

    [GeneratedRegex(
        @"if\(regist_dialog_server\.discovered == false\)[\s\S]{0,200}?if\(manual_host\.GetHost\(\)\.isEmpty\(\)\)\s*\r?\n\s*manual_host\.SetHost\(host\);")]
    private static partial Regex ManualHostRegex();

    [GeneratedRegex(@"property int target: (\d+)")]
    private static partial Regex TargetRegex();
}
