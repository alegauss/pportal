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

    /// <summary>
    /// PP809: SENKUSHA_PORT, which is the stream's plus one and not the stream's.
    ///
    /// Here beside its two neighbours rather than beside the phase that dials it, for the reason
    /// PP771 paid to learn on the other seam: a managed run aimed at 9295 got three unanswered INIT
    /// attempts and a connect that looked like a handshake this port had got wrong. A console
    /// answers nothing at all on the wrong number, so the three live in one place and a caller takes
    /// one rather than spelling it.
    ///
    /// PP829: AND THE RELAY DOES FORWARD IT NOW, which is the reverse of what this said. The old
    /// note argued a senkusha leg would be a third socket for a channel the message tap already
    /// recorded - true when it was written, and false twice over since. PP812 established that the
    /// C's tap sees nothing the port receives, and a session pointed at a relay that does not listen
    /// on 9297 does not merely go unrecorded: senkusha dials loopback, nothing answers, and the
    /// takion connect TIMES OUT. Every run through the relay died there before reaching the stream,
    /// so the mode PP812's design called the working one recorded nothing at all.
    /// </summary>
    public const int SenkushaPort = 9297;

    private readonly IPEndPoint consoleControl;
    private readonly Action<byte[], bool>? onDatagram;
    private readonly TcpListener control;
    private readonly UdpLeg stream;
    private readonly UdpLeg? senkusha;

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
            StreamPort,

            // PP829: and senkusha's, which a real session dials FIRST. Without it the relay is a
            // forwarder for the second half of a conversation that never starts.
            new IPEndPoint(console, SenkushaPort),
            SenkushaPort)
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
    /// <param name="consoleSenkusha">
    /// PP829: the console's senkusha endpoint, or null for a relay with no senkusha leg.
    ///
    /// Optional because a test that only exercises the stream leg should not have to bind a third
    /// socket to say so - and because null is what this class did before, so the absence is a
    /// state rather than a gap.
    /// </param>
    /// <param name="senkushaPort">The UDP port to listen on for it; 9297 for a real session.</param>
    public SessionRelay(
        IPEndPoint consoleControl,
        IPEndPoint consoleStream,
        Action<byte[], bool>? onDatagram,
        int controlPort,
        int streamPort,
        IPEndPoint? consoleSenkusha = null,
        int senkushaPort = 0)
    {
        ArgumentNullException.ThrowIfNull(consoleControl);
        ArgumentNullException.ThrowIfNull(consoleStream);

        this.consoleControl = consoleControl;
        this.onDatagram = onDatagram;

        control = new TcpListener(IPAddress.Loopback, controlPort);

        stream = new UdpLeg(streamPort, consoleStream, Offered, Forwarded);

        senkusha = consoleSenkusha is null
            ? null
            : new UdpLeg(senkushaPort, consoleSenkusha, Offered, Forwarded);
    }

    private void Offered(byte[] datagram, bool fromConsole) => onDatagram?.Invoke(datagram, fromConsole);

    private void Forwarded() => Interlocked.Increment(ref forwarded);

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
    public int StreamPortInUse => stream.NearPort;

    /// <summary>PP829: and senkusha's, or zero where this relay has no senkusha leg.</summary>
    public int SenkushaPortInUse => senkusha?.NearPort ?? 0;

    /// <summary>
    /// PP830: what each leg carried, which one total cannot say.
    ///
    /// A relayed session that records only senkusha's traffic has two explanations and the count
    /// separates them: the stream leg carrying nothing means nobody dialled it - PP769 has the
    /// stream phase ADOPT senkusha's socket rather than connect - while the stream leg carrying
    /// sends and no answers means the console stopped talking. One number reads as either.
    /// </summary>
    public (int Stream, int Senkusha) CarriedPerLeg => (stream.Carried, senkusha?.Carried ?? 0);

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
    public int UpstreamPortInUse => stream.FarPort;

    /// <summary>Starts every half. Returns as soon as they are listening.</summary>
    public void Start()
    {
        control.Start();

        _ = Task.Run(() => ForwardControl(stopping.Token));

        stream.Start(stopping.Token);
        senkusha?.Start(stopping.Token);
    }

    /// <summary>
    /// PP829: one UDP port forwarded both ways, which the relay now needs TWO of.
    ///
    /// Extracted rather than copied. The stream leg and senkusha's differ only in a port and an
    /// endpoint, and the two things this class gets right are exactly the two a second copy would
    /// get wrong: the pair of sockets PP617 had to split, and the per-leg <see cref="client"/> that
    /// says where an answer goes. One `client` shared between two legs would send senkusha's pongs
    /// to whoever last spoke on the stream port.
    /// </summary>
    private sealed class UdpLeg : IDisposable
    {
        private readonly UdpClient near;

        /// <summary>
        /// PP617: the socket that faces the console, which is not the one that faces the C.
        ///
        /// The first version forwarded both directions on the near socket alone, and every test
        /// passed because both ends of a test are on loopback. A real run does not work that way:
        /// that socket is BOUND to 127.0.0.1, so a datagram it sends to a console on the LAN
        /// carries a loopback source address and the answer has nowhere to go.
        ///
        /// Any address, so the OS picks the route and the source it puts on the datagram is one the
        /// console can answer.
        /// </summary>
        private readonly UdpClient far;

        private readonly IPEndPoint console;
        private readonly Action<byte[], bool> offered;
        private readonly Action forwarded;

        /// <summary>Where this leg's client spoke from, which is where its answers go back to.</summary>
        private IPEndPoint? client;

        public UdpLeg(int port, IPEndPoint console, Action<byte[], bool> offered, Action forwarded)
        {
            this.console = console;
            this.offered = offered;
            this.forwarded = forwarded;

            near = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            far = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        }

        public int NearPort => ((IPEndPoint)near.Client.LocalEndPoint!).Port;

        public int FarPort => ((IPEndPoint)far.Client.LocalEndPoint!).Port;

        /// <summary>PP830: how many this leg carried, either direction.</summary>
        public int Carried => Volatile.Read(ref carried);

        private int carried;

        public void Start(CancellationToken token)
        {
            _ = Task.Run(() => Pump(near, toConsole: true, token), token);
            _ = Task.Run(() => Pump(far, toConsole: false, token), token);
        }

        /// <summary>
        /// One socket's loop. One per socket, not one loop deciding a direction - PP613 did the
        /// latter and it passed every test because both ends of a test are on loopback.
        /// </summary>
        private void Pump(UdpClient from, bool toConsole, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] datagram;

                try
                {
                    datagram = from.Receive(ref sender);
                }
                catch (SocketException)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (toConsole)
                {
                    client = sender;
                    offered(datagram, false);
                    Carry(far, datagram, console);
                }
                else
                {
                    offered(datagram, true);

                    if (client is { } to)
                        Carry(near, datagram, to);
                }
            }
        }

        /// <summary>One datagram out of one socket, counted only where it left.</summary>
        private void Carry(UdpClient socket, byte[] datagram, IPEndPoint to)
        {
            try
            {
                socket.Send(datagram, datagram.Length, to);
                Interlocked.Increment(ref carried);
                forwarded();
            }
            catch (SocketException)
            {
                // One datagram, and the stream is lossy by nature. Counting a send that failed
                // would be the wrong number, so the increment is inside the try rather than after.
            }
            catch (ObjectDisposedException)
            {
                // Stopping.
            }
        }

        public void Dispose()
        {
            near.Dispose();
            far.Dispose();
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

    /// <summary>
    /// PP830: prints what each leg carried when the run ends, and nothing where there is no relay.
    ///
    /// A `using` in the capture rather than a line after the session, because the session can leave
    /// by several paths and the count is most wanted on the ones that failed.
    /// </summary>
    public sealed class RelayLegReport(SessionRelay? relay) : IDisposable
    {
        public void Dispose()
        {
            if (relay is null)
                return;

            (int stream, int senkusha) = relay.CarriedPerLeg;

            Console.WriteLine(
                $"[capture] relay carried {stream} on the stream leg and {senkusha} on senkusha's");
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
        senkusha?.Dispose();
        stopping.Dispose();
    }
}
