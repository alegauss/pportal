using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP667, under PP295: the AV route, managed, driving the receiver PP291 built.
///
/// PP366 held the route as three checks on the C. These hold the same three facts on the managed
/// side, plus the one PP366 could not: the decrypt's padding arithmetic, which the C hides inside
/// chiaki_gkcrypt_decrypt and the managed key stream refuses to hide.
/// </summary>
public class StreamAvDispatchTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x10 + i))];
    private static readonly byte[] Iv = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i))];

    /// <summary>A sink that remembers which arm it was handed.</summary>
    private sealed class Recording : IAudioSink
    {
        public List<string> Arms { get; } = [];
        public void Audio(in AvPacket packet, ReadOnlySpan<byte> payload) => Arms.Add("audio");
        public void Haptics(in AvPacket packet, ReadOnlySpan<byte> payload) => Arms.Add("haptics");
    }

    private sealed class Outbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to) { }
        public bool SendIdrRequest() => true;
        public void FecFailure(int frameIndex, bool idrRequestSent) { }
    }

    private static ManagedVideoReceiver Receiver(out List<int> delivered)
    {
        var frames = new List<int>();
        delivered = frames;
        return new ManagedVideoReceiver(
            (frame, lost, recovered) => { frames.Add(frame.Length); return true; },
            new Outbound());
    }

    private static AvPacket Packet(bool video, bool haptics, int offset, int size, ulong keyPos = 0)
        => new(video, 0, 1, 0, 1, 0, 0, 0, keyPos, offset, size, haptics);

    /// <summary>
    /// The decrypt matches the C's padding arithmetic at an UNALIGNED position.
    ///
    /// The packet's key_pos plus one block is where the C starts, and it is not a multiple of
    /// sixteen in general. The C rounds the stream's start down and its length up and reads from
    /// the padding in; GkKeyStream refuses anything unaligned, so this is where the rounding has
    /// to be. Held by encrypting with the same stream and decrypting back - a stream cipher's xor
    /// is its own inverse, so a wrong padding shows as bytes that do not return.
    /// </summary>
    [Theory]
    [InlineData(0UL, 16)]
    [InlineData(16UL, 16)]
    [InlineData(5UL, 20)]
    [InlineData(31UL, 1)]
    [InlineData(33UL, 47)]
    public void TheDecryptRoundsTheWayGkcryptDoes(ulong keyPos, int length)
    {
        byte[] plain = [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))];
        byte[] buffer = [.. plain];

        // Encrypt with the C's own recipe, written out: stream from the block below, read from
        // the padding in. If Decrypt's arithmetic differs from this, the round trip fails.
        ulong pre = keyPos % 16;
        int full = (int)((pre + (ulong)length + 15) / 16) * 16;
        byte[] stream = GkKeyStream.Generate(Key, Iv, keyPos - pre, full);
        for (int i = 0; i < length; i++)
            buffer[i] ^= stream[(int)pre + i];

        Assert.NotEqual(plain, buffer);

        StreamAvDispatch.Decrypt(Key, Iv, keyPos, buffer);

        Assert.Equal(plain, buffer);
    }

    /// <summary>
    /// Video goes to the receiver, and the receiver is DRIVEN - a frame comes out the far end.
    ///
    /// This is PP295's second criterion in one assertion: the managed video receiver reached by
    /// the managed stream connection's route, with the decrypt in between and the packet's own
    /// offsets naming the payload.
    /// </summary>
    [Fact]
    public void VideoIsDecryptedAndDrivesTheReceiver()
    {
        ManagedVideoReceiver receiver = Receiver(out List<int> delivered);
        var sink = new Recording();

        // The receiver drops every packet until it has a profile, and hands the profile's header
        // to the callback first - "a frame-shaped thing that is not a frame" - so the callback fires
        // twice here: once for these four bytes, once for the frame. That second length is the
        // assertion: it is the DECRYPTED payload, or the receiver was handed ciphertext.
        receiver.StreamInfo([1, 2, 3, 4]);

        // One-unit frame, so the receiver has a whole frame to deliver from a single packet.
        byte[] datagram = new byte[40];
        AvPacket packet = Packet(video: true, haptics: false, offset: 8, size: 24);

        // Pre-encrypt the payload so the decrypt has something to undo.
        byte[] plain = [.. Enumerable.Range(0, 24).Select(i => (byte)(0x40 + i))];
        Array.Copy(plain, 0, datagram, 8, 24);
        StreamAvDispatch.Decrypt(Key, Iv, packet.KeyPos + 16, datagram.AsSpan(8, 24));

        AvRoute route = StreamAvDispatch.Dispatch(packet, datagram, Key, Iv, receiver, sink);

        Assert.Equal(AvRoute.Video, route);
        Assert.Empty(sink.Arms);
        Assert.Equal(plain, datagram.AsSpan(8, 24).ToArray());

        // 22, not 24. Every unit carries a two-byte head the assembler strips - the receiver's own
        // tests build units as `new byte[payload + 2]` for that reason - so a 24-byte unit is a
        // 22-byte frame. The first draft of this expected 24 and was wrong in the useful direction:
        // the difference is the proof that the payload went THROUGH the frame path and not around it.
        Assert.Equal([4, 22], delivered);
    }

    /// <summary>Haptics is tested before the audio fallback, which is PP366's third check managed.</summary>
    [Fact]
    public void HapticsIsRoutedBeforeAudioIsAssumed()
    {
        ManagedVideoReceiver receiver = Receiver(out _);
        var sink = new Recording();
        byte[] datagram = new byte[32];

        Assert.Equal(AvRoute.Haptics, StreamAvDispatch.Dispatch(
            Packet(video: false, haptics: true, 0, 16), datagram, Key, Iv, receiver, sink));
        Assert.Equal(AvRoute.Audio, StreamAvDispatch.Dispatch(
            Packet(video: false, haptics: false, 0, 16), datagram, Key, Iv, receiver, sink));

        Assert.Equal(["haptics", "audio"], sink.Arms);
    }

    /// <summary>
    /// A key the decrypt cannot use drops the packet, and nothing downstream sees it.
    ///
    /// PP367's finding, kept: the C used to hand ciphertext on as a frame. The managed key stream
    /// cannot fail for the C's reason, so the one failure a caller can produce - a key of the wrong
    /// size - is the one asserted.
    /// </summary>
    [Fact]
    public void AnUnusableKeyDropsThePacketBeforeAnyReceiver()
    {
        ManagedVideoReceiver receiver = Receiver(out List<int> delivered);
        var sink = new Recording();

        AvRoute route = StreamAvDispatch.Dispatch(
            Packet(video: true, haptics: false, 0, 16), new byte[16], new byte[3], Iv, receiver, sink);

        Assert.Equal(AvRoute.Dropped, route);
        Assert.Empty(delivered);
        Assert.Empty(sink.Arms);
    }

    /// <summary>Offsets that name bytes outside the datagram are a drop, not an exception.</summary>
    [Theory]
    [InlineData(8, 100)]
    [InlineData(-1, 4)]
    [InlineData(4, -1)]
    public void OffsetsOutsideTheDatagramAreDropped(int offset, int size)
    {
        ManagedVideoReceiver receiver = Receiver(out _);

        Assert.Equal(AvRoute.Dropped, StreamAvDispatch.Dispatch(
            Packet(video: true, haptics: false, offset, size), new byte[16], Key, Iv, receiver, new Recording()));
    }

    /// <summary>
    /// The haptics bit is v12 audio's alone in the C, which is why the mirror's default is honest.
    ///
    /// The shim's parse this port builds AvPacket from is v9's, and v9 never writes is_haptics. A
    /// default of false is therefore the C's answer for every packet parsed today - and the day a
    /// v12 parse arrives, this is the line that says the bit has to come with it.
    /// </summary>
    [Fact]
    public void TheHapticsBitIsStillSetOnlyByTheV12AudioLayout()
    {
        if (StreamAvDispatchSource.Locate() is not { } path)
            return;

        Assert.True(
            StreamAvDispatchSource.HapticsIsStillAV12AudioBit(File.ReadAllText(path)),
            "takion.c no longer sets is_haptics exactly once, under the v12 audio guard, so the "
                + "mirror's default of false is no longer the C's own answer for a v9 parse");
    }

    /// <summary>And the default really is false, so the two positional construction sites stayed honest.</summary>
    [Fact]
    public void TheMirrorDefaultsHapticsToFalse()
        => Assert.False(new AvPacket(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).IsHaptics);
}
