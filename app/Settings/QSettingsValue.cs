using System.Text;

namespace ChiakiNg.Settings;

/// <summary>
/// PP2: how QSettings spells a value in the Windows registry, and how to read it back.
///
/// The Qt client writes everything through QSettings, which on Windows is the registry under
/// HKCU\SOFTWARE\Chiaki\Chiaki. Reading it is a registry read and costs almost nothing - but only
/// once the three encodings below are right, and each of them loses a user's registration
/// silently when it is wrong. They are transcribed from a real store on a machine that has run
/// the Qt client, not from Qt's source, and the bytes are in SelfTest.
///
/// 1. A QByteArray becomes the text `@ByteArray(` + payload + `)`, where every payload byte is
///    one UTF-16 code unit with a zero high byte. That is Latin-1, not UTF-8: byte 0xFC is the
///    single char U+00FC, and decoding the string as UTF-8 would turn it into something longer
///    and wrong.
///
/// 2. The payload may itself contain `)`. A real server_mac here is 90 47 48 82 FC 29 - its last
///    byte IS `)` - so the string ends `))`, and a parser that stops at the first `)` returns a
///    five-byte MAC address and no error. The terminator is the LAST `)`.
///
/// 3. A QByteArray containing a NUL cannot be a REG_SZ, so QSettings writes REG_BINARY holding
///    the UTF-16LE bytes of that same `@ByteArray(…)` text. A real rp_regist_key is
///    `3e91107c` followed by eight NULs, and it arrives as 40 00 42 00 79 00 … - which reads as
///    a 40-byte key rather than a 16-byte one if the outer layer is not peeled first.
/// </summary>
public static class QSettingsValue
{
    private const string ByteArrayPrefix = "@ByteArray(";

    /// <summary>
    /// The text a registry value stands for, whichever of the two ways QSettings wrote it.
    /// REG_BINARY carrying UTF-16LE text is unwrapped here so callers see one representation.
    /// </summary>
    public static string? AsString(object? raw) => raw switch
    {
        null => null,
        string s => s,
        // Rule 3. Trailing NULs are QSettings' own padding and are not part of the text.
        byte[] b => Encoding.Unicode.GetString(b).TrimEnd('\0'),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => raw.ToString(),
    };

    /// <summary>
    /// The bytes a `@ByteArray(…)` value carries, or null when the value is not one.
    ///
    /// Returns null rather than throwing on a value that is simply not a byte array: a store
    /// written by a version that spelled a field differently should read as absent, not as a
    /// crash on somebody's first launch after upgrading.
    /// </summary>
    public static byte[]? AsByteArray(object? raw)
    {
        string? text = AsString(raw);
        if (text is null || !text.StartsWith(ByteArrayPrefix, StringComparison.Ordinal))
            return null;

        // Rule 2: the last `)`, not the first.
        int end = text.LastIndexOf(')');
        if (end < ByteArrayPrefix.Length)
            return null;

        // Rule 1: one byte per char, low byte only. Latin1 does exactly that and refuses
        // nothing, which is what a payload of arbitrary bytes needs.
        string payload = text[ByteArrayPrefix.Length..end];
        return Encoding.Latin1.GetBytes(payload);
    }

    /// <summary>An int, whether it was stored as REG_DWORD or as text.</summary>
    public static int? AsInt(object? raw)
    {
        if (raw is int i)
            return i;
        string? text = AsString(raw);
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }
}
