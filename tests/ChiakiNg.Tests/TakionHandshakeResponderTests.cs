using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP606, under PP27: the responder as a participant, including over a real socket.
///
/// The state machine is tested on its own, and then the whole exchange is run over loopback UDP so
/// that "it answers correctly" is not a claim about spans. The C is not in this file - PP607 runs the
/// real takion against this responder over the same loopback - and the client side below is PP672's
/// writers, which PP672 holds byte for byte against what the C sends.
/// </summary>
public class TakionHandshakeResponderTests
{
    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0x5A + i))];

    private const uint OurTag = 0x55667788;
    private const uint ClientTag = 0x11223344;

    /// <summary>An INIT as the client writes it.</summary>
    private static byte[] Init(uint clientTag = ClientTag)
        => TakionClientDatagrams.WriteInit(clientTag);

    /// <summary>A COOKIE echoing whatever the ack carried, as the client writes it.</summary>
    private static byte[] CookieMessage(uint headerTag, ReadOnlySpan<byte> cookie)
        => TakionClientDatagrams.WriteCookie(headerTag, cookie);

    /// <summary>The cookie the responder chose, taken out of the ack it sent.</summary>
    private static byte[] CookieIn(byte[] ack)
        => ack.AsSpan(
            TakionInitAckDatagram.PayloadOffset + 0x10, TakionHandshake.CookieSize).ToArray();

    /// <summary>The three steps, in order, with the state moving each time.</summary>
    [Fact]
    public void TheExchangeRunsToDone()
    {
        var responder = new TakionHandshakeResponder(OurTag, Cookie);
        Assert.Equal(TakionResponderState.AwaitingInit, responder.State);

        byte[] ack = Assert.IsType<byte[]>(responder.Answer(Init()));
        Assert.Equal(TakionResponderState.AwaitingCookie, responder.State);
        Assert.Equal(ClientTag, responder.ClientTag);
        Assert.Equal(TakionHandshake.InitAckDatagramSize, ack.Length);

        byte[] cookieAck = Assert.IsType<byte[]>(
            responder.Answer(CookieMessage(OurTag, CookieIn(ack))));

        Assert.Equal(TakionResponderState.Done, responder.State);
        Assert.Equal(TakionHandshake.CookieAckDatagramSize, cookieAck.Length);
    }

    /// <summary>
    /// A REPEATED INIT GETS A REPEATED ACK, which is what makes a slow first answer survivable.
    ///
    /// takion retries its INIT when no ack arrives in time, and its cookie-ack receive tolerates a
    /// second INIT_ACK. Answering the retry with silence, or with a cookie ack, turns a slow
    /// responder into a failed connect.
    /// </summary>
    [Fact]
    public void ARetriedInitIsAnsweredAgain()
    {
        var responder = new TakionHandshakeResponder(OurTag, Cookie);

        byte[] first = Assert.IsType<byte[]>(responder.Answer(Init()));
        byte[] second = Assert.IsType<byte[]>(responder.Answer(Init()));

        Assert.Equal(first, second);
        Assert.Equal(2, responder.InitsSeen);
        Assert.Equal(TakionResponderState.AwaitingCookie, responder.State);
    }

    /// <summary>
    /// A cookie that is not the one sent, or that comes under the wrong tag, is not answered.
    ///
    /// Silence and not an exception: a peer on a real socket receives whatever arrives, and a
    /// harness that threw on a stray datagram would be reporting on the network.
    /// </summary>
    [Fact]
    public void AWrongCookieOrTagIsNotAnswered()
    {
        var responder = new TakionHandshakeResponder(OurTag, Cookie);
        byte[] ack = Assert.IsType<byte[]>(responder.Answer(Init()));

        byte[] wrong = CookieIn(ack);
        wrong[0] ^= 0xFF;
        Assert.Null(responder.Answer(CookieMessage(OurTag, wrong)));

        Assert.Null(responder.Answer(CookieMessage(OurTag + 1, CookieIn(ack))));
        Assert.Equal(TakionResponderState.AwaitingCookie, responder.State);

        // And the right one still completes it, so the refusals above are not a stuck state.
        Assert.NotNull(responder.Answer(CookieMessage(OurTag, CookieIn(ack))));
        Assert.Equal(TakionResponderState.Done, responder.State);
    }

    /// <summary>A cookie before any init, and a stray datagram, are both unanswered.</summary>
    [Fact]
    public void WhatIsOutOfOrderIsNotAnswered()
    {
        var responder = new TakionHandshakeResponder(OurTag, Cookie);

        Assert.Null(responder.Answer(CookieMessage(OurTag, Cookie)));
        Assert.Null(responder.Answer(new byte[7]));
        Assert.Equal(TakionResponderState.AwaitingInit, responder.State);
    }

    /// <summary>A cookie of the wrong length is refused at construction, not at the first answer.</summary>
    [Fact]
    public void AWrongLengthCookieIsRefusedUpFront()
        => Assert.Throws<ArgumentException>(() => new TakionHandshakeResponder(1, new byte[8]));

    /// <summary>
    /// THE WHOLE EXCHANGE OVER A REAL SOCKET, so "it answers" is not a claim about spans.
    ///
    /// Loopback UDP, both ends in this process, with receive timeouts so a lost datagram fails the
    /// test rather than hanging the suite - PP117's lesson, which is the one this repository pays
    /// for whenever a test owns a thread it did not write.
    /// </summary>
    [Fact]
    public void TheExchangeRunsOverLoopback()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        peer.Client.ReceiveTimeout = 5000;
        client.Client.ReceiveTimeout = 5000;

        var peerEndPoint = (IPEndPoint)peer.Client.LocalEndPoint!;
        var responder = new TakionHandshakeResponder(OurTag, Cookie);

        // INIT ->
        byte[] init = Init();
        client.Send(init, init.Length, peerEndPoint);

        IPEndPoint from = new(IPAddress.Any, 0);
        byte[] arrived = peer.Receive(ref from);

        byte[] ack = Assert.IsType<byte[]>(responder.Answer(arrived));
        peer.Send(ack, ack.Length, from);

        // <- INIT_ACK, and the client accepts it because the header carries ITS tag.
        byte[] backAtClient = client.Receive(ref from);
        Assert.True(TakionHandshake.InboundHeaderTagAccepted(
            BinaryPrimitives.ReadUInt32BigEndian(
                backAtClient.AsSpan(TakionMessageHeader.OffsetInDatagram)),
            ClientTag));

        // COOKIE ->
        byte[] cookie = CookieMessage(OurTag, CookieIn(backAtClient));
        client.Send(cookie, cookie.Length, peerEndPoint);

        arrived = peer.Receive(ref from);
        byte[] cookieAck = Assert.IsType<byte[]>(responder.Answer(arrived));
        peer.Send(cookieAck, cookieAck.Length, from);

        // <- COOKIE_ACK
        backAtClient = client.Receive(ref from);

        Assert.Equal(TakionHandshake.CookieAckDatagramSize, backAtClient.Length);
        Assert.Equal(
            TakionMessageHeader.CookieAckChunkType,
            backAtClient[TakionHandshake.ChunkTypeOffsetInDatagram]);
        Assert.Equal(TakionResponderState.Done, responder.State);
    }
}
