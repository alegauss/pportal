namespace ChiakiNg.Protocol;

/// <summary>The unit fields of one audio AV head, which is all of it that fits in a kept head.</summary>
/// <param name="UnitIndex">Which unit of the frame this datagram is.</param>
/// <param name="UnitsTotal">units_in_frame_total, which the C stores one less than.</param>
/// <param name="Source">source_units_count - how many NEW frame indices the packet carries.</param>
/// <param name="Fec">fec_units_count - how many earlier indices it repeats.</param>
/// <param name="UnitSize">The bytes of one unit.</param>
/// <param name="Codec">5 for Opus; the receiver refuses anything else.</param>
public readonly record struct AudioUnitCount(
    byte UnitIndex, ushort UnitsTotal, byte Source, byte Fec, byte UnitSize, byte Codec);

/// <summary>
/// PP736: how many frame indices one audio packet covers, measured rather than assumed.
///
/// THE QUESTION WAS AN ARITHMETIC ONE. chiaki_packet_stats_get compares a COUNT of pushes against a
/// SPAN taken from a floor and a ceiling. audioreceiver.c pushes exactly one number per packet - the
/// packet's own frame_index - while the indices it delivers run from that one upward, one per source
/// unit. So the span rises source_units_count times per push, and at two the congestion path would
/// read a clean window as fifty percent lost, every window, for the whole session.
///
/// THE ANSWER IS ONE, and PP736's own design says what that means: "at one unit per packet the two
/// agree and a clean window reports nothing lost". The feared defect does not exist, and this class
/// is what keeps that an answer rather than an assumption.
///
/// MEASURED FROM PP608's CAPTURE, which is real PS5 traffic and the only audio this tree has. Its
/// heads are eighteen bytes and a whole AV parse needs nineteen, so <see cref="AvPacketParse"/>
/// refuses them - but the three counts sit in the dword at byte five, well inside what was kept.
/// That is why this reads the fields directly instead of calling the parser: the parser is right to
/// refuse a head it cannot finish, and this asks a smaller question of the same bytes.
///
/// AND ELEVEN OF THE 450 ARE NOT AUDIO. They carry codec 255, arrive between 37ms and 84ms, and are
/// 548 or 1426 bytes - senkusha's MTU probes on the other of the two takions PP608's capture holds,
/// before the first real audio packet at 282ms. The C's receiver refuses them on the codec, so
/// counting them as audio would put eleven zeroes into a ratio they were never part of.
/// </summary>
public static class AudioUnitCounts
{
    /// <summary>How many bytes of a head this needs: through the codec at byte nine.</summary>
    public const int NeededBytes = 10;

    /// <summary>What PP608's capture holds, of each kind, so a re-recording that differs says so.</summary>
    public const int AudioHeads = 450;

    /// <summary>Of those, the ones the receiver would accept.</summary>
    public const int OpusHeads = 439;

    /// <summary>And the ones it refuses: senkusha's probes, sharing the audio base type.</summary>
    public const int SenkushaProbes = AudioHeads - OpusHeads;

    /// <summary>
    /// source_units_count, on every Opus head in the capture.
    ///
    /// The number PP736 turned on. One means a packet carries one new frame index, so the stats'
    /// count and their span advance together.
    /// </summary>
    public const int MeasuredSource = 1;

    /// <summary>fec_units_count: two earlier indices repeated in every packet.</summary>
    public const int MeasuredFec = 2;

    /// <summary>The bytes of one Opus unit, which is what a frame of sound costs on this link.</summary>
    public const int MeasuredUnitSize = 80;

    /// <summary>Before this arrival the capture is senkusha's takion, in microseconds.</summary>
    public const long FirstOpusMicroseconds = 282204;

    /// <summary>
    /// The unit fields of an audio head, or null where it is not one or is too short.
    /// </summary>
    public static AudioUnitCount? Read(ReadOnlySpan<byte> head)
    {
        if (head.Length < NeededBytes || (head[0] & AvPacketParse.BaseTypeMask) != TakionDispatch.Audio)
            return null;

        // av is rebased past the type byte, so the C's `av + 4` is head[5..].
        uint dword2 = (uint)((head[5] << 24) | (head[6] << 16) | (head[7] << 8) | head[8]);
        ushort unitsFec = (ushort)(dword2 & 0xffff);

        return new AudioUnitCount(
            (byte)((dword2 >> 0x18) & 0xff),
            (ushort)(((dword2 >> 0x10) & 0xff) + 1),
            (byte)(unitsFec & 0xf),
            (byte)((unitsFec >> 4) & 0xf),
            (byte)(unitsFec >> 8),
            head[9]);
    }

    /// <summary>Every audio head in a capture, senkusha's probes included.</summary>
    public static IReadOnlyList<AudioUnitCount> HeadsIn(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        return [.. datagrams.Select(one => Read(one.Head)).Where(one => one is not null).Select(one => one!.Value)];
    }

    /// <summary>The ones the C's audio receiver would take, which is the ratio's population.</summary>
    public static IReadOnlyList<AudioUnitCount> OpusIn(IReadOnlyList<CapturedDatagram> datagrams)
        => [.. HeadsIn(datagrams).Where(one => one.Codec == ManagedAudioReceiver.OpusCodec)];

    /// <summary>
    /// Whether the stats' count and their span advance in the same unit.
    ///
    /// True exactly when every packet carries one source unit. False is PP736's finding arriving:
    /// the congestion path would then report loss proportional to this number on a clean link.
    /// </summary>
    public static bool TheCountAndTheSpanAgree(IReadOnlyList<AudioUnitCount> heads)
    {
        ArgumentNullException.ThrowIfNull(heads);

        return heads.Count > 0 && heads.All(one => one.Source == MeasuredSource);
    }
}
