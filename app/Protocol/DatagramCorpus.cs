using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP608, under PP27: takion datagrams a PS5 actually sent, kept where they cannot be lost again.
///
/// PP601 to PP607 built the way into takion's receive loop and it runs - a real takion connects to
/// loopback and PP606's responder completes its handshake. What it had nothing of was traffic.
/// PP516's entry records two captures sitting on disk unread; neither was committed, and neither is
/// on this machine now. That is the whole of why this file is in tests/corpus/ beside PP297's
/// exchange rather than in a log directory: a recording that needs a console to make is not one to
/// keep somewhere a reinstall clears.
///
/// WHAT IS IN IT. 4025 datagrams over a five-second sample of a live session, each row an arrival
/// time in microseconds, the datagram's real length, and its first eighteen bytes. The mean gap is
/// 1159us, against the 1178us PP531 measured its MAC gate inside - so this is the spacing that
/// comparison assumes, taken again.
///
/// HEADS AND NOT PAYLOADS, which is what makes it committable. Eighteen bytes reaches the takion
/// header and stops: the type, the tag, the four MAC bytes, the key position and the chunk type.
/// The tag is drawn per session by chiaki_random_32 (PP602), so it identifies this recording and
/// nothing else - no account, no console, and no frame.
///
/// PP673: INBOUND ONLY, AND TWO TAKION CONNECTIONS. The takion channel's tap emits under
/// CHIAKI_MESSAGE_TAP_RECEIVED at a single site, so every row here arrived rather than left - a
/// reader treating it as a two-way capture is reading a direction the file does not hold. And its
/// 344 control heads carry TWO tags, 333 and eleven: not two directions, but the two takions a PS5
/// session runs, senkusha's and the stream connection's, each with its own random tag. A reader
/// that assumed one tag would call the eleven corrupt.
///
/// The Length column is the DATAGRAM's, not the head's. A capture written before that was true
/// carries the other version string, and TakionCaptureFile refuses it rather than reading sizes
/// that would all be wrong.
/// </summary>
public static class DatagramCorpus
{
    /// <summary>The capture, relative to the repository root.</summary>
    public const string RelativePath = @"tests\corpus\datagrams-ps5-5s.txt";

    /// <summary>How many datagrams it holds.</summary>
    public const int Datagrams = 4025;

    /// <summary>
    /// The mean arrival gap, in microseconds, as the capture reported when it was written.
    ///
    /// Stated so a reader knows what spacing this is without recomputing it, and checked against
    /// the rows so the number cannot drift from the file it describes.
    /// </summary>
    /// <remarks>
    /// PP742: 1201, and it was 1159. The file was recorded again to widen its heads, so every
    /// number derived from it is a number about the new sample - the same console, the same five
    /// seconds, a different five seconds of it. This one is checked against the rows below, which
    /// is what keeps it a claim rather than a decoration.
    /// </remarks>
    public const int MeanGapMicros = 1201;

    /// <summary>
    /// How many bytes of each datagram were kept.
    ///
    /// PP742: twenty-eight, and the file was recorded again to get them. Eighteen reached the MAC
    /// gate and stopped, two bytes short of the cheapest AV layout, so the port's own parser refused
    /// every AV head this capture holds.
    /// </summary>
    public const int HeadBytes = 28;

    /// <summary>The capture, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The datagrams, or null where the file is not a capture this version reads.</summary>
    public static IReadOnlyList<CapturedDatagram>? Read()
    {
        string? path = Locate();
        return path is null ? null : TakionCaptureFile.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// The mean gap between arrivals, computed from the rows rather than taken from the header.
    ///
    /// The point of computing it is that the constant above is then a claim about this file which
    /// can be wrong, rather than a number nobody checks - which is what CountedClaimTests exists to
    /// stop happening in prose and this is the same discipline one file over.
    /// </summary>
    public static double MeanGap(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        if (datagrams.Count < 2)
            return 0;

        long span = datagrams[^1].ArrivalMicroseconds - datagrams[0].ArrivalMicroseconds;
        return (double)span / (datagrams.Count - 1);
    }
}
