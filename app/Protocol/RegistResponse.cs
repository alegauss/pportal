using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// What a console sends back when it accepts a registration.
///
/// NOT ChiakiNg.Settings.RegisteredHost, which is the console as the registry stores it. This is
/// the wire form it arrives in, and the two carry different fields for different reasons.
/// </summary>
/// <param name="Nickname">The console's name, as the user sees it.</param>
/// <param name="RegistKey">The credential a later session identifies itself with.</param>
/// <param name="RpKey">The 16-byte key the session crypto is seeded from.</param>
/// <param name="ServerMac">Six bytes identifying the console on the network.</param>
/// <param name="RpKeyType">RP-KeyType, whose base is decided by the text itself.</param>
/// <param name="ApSsid">The access point fields, which a console on its own network reports.</param>
public readonly record struct RegistResponseFields(
    string? Nickname, byte[]? RegistKey, byte[]? RpKey, byte[]? ServerMac,
    uint RpKeyType, string? ApSsid, string? ApBssid, string? ApKey, string? ApName)
{
    /// <summary>
    /// Whether the response is usable, which the C decides by three flags rather than by the count.
    /// </summary>
    public bool Complete => RegistKey is not null && RpKey is not null && ServerMac is not null;
}

/// <summary>
/// PP29: the registration response, which is the only time a console hands over a key.
///
/// Registering happens once per console and everything afterwards depends on what comes back here -
/// the registration key a session presents, the RP key its crypto is seeded from, and the MAC that
/// identifies the console on the network. There is no second chance to read it: a field dropped
/// here is a console that has to be registered again, and the user is told nothing except that it
/// did not work.
///
/// Three header names change with the family
/// -----------------------------------------
/// PS5-Nickname, PS5-RegistKey and PS5-Mac against PS4- for the same three. The family comes from
/// the target the request was made with and NOT from the response - the same shape PP296 records
/// for the session's RP-Version, and the same consequence: a port that guessed would read a PS5's
/// answer as a PS4 with no fields at all.
///
/// RP-KeyType's base is decided by the text
/// ----------------------------------------
/// strtoul with base ZERO, which means C's own literal rules: 0x10 is sixteen, 010 is EIGHT, and 10
/// is ten. session.c's RP-Application-Reason is base 16 unconditionally (PP293), so the two headers
/// in the two exchanges parse the same-looking value differently, and a port sharing one helper
/// between them would be wrong about one of them.
///
/// The three hex fields do not check their lengths the same way
/// ------------------------------------------------------------
/// rp_key and server_mac are rejected unless they fill the buffer exactly. rp_regist_key is not -
/// it is accepted at any length the buffer can hold, and a short one leaves the rest zeroed.
/// Reproduced rather than tidied: refusing a short registration key would refuse consoles the C
/// accepts today.
/// </summary>
public static class RegistResponse
{
    /// <summary>How long a registration key can be.</summary>
    public const int RegistKeySize = 16;

    /// <summary>And the exact lengths the other two must be.</summary>
    public const int RpKeySize = 16;

    /// <summary>Six bytes.</summary>
    public const int MacSize = 6;

    /// <summary>The header carrying the console's name, for a family.</summary>
    public static string NicknameHeader(bool isPs5) => isPs5 ? "PS5-Nickname" : "PS4-Nickname";

    /// <summary>The registration key's.</summary>
    public static string RegistKeyHeader(bool isPs5) => isPs5 ? "PS5-RegistKey" : "PS4-RegistKey";

    /// <summary>And the MAC's.</summary>
    public static string MacHeader(bool isPs5) => isPs5 ? "PS5-Mac" : "PS4-Mac";

