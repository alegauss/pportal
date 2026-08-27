using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ChiakiNg.Protocol;

/// <summary>How one run of the race ended.</summary>
/// <param name="Selected">The candidate that won, or null where none did.</param>
/// <param name="Outcomes">What each datagram did, in arrival order.</param>
/// <param name="TimedOut">Whether the run ended because nothing more arrived.</param>
public readonly record struct RaceRunOutcome(
    Candidate? Selected, IReadOnlyList<RaceOutcome> Outcomes, bool TimedOut);

/// <summary>
/// PP459: the candidate race over real sockets - the last of the four PP340 named.
///
/// <see cref="CandidateRace"/> says why it left the sockets out: "a race whose only real input is
/// which datagram arrives first cannot be pinned down by a test that has to open twenty-three UDP
/// sockets to run". That was right about twenty-three, and it also means the class's headline claim is
/// the one thing it cannot demonstrate. THE WINNER IS THE FIRST TO ANSWER, NOT THE BEST - and a
/// decision function is handed its events already in order, so feeding it the winner first proves
/// only that the first event wins. Over a wire the order is latency, which is the claim.
///
/// Three stubs are enough for that, and for the other thing sockets decide: A CANDIDATE IS IDENTIFIED
/// BY THE SOURCE ADDRESS OF WHAT ARRIVES. Not by which probe it answers, and not by the echoed request
/// id - those are checked afterwards. So an answer from a port nobody probed does not credit the
/// candidate that was probed; it becomes a new DERIVED candidate, which is how the port learns the
/// address a NAT actually mapped. That is a property of the datagram rather than of any argument, and
/// it is why this needed a socket rather than a better fixture.
///
/// The decision stays in <see cref="CandidateRace"/>. This sends the probes, reads what comes back,
/// and hands over the source address, the type and the echo - nothing here chooses.
/// </summary>
public sealed class CandidateRaceRun : IDisposable
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>
    /// One socket for every candidate, as the C does it: `session->ipv4_sock` sends to each address
    /// in turn and every answer comes back on the same socket, which is what makes the source address
    /// the only thing distinguishing them.
    /// </summary>
    public int LocalPort { get; private set; }

    /// <summary>Binds to an ephemeral loopback port and reports which one.</summary>
    public int Bind()
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return LocalPort;
    }

    /// <summary>
    /// Probes every offered candidate and runs the race until one is selected, something is fatal, or
    /// nothing more arrives.
    /// </summary>
    /// <param name="race">The race, which owns every decision this makes.</param>
    /// <param name="identity">Who this end is, for the probes and for any reply.</param>
    /// <param name="requestIds">
    /// One id per round, which is what the race matches a response against. The probe carries
    /// <c>requestIds[0]</c>, since the round count is one.
    /// </param>
    /// <param name="timeout">How long to wait with nothing arriving.</param>
    public async Task<RaceRunOutcome> RunAsync(
        CandidateRace race,
        PunchIdentity identity,
        IReadOnlyList<byte[]> requestIds,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(requestIds);

        byte[] probe = PunchProbe.Build(
            requestIds[0], identity.LocalId, identity.ConsoleId, identity.SidLocal, identity.SidConsole);

        // Every offered candidate, in the order it was offered - which the race then does not use.
        foreach (Candidate candidate in race.Candidates.Take(race.Offered))
        {
            var to = new IPEndPoint(IPAddress.Parse(candidate.Address), candidate.Port);
            await socket.SendToAsync(probe, SocketFlags.None, to, cancellationToken).ConfigureAwait(false);
        }

        var outcomes = new List<RaceOutcome>();
        byte[] buffer = new byte[CandidateRace.MessageLength * 2];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(timeout);

            SocketReceiveFromResult got;
            try
            {
                got = await socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), bounded.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new RaceRunOutcome(race.Selected, outcomes, TimedOut: true);
            }
            catch (SocketException)
            {
                // An ICMP rejection from a stub that has gone away. The C logs and carries on here.
                continue;
            }

            var from = (IPEndPoint)got.RemoteEndPoint;
            int length = got.ReceivedBytes;

            uint messageType = length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(buffer) : 0;

            byte[]? echo = length >= CandidateRace.RequestIdOffset + CandidateRace.RequestIdLength
                ? buffer[CandidateRace.RequestIdOffset..(CandidateRace.RequestIdOffset + CandidateRace.RequestIdLength)]
                : null;

            RaceOutcome outcome = race.Receive(
                from.Address.ToString(), (ushort)from.Port, messageType, echo);

            outcomes.Add(outcome);

            switch (outcome)
            {
                case RaceOutcome.Selected:
                    return new RaceRunOutcome(race.Selected, outcomes, TimedOut: false);

                case RaceOutcome.Answered:
                    // The console probed us back; the C answers and keeps waiting.
                    byte[]? reply = PunchResponse.Build(
                        buffer.AsSpan(0, length),
                        identity.LocalId,
                        identity.ConsoleId,
                        identity.SidLocal,
                        identity.SidConsole,
                        from.Address.ToString(),
                        (ushort)from.Port);

                    if (reply is not null)
                    {
                        await socket.SendToAsync(reply, SocketFlags.None, from, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;

                case RaceOutcome.Fatal:
                    return new RaceRunOutcome(null, outcomes, TimedOut: false);

                default:
                    // Counted, WrongRequestId, Skipped, NewCandidate, ExtraLimitReached: all keep
                    // waiting, which is the asymmetry PP33 recorded.
                    break;
            }
        }
    }

    /// <summary>Releases the socket.</summary>
    public void Dispose()
    {
        socket.Dispose();
        GC.SuppressFinalize(this);
    }
}
