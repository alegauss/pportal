using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP455, PP340: the response's masked tail, sent and un-masked.
///
/// PP236 built the tail - the candidate's address and port hidden under the session ids - and PP268
/// sent the probe but never the answer. So the one part of this layout with a key in it had crossed a
/// socket in neither direction, and the key was checked only against the code that wrote it. That is
/// the arrangement PP268 exists to object to.
///
/// The last two tests are the ones that needed the un-masking rather than the socket: an IPv6
/// candidate's response carries its first four bytes and nothing else, which was prose in two places -
/// and the two places disagreed about it.
/// </summary>
public class PunchResponseTailTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private const ushort SidLocal = 0x1234;
    private const ushort SidConsole = 0xabcd;

    private static byte[] LocalId => [.. Enumerable.Range(1, PunchResponse.IdLength).Select(i => (byte)i)];

    private static byte[] ConsoleId => [.. Enumerable.Range(101, PunchResponse.IdLength).Select(i => (byte)i)];

    private static byte[] RequestId => [0xde, 0xad, 0xbe, 0xef, 0x42];

    private static byte[] Probe()
        => PunchProbe.Build(RequestId, LocalId, ConsoleId, SidLocal, SidConsole);

    /// <summary>Whether <paramref name="needle"/> occurs as a contiguous run in <paramref name="haystack"/>.</summary>
    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.AsSpan(at, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Answers a probe over loopback and hands back the datagram the console side received.
    /// </summary>
    private static async Task<byte[]?> AnsweredAsync(string candidateAddress, ushort candidatePort)
    {
        using var timeout = new CancellationTokenSource(Patience);

        using var console = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        console.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        using var client = new ProbeExchange();
        client.Bind();

        byte[]? sent = await client.AnswerAsync(
            (IPEndPoint)console.LocalEndPoint!,
            Probe(),
            LocalId,
            ConsoleId,
            SidLocal,
            SidConsole,
            candidateAddress,
            candidatePort,
            timeout.Token);

        if (sent is null)
            return null;

        byte[] buffer = new byte[PunchResponse.Length * 2];
        SocketReceiveFromResult got = await console.ReceiveFromAsync(
            buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

        return buffer[..got.ReceivedBytes];
    }

    /// <summary>
    /// The address and port come back out of the tail on the other side of a socket.
    /// </summary>
    [Fact]
    public async Task TheMaskedTailRoundTripsThroughASocket()
    {
        byte[]? datagram = await AnsweredAsync("203.0.113.7", 51234);

        Assert.NotNull(datagram);
        Assert.Equal(PunchResponse.Length, datagram.Length);

        MaskedTail? tail = PunchResponse.ReadMaskedTail(datagram, SidLocal, SidConsole);

        Assert.NotNull(tail);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), tail.Value.AsIpv4());
        Assert.Equal((ushort)51234, tail.Value.Port);
    }

    /// <summary>
    /// And the five echoed bytes survive it, which is what the console matches the answer on.
    /// </summary>
    [Fact]
    public async Task TheEchoedBytesSurviveTheSocket()
    {
        byte[]? datagram = await AnsweredAsync("203.0.113.7", 51234);

        Assert.NotNull(datagram);
        Assert.Equal(
            RequestId,
            datagram.AsSpan(PunchResponse.EchoAt, PunchResponse.EchoLength).ToArray());

        // And the type is the response's, not the probe's.
        Assert.Equal(
            PunchResponse.ResponseType,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(datagram));
    }

    /// <summary>
    /// THE KEY IS LOAD-BEARING: un-masking with the wrong session ids recovers something else.
    ///
    /// Without this, every assertion above would pass just as well if the tail were sent in clear -
    /// which is exactly the mistake PP236's note says a reader might make of the second copy of the
    /// ids.
    /// </summary>
    [Fact]
    public async Task TheWrongSessionIdsRecoverTheWrongAddress()
    {
        byte[]? datagram = await AnsweredAsync("203.0.113.7", 51234);
        Assert.NotNull(datagram);

        MaskedTail? wrong = PunchResponse.ReadMaskedTail(datagram, SidLocal, (ushort)(SidConsole ^ 1));

        Assert.NotNull(wrong);
        Assert.NotEqual(IPAddress.Parse("203.0.113.7"), wrong.Value.AsIpv4());

        // The port is keyed by sid_local alone, so a wrong sid_console leaves it correct - which is
        // the asymmetry the third copy of the local id creates.
        Assert.Equal((ushort)51234, wrong.Value.Port);
    }

    /// <summary>And the port's own key is sid_local, so getting that wrong moves the port.</summary>
    [Fact]
    public async Task TheWrongLocalIdMovesThePortToo()
    {
        byte[]? datagram = await AnsweredAsync("203.0.113.7", 51234);
        Assert.NotNull(datagram);

        MaskedTail? wrong = PunchResponse.ReadMaskedTail(datagram, (ushort)(SidLocal ^ 1), SidConsole);

        Assert.NotNull(wrong);
        Assert.NotEqual((ushort)51234, wrong.Value.Port);
    }

    /// <summary>A v4 candidate's four bytes are the whole of its address.</summary>
    [Fact]
    public async Task AV4CandidateSendsItsWholeAddress()
    {
        byte[]? datagram = await AnsweredAsync("198.51.100.42", 1234);
        Assert.NotNull(datagram);

        MaskedTail tail = PunchResponse.ReadMaskedTail(datagram, SidLocal, SidConsole)!.Value;

        Assert.True(tail.AreTheWholeAddress(IPAddress.Parse("198.51.100.42")));
    }

    /// <summary>
    /// THE MEASUREMENT: a v6 candidate's response carries its first four bytes and nothing else.
    ///
    /// PP236's note said the other twelve were "sent plain" and PP33's said they were "never sent at
    /// all". The tail runs 0x50 to 0x55 and the packet ends at 0x58, so PP33 was right - and PP455
    /// corrected the other sentence. This is the assertion behind the correction: the four bytes come
    /// back, they are the address's first four, and they are not the address.
    /// </summary>
    [Fact]
    public async Task AV6CandidateSendsOnlyItsFirstFourBytes()
    {
        // Every byte distinct and non-zero past the fourth, so "the rest never left" is checkable
        // rather than true by accident - "2001:db8::1" is zeros from byte 4 and would pass either way.
        const string text = "2001:db8:1122:3344:5566:7788:99aa:bbcc";
        var offered = IPAddress.Parse(text);

        byte[]? datagram = await AnsweredAsync(text, 3478);
        Assert.NotNull(datagram);
        Assert.Equal(PunchResponse.Length, datagram.Length);

        MaskedTail tail = PunchResponse.ReadMaskedTail(datagram, SidLocal, SidConsole)!.Value;

        // The first four of sixteen, and the port, which does fit.
        Assert.Equal(offered.GetAddressBytes()[..4], tail.AddressBytes);
        Assert.Equal((ushort)3478, tail.Port);

        // And they are not the address that was offered.
        Assert.False(tail.AreTheWholeAddress(offered));

        // The other twelve are nowhere in the datagram - not sent plain, not sent at all. As a
        // SEQUENCE: individual byte values recur by chance in eighty-eight bytes, and asserting on
        // those reported a failure the first time this was written.
        byte[] rest = offered.GetAddressBytes()[4..];
        Assert.False(Contains(datagram, rest), "the rest of the v6 address is in the datagram");

        // And nothing at all sits past the tail, which is why there was never room for it.
        Assert.All(
            datagram[(PunchResponse.PortKeyAt + 2)..].ToArray(),
            b => Assert.Equal((byte)0, b));
    }

    /// <summary>An address the family test refuses sends nothing at all.</summary>
    [Fact]
    public async Task AnAddressThatWillNotParseSendsNothing()
    {
        // A v4-mapped v6 literal: it has a dot, so PP236's family test hands it to the v4 parser.
        Assert.Null(await AnsweredAsync("::ffff:1.2.3.4", 1234));
    }

    /// <summary>The reader refuses a datagram too short to hold a tail.</summary>
    [Fact]
    public void ReadMaskedTailRefusesAShortPacket()
    {
        Assert.Null(PunchResponse.ReadMaskedTail(new byte[PunchResponse.PortKeyAt + 1], 1, 2));
        Assert.Null(PunchResponse.ReadMaskedTail([], 1, 2));
    }

    /// <summary>
    /// The builder and the reader agree off the wire too, which is what makes the socket tests about
    /// the wire rather than about the arithmetic.
    /// </summary>
    [Fact]
    public void TheBuilderAndTheReaderAgreeOffTheWire()
    {
        byte[]? packet = PunchResponse.Build(
            Probe(), LocalId, ConsoleId, SidLocal, SidConsole, "192.0.2.33", 9999);

        Assert.NotNull(packet);

        MaskedTail tail = PunchResponse.ReadMaskedTail(packet, SidLocal, SidConsole)!.Value;

        Assert.Equal(IPAddress.Parse("192.0.2.33"), tail.AsIpv4());
        Assert.Equal((ushort)9999, tail.Port);
    }
}
