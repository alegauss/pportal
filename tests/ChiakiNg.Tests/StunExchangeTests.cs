using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP452, PP340: one STUN binding exchange over a real socket.
///
/// PP33 built the request and read the response, and PP340 recorded that nothing sent either. So the
/// twenty bytes out and the address back had only ever been checked against each other, which is the
/// one arrangement that cannot catch a field at the wrong offset - both halves would be wrong
/// together. Here a stub server answers on loopback with bytes this port composed, and the address
/// comes back through the reader.
///
/// The last two tests are the ones worth the socket. StunMessage skips an attribute by 4 + length
/// with no rounding, and its own note says that works "because the servers in the list send only
/// aligned attributes". A conformant response with a five-byte attribute is now sent, and what
/// happens to it is measured.
/// </summary>
public class StunExchangeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs one exchange against a stub server that answers with whatever
    /// <paramref name="answer"/> makes of the request.
    /// </summary>
    private static async Task<StunExchangeResult> AgainstAsync(Func<byte[], byte[]> answer)
    {
        using var timeout = new CancellationTokenSource(Patience);

        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;

        Task serving = Task.Run(
            async () =>
            {
                byte[] buffer = new byte[1500];
                SocketReceiveFromResult got = await server.ReceiveFromAsync(
                    buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

                byte[] reply = answer(buffer[..got.ReceivedBytes]);
                await server.SendToAsync(reply, SocketFlags.None, got.RemoteEndPoint, timeout.Token);
            },
            timeout.Token);

        using var client = new StunExchange();
        client.Bind();

        StunExchangeResult result = await client.ExchangeAsync(
            serverEndPoint, timeout: Patience, cancellationToken: timeout.Token);

        await serving;
        return result;
    }

    /// <summary>The twelve bytes a request carries, which a response has to echo.</summary>
    private static byte[] TransactionIdOf(byte[] request) => request[8..20];

    /// <summary>
    /// A v4 mapped address makes the round trip: composed, sent, received, read.
    /// </summary>
    [Fact]
    public async Task AnXoredV4AddressCrossesTheSocket()
    {
        StunExchangeResult result = await AgainstAsync(request => StunMessage.BuildBindingResponse(
            TransactionIdOf(request), IPAddress.Parse("203.0.113.7"), 51234));

        Assert.Equal(StunResult.Ok, result.Result);
        Assert.False(result.TimedOut);
        Assert.Equal("203.0.113.7", result.Mapped?.Address);
        Assert.Equal((ushort)51234, result.Mapped?.Port);
    }

    /// <summary>
    /// And a v6 one, which is the test the XOR key was worth writing.
    ///
    /// The key is the request buffer from offset 4 - the cookie followed by the transaction id - and
    /// the builder assembles it from the two pieces separately. If the reader's idea of the key and
    /// the wire's disagreed by even a byte, the address would come back as noise rather than as an
    /// error, which is why this crosses a socket rather than being asserted against a fixture.
    /// </summary>
    [Fact]
    public async Task AnXoredV6AddressCrossesTheSocket()
    {
        StunExchangeResult result = await AgainstAsync(request => StunMessage.BuildBindingResponse(
            TransactionIdOf(request), IPAddress.Parse("2001:db8::1"), 3478));

        Assert.Equal(StunResult.Ok, result.Result);
        // The `!` is load-bearing: SonarLint's S8969 calls it redundant here and the compiler
        // disagrees with CS8629, because Mapped is a nullable StunResponse and nothing above narrows
        // it. Assert.Equal on the Result above does not count as a narrowing.
        Assert.Equal(IPAddress.Parse("2001:db8::1"), IPAddress.Parse(result.Mapped!.Value.Address));
        Assert.Equal((ushort)3478, result.Mapped?.Port);
    }

    /// <summary>
    /// The plain MAPPED-ADDRESS is read too, unobfuscated - and PP33 recorded that whichever of the
    /// two arrives first is believed, plain one included.
    /// </summary>
    [Fact]
    public async Task APlainMappedAddressIsReadWithoutTheXor()
    {
        StunExchangeResult result = await AgainstAsync(request => StunMessage.BuildBindingResponse(
            TransactionIdOf(request), IPAddress.Parse("198.51.100.42"), 1234, xored: false));

        Assert.Equal(StunResult.Ok, result.Result);
        Assert.Equal("198.51.100.42", result.Mapped?.Address);
        Assert.Equal((ushort)1234, result.Mapped?.Port);
    }

    /// <summary>
    /// An answer echoing somebody else's transaction id is refused - which is the only thing telling
    /// this answer from a stale one on a port anything can reach.
    /// </summary>
    [Fact]
    public async Task AnAnswerWithTheWrongTransactionIdIsRefused()
    {
        StunExchangeResult result = await AgainstAsync(_ => StunMessage.BuildBindingResponse(
            new byte[StunMessage.TransactionIdLength], IPAddress.Parse("203.0.113.7"), 51234));

        Assert.Equal(StunResult.WrongTransactionId, result.Result);
        Assert.Null(result.Mapped);
        Assert.False(result.TimedOut);
    }

    /// <summary>Silence is silence, and is not reported as a malformed answer.</summary>
    [Fact]
    public async Task NothingAnsweringIsATimeoutAndNotARefusal()
    {
        using var timeout = new CancellationTokenSource(Patience);

        // A bound-and-never-read socket rather than a closed port: a closed one draws an ICMP
        // rejection, and this is the case where the datagram simply goes unanswered.
        using var quiet = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        quiet.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        using var client = new StunExchange();
        client.Bind();

        StunExchangeResult result = await client.ExchangeAsync(
            (IPEndPoint)quiet.LocalEndPoint!,
            timeout: TimeSpan.FromMilliseconds(250),
            cancellationToken: timeout.Token);

        Assert.True(result.TimedOut);
        Assert.Null(result.Mapped);
    }

    /// <summary>
    /// A fresh transaction id per request, which RFC 5389 asks for and
    /// <see cref="StunResult.WrongTransactionId"/> is the reason for.
    /// </summary>
    [Fact]
    public async Task EachRequestCarriesAFreshTransactionId()
    {
        using var timeout = new CancellationTokenSource(Patience);

        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var to = (IPEndPoint)server.LocalEndPoint!;

        using var client = new StunExchange();
        client.Bind();

        await client.ExchangeAsync(to, timeout: TimeSpan.FromMilliseconds(100), cancellationToken: timeout.Token);
        byte[] first = client.LastTransactionId!;

        await client.ExchangeAsync(to, timeout: TimeSpan.FromMilliseconds(100), cancellationToken: timeout.Token);
        byte[] second = client.LastTransactionId!;

        Assert.Equal(StunMessage.TransactionIdLength, first.Length);
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// THE CONTROL: an attribute whose length is already a multiple of four is stepped over correctly
    /// and the mapped address behind it is found.
    /// </summary>
    [Fact]
    public async Task AnAlignedAttributeBeforeTheAddressIsSteppedOver()
    {
        StunExchangeResult result = await AgainstAsync(request => StunMessage.BuildBindingResponse(
            TransactionIdOf(request),
            IPAddress.Parse("203.0.113.7"),
            51234,
            leading: (Type: (ushort)0x8022, Value: [1, 2, 3, 4])));

        Assert.Equal(StunResult.Ok, result.Result);
        Assert.Equal("203.0.113.7", result.Mapped?.Address);
    }

    /// <summary>
    /// PP453: and a conformant response carrying a FIVE-byte attribute now arrives with its address
    /// intact.
    ///
    /// This test is the reason PP453 exists. When it was written the assertion was the opposite: the
    /// cursor landed three bytes inside the RFC's padding, read a length of 2048 out of the mapped
    /// address's own bytes, and the message was refused as overrunning while the address sat in the
    /// datagram. Measuring that over a socket is what turned PP200's accepted note into a repair.
    /// </summary>
    [Fact]
    public async Task AConformantPaddedAttributeKeepsItsAddress()
    {
        StunExchangeResult result = await AgainstAsync(request => StunMessage.BuildBindingResponse(
            TransactionIdOf(request),
            IPAddress.Parse("203.0.113.7"),
            51234,
            leading: (Type: (ushort)0x8022, Value: [1, 2, 3, 4, 5])));

        Assert.Equal(StunResult.Ok, result.Result);
        Assert.Equal("203.0.113.7", result.Mapped?.Address);
        Assert.Equal((ushort)51234, result.Mapped?.Port);
        Assert.False(result.TimedOut);
    }

    /// <summary>
    /// The builder and the reader agree without a socket too, which is what makes the socket tests
    /// about the wire rather than about the arithmetic.
    /// </summary>
    [Fact]
    public void TheBuilderAndTheReaderAgreeOffTheWire()
    {
        byte[] id = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        byte[] request = StunMessage.BuildBindingRequest(id);

        byte[] response = StunMessage.BuildBindingResponse(id, IPAddress.Parse("192.0.2.33"), 9999);

        StunResponse? mapped = StunMessage.Read(response, request, out StunResult result);

        Assert.Equal(StunResult.Ok, result);
        Assert.Equal("192.0.2.33", mapped?.Address);
        Assert.Equal((ushort)9999, mapped?.Port);
    }

    /// <summary>A response is refused where the id is not twelve bytes, rather than built short.</summary>
    [Fact]
    public void TheBuilderRefusesAnIdOfTheWrongLength()
    {
        Assert.Throws<ArgumentException>(
            () => StunMessage.BuildBindingResponse(new byte[11], IPAddress.Loopback, 1));
    }
}
