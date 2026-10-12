using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP291: the assembled receiver, against the C where the C can be driven at all.
///
/// The native harness has a hard limit and VideoReceiver's own summary states it: with no stream
/// connection behind it, a corrupt-frame report reaches zeroed memory and the process aborts. So
/// the two can only be compared on sequences that never report one - which is frame index 1, the
/// one index the sequencer excepts.
///
/// That is a narrow window and it is the only differential surface there is, so it is used for what
/// it covers - the delivery path, the bytes, the callback contract - and the loss cases are held by
/// the case tables PP291's three decisions already carry.
/// </summary>
public class ManagedVideoReceiverTests(ITestOutputHelper output)
{
    /// <summary>Collects what a receiver decided to send, so a test can assert on it.</summary>
    private sealed class Outbound : IVideoReceiverOutbound
    {
        public List<(ushort From, ushort To)> Corrupt { get; } = [];

        public int IdrRequests { get; private set; }

        public List<(int Frame, bool Sent)> Failures { get; } = [];

        public bool IdrSucceeds { get; set; } = true;

        public void SendCorruptFrame(ushort from, ushort to) => Corrupt.Add((from, to));

        public bool SendIdrRequest()
        {
            IdrRequests++;
            return IdrSucceeds;
        }

        public void FecFailure(int frameIndex, bool idrRequestSent)
            => Failures.Add((frameIndex, idrRequestSent));
    }

    /// <summary>Two bytes of header then payload, which is the shape a video unit has.</summary>
    private static byte[] Unit(int payload, int seed)
    {
        var data = new byte[payload + 2];
        new Random(seed).NextBytes(data.AsSpan(2));
        return data;
    }

    /// <summary>
    /// THE COMPARISON. Frame 1, delivered whole, reaches both callbacks with the same bytes.
    /// </summary>
    [Fact]
    public void BothReceiversDeliverTheSameFrame()
    {
        const int Units = 4;
        const int Payload = 96;
        byte[] header = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42];

        var nativeFrames = new List<byte[]>();
        var managedFrames = new List<byte[]>();

        // PP670: the C receiver is the oracle, and the build says whether it still has one.
        if (ShimFramePathShape.WrappingHeader() is null)
            return;

        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new VideoReceiver(
            (frame, lost, recovered) => { nativeFrames.Add(frame.ToArray()); return true; },
            ChiakiNg.Session.ChiakiCodec.H264);
        native.StreamInfo(header, 1920, 1080);

