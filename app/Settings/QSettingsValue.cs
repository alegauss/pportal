using System.Globalization;
using System.Text;

namespace ChiakiNg.Settings;

/// <summary>A QRect as QSettings spells it: `@Rect(x y width height)`.</summary>
public readonly record struct QRectValue(int X, int Y, int Width, int Height);

/// <summary>
/// PP2: how QSettings spells a value in the Windows registry, and how to read it back.
///
/// The Qt client writes everything through QSettings, which on Windows is the registry under
/// HKCU\SOFTWARE\Chiaki\Chiaki. Reading it is a registry read and costs almost nothing - but only
/// once the encodings below are right, and each of them loses something silently when it is
/// wrong. None of them is transcribed from Qt's source: each was read back out of a store a Qt
/// build actually wrote, and the bytes and the strings are in SelfTest.
///
/// The byte-array rules, from a store the Qt client itself wrote
/// -------------------------------------------------------------
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
///
/// The rest of the grammar, from a probe store written by Qt 6.11 and read back
/// ---------------------------------------------------------------------------
/// 4. `@` is the escape character for the whole scheme, so a plain string that begins with one
///    is written with a second: a user whose console nickname is `@home` has `@@home` in the
///    registry. A reader that skips this hands that user a nickname they never typed, on every
///    screen, forever. This is the one rule here that is not about a hardware key, and it is the
///    likeliest of them to be hit.
///
/// 5. A bool is REG_SZ `true` or `false` - lower-case text, not a REG_DWORD. .NET's bool.Parse
///    handles those two and nothing else, so `1` and `0` are accepted here as well: they are
///    what an older store, or a hand-edited one, can hold.
///
/// 6. A double is REG_SZ in the C locale, and it does not keep its point: 1.0 comes back as the
///    text `1`. So a double must be parsed with InvariantCulture and must accept an integer -
///    on a machine whose locale writes 0,05 the naive parse is off by a factor of a hundred.
///
/// 7. An int is REG_DWORD, and .NET hands a DWORD back as a SIGNED int. Qt writes a uint through
///    the same DWORD, so a value above int.MaxValue arrives negative and has to be reinterpreted
///    rather than clamped or rejected.
///
/// 8. Geometry is `@Rect(x y width height)`, space separated, and QSize and QPoint share that
///    grammar with two numbers. Only the rect is read here because only the rect is stored:
///    settings/geometry and settings/stream_geometry.
///
/// 9. A QStringList is REG_MULTI_SZ, a genuinely different type rather than another `@…` text.
///    No preference uses one, so there is no reader for it - but it is refused explicitly below
///    instead of falling through to ToString(), which would hand a caller "System.String[]".
/// </summary>
public static class QSettingsValue
{
    private const string ByteArrayPrefix = "@ByteArray(";
    private const string StringPrefix = "@String(";
    private const string RectPrefix = "@Rect(";

    /// <summary>
    /// The text QSettings wrote, before any of the `@…` grammar is interpreted.
    ///
    /// This is the layer the typed readers below work on, and the reason they are not built on
    /// <see cref="AsString"/>: unescaping has to happen after the typed forms are recognised, or
    /// a string a user typed as `@ByteArray(x)` - stored `@@ByteArray(x)` - would unescape into
    /// something that then decodes as a byte array. Qt resolves the same ambiguity with the same
    /// ordering.
    /// </summary>
    public static string? AsRawText(object? raw) => raw switch
    {
        null => null,
        string s => s,
        // Rule 3. Trailing NULs are QSettings' own padding and are not part of the text.
        byte[] b => Encoding.Unicode.GetString(b).TrimEnd('\0'),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        // Rule 9. A list is not a scalar, and saying so is better than a plausible-looking
        // rendering of one.
        string[] => null,
        _ => raw.ToString(),
    };

    /// <summary>
    /// The string a value means: the raw text with the `@` escape undone, or the contents of an
    /// explicit `@String(…)`.
    ///
    /// A typed value that is not a string - a rect, a byte array - is returned as its literal
    /// text rather than as null, because no caller asks a geometry for its nickname and a null
    /// there would read as a missing key rather than as a mistyped one.
    /// </summary>
    public static string? AsString(object? raw)
    {
        string? text = AsRawText(raw);
        if (text is null || text.Length == 0 || text[0] != '@')
            return text;

        // Rule 4's ordering: the typed forms are recognised on the raw text first. `@Invalid()`
        // is QSettings' spelling of a value that is present and holds nothing.
        if (text.EndsWith(')'))
        {
            if (text.StartsWith(StringPrefix, StringComparison.Ordinal))
                return text[StringPrefix.Length..^1];
            if (text == "@Invalid()")
                return null;
        }

        // Rule 4: one `@` is the escape, so `@@home` is the string `@home`.
        return text.StartsWith("@@", StringComparison.Ordinal) ? text[1..] : text;
    }

    /// <summary>
    /// The bytes a `@ByteArray(…)` value carries, or null when the value is not one.
    ///
    /// Returns null rather than throwing on a value that is simply not a byte array: a store
    /// written by a version that spelled a field differently should read as absent, not as a
    /// crash on somebody's first launch after upgrading.
    /// </summary>
    public static byte[]? AsByteArray(object? raw)
    {
        string? text = AsRawText(raw);
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
        string? text = AsRawText(raw);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Rule 7: a uint, reinterpreted from the signed int a REG_DWORD arrives as rather than
    /// clamped. Qt wrote the same 32 bits; only .NET's reading of the sign differs.
    /// </summary>
    public static uint? AsUInt(object? raw)
    {
        if (raw is int i)
            return unchecked((uint)i);
        string? text = AsRawText(raw);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : null;
    }

    /// <summary>Rule 5: `true`/`false` as Qt writes them, and `1`/`0` as an older store can hold.</summary>
    public static bool? AsBool(object? raw)
    {
        if (raw is int i)
            return i != 0;
        string? text = AsRawText(raw);
        if (text is null)
            return null;
        if (bool.TryParse(text, out bool parsed))
            return parsed;
        return text switch { "1" => true, "0" => false, _ => null };
    }

    /// <summary>
    /// Rule 6: the C locale, and an integer is a legal double. Named for what Qt calls it -
    /// GetZoomFactor and the placebo parameters are all toFloat - but read at double width,
    /// because narrowing is the caller's decision and not the store's.
    /// </summary>
    public static double? AsDouble(object? raw)
    {
        if (raw is int i)
            return i;
        string? text = AsRawText(raw);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Rule 8: `@Rect(x y width height)`. Null for anything that is not one, including a rect
    /// with the wrong number of parts - a window restored to three of its four edges is worse
    /// than a window restored to its default.
    /// </summary>
    public static QRectValue? AsRect(object? raw)
    {
        string? text = AsRawText(raw);
        if (text is null
                || !text.StartsWith(RectPrefix, StringComparison.Ordinal)
                || !text.EndsWith(')'))
            return null;

        string[] parts = text[RectPrefix.Length..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return null;

        var n = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
                return null;
        }
        return new QRectValue(n[0], n[1], n[2], n[3]);
    }
}
