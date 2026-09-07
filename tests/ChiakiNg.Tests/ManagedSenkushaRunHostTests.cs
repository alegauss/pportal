using System.Buffers.Binary;
using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP791, under PP784: the host that performs PP790's run, and the six protobufs it sends.
///
/// PP790 wrote the run and it took an interface only doubles implemented, which PP669 calls a
/// mapping rather than a call and PP741 counts the cost of. What was missing was three things at
/// once: bytes for the six senders, a raw send for the pings, and a class joining them to the port's
/// own takion.
///
/// NONE OF THIS NEEDS A CONSOLE. The senders are pure, the ping formatter writes into a caller's
/// buffer, and the timeout table is a table - so what a gate can assert is everything except the
/// three loops, which need something on the other end of a socket.
/// </summary>
public class ManagedSenkushaRunHostTests
{
    /// <summary>
    /// PP791: the seam is filled in app, which is the line's own criterion.
    ///
    /// Asked by reflection rather than by grep, which is what <see cref="SeamReach"/> exists for: a
    /// primary-constructor class whose base list wraps is invisible to a line-oriented pattern, and
    /// this port got three of seven such answers wrong once.
    /// </summary>
    [Fact]
    public void TheRunsHostIsImplementedInApp()
    {
        Type[] filling =
            [.. typeof(ManagedSenkushaRun).Assembly.GetTypes()
                .Where(one => one is { IsClass: true, IsAbstract: false })
                .Where(one => typeof(ISenkushaRunHost).IsAssignableFrom(one))];

        Assert.Contains(typeof(ManagedSenkushaRunHost), filling);

        // And the census agrees, which is the row PP790 put back coming off again.
        Assert.DoesNotContain(
            SeamReach.Expected, one => one.Interface.Contains("ISenkushaRunHost", StringComparison.Ordinal));
    }

    /// <summary>
    /// PP791: the version request carries ONE version, unpacked, which nanopb's encoder writes.
    ///
    /// chiaki_pb_encode_list writes a tag before every item, so a single-element repeated uint32 is
    /// a tag and a varint - not the length-delimited block proto3 would pack it into. A console
    /// reading a packed field where an unpacked one was promised sees no versions at all.
    /// </summary>
    [Fact]
    public void TheVersionRequestIsOneUnpackedVersion()
    {
        SenkushaSend message = SenkushaMessages.SetVersion();

        Assert.Equal(SenkushaMessages.OrdinaryData, message.DataType);
        Assert.Equal(SenkushaMessage.TakionProtocolRequest, message.PayloadType);

        // Field 31, wire type 2, two bytes long: field 1 as a varint of nine.
        Assert.Equal([0xFA, 0x01, 0x02, 0x08, 0x09], message.Body[^5..]);

        Tkproto.TakionMessage parsed = Tkproto.TakionMessage.Parser.ParseFrom(message.Body);
        Assert.Equal(Tkproto.TakionMessage.Types.PayloadType.Takionprotocolrequest, parsed.Type);
        Assert.Equal([SenkushaMessages.SupportedVersion], parsed.TakionProtocolRequest.SupportedTakionVersions);
    }

    /// <summary>
    /// PP791: senkusha's BIG carries three EMPTY strings, and empty is not absent.
    ///
    /// The C sets all three through chiaki_pb_encode_string with "", and nanopb writes the field
    /// with a length of zero. A writer that skipped them sends a shorter message the console reads
    /// as a different one - and there is nothing to put in them, because senkusha launches nothing.
    /// </summary>
    [Fact]
    public void TheBigCarriesThreeEmptyStringsAndAVersion()
    {
        SenkushaSend message = SenkushaMessages.Big();

        Assert.Equal(SenkushaMessages.OrdinaryData, message.DataType);
        Assert.Equal(SenkushaMessage.Big, message.PayloadType);

        Tkproto.TakionMessage parsed = Tkproto.TakionMessage.Parser.ParseFrom(message.Body);
        Assert.Equal(Tkproto.TakionMessage.Types.PayloadType.Big, parsed.Type);
        Assert.Equal(SenkushaMessages.BigClientVersion, parsed.BigPayload.ClientVersion);

        // The three tags with a length of zero, in the payload's own bytes: fields 2, 3 and 4 at
        // wire type 2. Asserted on the encoding because a parser reports an absent string and an
        // empty one the same way, which is the difference this claim is about.
        byte[] body = message.Body;

        foreach (int field in new[]
            {
                SenkushaExchangeParticipant.SessionKeyField,
                SenkushaExchangeParticipant.LaunchSpecField,
                SenkushaExchangeParticipant.EncryptedKeyField,
            })
        {
            byte[] empty = [(byte)((field << 3) | 2), 0x00];
            Assert.True(
                Contains(body, empty),
                $"BigPayload field {field} is not present with a length of zero");
        }
    }