        var outbound = new Outbound();
        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) => { managedFrames.Add(frame.ToArray()); return true; },
            outbound);
        managed.StreamInfo(header);

        for (ushort i = 0; i < Units; i++)
        {
            byte[] unit = Unit(Payload, seed: 40 + i);
            native.AvPacket(1, i, Units, 0, unit);
            managed.AvPacket(1, i, Units, 0, unit);
        }

        // The header goes first in both, then the picture.
        Assert.Equal(nativeFrames.Count, managedFrames.Count);
        for (int i = 0; i < nativeFrames.Count; i++)
            Assert.Equal(nativeFrames[i], managedFrames[i]);

        // ...and nothing was reported, which is what makes this comparison possible at all.
        Assert.Empty(outbound.Corrupt);
        output.WriteLine($"{nativeFrames.Count} callback(s), identical, {nativeFrames[^1].Length} bytes of picture");
    }

    /// <summary>The header reaches the callback before any picture does.</summary>
    [Fact]
    public void TheProfileHeaderIsDeliveredFirst()
    {
        byte[] header = [1, 2, 3, 4];
        var seen = new List<int>();

        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) => { seen.Add(frame.Length); return true; }, new Outbound());
        managed.StreamInfo(header);

        managed.AvPacket(1, 0, 1, 0, Unit(64, 1));

        Assert.Equal(2, seen.Count);
        Assert.Equal(header.Length, seen[0]);
    }

    /// <summary>An adaptive stream index with no profile behind it is dropped, not indexed.</summary>
    [Fact]
    public void AnUnknownProfileIndexIsRefused()
    {
        var seen = new List<int>();
        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) => { seen.Add(frame.Length); return true; }, new Outbound());
        managed.StreamInfo([1, 2, 3, 4]);

        managed.AvPacket(1, 0, 1, 0, Unit(64, 1), adaptiveStreamIndex: 3);

        Assert.Empty(seen);
    }

    /// <summary>
    /// A gap reports the range once, through the delegate rather than into a session pointer.
    ///
    /// This is the case the native harness cannot be asked - it is why the seam exists.
    /// </summary>
    [Fact]
    public void AGapIsReportedThroughTheSeam()
    {
        var outbound = new Outbound();
        var managed = new ManagedVideoReceiver((f, l, r) => true, outbound);
        managed.StreamInfo([1, 2, 3, 4]);

        managed.AvPacket(1, 0, 1, 0, Unit(64, 1));
        managed.AvPacket(5, 0, 1, 0, Unit(64, 2));

        Assert.Single(outbound.Corrupt);
        Assert.Equal((ushort)2, outbound.Corrupt[0].From);
        Assert.Equal((ushort)4, outbound.Corrupt[0].To);
    }

    /// <summary>
    /// With a parser attached, a frame it declines is still delivered.
    ///
    /// PP57's finding, and the C's behaviour: success is derived from the flush result alone, so a
    /// slice the parser will not describe still reaches the callback. A port that treated an
    /// unparseable frame as a failure would drop every frame on a codec mismatch instead of showing
    /// a broken picture, which is a different and worse symptom.
    /// </summary>
    [Fact]
    public void AFrameTheParserDeclinesIsStillDelivered()
    {
        var seen = new List<int>();
        using var bitstream = new Bitstream(ChiakiNg.Session.ChiakiCodec.H264);

        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) => { seen.Add(frame.Length); return true; },
            new Outbound(), idrOnFecFailure: false, bitstream);
        managed.StreamInfo([1, 2, 3, 4]);

        // Random payload with no start code: the parser declines it (bitstream.c:162).
        managed.AvPacket(1, 0, 1, 0, Unit(96, 5));

        // The header and the picture, both.
        Assert.Equal(2, seen.Count);
    }

    /// <summary>
    /// The frames-lost counter resets only when a frame is actually taken by the callback.
    /// </summary>
    [Fact]
    public void ARefusedFrameDoesNotResetTheLossCount()
    {
        var managed = new ManagedVideoReceiver((f, l, r) => false, new Outbound());
        managed.StreamInfo([1, 2, 3, 4]);

        managed.AvPacket(1, 0, 1, 0, Unit(64, 1));

        // Nothing was accepted, so nothing became the last complete frame either.
        Assert.Equal(0, managed.FramesLostTotal);
    }

    // PP855: test/bitstream.c's recorded H.265 header and P-slice, as BitstreamParserOracleTests holds
    // them. H.265 and not H.264 because its slice cannot be read without the parameter sets - the
    // H.264 one parses with no header at all, so it could not tell whether the header was read.
    private static readonly byte[] H265Header =
    [
        0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
        0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
        0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
        0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
        0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
        0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
    ];

    private static readonly byte[] H265SliceP =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd0, 0x97, 0x61, 0x28, 0x23, 0x2d, 0x8b, 0x80, 0x6f, 0xfd,
        0x2f, 0x2b, 0x11, 0xd4, 0x55, 0x04, 0x90, 0x18, 0x49, 0xe5, 0xbc, 0xc4, 0x97, 0xbc, 0x3d, 0xeb,
    ];

    private static readonly byte[] H265SliceI =
    [
        0x00, 0x00, 0x00, 0x01, 0x28, 0x01, 0xac, 0x25, 0xcf, 0x83, 0xff, 0x23, 0x54, 0xab, 0x5c, 0xf5,
        0x7a, 0x06, 0x7c, 0x3f, 0x31, 0x9b, 0xe6, 0x10, 0x57, 0xe8, 0x0e, 0xcf, 0xdd, 0xda, 0xdb, 0x3f,
    ];

    private static readonly byte[] H265SlicePRef5 =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd7, 0x85, 0x6a, 0xae, 0xa6, 0x11, 0x80, 0x95, 0x80, 0x0a,
        0xec, 0x5e, 0xdf, 0x39, 0x86, 0xe6, 0xd9, 0x07, 0x49, 0x17, 0xe2, 0x62, 0x57, 0x14, 0xd7, 0x08,
    ];

    /// <summary>
    /// PP855: A P-SLICE IS DECODED ONLY AGAINST A REFERENCE THAT ARRIVED. Frame 7 names reference 5,
    /// which is frame 1: with frames 1 to 6 in hand it goes to the decoder, and with frame 1 never
    /// received - and nothing older to substitute - it is lost instead of decoded against a picture
    /// the decoder never had, which is what a smear is.
    /// </summary>
    [Fact]
    public void APSliceIsDecodedOnlyAgainstAReferenceThatArrived()
    {
        foreach (bool firstArrives in new[] { true, false })
        {
            var seen = new List<int>();
            using var bitstream = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265);

            var managed = new ManagedVideoReceiver(
                (frame, lost, recovered) => { seen.Add(frame.Length); return true; },
                new Outbound(), idrOnFecFailure: true, bitstream);
            managed.StreamInfo(H265Header);

            for (ushort index = firstArrives ? (ushort)1 : (ushort)2; index <= 6; index++)
                managed.AvPacket(index, 0, 1, 0, Wrapped(H265SliceI));

            int before = seen.Count;
            managed.AvPacket(7, 0, 1, 0, Wrapped(H265SlicePRef5));

            output.WriteLine($"frame 1 {(firstArrives ? "arrived" : "lost")}: {seen.Count - before} delivered, {managed.FramesLostTotal} lost");

            Assert.Equal(firstArrives ? before + 1 : before, seen.Count);
            Assert.Equal(firstArrives ? 0 : 1, managed.FramesLostTotal);
        }
    }

    /// <summary>A slice as one whole video unit: the two-byte unit header, then the NAL.</summary>
    private static byte[] Wrapped(byte[] slice) => [0x00, 0x00, .. slice];

    /// <summary>
    /// PP855: WITH A PARSER, A P-FRAME WITH NO REFERENCE IS LOST AND ITS GAP REPORTED. The live root
    /// gave the receiver none, so such a frame went to the decoder as if whole and nothing asked the
    /// console to recover - the smear that opened this line.
    /// </summary>
    [Fact]
    public void APFrameWithNoReferenceIsLostAndItsGapReported()
    {
        var seen = new List<int>();
        var outbound = new Outbound();
        using var bitstream = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265);

        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) => { seen.Add(frame.Length); return true; },
            outbound, idrOnFecFailure: true, bitstream);
        managed.StreamInfo(H265Header);

        // Frame 1 is a P-slice and nothing came before it: no reference, so it is lost - not shown.
        managed.AvPacket(1, 0, 1, 0, Wrapped(H265SliceP));

        Assert.Equal([H265Header.Length], seen);
        Assert.Equal(1, managed.FramesLostTotal);

        // And the next frame reports the gap it left, which is what asks the console to recover.
        managed.AvPacket(2, 0, 1, 0, Wrapped(H265SliceP));

        Assert.Contains(((ushort)0, (ushort)1), outbound.Corrupt);
    }

    /// <summary>
    /// PP855: THE VIDEO ARM IS HANDED BOTH, which the live root passed neither of: the parser, so
    /// the P-frame above is caught, and the IDR flag, so a FEC failure is followed by a request.
    /// </summary>
    [Fact]
    public void TheVideoArmCarriesTheParserAndTheIdrFlag()
    {
        var seen = new List<int>();
        using var bitstream = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265);

        ManagedVideoReceiver arm = ChiakiNg.Session.ManagedStreamPhase.VideoArm(
            (frame, lost, recovered) => { seen.Add(frame.Length); return true; },
            new Outbound(), idrOnFecFailure: true, bitstream);

        arm.StreamInfo(H265Header);
        arm.AvPacket(1, 0, 1, 0, Wrapped(H265SliceP));

        Assert.Equal(1, arm.FramesLostTotal);

        bool flag = (bool)typeof(ManagedVideoReceiver)
            .GetField("idrOnFecFailure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(arm)!;
        Assert.True(flag);
    }

    /// <summary>
    /// PP855: and the live root goes through that arm with the run's own flag and codec - the same
    /// values the C's connect info is given, named once in StreamRun.
    /// </summary>
    [Fact]
    public void TheLiveRootBuildsTheReceiverThroughTheArm()
    {
        string? phase = ChiakiNg.Session.SanitizerSource.LocateRelative(Path.Combine("app", "Session", "ManagedStreamPhase.cs"));
        string? run = ChiakiNg.Session.SanitizerSource.LocateRelative(Path.Combine("app", "Session", "StreamRun.cs"));
        if (phase is null || run is null)
            return;

        string phaseCode = ChiakiNg.Session.CCall.Code(File.ReadAllText(phase));
        string runCode = ChiakiNg.Session.CCall.Code(File.ReadAllText(run));

        Assert.True(ChiakiNg.Session.CCall.CalledWith(phaseCode, "VideoArm", "video, outbound, IdrOnFecFailure"));

        // And the receiver hands the profile's header to the parser at the switch, as the C's next
        // line after the callback does. Held by its text because no recorded fixture parses
        // differently without it: their SPS widths happen to equal the zeroed defaults.
        string? receiver = ChiakiNg.Session.SanitizerSource.LocateRelative(Path.Combine("app", "Protocol", "ManagedVideoReceiver.cs"));
        Assert.NotNull(receiver);
        Assert.True(ChiakiNg.Session.CCall.Happens(
            ChiakiNg.Session.CCall.Code(File.ReadAllText(receiver)), "bitstream?.ReadHeader(profiles[profileCur])"));
        Assert.DoesNotContain("new ManagedVideoReceiver(video, outbound)", phaseCode, StringComparison.Ordinal);

        Assert.True(ChiakiNg.Session.CCall.CalledWith(runCode, "connect.SetFlags", "autoDowngrade: true, keyboard: false, dualSense: false, idrOnFecFailure: idrOnFecFailure"));
        Assert.Contains("IdrOnFecFailure = idrOnFecFailure", runCode, StringComparison.Ordinal);
        Assert.Contains("Codec = codec", runCode, StringComparison.Ordinal);
    }
}
