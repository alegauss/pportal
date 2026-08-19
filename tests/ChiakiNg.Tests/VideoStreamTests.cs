using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP123: twenty-four packets off a real PlayStation stream, parsed and decrypted end to end.
///
/// Every other vector in this tree records one function's answer. This one records what the wire
/// carried and what a decoder was supposed to see, which makes it the only case that catches a
/// mistake in how the pieces are JOINED rather than in any one of them - the key position most of
/// all, which is a packet's own plus one block and is wrong silently.
/// </summary>
public class VideoStreamTests : IDisposable
{
    private readonly string? file = VideoStreamVectors.Locate();
    private readonly List<IDisposable> owned = [];

    public void Dispose()
    {
        foreach (IDisposable d in owned)
            d.Dispose();
        GC.SuppressFinalize(this);
    }

    private (GkCrypt Crypt, IReadOnlyList<VideoPacketCase> Cases) Recording()
    {
        string text = File.ReadAllText(file!);
        (byte[] handshakeKey, byte[] secret, byte index) = VideoStreamVectors.Session(text);

        var crypt = new GkCrypt(0, index, handshakeKey, secret);
        owned.Add(crypt);
        return (crypt, VideoStreamVectors.Parse(file!));
    }

    [Fact]
    public void TheRecordingIsReadable()
    {
        if (file is null)
            return;

        string text = File.ReadAllText(file);
        (byte[] handshakeKey, byte[] secret, byte index) = VideoStreamVectors.Session(text);

        Assert.Equal(16, handshakeKey.Length);
        Assert.Equal(32, secret.Length);

        // 3 is the video stream's crypt index. gkcrypt derives a different key stream per index,
        // so a port that reached for the audio one would decrypt every packet to garbage and get
        // no error saying so.
        Assert.Equal(3, index);
        Assert.Equal(24, VideoStreamVectors.Parse(file).Count);
    }

    /// <summary>
    /// Each packet: parse the header, decrypt the payload at key_pos + one block, and compare to
    /// the NALU the recording says a decoder saw.
    ///
    /// One test per packet, so a failure names which frame of the stream stopped matching - and
    /// the position advances through the session, so "the fourth one" and "all of them" are
    /// different bugs.
    /// </summary>
    [Theory]
    [MemberData(nameof(PacketIndices))]
    public void EachRecordedPacketDecryptsToItsNalu(int index)
    {
        if (file is null)
            return;

        (GkCrypt crypt, IReadOnlyList<VideoPacketCase> cases) = Recording();
        VideoPacketCase c = cases[index];

        using var keyState = new KeyState();
        byte[] buffer = (byte[])c.Packet.Clone();

        AvPacket? parsed = Takion.ParseV9(keyState, buffer, out ChiakiError error);
        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(parsed);

        AvPacket p = parsed.Value;
        Assert.True(p.IsVideo);
        Assert.Equal(c.Nalu.Length, p.DataSize);

        // The payload is a span of the buffer it arrived in, and decrypting it in place is what
        // the receive path does. Copied out here only to compare.
        byte[] payload = buffer[p.DataOffset..(p.DataOffset + p.DataSize)];
        crypt.Decrypt(p.KeyPos + (ulong)GkCrypt.BlockSize, payload, payload.Length);

        Assert.Equal(c.Nalu, payload);
    }

    /// <summary>
    /// The block offset is load-bearing. Decrypting at the packet's own key position - the
    /// obvious reading, and the one a rewrite takes - produces bytes that are not the NALU, and
    /// produces them without any error at all.
    /// </summary>
    [Fact]
    public void DecryptingAtThePacketsOwnPositionDoesNotGiveTheNalu()
    {
        if (file is null)
            return;

        (GkCrypt crypt, IReadOnlyList<VideoPacketCase> cases) = Recording();
        VideoPacketCase c = cases[0];

        using var keyState = new KeyState();
        byte[] buffer = (byte[])c.Packet.Clone();
        AvPacket p = Takion.ParseV9(keyState, buffer, out _)!.Value;

        byte[] payload = buffer[p.DataOffset..(p.DataOffset + p.DataSize)];
        crypt.Decrypt(p.KeyPos, payload, payload.Length);

        Assert.NotEqual(c.Nalu, payload);
    }

    /// <summary>
    /// And the crypt index is too: the audio stream's key decrypts a video packet to something
    /// that is not the NALU, silently. This is the one a port gets wrong by reading a constant
    /// off the wrong line.
    /// </summary>
    [Fact]
    public void TheAudioCryptIndexDoesNotDecryptVideo()
    {
        if (file is null)
            return;

        string text = File.ReadAllText(file);
        (byte[] handshakeKey, byte[] secret, byte index) = VideoStreamVectors.Session(text);
        IReadOnlyList<VideoPacketCase> cases = VideoStreamVectors.Parse(file);

        using var wrong = new GkCrypt(0, (byte)(index - 1), handshakeKey, secret);
        using var keyState = new KeyState();

        byte[] buffer = (byte[])cases[0].Packet.Clone();
        AvPacket p = Takion.ParseV9(keyState, buffer, out _)!.Value;

        byte[] payload = buffer[p.DataOffset..(p.DataOffset + p.DataSize)];
        wrong.Decrypt(p.KeyPos + (ulong)GkCrypt.BlockSize, payload, payload.Length);

        Assert.NotEqual(cases[0].Nalu, payload);
    }

    public static TheoryData<int> PacketIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < 24; i++)
            data.Add(i);
        return data;
    }
}
