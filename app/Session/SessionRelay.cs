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
    private readonly CancellationTokenSource stopping = new();
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
    }

    /// <summary>Where the session should be pointed, which is what `--via` takes.</summary>
    public static string Via => IPAddress.Loopback.ToString();

    /// <summary>How many datagrams have been carried, both directions.</summary>
    public int DatagramsForwarded { get; private set; }

    /// <summary>The port the UDP half is on, which a test needs when it did not choose one.</summary>
    public int StreamPortInUse => ((IPEndPoint)stream.Client.LocalEndPoint!).Port;

    /// <summary>Starts both halves. Returns as soon as they are listening.</summary>
    public void Start()
    {
        control.Start();

        _ = Task.Run(() => ForwardControl(stopping.Token));
        _ = Task.Run(() => ForwardStream(stopping.Token));
    }

    /// <summary>
    /// The UDP half, and the only one that reports.
    ///
    /// One socket, two directions, told apart by who the sender is: anything from the console goes
    /// back to the last local endpoint that spoke, and anything else goes to the console. That is
    /// enough because a session is one client - and a second one would be a second capture anyway.
    /// </summary>
    private void ForwardStream(CancellationToken token)
    {
        IPEndPoint? client = null;

        while (!token.IsCancellationRequested)
        {
            byte[] datagram;
            var from = new IPEndPoint(IPAddress.Any, 0);

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

            // PP613: by ENDPOINT and not by address. A test runs both sides on loopback, where an
            // address tells nobody apart - and the failure is not cosmetic there: every datagram
            // reads as the console's, so the client is never learnt and nothing is forwarded. The
            // console answers from the port takion connected to, so the pair is what identifies it.
            bool fromConsole = from.Equals(consoleStream);

            if (!fromConsole)
                client = from;

            onDatagram?.Invoke(datagram, fromConsole);
            DatagramsForwarded++;

            IPEndPoint? to = fromConsole ? client : consoleStream;
            if (to is null)
                continue;

            try
            {
                stream.Send(datagram, datagram.Length, to);
            }
            catch (SocketException)
            {
                // A send that fails is one datagram, and the stream is lossy by nature. Recording
                // it as carried would be the wrong count, so this is before the increment above.
                DatagramsForwarded--;
            }
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
        stopping.Dispose();
    }
}
