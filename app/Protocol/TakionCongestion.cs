using System.Buffers.Binary;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP749: chiaki_takion_format_congestion, which was the one takion datagram with no managed writer.
///
/// FIFTEEN BYTES: a type byte, three big-endian shorts, four zeroes and the low half of the key
/// position. PP714 wrote the thread that computes the two numbers and nothing could put them on a
/// wire, because this did not exist.
///
/// WORD_0 IS NOT A PARAMETER. congestioncontrol.c declares its packet with a zeroing initialiser
/// and assigns received and lost only, so the first short goes out as zero on every congestion
/// packet a session sends. Taking it as an argument would invite a caller to fill a field the C
/// never fills, and <see cref="TakionCongestionSource.TheFirstWordIsNeverAssigned"/> holds the C to
/// it rather than leaving that as a reading.
/// </summary>
public static class TakionCongestion
{
    /// <summary>CHIAKI_TAKION_CONGESTION_PACKET_SIZE.</summary>
    public const int PacketSize = 0xf;

    /// <summary>TAKION_PACKET_TYPE_CONGESTION, which is the datagram's first byte.</summary>
    public const byte PacketType = 5;

    /// <summary>Where the C's congestioncontrol.c is, for the claim about word_0.</summary>
    public const string ControlRelativePath = @"lib\src\congestioncontrol.c";

    /// <summary>Where the format itself is.</summary>
    public const string TakionRelativePath = @"lib\src\takion.c";

    /// <summary>
    /// Writes one congestion packet.
    /// </summary>
    /// <param name="datagram">Exactly <see cref="PacketSize"/> bytes.</param>
    /// <param name="received">What the stats counted arriving.</param>
    /// <param name="lost">And what they counted missing, after the clamp.</param>
    /// <param name="keyPos">The ledger's position for this packet; only its low half goes out.</param>
    public static void Write(Span<byte> datagram, ushort received, ushort lost, ulong keyPos)
    {
        if (datagram.Length != PacketSize)
            throw new ArgumentException($"a congestion packet is {PacketSize} bytes", nameof(datagram));

        datagram[0] = PacketType;

        // word_0, which the C never assigns.
        BinaryPrimitives.WriteUInt16BigEndian(datagram[1..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(datagram[3..], received);
        BinaryPrimitives.WriteUInt16BigEndian(datagram[5..], lost);
        BinaryPrimitives.WriteUInt32BigEndian(datagram[7..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(datagram[0xb..], (uint)keyPos);
    }
}

/// <summary>
/// PP749: the congestion thread's reports, put on the takion's socket.
///
/// PP714's thread computes a received and a lost every two hundred milliseconds and hands them to
/// <see cref="ICongestionSink"/>. This is what stands behind that seam - the same shape as PP748's
/// message sink, one datagram kind over.
/// </summary>
public sealed class TakionCongestionSink(ManagedTakion takion) : ICongestionSink
{
    /// <summary>How many reports this sink has handed to the takion.</summary>
    public int Offered { get; private set; }

    /// <summary>And how many of those the socket took.</summary>
    public int Sent { get; private set; }

    /// <summary>The last error, so a caller can see why one did not go.</summary>
    public ChiakiError? Last { get; private set; }

    /// <inheritdoc/>
    public void Send(CongestionReport report)
    {
        Offered++;

        ChiakiError sent = takion.SendCongestion(report.Received, report.Lost);
        Last = sent;

        if (sent == ChiakiError.Success)
            Sent++;
    }
}

/// <summary>PP749: what the port copied out of the C, held where it was copied from.</summary>
public static class TakionCongestionSource
{
    /// <summary>congestioncontrol.c, or null outside a checkout.</summary>
    public static string? LocateControl() => Session.SanitizerSource.LocateRelative(
        TakionCongestion.ControlRelativePath);

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateTakion() => Session.SanitizerSource.LocateRelative(
        TakionCongestion.TakionRelativePath);

    /// <summary>
    /// Whether congestioncontrol.c still leaves the packet's first word alone.
    ///
    /// The claim <see cref="TakionCongestion.Write"/> makes by not taking it: the packet is zeroed
    /// at declaration and only received and lost are assigned. The day a third assignment appears,
    /// this port is sending a field the C has started filling.
    ///
    /// THE POSITIVE HALF IS WHAT MAKES IT A CHECK. A bare "does not contain" answers yes to an
    /// empty file, so a source that had gone missing would read as agreement - which is the shape
    /// DriftReadsTheFile exists to refuse. The packet and its two assignments have to be found
    /// before their absence means anything.
    /// </summary>
    public static bool TheFirstWordIsNeverAssigned(string controlSource)
    {
        ArgumentNullException.ThrowIfNull(controlSource);

        string code = Session.CCall.Code(controlSource);

        return code.Contains("ChiakiTakionCongestionPacket packet", StringComparison.Ordinal)
            && code.Contains("packet.received", StringComparison.Ordinal)
            && code.Contains("packet.lost", StringComparison.Ordinal)
            && !code.Contains("word_0", StringComparison.Ordinal);
    }

    /// <summary>And whether the format still writes the fields at the offsets this port uses.</summary>
    public static bool TheOffsetsAreStillThese(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        string? body = Session.CFunction.Body(takionSource, "chiaki_takion_format_congestion");

        return body is not null
            && body.Contains("buf + 1", StringComparison.Ordinal)
            && body.Contains("buf + 3", StringComparison.Ordinal)
            && body.Contains("buf + 5", StringComparison.Ordinal)
            && body.Contains("buf + 7", StringComparison.Ordinal)
            && body.Contains("buf + 0xb", StringComparison.Ordinal);
    }
}