    /// <summary>PP791: and the disconnect says what the C says, byte for byte.</summary>
    [Fact]
    public void TheDisconnectCarriesTheCsOwnReason()
    {
        SenkushaSend message = SenkushaMessages.Disconnect();

        Assert.Equal(SenkushaMessage.Disconnect, message.PayloadType);

        Tkproto.TakionMessage parsed = Tkproto.TakionMessage.Parser.ParseFrom(message.Body);
        Assert.Equal(SenkushaMessages.DisconnectReason, parsed.DisconnectPayload.Reason);
    }

    /// <summary>
    /// PP791: the four SENKUSHA-typed commands, each under the data type an ack rides on.
    ///
    /// Eight and not one. What tells senkusha's protobufs apart on channel one is that byte, and
    /// PP419 found the recording emitting it where the payload type belonged - so both are carried
    /// and both are asserted.
    /// </summary>
    [Fact]
    public void TheFourCommandsGoUnderTheAckedDataType()
    {
        SenkushaSend echo = SenkushaMessages.EchoCommandMessage(true);
        SenkushaSend mtu = SenkushaMessages.MtuCommandMessage(3, 1454);
        SenkushaSend client = SenkushaMessages.ClientMtuCommandMessage(1, 1200, true, 1200);

        foreach (SenkushaSend one in new[] { echo, mtu, client })
        {
            Assert.Equal(SenkushaMessages.CommandData, one.DataType);
            Assert.Equal(SenkushaMessage.Senkusha, one.PayloadType);
        }

        Tkproto.SenkushaPayload enabled =
            Tkproto.TakionMessage.Parser.ParseFrom(echo.Body).SenkushaPayload;
        Assert.Equal(Tkproto.SenkushaPayload.Types.Command.EchoCommand, enabled.Command);
        Assert.True(enabled.EchoCommand.State);

        Assert.False(
            Tkproto.TakionMessage.Parser.ParseFrom(
                SenkushaMessages.EchoCommandMessage(false).Body).SenkushaPayload.EchoCommand.State);

        Tkproto.SenkushaPayload probe =
            Tkproto.TakionMessage.Parser.ParseFrom(mtu.Body).SenkushaPayload;
        Assert.Equal(Tkproto.SenkushaPayload.Types.Command.MtuCommand, probe.Command);
        Assert.Equal(3u, probe.MtuCommand.Id);
        Assert.Equal(1454u, probe.MtuCommand.MtuReq);
        Assert.Equal(1u, probe.MtuCommand.Num);

        // The client command's own state field is THREE, where the echo command's is one. Three
        // lines apart in the proto and the same word for both.
        Tkproto.SenkushaPayload opening =
            Tkproto.TakionMessage.Parser.ParseFrom(client.Body).SenkushaPayload;
        Assert.Equal(Tkproto.SenkushaPayload.Types.Command.ClientMtuCommand, opening.Command);
        Assert.Equal(1u, opening.ClientMtuCommand.Id);
        Assert.Equal(1200u, opening.ClientMtuCommand.MtuReq);
        Assert.True(opening.ClientMtuCommand.State);
        Assert.Equal(1200u, opening.ClientMtuCommand.MtuDown);
    }

