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

        var seen = new System.Collections.Concurrent.ConcurrentQueue<(byte[] Bytes, bool FromConsole)>();

        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            consoleStream,
            (bytes, fromConsole) => seen.Enqueue((bytes, fromConsole)),
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

        Assert.True(
            SpinWait.SpinUntil(() => !seen.IsEmpty, TimeSpan.FromSeconds(5)),
            "the relay carried the datagram and reported nothing");

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
    ///
    /// PP659: AND IT WAITS FOR THE COUNT RATHER THAN ASSUMING IT. This test failed alone in a suite
    /// of five thousand twice in two days and passed on every re-run, and the reason is one line of
    /// the relay: the counter is incremented AFTER the send, so a receiver holding the datagram can
    /// still read the old value. Receiving is not a receipt for the count, and asserting on it the
    /// instant the datagram lands is a race the loopback usually wins and sometimes does not.
    ///
    /// The recorder is thread-safe for a second reason, which nothing had yet hit: both forwarding
    /// directions call it, and two threads adding to one List is a corruption rather than a wrong
    /// number.
    /// </summary>
    [Fact]
    public void TheConsolesAnswerComesBackMarked()
    {
        using var console = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        console.Client.ReceiveTimeout = 5000;
        var consoleStream = (IPEndPoint)console.Client.LocalEndPoint!;

        var seen = new System.Collections.Concurrent.ConcurrentQueue<(byte[] Bytes, bool FromConsole)>();

        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            consoleStream,
            (bytes, fromConsole) => seen.Enqueue((bytes, fromConsole)),
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

        // Both directions are recorded before their send, so having the datagram means the record
        // exists - but the COUNT moves after it, so that one is waited for.
        Assert.Contains(seen, s => s.FromConsole && s.Bytes.SequenceEqual(down));
        Assert.Contains(seen, s => !s.FromConsole && s.Bytes.SequenceEqual(up));

        Assert.True(
            SpinWait.SpinUntil(() => relay.DatagramsForwarded == 2, TimeSpan.FromSeconds(5)),
            $"the relay carried both datagrams and counted {relay.DatagramsForwarded}");
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

    /// <summary>
    /// PP829: SENKUSHA'S LEG CARRIES BOTH WAYS, which is the whole of what a session needs first.
    ///
    /// The relay listened on 9295 and 9296 and not on 9297, so a session pointed at it dialled
    /// loopback for senkusha and nothing answered - the takion connect timed out and every run
    /// through the relay died before the stream. This is that port, forwarded.
    /// </summary>
    [Fact]
    public void TheSenkushaLegCarriesBothDirections()
    {
        using var consoleSenkusha = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        consoleSenkusha.Client.ReceiveTimeout = 5000;

        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            new IPEndPoint(IPAddress.Loopback, 2),
            null,
            0,
            0,
            (IPEndPoint)consoleSenkusha.Client.LocalEndPoint!,
            0);

        relay.Start();

        Assert.NotEqual(0, relay.SenkushaPortInUse);

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        client.Client.ReceiveTimeout = 5000;

        byte[] up = [.. Enumerable.Range(0, 548).Select(i => (byte)(i & 0xff))];
        client.Send(up, up.Length, new IPEndPoint(IPAddress.Loopback, relay.SenkushaPortInUse));

        var from = new IPEndPoint(IPAddress.Any, 0);
        Assert.Equal(up, consoleSenkusha.Receive(ref from));

        // And back, to where the console's answers actually land: the port the relay reached it
        // from, which is not the loopback one the client speaks to.
        byte[] down = [1, 2, 3, 4, 5];
        consoleSenkusha.Send(down, down.Length, new IPEndPoint(IPAddress.Loopback, from.Port));

        var back = new IPEndPoint(IPAddress.Any, 0);
        Assert.Equal(down, client.Receive(ref back));
    }

    /// <summary>
    /// AND THE TWO LEGS DO NOT SHARE A CLIENT, which is what a second copy would have got wrong.
    ///
    /// Each leg learns where its own client spoke from, so an answer goes back to the socket that
    /// asked. One endpoint shared between them would send senkusha's pongs to whoever last spoke on
    /// the stream port - and on a real session those are two different sockets.
    /// </summary>
    [Fact]
    public void EachLegAnswersItsOwnClient()
    {
        using var consoleStream = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var consoleSenkusha = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        consoleStream.Client.ReceiveTimeout = 5000;
        consoleSenkusha.Client.ReceiveTimeout = 5000;

        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            (IPEndPoint)consoleStream.Client.LocalEndPoint!,
            null,
            0,
            0,
            (IPEndPoint)consoleSenkusha.Client.LocalEndPoint!,
            0);

        relay.Start();

        using var onStream = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var onSenkusha = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        onStream.Client.ReceiveTimeout = 5000;
        onSenkusha.Client.ReceiveTimeout = 5000;

        // Both clients speak, so both legs have learnt an endpoint - and they are different ones.
        onStream.Send([9], 1, new IPEndPoint(IPAddress.Loopback, relay.StreamPortInUse));
        onSenkusha.Send([8], 1, new IPEndPoint(IPAddress.Loopback, relay.SenkushaPortInUse));

        var fromStream = new IPEndPoint(IPAddress.Any, 0);
        var fromSenkusha = new IPEndPoint(IPAddress.Any, 0);

        Assert.Equal([9], consoleStream.Receive(ref fromStream));
        Assert.Equal([8], consoleSenkusha.Receive(ref fromSenkusha));

        // Now each console answers, and each answer must reach the client that asked THAT leg.
        consoleSenkusha.Send([77], 1, new IPEndPoint(IPAddress.Loopback, fromSenkusha.Port));

        var back = new IPEndPoint(IPAddress.Any, 0);
        Assert.Equal([77], onSenkusha.Receive(ref back));
    }

    /// <summary>With no senkusha endpoint there is no leg, which is what this class did before.</summary>
    [Fact]
    public void WithoutASenkushaEndpointThereIsNoLeg()
    {
        using var relay = new SessionRelay(
            new IPEndPoint(IPAddress.Loopback, 1),
            new IPEndPoint(IPAddress.Loopback, 2),
            null,
            0,
            0);

        relay.Start();

        Assert.Equal(0, relay.SenkushaPortInUse);
    }

    /// <summary>
    /// PP829: and a relay built for a console listens on all three of the ports a session dials.
    ///
    /// The three are constants here and the session takes them from the same place, so what this
    /// holds is that the relay opened one per port rather than that the numbers are what they are.
    /// </summary>
    [Fact]
    public void ARealRelayListensOnEveryPortASessionDials()
    {
        using var relay = new SessionRelay(IPAddress.Loopback);

        relay.Start();

        Assert.Equal(SessionRelay.ControlPort, relay.ControlPortInUse);
        Assert.Equal(SessionRelay.StreamPort, relay.StreamPortInUse);
        Assert.Equal(SessionRelay.SenkushaPort, relay.SenkushaPortInUse);
    }
}
