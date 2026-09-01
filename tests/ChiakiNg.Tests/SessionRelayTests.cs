using System.Net;
using System.Net.Sockets;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP613, under PP27: the relay carries a session's two ports and sees whole datagrams.
///
/// PP612 settled that the tap's eighteen bytes are the vendored C's decision, so recording payloads
/// means either patching libchiaki - which a non-goal refuses and PP27 is not exempt from - or
/// carrying the traffic. This carries it.
///
/// The console here is a socket in this process, because what is being tested is the forwarding.
/// Whether a PS5 answers through it is a run, not an assertion - PP22's line about what only a
/// runner can say applies to hardware too.
/// </summary>
public class SessionRelayTests
{
    /// <summary>The two ports the local path uses, which is why this is a forwarder and not a proxy.</summary>
    [Fact]
    public void ThePortsAreTheOnesTheLocalPathUses()
    {
        Assert.Equal(9295, SessionRelay.ControlPort);
        Assert.Equal(9296, SessionRelay.StreamPort);
        Assert.Equal("127.0.0.1", SessionRelay.Via);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a datagram to the console is carried whole, reported whole, and
    /// arrives.
    ///
    /// Whole is the point. The tap gives eighteen bytes; this gives the datagram, which is what
    /// PP27's remaining half has to be fed.
    /// </summary>
    [Fact]
    public void ADatagramReachesTheConsoleWholeAndIsReported()
    {
        using var console = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        console.Client.ReceiveTimeout = 5000;
        var consoleStream = (IPEndPoint)console.Client.LocalEndPoint!;

        var seen = new List<(byte[] Bytes, bool FromConsole)>();
        
        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            consoleStream,
            (bytes, fromConsole) => seen.Add((bytes, fromConsole)),
            0,
            0);

        relay.Start();

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        byte[] payload = [.. Enumerable.Range(0, 1400).Select(i => (byte)(i & 0xff))];
        client.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, relay.StreamPortInUse));

        var from = new IPEndPoint(IPAddress.Any, 0);
        byte[] arrived = console.Receive(ref from);

        Assert.Equal(payload, arrived);
        Assert.True(arrived.Length > 18, "the relay truncated, which is the thing it exists not to do");

        SpinWait.SpinUntil(() => seen.Count > 0, TimeSpan.FromSeconds(5));
        (byte[] bytes, bool fromConsole) = Assert.Single(seen);

        Assert.Equal(payload, bytes);
        Assert.False(fromConsole);
    }

    /// <summary>
    /// And the console's answer comes back to the client, marked as the console's.
    ///
    /// PP617: the direction is which SOCKET it arrived on, not which endpoint sent it. The first
    /// version compared endpoints on one socket, and it passed here because both ends of a test are
    /// on loopback - while a real console got datagrams from a loopback-bound socket and had
    /// nowhere to answer. This sends the reply where a console's reply actually lands: the port the
    /// relay used to reach it.
    /// </summary>
    [Fact]
    public void TheConsolesAnswerComesBackMarked()
    {
        using var console = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        console.Client.ReceiveTimeout = 5000;
        var consoleStream = (IPEndPoint)console.Client.LocalEndPoint!;

        var seen = new List<(byte[] Bytes, bool FromConsole)>();
        
        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            consoleStream,
            (bytes, fromConsole) => seen.Add((bytes, fromConsole)),
            0,
            0);

        relay.Start();

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        client.Client.ReceiveTimeout = 5000;

        byte[] up = [0x02, 0xAA];
        client.Send(up, up.Length, new IPEndPoint(IPAddress.Loopback, relay.StreamPortInUse));

        var from = new IPEndPoint(IPAddress.Any, 0);
        console.Receive(ref from);

        // A console answers the source it was sent from, which is the relay's upstream socket.
        Assert.Equal(relay.UpstreamPortInUse, from.Port);

        byte[] down = [0x00, 0x11, 0x22, 0x33];
        console.Send(down, down.Length, from);

        byte[] backAtClient = client.Receive(ref from);

        Assert.Equal(down, backAtClient);
        Assert.Contains(seen, s => s.FromConsole && s.Bytes.SequenceEqual(down));
        Assert.Contains(seen, s => !s.FromConsole && s.Bytes.SequenceEqual(up));
        Assert.Equal(2, relay.DatagramsForwarded);
    }

    /// <summary>
    /// THE ONE THE FIRST VERSION COULD NOT FAIL: the two sockets are different.
    ///
    /// A relay whose upstream is the loopback socket sends to a console from an address it cannot
    /// answer. Everything else here still passes in that state, because a test's console is on
    /// loopback too - so this asserts the shape rather than the behaviour, which is the only way a
    /// test on one machine can see it.
    /// </summary>
    [Fact]
    public void TheTwoSidesAreDifferentSockets()
    {
        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            new IPEndPoint(IPAddress.Loopback, 1),
            null,
            0,
            0);

        Assert.NotEqual(relay.StreamPortInUse, relay.UpstreamPortInUse);
    }

    /// <summary>
    /// The control half accepts and connects, which is what lets a session get as far as takion.
    ///
    /// Bytes both ways over one connection: a forwarder that only carried the request would leave
    /// ctrl hanging, and ctrl is what tells the session it may start streaming.
    /// </summary>
    [Fact]
    public void TheControlHalfCarriesBytesBothWays()
    {
        var console = new TcpListener(IPAddress.Loopback, 0);
        console.Start();
        var consoleControl = (IPEndPoint)console.LocalEndpoint;

        try
        {

            using var relay = new SessionRelay(
                consoleControl,
                new IPEndPoint(IPAddress.Loopback, 1),
                null,
                0,
                0);

            relay.Start();

            using var near = new TcpClient();
            near.Connect(IPAddress.Loopback, relay.ControlPortInUse);

            using TcpClient far = console.AcceptTcpClient();

            byte[] up = [0x47, 0x45, 0x54];
            near.GetStream().Write(up);

            byte[] buffer = new byte[8];
            far.ReceiveTimeout = 5000;
            int read = far.GetStream().Read(buffer, 0, buffer.Length);

            Assert.Equal(up, buffer[..read]);

            byte[] down = [0x32, 0x30, 0x30];
            far.GetStream().Write(down);

            near.ReceiveTimeout = 5000;
            read = near.GetStream().Read(buffer, 0, buffer.Length);

            Assert.Equal(down, buffer[..read]);
        }
        finally
        {
            console.Stop();
        }
    }

    /// <summary>Disposing twice is safe, which every failure path in a harness does.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            new IPEndPoint(IPAddress.Loopback, 1),
            null,
            0,
            0);

        relay.Start();
        relay.Dispose();
        relay.Dispose();

        // Said rather than implied: the second call returning is the whole of what this holds.
        Assert.Equal(0, relay.DatagramsForwarded);
    }

    /// <summary>And a null console is refused, rather than binding and forwarding nowhere.</summary>
    [Fact]
    public void ANullConsoleIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionRelay((IPAddress)null!));
        Assert.Throws<ArgumentNullException>(
            () => new SessionRelay(null!, new IPEndPoint(IPAddress.Loopback, 1), null, 1, 2));
    }
}
