using System.Net;
using System.Net.Sockets;

namespace ChiakiNg.Session;

/// <summary>
/// PP613, under PP27: a relay between the C and the console, so whole datagrams can be recorded
/// without editing libchiaki.
///
/// PP612 found the tap truncates at eighteen bytes inside the vendored C, so a capture with
/// payloads is the local patch a non-goal refuses and PP27 is not exempt from it. This is the door
/// that costs no rule: the session is pointed here by PP614's `--via`, this forwards every byte to
/// the console and back, and it sees whole datagrams because it is the one carrying them.
///
/// TWO PORTS, AND THEY ARE FIXED. The local path does not negotiate: session.c requests over
/// SESSION_PORT, ctrl.c connects to SESSION_CTRL_PORT - the same 9295 - and streamconnection.c
/// hands takion STREAM_CONNECTION_PORT, 9296. So this is a TCP forwarder and a UDP forwarder, not
/// a proxy that has to understand what it carries.
///
/// ONLY THE UDP SIDE IS OBSERVED. The TCP half exists so the session reaches takion at all; PP27's
/// question is about datagrams, and handing the ctrl bytes to a recorder would be recording a
/// channel PP297's corpus already holds.
///
/// IT IS NOT A PROXY TO SHIP. It exists to record, it binds loopback, and it forwards without
/// looking - a session that left one running would be a hop in the product for no reason.
/// </summary>
public sealed class SessionRelay : IDisposable
{
    /// <summary>SESSION_PORT and SESSION_CTRL_PORT, which are the same number.</summary>
    public const int ControlPort = 9295;

    /// <summary>STREAM_CONNECTION_PORT, where takion's datagrams go.</summary>
    public const int StreamPort = 9296;

    private readonly IPEndPoint consoleControl;
    private readonly IPEndPoint consoleStream;
    private readonly Action<byte[], bool>? onDatagram;
    private readonly TcpListener control;
    private readonly UdpClient stream;

    /// <summary>
    /// PP617: the socket that faces the console, which is not the one that faces the C.
    ///
    /// The first version forwarded both directions on <see cref="stream"/> alone, and every test
    /// passed because both ends of a test are on loopback. A real run does not work that way: that
    /// socket is BOUND to 127.0.0.1, so a datagram it sends to a console on the LAN carries a
    /// loopback source address and the answer has nowhere to go. The session reached ctrl, takion
    /// sent its INIT three times, and no ack ever came back.
    ///
    /// So there are two, one per side, and the pairing is the relay: what arrives on the loopback
    /// socket goes out of this one, and what arrives here goes back to the client that spoke. The
    /// TCP half never had the problem because ConnectAsync makes its own socket with a route the OS
    /// chooses.
    /// </summary>
    private readonly UdpClient upstream;

    private readonly CancellationTokenSource stopping = new();
    private IPEndPoint? client;
    private bool disposed;

    /// <summary>
    /// The relay a session uses: loopback on the two fixed ports, forwarding to the console's.
    /// </summary>
    /// <param name="console">Where the console is, as discovery found it.</param>
    /// <param name="onDatagram">Every takion datagram, whole. See the other constructor.</param>
    public SessionRelay(IPAddress console, Action<byte[], bool>? onDatagram = null)
        : this(
            new IPEndPoint(console ?? throw new ArgumentNullException(nameof(console)), ControlPort),
            new IPEndPoint(console, StreamPort),
            onDatagram,
            ControlPort,
            StreamPort)
    {
    }

    /// <param name="consoleControl">The console's TCP endpoint.</param>
    /// <param name="consoleStream">The console's UDP endpoint, where takion's answers come from.</param>
    /// <param name="onDatagram">
    /// Every takion datagram, whole, with true where it came FROM the console. Called on the
    /// forwarding thread, so a recorder that blocks here delays the stream it is recording.
    /// </param>
    /// <param name="controlPort">The TCP port to listen on; 9295 for a real session.</param>
    /// <param name="streamPort">The UDP port to listen on; 9296 for a real session.</param>
    public SessionRelay(
        IPEndPoint consoleControl,
        IPEndPoint consoleStream,
        Action<byte[], bool>? onDatagram,
        int controlPort,
        int streamPort)
    {
        ArgumentNullException.ThrowIfNull(consoleControl);
        ArgumentNullException.ThrowIfNull(consoleStream);

        this.consoleControl = consoleControl;
        this.consoleStream = consoleStream;
        this.onDatagram = onDatagram;

        control = new TcpListener(IPAddress.Loopback, controlPort);
        stream = new UdpClient(new IPEndPoint(IPAddress.Loopback, streamPort));

        // Any address, so the OS picks the route to the console and the source it puts on the
        // datagram is one the console can answer.
        upstream = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
    }

    /// <summary>Where the session should be pointed, which is what `--via` takes.</summary>
    public static string Via => IPAddress.Loopback.ToString();

