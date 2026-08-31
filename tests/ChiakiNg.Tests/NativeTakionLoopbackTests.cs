using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP607, under PP27: the real C takion completes a handshake against this port's own responder.
///
/// Everything from PP601 to PP606 was written to make this run. takion's receive loop is bound to a
/// socket and a thread and every handler on it is file-local, so it is reached by BEING a peer
/// rather than by exposing a function - which is what the vendored-C non-goal leaves open.
///
/// What this proves is not timing yet. It is that the loop can be driven at all: the INIT that goes
/// out is the C's own, the INIT_ACK and COOKIE_ACK that come back are this port's, and the connected
/// event firing is takion saying its handshake completed.
/// </summary>
public class NativeTakionLoopbackTests(ITestOutputHelper output)
{
    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0x3C + i))];

    /// <summary>
    /// THE RUN. A takion connects, our responder answers, and the connected event fires.
    ///
    /// Bounded on both sides: the socket has a receive timeout and the loop has a deadline, so a
    /// responder that stopped answering fails this test rather than hanging the suite. That is
    /// PP117's lesson, which this repository pays whenever a test owns a thread it did not write.
    /// </summary>
    [Fact]
    public void ARealTakionCompletesTheHandshakeAgainstOurResponder()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Client.ReceiveTimeout = 2000;

        ushort port = (ushort)((IPEndPoint)peer.Client.LocalEndPoint!).Port;
        var responder = new TakionHandshakeResponder(0x55667788, Cookie);

        NativeTakionLoopback? takion =
            NativeTakionLoopback.TryConnect(port, protocolVersion: 9, out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(takion);

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            var from = new IPEndPoint(IPAddress.Any, 0);

            while (DateTime.UtcNow < deadline && !takion!.Connected)
            {
                byte[] arrived;

                try
                {
                    arrived = peer.Receive(ref from);
                }
                catch (SocketException)
                {
                    // The receive timed out: takion is between retries, or it has given up.
                    continue;
                }

                output.WriteLine($"{arrived.Length} bytes from the C");

                if (responder.Answer(arrived) is { } reply)
                    peer.Send(reply, reply.Length, from);
            }

            Assert.True(
                takion!.Connected,
                $"the connected event did not fire. Responder state {responder.State}, "
                    + $"{responder.InitsSeen} init(s) seen, {takion.EventCount} event(s)");

            Assert.Equal(TakionResponderState.Done, responder.State);
        }
        finally
        {
            // Joins the C thread before anything else happens, whatever the assertions did.
            takion?.Dispose();
        }
    }

    /// <summary>
    /// Port zero is not a peer, and the C says so rather than connecting to whatever the OS picks.
    /// </summary>
    [Fact]
    public void PortZeroIsRefused()
    {
        Assert.Null(NativeTakionLoopback.TryConnect(0, 9, out ChiakiError error));
        Assert.Equal(ChiakiError.InvalidData, error);
    }

    /// <summary>
    /// An unknown protocol version is refused by the C's own switch, before any socket is made.
    ///
    /// Worth a case because it is the one argument this entry point passes straight through to a
    /// decision takion makes, and a harness that quietly connected on an unknown version would be
    /// measuring a parse path nothing selects.
    /// </summary>
    [Fact]
    public void AnUnknownProtocolVersionIsRefused()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        ushort port = (ushort)((IPEndPoint)peer.Client.LocalEndPoint!).Port;

        NativeTakionLoopback? takion = NativeTakionLoopback.TryConnect(port, 3, out ChiakiError error);

        using (takion)
        {
            Assert.Null(takion);
            Assert.Equal(ChiakiError.InvalidData, error);
        }
    }

    /// <summary>Disposing twice is not a double free, which a harness will do on every failure path.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        ushort port = (ushort)((IPEndPoint)peer.Client.LocalEndPoint!).Port;

        NativeTakionLoopback? takion = NativeTakionLoopback.TryConnect(port, 9, out _);
        Assert.NotNull(takion);

        takion!.Dispose();
        takion.Dispose();

        Assert.False(takion.Connected);
        Assert.Equal(0, takion.EventCount);
    }
}
