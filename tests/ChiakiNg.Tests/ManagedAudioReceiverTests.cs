using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP740, under PP707: audioreceiver.c managed, and the eight slots PP738's census had nothing for.
///
/// PP667's route ends at IAudioSink and nothing outside this project implemented it, so a managed
/// run reached the sound seam and stopped. These hold the receiver behind it to the C's own
/// behaviour: the prefill before playback, delivery strictly by frame index, the repeated FEC
/// frames, the eviction rule, and the concealment that lets playback move past a hole.
/// </summary>
public class ManagedAudioReceiverTests(ITestOutputHelper output)
{
    /// <summary>Records what the receiver decided to hand out, concealment included.</summary>
    private sealed class Heard : IAudioFrameSink
    {
        public List<ManagedAudioHeader> Headers { get; } = [];

        /// <summary>One entry per delivered frame; null is the C's concealed frame.</summary>
        public List<byte[]?> Frames { get; } = [];

        public void Header(in ManagedAudioHeader header) => Headers.Add(header);

        public void Frame(ReadOnlySpan<byte> frame) => Frames.Add(frame.IsEmpty ? null : frame.ToArray());
    }

    private static AvPacket Audio(
        ushort frameIndex, byte source, byte fec, byte unitSize, bool haptics = false, byte codec = 5)
        => new(
            IsVideo: false,
            PacketIndex: 0,
            FrameIndex: frameIndex,
            UnitIndex: 0,
            UnitsInFrameTotal: (ushort)(source + fec),
            UnitsInFrameFec: (ushort)((unitSize << 8) | ((fec & 0xf) << 4) | (source & 0xf)),
            Codec: codec,
            AdaptiveStreamIndex: 0,
            KeyPos: 0,
            DataOffset: 0,
            DataSize: unitSize * (source + fec),
            IsHaptics: haptics);

    /// <summary>A payload whose n'th unit is n+1 repeated, so a delivered frame says which it was.</summary>
    private static byte[] Payload(int units, byte unitSize)
    {
        var buffer = new byte[units * unitSize];

        for (int unit = 0; unit < units; unit++)
            Array.Fill(buffer, (byte)(unit + 1), unit * unitSize, unitSize);

        return buffer;
    }

    /// <summary>The three counts the C packs into one 16-bit field, read back.</summary>
    [Theory]
    [InlineData(0x0000, 0, 0, 0)]
    [InlineData(0x0201, 1, 0, 2)]
    [InlineData(0x2032, 2, 3, 0x20)]
    [InlineData(0xffff, 15, 15, 0xff)]
    public void TheUnitCountsAreTheCsThreeFields(int packed, byte source, byte fec, byte unitSize)
    {
        (byte gotSource, byte gotFec, byte gotSize) = ManagedAudioReceiver.Units((ushort)packed);

        Assert.Equal(source, gotSource);
        Assert.Equal(fec, gotFec);
        Assert.Equal(unitSize, gotSize);
    }

    /// <summary>
    /// The four refusals, each of which returns before anything is buffered.
    ///
    /// Each is a log line and a return in the C, and a packet that passed one of them into the
    /// jitter buffer would index off the end of the payload it names.
    /// </summary>
    [Fact]
    public void EveryMalformedPacketIsRefusedBeforeItIsBuffered()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        Assert.Equal(
            AudioIntake.UnknownCodec,
            receiver.AvPacket(Audio(0, 1, 0, 4, codec: 2), Payload(1, 4)));

        Assert.Equal(
            AudioIntake.Empty,
            receiver.AvPacket(Audio(0, 0, 0, 0), []));

        // units_in_frame_total disagreeing with the two counts packed beside it.
        AvPacket mismatched = Audio(0, 2, 1, 4) with { UnitsInFrameTotal = 4 };
        Assert.Equal(AudioIntake.UnitCountMismatch, receiver.AvPacket(mismatched, Payload(3, 4)));

        AvPacket wrongSize = Audio(0, 2, 1, 4) with { DataSize = 8 };
        Assert.Equal(AudioIntake.SizeMismatch, receiver.AvPacket(wrongSize, Payload(3, 4)));

