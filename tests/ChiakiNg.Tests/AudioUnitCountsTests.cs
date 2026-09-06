using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP736: the units-per-packet ratio, measured off PP608's capture rather than reasoned about.
///
/// The filing said the arithmetic "either collapses to zero or becomes the loudest finding in the
/// congestion path", and that nothing in the tree stated the number. It does now: one source unit
/// per packet, on all 439 Opus heads a PS5 actually sent, so the packet stats' count and their span
/// advance together and a clean window reports nothing lost.
///
/// PP740 is why this could be asked at all - the receiver it ported is what says which of these
/// heads the C would accept, and eleven of them it would not.
/// </summary>
public class AudioUnitCountsTests(ITestOutputHelper output)
{
    private static IReadOnlyList<CapturedDatagram>? Capture() => DatagramCorpus.Read();

    /// <summary>
    /// THE RATIO IS ONE, which is the whole of PP736's answer.
    ///
    /// Asserted over every Opus head rather than a sampled one: a capture in which most packets
    /// carry one unit and a few carry two would still break the stats, and an average would hide it.
    /// </summary>
    [Fact]
    public void EveryOpusPacketCarriesOneSourceUnit()
    {
        if (Capture() is not { } datagrams)
            return;

        IReadOnlyList<AudioUnitCount> opus = AudioUnitCounts.OpusIn(datagrams);

        output.WriteLine($"{opus.Count} Opus head(s) of {AudioUnitCounts.HeadsIn(datagrams).Count} audio");

        Assert.Equal(AudioUnitCounts.OpusHeads, opus.Count);
        Assert.All(opus, one => Assert.Equal(AudioUnitCounts.MeasuredSource, one.Source));
        Assert.True(AudioUnitCounts.TheCountAndTheSpanAgree(opus));
    }

    /// <summary>
    /// And the other two fields, because the ratio alone would not say what a packet IS.
    ///
    /// Two FEC units means the redundancy arm PP740 ported fires on every real packet rather than
    /// being a path the port has never run - so the frame indices it derives are load-bearing.
    /// </summary>
    [Fact]
    public void EveryOpusPacketRepeatsTwoEarlierFramesInEightyByteUnits()
    {
        if (Capture() is not { } datagrams)
            return;

        IReadOnlyList<AudioUnitCount> opus = AudioUnitCounts.OpusIn(datagrams);

        Assert.All(opus, one =>
        {
            Assert.Equal(AudioUnitCounts.MeasuredFec, one.Fec);
            Assert.Equal(AudioUnitCounts.MeasuredUnitSize, one.UnitSize);

            // The C refuses a packet whose two counts disagree with the total beside them.
            Assert.Equal(one.Source + one.Fec, one.UnitsTotal);
        });
    }

    /// <summary>
    /// THE ELEVEN THAT ARE NOT AUDIO, excluded by the receiver's own rule and not by a hand-list.
    ///
    /// They share the audio base type and carry codec 255. PP608's capture holds two takions, and
    /// these are senkusha's MTU probes: 548 or 1426 bytes, all of them arriving before the first
    /// Opus head. Counting them would put eleven zero-source packets into the ratio.
    /// </summary>
    [Fact]
    public void TheSenkushaProbesShareTheAudioTypeAndAreRefusedOnTheCodec()
    {
        if (Capture() is not { } datagrams)
            return;

        CapturedDatagram[] probes =
        [
            .. datagrams.Where(one =>
                AudioUnitCounts.Read(one.Head) is { } head && head.Codec != ManagedAudioReceiver.OpusCodec),
        ];

        output.WriteLine(string.Join(", ", probes.Select(one => $"{one.ArrivalMicroseconds}us/{one.Length}B")));

        Assert.Equal(AudioUnitCounts.SenkushaProbes, probes.Length);
        Assert.All(probes, one => Assert.Contains(one.Length, (int[])[548, 1426]));

        long firstOpus = datagrams
            .Where(one => AudioUnitCounts.Read(one.Head) is { Codec: ManagedAudioReceiver.OpusCodec })
            .Min(one => one.ArrivalMicroseconds);

        Assert.Equal(AudioUnitCounts.FirstOpusMicroseconds, firstOpus);
        Assert.All(probes, one => Assert.True(one.ArrivalMicroseconds < firstOpus));
    }

    /// <summary>
    /// And the receiver really refuses one, so the exclusion above is the C's rule and not a filter.
    /// </summary>
    [Fact]
    public void TheReceiverRefusesAProbeOnItsCodec()
    {
        if (Capture() is not { } datagrams)
            return;

        AudioUnitCount probe = Assert.Single(
            AudioUnitCounts.HeadsIn(datagrams).Where(one => one.Codec != ManagedAudioReceiver.OpusCodec).Take(1));

        var receiver = new ManagedAudioReceiver(new Silent());

        AudioIntake verdict = receiver.AvPacket(
            new AvPacket(
                IsVideo: false,
                PacketIndex: 0,
                FrameIndex: 0,
                UnitIndex: probe.UnitIndex,
                UnitsInFrameTotal: probe.UnitsTotal,
                UnitsInFrameFec: 0,
                Codec: probe.Codec,
                AdaptiveStreamIndex: 0,
                KeyPos: 0,
                DataOffset: 0,
                DataSize: 1),
            [0]);

        Assert.Equal(AudioIntake.UnknownCodec, verdict);
    }

    /// <summary>A head that is not audio, or is shorter than the fields, answers nothing.</summary>
    [Fact]
    public void ANonAudioOrShortHeadIsNotRead()
    {
        // Base type 2 is video, and 0 is control.
        Assert.Null(AudioUnitCounts.Read([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Null(AudioUnitCounts.Read([0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        // Audio, but one byte short of the codec.
        Assert.Null(AudioUnitCounts.Read([0x03, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Null(AudioUnitCounts.Read([]));

        Assert.NotNull(AudioUnitCounts.Read([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    /// <summary>
    /// The three counts come out of the dword the C packs them into, at the offset it packs them at.
    /// </summary>
    [Fact]
    public void TheFieldsAreReadFromTheDwordAtByteFive()
    {
        // 0x00 02 50 21: unit_index 0, units_total 2+1=3, units_in_frame_fec 0x5021.
        AudioUnitCount? read = AudioUnitCounts.Read([0x03, 0, 0, 0, 0, 0x00, 0x02, 0x50, 0x21, 5]);

        Assert.NotNull(read);
        Assert.Equal(0, read.Value.UnitIndex);
        Assert.Equal(3, read.Value.UnitsTotal);
        Assert.Equal(1, read.Value.Source);
        Assert.Equal(2, read.Value.Fec);
        Assert.Equal(0x50, read.Value.UnitSize);
        Assert.Equal(5, read.Value.Codec);
    }

    /// <summary>A sink that keeps nothing, for the tests that only want the verdict.</summary>
    private sealed class Silent : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }
}