    /// <summary>
    /// How many datagrams have been carried, both directions.
    ///
    /// PP659: read and written across threads, so it is an interlocked counter rather than a plain
    /// property. TWO forwarding threads increment it - one per direction - and `count++` from two
    /// threads loses updates, which for a relay carrying thousands of datagrams a second is a
    /// number that drifts low for reasons nothing records.
    ///
    /// AND IT IS WRITTEN AFTER THE SEND, which is the half that made a test flake. The datagram is
    /// on the wire before the counter moves, so a reader that has just RECEIVED one can still see
    /// the old value - a real ordering and not a wrong one, since counting a send that threw would
    /// be the wrong number. What it means for a caller is that the count is eventually right and
    /// never a receipt: something waiting on it has to wait, which is what the tests now do.
    /// </summary>
    public int DatagramsForwarded => Volatile.Read(ref forwarded);

    private int forwarded;

    /// <summary>The port the UDP half is on, which a test needs when it did not choose one.</summary>
    public int StreamPortInUse => ((IPEndPoint)stream.Client.LocalEndPoint!).Port;

    /// <summary>
    /// The port the TCP half is on, likewise.
    ///
    /// Read back rather than remembered, so a caller that passed zero gets what the OS chose. A
    /// test that picked its own port by binding and letting go races every other test doing the
    /// same, which is what four of these did on the run that added the second socket.
    /// </summary>
    public int ControlPortInUse => ((IPEndPoint)control.LocalEndpoint).Port;

    /// <summary>
    /// PP617: the port facing the console, which the OS chose.
    ///
    /// A console answers to whatever source it was sent from, so this is where its datagrams
    /// arrive - and a test standing in for one has to send here rather than to the loopback port,
    /// which is the difference the first version could not tell.
    /// </summary>
    public int UpstreamPortInUse => ((IPEndPoint)upstream.Client.LocalEndPoint!).Port;

    /// <summary>Starts both halves. Returns as soon as they are listening.</summary>
    public void Start()
    {
        control.Start();

        _ = Task.Run(() => ForwardControl(stopping.Token));
        _ = Task.Run(() => ForwardStream(stopping.Token));
        _ = Task.Run(() => ForwardDownstream(stopping.Token));
    }

    /// <summary>
    /// PP617: what the C sent, out to the console - and where the client's endpoint is learnt.
    ///
    /// One loop per socket, not one loop deciding a direction. PP613 did the latter and it passed
    /// every test because both ends of a test are on loopback; the sending socket is bound there,
    /// so a real console got datagrams it could not answer.
    /// </summary>
    private void ForwardStream(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] datagram;

            try
            {
                datagram = stream.Receive(ref from);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            client = from;
            onDatagram?.Invoke(datagram, false);
            Carry(upstream, datagram, consoleStream);
        }
    }

    /// <summary>What the console sent, back to whoever spoke first.</summary>
    private void ForwardDownstream(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] datagram;

            try
            {
                datagram = upstream.Receive(ref from);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            onDatagram?.Invoke(datagram, true);

            if (client is { } to)
                Carry(stream, datagram, to);
        }
    }

    /// <summary>One datagram out of one socket, counted only where it left.</summary>
    private void Carry(UdpClient socket, byte[] datagram, IPEndPoint to)
    {
        try
        {
            socket.Send(datagram, datagram.Length, to);
            Interlocked.Increment(ref forwarded);
        }
        catch (SocketException)
        {
            // One datagram, and the stream is lossy by nature. Counting a send that failed would be
            // the wrong number, so the increment is inside the try rather than after it.
        }
        catch (ObjectDisposedException)
        {
            // Stopping.
        }
    }

    /// <summary>The TCP half, which exists so the session reaches takion at all.</summary>
    private async Task ForwardControl(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient near;

            try
            {
                near = await control.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => Pump(near, token), token);
        }
    }

    private async Task Pump(TcpClient near, CancellationToken token)
    {
        using (near)
        using (var far = new TcpClient())
        {
            try
            {
                await far.ConnectAsync(consoleControl, token).ConfigureAwait(false);

                await Task.WhenAny(
                    Copy(near.GetStream(), far.GetStream(), token),
                    Copy(far.GetStream(), near.GetStream(), token)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
            {
                // The console closed, or we are stopping. Either way this connection is over and
                // the next one is the caller's business.
            }
        }
    }

    private static async Task Copy(NetworkStream from, NetworkStream to, CancellationToken token)
    {
        byte[] buffer = new byte[8192];

        while (!token.IsCancellationRequested)
        {
            int read = await from.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0)
                return;

            await to.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }

    /// <summary>Stops both halves and releases the two sockets.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        stopping.Cancel();
        control.Stop();
        stream.Dispose();
        upstream.Dispose();
        stopping.Dispose();
    }
}
