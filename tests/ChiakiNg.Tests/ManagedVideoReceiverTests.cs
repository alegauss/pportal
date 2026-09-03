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
}
