using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP809, under PP784: the composition root PP792's seam was waiting for.
///
/// PP792 gave session.c a callback to hand senkusha to and PP805 wired a takion's arrivals to the
/// arms that read them. Nothing put the two on a session, so every session in this tree took the
/// other arm and ran the C's senkusha - which is PP762's state one phase earlier, and PP762 shipped
/// green because a census that counts types cannot see a missing call.
///
/// SMALLER THAN <see cref="ManagedStreamPhase"/> BY A LONG WAY. Senkusha has no decoder, no pad, no
/// receivers, no launch spec and no keying: a takion on its own port, the arrivals, a router and the
/// run. <see cref="SenkushaRunWiring"/> ties those four, so what this owns is the thread, the
/// install and the teardown.
///
/// THE PORT IS 9297 AND IT IS TAKEN, NOT ASKED FOR. PP771 paid for that rule on the other seam: a
/// run aimed at the wrong number got three unanswered INIT attempts and a connect that read like a
/// handshake this port had got wrong. <see cref="SessionRelay.SenkushaPort"/> is where the number
/// lives, beside the two it is one away from.
///
/// INSTALLED BEFORE THE SESSION STARTS, for <see cref="ManagedStreamPhase"/>'s reason: the C reaches
/// the senkusha phase on its own thread and does not ask twice, and it reaches it EARLIER than the
/// stream - a few lines after ctrl reports a session id.
/// </summary>
public sealed class ManagedSenkushaPhase : IDisposable
{
    private readonly SenkushaHandover handover = new();
    private readonly ManagedSenkushaRunner runner;
    private readonly Native.ChiakiSession session;
    private readonly IPEndPoint peer;

    private Thread? thread;

    /// <param name="session">The C session, whose senkusha phase this takes. Not owned.</param>
    /// <param name="console">The console's address, which discovery already answered with.</param>
    public ManagedSenkushaPhase(Native.ChiakiSession session, IPAddress console)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(console);

        this.session = session;
        peer = new IPEndPoint(console, SessionRelay.SenkushaPort);

        runner = new ManagedSenkushaRunner(Build);
    }

    /// <summary>What the run answered, or null while it has not finished.</summary>
    public SenkushaRunnerOutcome? Outcome { get; private set; }

    /// <summary>
    /// The router this phase built, or null where no start ever came.
    ///
    /// Reached through so a caller can read what the outcome cannot carry - how many datagrams
    /// arrived and how many an arm acted on, which is the reading PP773 says separates "nothing
    /// arrived" from "something arrived and no handler claimed it".
    /// </summary>
    public SenkushaRouter? Router { get; private set; }

    /// <summary>The host, likewise: what each wait ended on lives there and not here.</summary>
    public ManagedSenkushaRunHost? Host => runner.Host;

    /// <summary>Whether the session's stop has reached the handover.</summary>
    public bool Stopped => handover.Stopped;

    /// <summary>
    /// Install this as the session's senkusha phase and start waiting for it.
    ///
    /// Before <see cref="Native.ChiakiSession.Start"/>, and the runner's thread goes up first: the C
    /// signals the handover and then blocks on it, so a runner that had not reached its wait would
    /// make the session thread wait out a slice for nothing.
    /// </summary>
    public void InstallOn()
    {
        if (thread is not null)
            throw new InvalidOperationException("this phase is already installed.");

        thread = new Thread(() => Outcome = runner.Run(handover))
        {
            IsBackground = true,
            Name = "managed senkusha run",
        };

        thread.Start();
        handover.InstallOn(session);
    }

    /// <summary>Waits for the run to finish, which the session thread has already been told about.</summary>
    public bool Join(TimeSpan timeout) => thread is null || thread.Join(timeout);

    /// <summary>
    /// The four parts, built inside the runner rather than in this constructor.
    ///
    /// PP754's rule: a start that never comes should build no host, because constructing one takes
    /// a socket - and senkusha's is a second socket on a machine already holding the stream's.
    /// </summary>
    private ManagedSenkushaRunHost Build()
    {
        (ManagedSenkushaRunHost host, SenkushaRouter router) = SenkushaRunWiring.Over(Tag(), peer);

        // PP769: the socket the session handed over, where it handed one. session.c declares
        // data_sock NULL and never assigns it, so this is null on every path this tree runs and the
        // takion opens its own - which is what the C's senkusha does with close_socket true. Set
        // here because only the seam knows it, and only after the start.
        host.Takion.Adopted = handover.Socket;

        Router = router;

        return host;
    }

    /// <summary>
    /// A takion's local tag, drawn per run. chiaki_random_32's job, and the property that matters is
    /// the same: the console echoes it, so senkusha's must not be the stream's.
    /// </summary>
    private static uint Tag()
    {
        Span<byte> four = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(four);
        return BitConverter.ToUInt32(four);
    }

    /// <summary>
    /// PP768's order and never any other: cancel, join, then free.
    ///
    /// A handover freed under a live waiter runs a wait on memory the allocator has taken back, and
    /// it fails intermittently rather than reliably - which is the expensive way to find it. The
    /// join is bounded, and a thread that is not where this thinks it is leaks the handover
    /// deliberately rather than being freed under.
    /// </summary>
    public void Dispose()
    {
        if (thread is { } running)
        {
            handover.Cancel();

            if (!running.Join(TimeSpan.FromSeconds(2)))
            {
                GC.SuppressFinalize(this);
                return;
            }
        }

        handover.Dispose();
        GC.SuppressFinalize(this);
    }
}
