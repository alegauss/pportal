using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP422: the microphone's audio header, and the two arguments that were the wrong way round.
///
/// <c>chiaki_audio_header_set</c> takes (channels, bits, rate, frame_size).
/// <c>stream_connection_enable_microphone</c> passed 16 and 1. That scans as sixteen bits and one
/// channel, which IS what a microphone is - and the parameter order is the reverse, so what went out
/// announced sixteen channels at one bit.
///
/// THE CAPTURE SETTLED IT. PP396's recording carries the STREAMINFO that call produces, and
/// <c>chiaki_audio_header_save</c> writes bits first: the two bytes on the wire were 01 and 10.
///
/// THE TWO CALLERS DISAGREEING IS THE EVIDENCE. gui/src/streamsession.cpp passes 2 then 16 to the
/// same function. One function, two call sites, opposite orders, and only one of them can be right
/// about what a microphone is.
///
/// AND THE NUMBERS THEMSELVES SAY WHICH IS WHICH, which is what makes this checkable rather than a
/// matter of reading the signature carefully. A microphone has one or two channels and eight or
/// sixteen bits; the ranges do not overlap, so an eight-or-more in the channel slot is a bit depth
/// in the wrong place.
/// </summary>
public static class AudioHeaderArguments
{
    /// <summary>Where the library builds the microphone header it sends.</summary>
    public const string LibRelativePath = @"lib\src\streamconnection.c";

    /// <summary>And where the Qt client builds its own.</summary>
    public const string GuiRelativePath = @"gui\src\streamsession.cpp";

    /// <summary>
    /// The most channels a microphone header can plausibly declare.
    ///
    /// Two, in the Qt client's own call. Eight is the bound because it is below every bit depth a
    /// caller would pass and above every channel count, so a value at or over it in the channel slot
    /// is the bit depth.
    /// </summary>
    public const int TooManyChannelsToBeAChannelCount = 8;

    /// <summary>ChiakiAudioHeader's field order, which is what the signature follows.</summary>
    public static IReadOnlyList<string> FieldOrder { get; } =
        ["channels", "bits", "rate", "frame_size"];

    /// <summary>The library's file, or null outside a checkout.</summary>
    public static string? LocateLib() => SanitizerSource.LocateRelative(LibRelativePath);

    /// <summary>The Qt client's, or null outside a checkout.</summary>
    public static string? LocateGui() => SanitizerSource.LocateRelative(GuiRelativePath);

    /// <summary>
    /// Whether both files pass a channel count where the signature wants one.
    ///
    /// Asked of BOTH, because the two disagreeing is what proved it: a check on one would pass the
    /// day somebody made the other match.
    /// </summary>
    public static bool BothCallersPutChannelsFirst(string libCore, string guiSource)
    {
        ArgumentNullException.ThrowIfNull(libCore);
        ArgumentNullException.ThrowIfNull(guiSource);

        return ChannelsComeFirstIn(libCore) && ChannelsComeFirstIn(guiSource);
    }

    /// <summary>
    /// Whether every <c>chiaki_audio_header_set</c> in one file puts channels before bits.
    ///
    /// A call whose channel argument is not a literal is counted and not judged: a named constant is
    /// not something this can read, and refusing it would make the check about spelling.
    /// </summary>
    public static bool ChannelsComeFirstIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        const string call = "chiaki_audio_header_set(";
        var found = 0;

        for (int at = code.IndexOf(call, StringComparison.Ordinal);
             at >= 0;
             at = code.IndexOf(call, at + call.Length, StringComparison.Ordinal))
        {
            int end = code.IndexOf(')', at);
            if (end < 0)
                return false;

            // header, channels, bits, rate, frame_size
            string[] arguments = code[(at + call.Length)..end].Split(',');
            if (arguments.Length < FieldOrder.Count + 1)
                return false;

            found++;

            if (int.TryParse(
                    arguments[1],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int channels)
                && channels >= TooManyChannelsToBeAChannelCount)
            {
                return false;
            }
        }

        // A file with no call in it has not passed this; it has nothing to say.
        return found >= 1;
    }

    /// <summary>
    /// The header bytes a set of values produces, as <c>chiaki_audio_header_save</c> writes them.
    ///
    /// Bits FIRST, which is the detail that turned the swap into something the wire could show:
    /// the arguments are (channels, bits) and the bytes are (bits, channels), so a reader comparing
    /// the call to the capture has to cross over once.
    /// </summary>
    public static byte[] Save(byte channels, byte bits, uint rate, uint frameSize)
    {
        var header = new byte[HeaderSize];

        header[0] = bits;
        header[1] = channels;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), rate);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(6), frameSize);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0xa), Unknown);

        return header;
    }

    /// <summary>CHIAKI_AUDIO_HEADER_SIZE.</summary>
    public const int HeaderSize = 0xe;

    /// <summary>What chiaki_audio_header_set always writes into the unnamed trailing field.</summary>
    public const uint Unknown = 1;

    /// <summary>The microphone header the library sends, with the arguments the right way round.</summary>
    public static byte[] Microphone() => Save(channels: 1, bits: 16, rate: 48000, frameSize: 480);
}
