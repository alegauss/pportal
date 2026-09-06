using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP750: the two gkcrypt operations, and the feedback packet they were the only thing missing from.
///
/// PP731 derived the material, PP416 made the stream and PP418 chose the window - and nothing ever
/// encrypted or signed anything, so IFeedbackSink stayed a seam only doubles filled while the
/// message and congestion sinks were closed around it.
/// </summary>
public class TakionFeedbackSinkTests(ITestOutputHelper output) : IDisposable
{
    private readonly UdpClient peer = new(new IPEndPoint(IPAddress.Loopback, 0));

    private IPEndPoint PeerEndPoint => (IPEndPoint)peer.Client.LocalEndPoint!;

    public void Dispose()
    {
        peer.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ManagedGkCrypt Crypt()
        => ManagedGkCrypt.Derive(
            ManagedGkCryptPair.LocalIndex,
            [.. Enumerable.Repeat((byte)0x11, 16)],
            [.. Enumerable.Repeat((byte)0x22, 32)]);

    private Thread AnswerHandshake(TakionHandshakeResponder responder)
    {
        var thread = new Thread(() =>
        {
            var from = new IPEndPoint(IPAddress.Loopback, 0);

            while (responder.State != TakionResponderState.Done)
            {
                byte[] datagram = peer.Receive(ref from);

                if (responder.Answer(datagram) is { } answer)
                    peer.Send(answer, answer.Length, from);
            }
        })
        {
            IsBackground = true,
            Name = "takion peer",
        };

        thread.Start();
        return thread;
    }

    /// <summary>
    /// ENCRYPT IS THE XOR, so applying it twice at one position gives the plaintext back.
    ///
    /// The stream cipher's own property, and the reason the C has one function for both directions.
    /// </summary>
    [Fact]
    public void EncryptingTwiceAtOnePositionIsTheIdentity()
    {
        ManagedGkCrypt crypt = Crypt();
        byte[] plain = [.. Enumerable.Range(0, 40).Select(one => (byte)one)];
        byte[] buffer = [.. plain];

        crypt.Encrypt(0x1234, buffer);
        Assert.NotEqual(plain, buffer);

        crypt.Encrypt(0x1234, buffer);
        Assert.Equal(plain, buffer);
    }

    /// <summary>And it really is the key stream, which is what makes it the C's cipher.</summary>
    [Fact]
    public void TheMaskIsTheKeyStreamAtThatPosition()
    {
        ManagedGkCrypt crypt = Crypt();
        byte[] buffer = new byte[32];

        crypt.Encrypt(0x40, buffer);

        // Zeroes XORed with the stream ARE the stream.
        Assert.Equal(crypt.KeyStream(0x40, 32), buffer);
    }

    /// <summary>A GMAC is four bytes and is the tag over the packet it was handed.</summary>
    [Fact]
    public void TheGmacIsTheTagOverThePacket()
    {
        ManagedGkCrypt crypt = Crypt();
        byte[] packet = [.. Enumerable.Repeat((byte)0xab, 24)];

        Span<byte> tag = stackalloc byte[TakionFeedbackSends.GmacSize];
        crypt.Gmac(0x100, packet, tag);

        byte[] expected = Ghash.Tag(
            crypt.GmacKeyFor(0x100), crypt.GmacIvFor(0x100), packet, TakionFeedbackSends.GmacSize);

        Assert.Equal(expected, tag.ToArray());
        Assert.Throws<ArgumentException>(() => crypt.Gmac(0, packet, new byte[3]));
    }

    /// <summary>
    /// A FEEDBACK PACKET REACHES THE PEER, encrypted and signed at the three positions the C uses.
    ///
    /// The whole point of the task: the ledger advances by the payload plus a block, the payload is
    /// encrypted a block PAST the position, and the MAC is taken AT it. This decrypts what arrived
    /// with the same crypt and gets the formatted state back, which no other arrangement would give.
    /// </summary>
    [Fact]
    public void AFeedbackStateArrivesEncryptedAtTheCsPositions()
    {
        var responder = new TakionHandshakeResponder(0x0000_6e6e, [.. Enumerable.Repeat((byte)0x65, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_1750) { LocalCrypt = Crypt() };
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var motion = new FeedbackMotion(
            0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 100, -100, 200, -200);

        var sink = new TakionFeedbackSink(takion);
        sink.SendState(0x0042, motion);

        var from = new IPEndPoint(IPAddress.Loopback, 0);
        peer.Client.ReceiveTimeout = 5000;
        byte[] arrived = peer.Receive(ref from);

        int head = TakionFeedbackSends.Feedback.HeadSize;
        int payloadSize = FeedbackPayload.StateSize(v12: true);

        output.WriteLine($"{arrived.Length} bytes, type {arrived[0]}, seq {BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(1))}");

        Assert.Equal(head + payloadSize, arrived.Length);
        Assert.Equal(TakionFeedbackSends.FeedbackStateType, arrived[0]);
        Assert.Equal(0x0042, BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(1)));

        // The position went out, and the payload decrypts a block past it with a fresh crypt.
        uint keyPos = BinaryPrimitives.ReadUInt32BigEndian(arrived.AsSpan(TakionFeedbackSends.Feedback.KeyPosOffset));

        byte[] payload = arrived[head..];
        Crypt().Encrypt(keyPos + (ulong)TakionFeedbackSends.BlockSize, payload);

        Span<byte> expected = stackalloc byte[payloadSize];
        FeedbackPayload.FormatState(expected, v12: true, motion);

        Assert.Equal(expected.ToArray(), payload);
        Assert.Equal(1, takion.FeedbackSent);
        Assert.Equal(1, sink.Sent);
    }

    /// <summary>
    /// AND THE MAC CHECKS, over the packet with its own field zeroed - which is why it is written last.
    /// </summary>
    [Fact]
    public void TheMacIsOverThePacketWithItsOwnFieldZeroed()
    {
        var responder = new TakionHandshakeResponder(0x0000_6f6f, [.. Enumerable.Repeat((byte)0x66, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_2750) { LocalCrypt = Crypt() };
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var sink = new TakionFeedbackSink(takion);
        sink.SendHistory(7, [1, 2, 3, 4]);

        var from = new IPEndPoint(IPAddress.Loopback, 0);
        peer.Client.ReceiveTimeout = 5000;
        byte[] arrived = peer.Receive(ref from);

        Assert.Equal(TakionFeedbackSends.FeedbackHistoryType, arrived[0]);

        int macAt = TakionFeedbackSends.Feedback.MacOffset;
        byte[] mac = arrived[macAt..(macAt + TakionFeedbackSends.GmacSize)];
        uint keyPos = BinaryPrimitives.ReadUInt32BigEndian(arrived.AsSpan(TakionFeedbackSends.Feedback.KeyPosOffset));

        // Re-take it the way a console would: the same bytes with the MAC field back to zero.
        byte[] checkable = [.. arrived];
        Array.Clear(checkable, macAt, TakionFeedbackSends.GmacSize);

        Span<byte> expected = stackalloc byte[TakionFeedbackSends.GmacSize];
        Crypt().Gmac(keyPos, checkable, expected);

        output.WriteLine($"mac {Convert.ToHexString(mac)}");

        Assert.Equal(expected.ToArray(), mac);
    }

    /// <summary>Without a local cipher there is nothing to sign under, and the send is refused.</summary>
    [Fact]
    public void ASendWithNoLocalCryptIsRefused()
    {
        using var takion = new ManagedTakion(0x0000_3750);
        var sink = new TakionFeedbackSink(takion);

        sink.SendHistory(1, [9]);

        Assert.Equal(ChiakiError.Uninitialized, sink.Last);
        Assert.Equal(0, sink.Sent);
        Assert.Equal(0, takion.FeedbackSent);
    }

    /// <summary>PP741: and the seam it fills is off the unreached list, with nothing replacing it.</summary>
    [Fact]
    public void TheFeedbackSeamIsNoLongerUnreached()
    {
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(typeof(TakionFeedbackSink).Assembly);

        output.WriteLine(string.Join(", ", unreached));

        Assert.DoesNotContain(nameof(IFeedbackSink), unreached);
        Assert.Equal([.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)], unreached);
    }
}