    /// <summary>Reads a registration response's headers.</summary>
    public static RegistResponseFields Parse(ChiakiTarget target, IReadOnlyList<HttpHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        bool isPs5 = RpVersion.IsPs5(target);
        string? nickname = null, apSsid = null, apBssid = null, apKey = null, apName = null;
        byte[]? registKey = null, rpKey = null, mac = null;
        uint keyType = 0;

        foreach (HttpHeader header in headers)
        {
            // Ordinal throughout - the C is strcmp for every one of these. PP296 is the note about
            // what that costs, and it costs the same here.
            if (header.Key == "AP-Ssid")
                apSsid = header.Value;
            else if (header.Key == "AP-Bssid")
                apBssid = header.Value;
            else if (header.Key == "AP-Key")
                apKey = header.Value;
            else if (header.Key == "AP-Name")
                apName = header.Value;
            else if (header.Key == NicknameHeader(isPs5))
                nickname = header.Value;
            else if (header.Key == RegistKeyHeader(isPs5))
            {
                // No exact-length check, deliberately - see the class note.
                registKey = ParseHex(header.Value, RegistKeySize, exact: false);
            }
            else if (header.Key == "RP-KeyType")
                keyType = ParseAutoBase(header.Value);
            else if (header.Key == "RP-Key")
                rpKey = ParseHex(header.Value, RpKeySize, exact: true);
            else if (header.Key == MacHeader(isPs5))
                mac = ParseHex(header.Value, MacSize, exact: true);
        }

        return new RegistResponseFields(nickname, registKey, rpKey, mac, keyType, apSsid, apBssid, apKey, apName);
    }

    /// <summary>
    /// parse_hex, and what the C does with a value it cannot use.
    /// </summary>
    /// <returns>
    /// The bytes, zero-padded to <paramref name="size"/>, or null where the value is not hex, is
    /// too long, or - when <paramref name="exact"/> - does not fill the buffer.
    /// </returns>
    public static byte[]? ParseHex(string? value, int size, bool exact)
    {
        if (value is null || value.Length % 2 != 0 || value.Length / 2 > size)
            return null;

        var bytes = new byte[size];
        for (int i = 0; i < value.Length; i += 2)
        {
            if (!byte.TryParse(value.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return null;

            bytes[i / 2] = b;
        }

        return !exact || value.Length / 2 == size ? bytes : null;
    }

    /// <summary>
    /// strtoul with base 0: the text decides. 0x is hex, a leading 0 is OCTAL, anything else is ten.
    /// </summary>
    public static uint ParseAutoBase(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        string text = value.Trim();
        bool negate = text.StartsWith('-');
        if (negate || text.StartsWith('+'))
            text = text[1..];

        int radix = 10;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            radix = 16;
            text = text[2..];
        }
        else if (text.Length > 1 && text[0] == '0')
        {
            radix = 8;
            text = text[1..];
        }

        uint result = 0;
        foreach (char c in text)
        {
            int digit = Uri.IsHexDigit(c) ? Convert.ToInt32(c.ToString(), 16) : -1;
            if (digit < 0 || digit >= radix)
                break;

            result = unchecked((result * (uint)radix) + (uint)digit);
        }

        return negate ? unchecked(0u - result) : result;
    }

    /// <summary>PP29: whether the C still names the three headers by family.</summary>
    public static bool TheHeadersAreStillPerFamily(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("ps5 ? \"PS5-Nickname\" : \"PS4-Nickname\"", StringComparison.Ordinal)
            && core.Contains("ps5 ? \"PS5-RegistKey\" : \"PS4-RegistKey\"", StringComparison.Ordinal)
            && core.Contains("ps5 ? \"PS5-Mac\" : \"PS4-Mac\"", StringComparison.Ordinal);
    }

    /// <summary>And whether RP-KeyType still lets the text choose its base.</summary>
    public static bool TheKeyTypeStillAutoDetectsItsBase(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("host->rp_key_type = (uint32_t)strtoul(header->value, NULL, 0);", StringComparison.Ordinal);
    }

    /// <summary>And whether the registration key is still the one hex field with no length check.</summary>
    public static bool TheRegistKeyStillHasNoLengthCheck(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // The other two compare buf_size against the field; this one only tests the error.
        return core.Contains("err != CHIAKI_ERR_SUCCESS || buf_size != sizeof(host->rp_key)", StringComparison.Ordinal)
            && core.Contains("err != CHIAKI_ERR_SUCCESS || buf_size != sizeof(host->server_mac)", StringComparison.Ordinal);
    }
}
