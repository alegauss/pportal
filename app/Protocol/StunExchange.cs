using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ChiakiNg.Protocol;

/// <summary>What one STUN binding exchange produced.</summary>
/// <param name="Mapped">The address the server saw, or null where none was read.</param>
/// <param name="Result">Why, where <paramref name="Mapped"/> is null.</param>
/// <param name="TimedOut">
/// Whether nothing answered at all. Kept apart from <paramref name="Result"/> because silence and a
/// malformed answer are different failures and PP259 showed the core reporting both in one sentence.
/// </param>
public readonly record struct StunExchangeResult(
    StunResponse? Mapped, StunResult Result, bool TimedOut = false);

/// <summary>
/// PP452: one STUN binding request, sent and answered, over a real socket.
///
/// PP340 named what was missing by name: StunMessage, NatProbe, PunchExchange and CandidateRace carry
/// no socket. This is the first of the four, and it is the same argument PP268 made for the punch
/// probe - <see cref="StunMessage"/> builds twenty bytes and reads an address back, and until now
/// nothing put either on a wire. A field at the wrong offset is exactly the mistake a reader checked
/// only against its own writer cannot catch, because both would be wrong together.
///
/// So the exchange runs, and the answers it can give are the ones the ported rules already name: an
/// address, one of <see cref="StunResult"/>'s ten refusals, or silence. Nothing here chooses a server
/// from the list, retries, or measures port allocation - the list, the three-call branch and the
/// allocation test belong to PP259 and PP33, and this is what they are made of.
///
/// THE TRANSACTION ID IS CRYPTOGRAPHICALLY RANDOM. RFC 5389 asks for that and the reason is this
/// port's own <see cref="StunResult.WrongTransactionId"/>: the id is the only thing separating this
/// answer from a stale one or somebody else's, on a socket that is bound to a port anything can
/// reach.
///
/// AND ONE DIVERGENCE FROM THE RFC IS NOW DEMONSTRABLE RATHER THAN ARGUED. StunMessage skips an
/// attribute by 4 + length with no rounding, which its own note says "works today because the servers
/// in the list send only aligned attributes". Over this socket a conformant response carrying a
/// five-byte attribute before the mapped address can be sent, and what the reader does with it is a
/// measurement instead of a sentence.
/// </summary>
public sealed class StunExchange : IDisposable
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>
    /// How long to wait for an answer where the caller names nothing.
    ///
    /// Not a core constant: the C's STUN receive is a blocking recvfrom under a socket timeout set
    /// where the socket is made, and this class does not own that socket's lifetime. Stated here so a
    /// caller can see what it gets rather than inherit a number from somewhere else.
    /// </summary>
    public static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(1);

    /// <summary>The port this side bound.</summary>
    public int LocalPort { get; private set; }

    /// <summary>The transaction id of the last request sent, which is also its XOR key.</summary>
    public byte[]? LastTransactionId { get; private set; }

    /// <summary>Binds to an ephemeral loopback port and reports which one.</summary>
    public int Bind()
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return LocalPort;
    }

    /// <summary>
    /// Sends one binding request and reads whatever answers it.
    /// </summary>
    /// <param name="server">The STUN server to ask.</param>
    /// <param name="transactionId">
    /// The id to use, or null for a fresh random one. A caller passes one only to reproduce a
    /// specific exchange; the random default is the behaviour.
    /// </param>
    /// <param name="timeout">How long to wait, or <see cref="DefaultTimeout"/>.</param>
    public async Task<StunExchangeResult> ExchangeAsync(
        IPEndPoint server,
        byte[]? transactionId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        byte[] id = transactionId ?? RandomNumberGenerator.GetBytes(StunMessage.TransactionIdLength);
        LastTransactionId = id;

        byte[] request = StunMessage.BuildBindingRequest(id);

        await socket.SendToAsync(request, SocketFlags.None, server, cancellationToken)
            .ConfigureAwait(false);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout ?? DefaultTimeout);

        // A whole datagram or nothing: UDP truncates silently, and a mapped address read out of a cut
        // message is PP451's failure in a different file.
        byte[] buffer = new byte[1500];

        SocketReceiveFromResult received;
        try
        {
            received = await socket.ReceiveFromAsync(
                buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), bounded.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new StunExchangeResult(null, StunResult.NoAddress, TimedOut: true);
        }
        catch (SocketException)
        {
            // An ICMP port-unreachable from a server that is not listening, which Windows reports on
            // the next receive. Silence as far as this exchange is concerned.
            return new StunExchangeResult(null, StunResult.NoAddress, TimedOut: true);
        }

        StunResponse? mapped = StunMessage.Read(
            buffer[..received.ReceivedBytes], request, out StunResult result);

        return new StunExchangeResult(mapped, result);
    }

    /// <summary>Releases the socket.</summary>
    public void Dispose()
    {
        socket.Dispose();
        GC.SuppressFinalize(this);
    }
}