        Assert.Empty(heard.Frames);
        Assert.Equal(0, receiver.Buffered);
    }

    /// <summary>
    /// NOTHING IS DELIVERED UNTIL THREE ARE HELD, which is the whole point of a jitter buffer.
    ///
    /// A port that handed each unit straight on would pass every round-trip test written about it
    /// and reorder sound the first time two packets swapped.
    /// </summary>
    [Fact]
    public void PlaybackWaitsForThePrefill()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        receiver.Frame(10, haptics: false, [0xaa]);
        receiver.Frame(11, haptics: false, [0xbb]);

        Assert.Empty(heard.Frames);
        Assert.False(receiver.PlaybackStarted);
        Assert.Equal(2, receiver.Buffered);

        receiver.Frame(12, haptics: false, [0xcc]);

        Assert.True(receiver.PlaybackStarted);

        // The third arrival starts playback at the OLDEST held index and then drains all three,
        // because delivering one makes the next deliverable from the same buffer.
        Assert.Equal(3, heard.Frames.Count);
        Assert.Equal([0xaa], heard.Frames[0]);
        Assert.Equal([0xbb], heard.Frames[1]);
        Assert.Equal([0xcc], heard.Frames[2]);
        Assert.Equal(0, receiver.Buffered);
        Assert.Equal((ushort?)13, receiver.NextFrameIndex);
    }

    /// <summary>
    /// AND OUT-OF-ORDER ARRIVALS COME BACK IN ORDER, which is the other half.
    ///
    /// Delivery is by index and not by arrival, so the buffer is what a reordering network is for.
    /// </summary>
    [Fact]
    public void FramesAreDeliveredByIndexRatherThanByArrival()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        receiver.Frame(7, haptics: false, [7]);
        receiver.Frame(5, haptics: false, [5]);
        receiver.Frame(6, haptics: false, [6]);

        Assert.Equal([[5], [6], [7]], heard.Frames);
    }

    /// <summary>
    /// A frame older than what playback is waiting for is dropped rather than played late.
    /// </summary>
    [Fact]
    public void AFrameBehindPlaybackIsDropped()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        receiver.Frame(20, haptics: false, [20]);
        receiver.Frame(21, haptics: false, [21]);
        receiver.Frame(22, haptics: false, [22]);

        Assert.Equal(3, heard.Frames.Count);
        Assert.Equal((ushort?)23, receiver.NextFrameIndex);

        receiver.Frame(19, haptics: false, [19]);

        Assert.Equal(3, heard.Frames.Count);
        Assert.Equal(0, receiver.Buffered);
    }

    /// <summary>
    /// A HOLE IS CONCEALED AS SOON AS ANYTHING NEWER ARRIVES, which is sooner than it reads.
    ///
    /// The C emits <c>frame_cb(NULL, 0)</c> and steps over the index. The lookahead condition it
    /// guards that with only applies at or above the prefill - and below it the else arm is a flat
    /// true, so ONE newer frame gives up on the awaited one. That is the behaviour, and the test
    /// says so rather than the shape the condition suggests.
    /// </summary>
    [Fact]
    public void AMissingIndexIsConcealedAsSoonAsAnythingNewerArrives()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        // Start playback and drain, leaving the receiver waiting on 103.
        receiver.Frame(100, haptics: false, [100]);
        receiver.Frame(101, haptics: false, [101]);
        receiver.Frame(102, haptics: false, [102]);

        Assert.Equal((ushort?)103, receiver.NextFrameIndex);
        Assert.Equal(3, heard.Frames.Count);

        // 103 never arrives. 104 alone is enough: the buffer is below the prefill, so the else arm
        // conceals 103 and then delivers 104 out of the same call.
        receiver.Frame(104, haptics: false, [104]);

        output.WriteLine(string.Join(", ", heard.Frames.Select(one => one is null ? "concealed" : one[0].ToString())));

        Assert.Equal(5, heard.Frames.Count);
        Assert.Null(heard.Frames[3]);
        Assert.Equal([104], heard.Frames[4]);
        Assert.Equal((ushort?)105, receiver.NextFrameIndex);
    }

    /// <summary>
    /// PP740: THE BUFFER NEVER HOLDS MORE THAN THE PREFILL, so five of its eight slots are dead.
    ///
    /// Traced rather than guessed, and it falls out of two facts. Before playback starts nothing is
    /// delivered, and the third arrival is what starts it - so the count reaches three and no more.
    /// After it starts, every call drains: the awaited index is either held, or it is missing and
    /// every held index is newer than it, which is exactly the concealment condition. So the loop
    /// leaves the buffer empty every time.
    ///
    /// WHICH MAKES THE EVICTION PATH UNREACHABLE TOO - <c>store_audio_frame_locked</c>'s arm for a
    /// full buffer needs eight occupied slots, and the third store is the one that starts playback
    /// and empties it. So between calls the buffer holds at most PREFILL - 1, and the three counted
    /// inside the call that drains them is the transient peak. This is asserted rather than left as
    /// a reading, because the assertion is what would notice the day a change makes capacity matter.
    /// </summary>
    [Fact]
    public void TheBufferNeverHoldsMoreThanThePrefill()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);
        int deepest = 0;

        // Arrivals with holes, out of order, and repeated - everything that would fill a buffer.
        foreach (ushort index in (ushort[])[300, 305, 302, 301, 309, 304, 304, 307, 303, 320, 311])
        {
            receiver.Frame(index, haptics: false, [(byte)(index - 300)]);
            deepest = Math.Max(deepest, receiver.Buffered);
        }

        output.WriteLine($"deepest the buffer ever got: {deepest} of {ManagedAudioReceiver.JitterBufferSize}");

        Assert.Equal(ManagedAudioReceiver.JitterPrefill - 1, deepest);
        Assert.True(
            deepest < ManagedAudioReceiver.JitterBufferSize,
            "the buffer reached its declared size, so the eviction path is reachable after all");

        // And once playback has started it is empty after every call, which is the other half.
        Assert.Equal(0, receiver.Buffered);
        Assert.True(receiver.PlaybackStarted);
    }

    /// <summary>
    /// Haptics is never buffered: newer goes straight out and older is dropped.
    /// </summary>
    [Fact]
    public void HapticsBypassesTheJitterBuffer()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        receiver.Frame(5, haptics: true, [5]);

        Assert.Equal([[5]], heard.Frames);
        Assert.Equal(0, receiver.Buffered);

        receiver.Frame(4, haptics: true, [4]);
        Assert.Single(heard.Frames);

        receiver.Frame(6, haptics: true, [6]);
        Assert.Equal(2, heard.Frames.Count);
    }

    /// <summary>
    /// THE FEC UNITS ARE EARLIER FRAMES, which is what makes audio redundancy repetition.
    ///
    /// A packet at index n with two source units and one FEC unit carries n, n+1 and n-1. Reading
    /// them as parity - or as three consecutive frames - puts a frame under the wrong index, which
    /// the jitter buffer then delivers in the wrong place.
    /// </summary>
    [Fact]
    public void TheFecUnitsCarryTheFrameIndicesBeforeThePackets()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        // Past the startup window, so the FEC arm is not skipped.
        AvPacket packet = Audio(40000, source: 2, fec: 1, unitSize: 1);

        Assert.Equal(AudioIntake.Accepted, receiver.AvPacket(packet, Payload(3, 1)));

        // 39999 (the FEC repeat), 40000 and 40001 - three slots, and the prefill drains them.
        Assert.Equal(3, heard.Frames.Count);
        Assert.Equal([3], heard.Frames[0]);
        Assert.Equal([1], heard.Frames[1]);
        Assert.Equal([2], heard.Frames[2]);
        Assert.Equal((ushort?)40002, receiver.NextFrameIndex);
    }

    /// <summary>
    /// AND THE STARTUP ARM SKIPS THE ONES THAT WOULD WRAP BEHIND THE FIRST FRAME.
    ///
    /// At index 0 the repeat would be 0xffff, which is the far end of the sequence space rather than
    /// a frame that was ever sent. The C skips it until the stream has run past 1&lt;&lt;15.
    /// </summary>
    [Fact]
    public void TheStartupArmSkipsAFecRepeatThatWouldUnderflow()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        Assert.Equal(
            AudioIntake.Accepted,
            receiver.AvPacket(Audio(0, source: 2, fec: 1, unitSize: 1), Payload(3, 1)));

        // Only the two source units were offered; nothing wrapped to 0xffff.
        Assert.Equal(2, receiver.Buffered);
        Assert.False(receiver.PlaybackStarted);
    }

    /// <summary>The receiver pushes the packet's frame index to the run's stats, once per packet.</summary>
    [Fact]
    public void ThePacketsFrameIndexReachesTheStats()
    {
        var stats = new ManagedPacketStats();
        var receiver = new ManagedAudioReceiver(new Heard(), stats);

        receiver.AvPacket(Audio(40000, source: 2, fec: 1, unitSize: 1), Payload(3, 1));
        receiver.AvPacket(Audio(40001, source: 2, fec: 1, unitSize: 1), Payload(3, 1));

        PacketWindow window = stats.Read(reset: false);

        Assert.True(window.Total > 0, "the stats saw nothing");
    }

    /// <summary>A STREAMINFO resets the sequencing and tells the sink, in that order.</summary>
    [Fact]
    public void StreamInfoResetsEverythingAndThenAnnounces()
    {
        var heard = new Heard();
        var receiver = new ManagedAudioReceiver(heard);

        receiver.Frame(10, haptics: false, [10]);
        receiver.Frame(11, haptics: false, [11]);
        receiver.Frame(12, haptics: false, [12]);

        Assert.True(receiver.PlaybackStarted);

        receiver.StreamInfo(ManagedAudioHeader.Set(2, 16, 48000, 480));

        Assert.False(receiver.PlaybackStarted);
        Assert.Equal(0, receiver.Buffered);
        Assert.Null(receiver.NextFrameIndex);

        ManagedAudioHeader announced = Assert.Single(heard.Headers);
        Assert.Equal(2, announced.Channels);
        Assert.Equal(16, announced.Bits);
        Assert.Equal(48000u, announced.Rate);
        Assert.Equal(480u, announced.FrameSize);
        Assert.Equal(1u, announced.Unknown);
    }

    /// <summary>
    /// THE TWO ARMS SHARE NO STATE, which is why the pair exists rather than one receiver.
    ///
    /// PP740's whole reason: the C holds two instances and IAudioSink is one object with two
    /// methods. An implementation putting both on one receiver lets a haptics frame advance the
    /// sound path's frame_index_prev, and every test above still passes.
    /// </summary>
    [Fact]
    public void ThePairsHapticsArmCannotMoveTheAudioArmsSequence()
    {
        var sound = new Heard();
        var pad = new Heard();
        IAudioSink pair = new ManagedAudioReceiverPair(sound, pad);

        // A haptics packet ahead of anything the sound path has seen - and 500 rather than 40000,
        // because RFC 1982 says a number past half the space is not "greater" and the arm would
        // drop it. PP715 stepped in that one setting up a ceiling the same way.
        pair.Haptics(Audio(500, source: 1, fec: 0, unitSize: 1, haptics: true), Payload(1, 1));
        Assert.Single(pad.Frames);
        Assert.Empty(sound.Frames);

        // The sound path still buffers and delivers from its own beginning.
        for (ushort index = 100; index <= 102; index++)
            pair.Audio(Audio(index, source: 1, fec: 0, unitSize: 1), Payload(1, 1));

        Assert.Equal(3, sound.Frames.Count);
    }

    /// <summary>
    /// The two numbers the port copied are still the C's.
    ///
    /// PP58: a constant transcribed out of a file the port also ships is a claim about that file,
    /// and a #define moved upstream changes what a correct port is.
    /// </summary>
    [Fact]
    public void ThePrefillAndTheBufferSizeAreTheCs()
    {
        if (ManagedAudioReceiverSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(ManagedAudioReceiver.JitterPrefill, ManagedAudioReceiverSource.PrefillIn(source));
        Assert.Equal(ManagedAudioReceiver.JitterBufferSize, ManagedAudioReceiverSource.BufferSizeIn(source));
    }

    /// <summary>
    /// THE HEADER'S TWO BYTE ORDERS DISAGREE, and the port disagrees with itself the same way.
    ///
    /// chiaki_audio_header_load reads channels then bits; chiaki_audio_header_save writes bits then
    /// channels. So save does not round-trip through load, in the C or here. PP402's rule: the bytes
    /// go to a console, so the port reproduces what the console is sent - and asserts the asymmetry
    /// rather than quietly fixing one half, because a fix here would only desynchronise the port.
    /// </summary>
    [Fact]
    public void SaveAndLoadDisagreeAboutTheFirstTwoBytes()
    {
        var header = ManagedAudioHeader.Set(channels: 2, bits: 16, rate: 48000, frameSize: 480);

        Span<byte> wire = stackalloc byte[ManagedAudioHeader.Size];
        header.Save(wire);

        Assert.Equal(16, wire[0]);
        Assert.Equal(2, wire[1]);

        ManagedAudioHeader read = ManagedAudioHeader.Load(wire);

        Assert.Equal(16, read.Channels);
        Assert.Equal(2, read.Bits);
        Assert.NotEqual(header, read);

        // And everything after the first two bytes does round-trip.
        Assert.Equal(header.Rate, read.Rate);
        Assert.Equal(header.FrameSize, read.FrameSize);
        Assert.Equal(header.Unknown, read.Unknown);

        if (ManagedAudioReceiverSource.LocateHeader() is not { } path)
            return;

        Assert.True(
            ManagedAudioReceiverSource.LoadAndSaveDisagree(File.ReadAllText(path)),
            "the C's load and save agree now, so this port's Save is wrong rather than faithful");
    }

    /// <summary>A header shorter than the C's fourteen bytes is refused rather than read past.</summary>
    [Fact]
    public void AShortHeaderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ManagedAudioHeader.Load(new byte[ManagedAudioHeader.Size - 1]));

        Assert.Throws<ArgumentException>(() =>
        {
            var buffer = new byte[ManagedAudioHeader.Size - 1];
            default(ManagedAudioHeader).Save(buffer);
        });
    }
}
