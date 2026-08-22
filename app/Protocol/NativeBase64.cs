using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP269: the library's own base64 encoder, reachable so a claim about it can be run.
///
/// PP261 established BY READING that a conversion which does not fit returns its error without
/// writing a terminator, leaving the destination partly written - and that the print beneath the
/// failure branch would therefore run past whatever was written. Reading is what was available; this
/// is the measurement.
///
/// The destination is handed over as the caller filled it, and handed back untouched apart from
/// whatever the encoder did, so what was written can be told from what was left.
/// </summary>
public static class NativeBase64
{
    /// <summary>
    /// The error the library returns when the destination is too small.
    ///
    /// Twelve, because it is the thirteenth member of an enum whose first is zero - counted from
    /// the header rather than guessed, and asserted there by
    /// <see cref="NativeBase64Source.TheErrorCodeIsStillTwelve"/> so a member inserted above it
    /// fails a test rather than turning every comparison here into a different question.
    /// </summary>
    public const int BufferTooSmall = 12;

    /// <summary>And success, which the enum states outright.</summary>
    public const int Success = 0;

    /// <summary>
    /// Runs the real encoder over <paramref name="destination"/>, in place.
    /// </summary>
    /// <param name="source">Bytes to encode.</param>
    /// <param name="destination">
    /// Where they go. Fill it beforehand to tell what the encoder wrote from what it left.
    /// </param>
    /// <returns>The library's own error code.</returns>
    public static int Encode(byte[] source, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        return EncodeNative(source, source.Length, destination, destination.Length);
    }

    /// <summary>
    /// Whether the encoder terminated what it wrote - which is the question PP261 answered by
    /// reading.
    /// </summary>
    public static bool IsTerminated(byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Array.IndexOf(destination, (byte)0) >= 0;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_base64_encode",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EncodeNative(byte[] source, int sourceLength, byte[] destination, int destinationLength);
}

/// <summary>
/// PP269: where the error code's value comes from.
/// </summary>
public static class NativeBase64Source
{
    /// <summary>Where the declaration lives. Named, so PP278's sweep can see it.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\common.h";

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? Locate()
        => ChiakiNg.Session.SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>
    /// Whether the error is still the thirteenth member, counted rather than read off a comment.
    ///
    /// The enum names one value explicitly and lets the rest follow, so the number this port uses
    /// is a POSITION - and a member inserted above it moves every code after it at once.
    /// </summary>
    public static bool TheErrorCodeIsStillTwelve(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        string text = header.Replace("\r\n", "\n", StringComparison.Ordinal);

        int opens = text.IndexOf("CHIAKI_ERR_SUCCESS = 0,", StringComparison.Ordinal);
        int wanted = text.IndexOf("CHIAKI_ERR_BUF_TOO_SMALL", opens < 0 ? 0 : opens, StringComparison.Ordinal);

        if (opens < 0 || wanted < 0)
            return false;

        int position = -1;
        foreach (string line in text[opens..wanted].Split('\n'))
        {
            if (line.Contains("CHIAKI_ERR_", StringComparison.Ordinal))
                position++;
        }

        // The members before it, plus the one that is zero.
        return position + 1 == NativeBase64.BufferTooSmall;
    }
}