    /// <summary>
    /// PP791: a ping is a v7 audio header, four zero bytes, then the tag.
    ///
    /// The offset is the whole of it. <see cref="SenkushaArrivals"/> reads a pong's tag four bytes
    /// into the payload and compares it, so a ping writing the tag anywhere else is answered by a
    /// pong that matches nothing and every round trip is a timeout.
    ///
    /// AND THE HEADER IS EXACTLY MTU_AV_PACKET_ADD. The outbound test asserts that in the C, because
    /// its probe length is computed from the constant rather than from what the formatter wrote.
    /// </summary>
    [Fact]
    public void APingIsAHeaderFourZerosAndTheTag()
    {
        byte[] packet = new byte[SenkushaMeasurements.PingBufferBytes];
        Array.Fill(packet, (byte)0xEE);

        Assert.True(ManagedSenkushaRunHost.WritePing(packet, 7, 3, 0xDEADBEEF, out int headerSize));
        Assert.Equal((int)SenkushaMeasurements.AvPacketAdd, headerSize);

        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(headerSize, 4)));
        Assert.Equal(
            0xDEADBEEF,
            BinaryPrimitives.ReadUInt32BigEndian(
                packet.AsSpan(headerSize + SenkushaMeasurements.TagOffsetPastHeader, 4)));

        // And the C's own parse reads back what was asked for, which is what makes the header a
        // header rather than eighteen bytes of something.
        V7AvHeader? read = AvPacketV7.Parse(packet, out ChiakiError error);
        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(read);
        Assert.False(read.Value.IsVideo);
        Assert.Equal((ushort)7, read.Value.FrameIndex);
        Assert.Equal((ushort)3, read.Value.UnitIndex);
        Assert.Equal(SenkushaMeasurements.PingCodec, read.Value.Codec);

        // The tag sits where the arrivals look for it, counted from the payload rather than the
        // packet - which is the join between the sender and the arm that answers it.
        Assert.Equal(
            headerSize + SenkushaArrivals.TagOffset,
            headerSize + SenkushaMeasurements.TagOffsetPastHeader);
    }

    /// <summary>
    /// PP791: the outbound buffer is zero through the header and the tag, then CHIAKI repeating.
    ///
    /// Sized for the search's ceiling minus the two headers, because every smaller probe is a prefix
    /// of it - which is why one allocation serves a whole search and the padding is written once.
    /// </summary>
    [Fact]
    public void TheOutboundBufferIsPaddedPastTheTag()
    {
        byte[] packet = ManagedSenkushaRunHost.OutboundBuffer(SenkushaMeasurements.MtuMax);

        Assert.Equal(
            (int)(SenkushaMeasurements.MtuMax - SenkushaMeasurements.UdpPacketAdd), packet.Length);

        int from = (int)SenkushaMeasurements.AvPacketAdd + SenkushaArrivals.TagEnd;
        Assert.All(packet[..from], one => Assert.Equal(0, one));

        ReadOnlySpan<byte> padding = SenkushaMeasurements.OutboundPadding;
        Assert.Equal(padding[0], packet[from]);
        Assert.Equal(padding[1], packet[from + 1]);
        Assert.Equal(padding[0], packet[from + padding.Length]);
    }

    /// <summary>
    /// PP791: the three waits are three different numbers, and each state gets its own.
    ///
    /// The connect's thirty seconds is the longest wait in the tree and the pong's one second the
    /// shortest, in the same file. A run that gave every state EXPECT_TIMEOUT_MS would spend fifty
    /// seconds on ten pings nobody is answering, and would give up on a console still connecting.
    /// </summary>
    [Fact]
    public void EachStateWaitsTheMacroTheCGivesIt()
    {
        Assert.Equal(SenkushaTimeouts.ConnectMs, SenkushaTimeouts.For(SenkushaState.TakionConnect));
        Assert.Equal(SenkushaTimeouts.ExpectPongMs, SenkushaTimeouts.For(SenkushaState.ExpectPong));
        Assert.Equal(SenkushaTimeouts.ExpectMs, SenkushaTimeouts.For(SenkushaState.ExpectBang));
        Assert.Equal(SenkushaTimeouts.ExpectMs, SenkushaTimeouts.For(SenkushaState.ExpectDataAck));

        Assert.Equal(30000, SenkushaTimeouts.ConnectMs);
        Assert.Equal(5000, SenkushaTimeouts.ExpectMs);
        Assert.Equal(1000, SenkushaTimeouts.ExpectPongMs);

        // The host's own table follows it, and its knobs are what a driver shortens.
        var host = new ManagedSenkushaRunHost(
            new ManagedTakion(tagLocal: 0x5A5A5A5A), new IPEndPoint(IPAddress.Loopback, 9297), new SenkushaArrivals())
        {
            ExpectTimeoutMs = 11,
            ConnectTimeoutMs = 22,
            ExpectPongTimeoutMs = 33,
        };

        Assert.Equal(22, host.TimeoutFor(SenkushaState.TakionConnect));
        Assert.Equal(33, host.TimeoutFor(SenkushaState.ExpectPong));
        Assert.Equal(11, host.TimeoutFor(SenkushaState.ExpectMtu));
    }

    /// <summary>
    /// PP791: the run walks this host and stops where the host says it does.
    ///
    /// Driven with no console: the arrivals never finish a wait, so the run leaves at the first one
    /// and by the label a failed connect takes - which is `quit` and NOT the one that closes,
    /// because a connect that failed opened nothing.
    /// </summary>
    [Fact]
    public void AHostWhoseConnectFailsLeavesByTheLabelThatClosesNothing()
    {
        var host = new ManagedSenkushaRunHost(
            new ManagedTakion(tagLocal: 0x5A5A5A5A), new IPEndPoint(IPAddress.Loopback, 1), new SenkushaArrivals())
        {
            HandshakeTimeoutMs = 1,
        };

        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        Assert.Equal(SenkushaExit.Quit, reading.Exit);
        Assert.Equal(SenkushaRung.Start, reading.Rung);
        Assert.NotNull(host.LastHandshake);

        // Nothing was sent, because nothing past the connect was reached.
        Assert.Empty(host.Sent);
    }

    /// <summary>Whether one byte sequence occurs inside another.</summary>
    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.AsSpan(at, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
    }
}
