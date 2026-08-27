using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP459, PP340: the candidate race over real sockets.
///
/// PP33 ported the decision and said why it stopped there - a race cannot be pinned down by a test
/// that opens twenty-three sockets. True, and it also leaves the class unable to demonstrate its own
/// headline: the winner is the first to ANSWER, not the best. A decision function is handed its events
/// in order, so feeding it the winner first proves nothing about which winner the wire would pick.
///
/// Three stubs settle it, and the second thing sockets decide: a candidate is identified by the SOURCE
/// ADDRESS of what arrives, so an answer from a port nobody probed becomes a new derived candidate
/// rather than crediting the one that was probed.
/// </summary>
public class CandidateRaceRunTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private const ushort SidLocal = 0x1234;
    private const ushort SidConsole = 0xabcd;

    private readonly List<Socket> stubs = [];

    private static PunchIdentity Identity => new(
        [.. Enumerable.Range(1, PunchResponse.IdLength).Select(i => (byte)i)],
        [.. Enumerable.Range(101, PunchResponse.IdLength).Select(i => (byte)i)],
        SidLocal,
        SidConsole);

    private static IReadOnlyList<byte[]> RequestIds => [[0xde, 0xad, 0xbe, 0xef, 0x42]];

    /// <summary>A stub candidate: a bound loopback socket, and the Candidate that names it.</summary>
    private (Socket Socket, Candidate Candidate) Stub(CandidateType type = CandidateType.Local)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        stubs.Add(socket);

        var bound = (IPEndPoint)socket.LocalEndPoint!;
        return (socket, new Candidate(type, "127.0.0.1", "0.0.0.0", (ushort)bound.Port, 0));
    }

    /// <summary>
    /// Waits for a probe, then answers it after <paramref name="delay"/> with a well-formed response
    /// echoing the probe's five bytes.
    /// </summary>
    /// <param name="from">
    /// The socket to answer FROM, where that is not the one probed - which is how a NAT's other port
    /// is simulated.
    /// </param>
    private static Task AnswerAfterAsync(
        Socket probed, TimeSpan delay, CancellationToken token, Socket? from = null)
        => Task.Run(
            async () =>
            {
                byte[] buffer = new byte[PunchResponse.Length * 2];
                SocketReceiveFromResult got = await probed.ReceiveFromAsync(
                    buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), token);

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token);

                byte[] response = new byte[PunchResponse.Length];
                BinaryPrimitives.WriteUInt32BigEndian(response, PunchResponse.ResponseType);
                buffer.AsSpan(PunchResponse.EchoAt, PunchResponse.EchoLength)
                    .CopyTo(response.AsSpan(PunchResponse.EchoAt));

                await (from ?? probed).SendToAsync(
                    response, SocketFlags.None, got.RemoteEndPoint, token);
            },
            token);

    /// <summary>
    /// THE CLAIM: the LAST candidate offered wins, because it answered first.
    ///
    /// The two ahead of it answer late enough that the race is already over. Offered order is
    /// therefore not the outcome, and a port that sorted candidates by type before racing them would
    /// pick a different console here - which is what PP33's note says and what only a wire can show.
    /// </summary>
    [Fact]
    public async Task TheFirstToAnswerWinsAndNotTheFirstOffered()
    {
        using var timeout = new CancellationTokenSource(Patience);

        (Socket slowA, Candidate a) = Stub();
        (Socket slowB, Candidate b) = Stub();
        (Socket fast, Candidate c) = Stub();

        var race = new CandidateRace([a, b, c], RequestIds);

        _ = AnswerAfterAsync(slowA, TimeSpan.FromMilliseconds(250), timeout.Token);
        _ = AnswerAfterAsync(slowB, TimeSpan.FromMilliseconds(250), timeout.Token);
        Task answering = AnswerAfterAsync(fast, TimeSpan.Zero, timeout.Token);

        using var run = new CandidateRaceRun();
        run.Bind();

        RaceRunOutcome outcome = await run.RunAsync(
            race, Identity, RequestIds, Silence, timeout.Token);

        await answering;

        Assert.False(outcome.TimedOut);
        Assert.Equal(RaceOutcome.Selected, outcome.Outcomes[^1]);

        // The third offered, and the first to answer.
        Assert.Equal(c.Port, outcome.Selected?.Port);
        Assert.NotEqual(a.Port, outcome.Selected?.Port);
        Assert.NotEqual(b.Port, outcome.Selected?.Port);
    }

    /// <summary>
    /// And with the fast one offered FIRST the answer is the same, so the test above is about arrival
    /// order rather than about position.
    /// </summary>
    [Fact]
    public async Task TheSameCandidateWinsFromTheFrontOfTheOffer()
    {
        using var timeout = new CancellationTokenSource(Patience);

        (Socket fast, Candidate c) = Stub();
        (Socket slow, Candidate a) = Stub();

        var race = new CandidateRace([c, a], RequestIds);

        Task answering = AnswerAfterAsync(fast, TimeSpan.Zero, timeout.Token);
        _ = AnswerAfterAsync(slow, TimeSpan.FromMilliseconds(250), timeout.Token);

        using var run = new CandidateRaceRun();
        run.Bind();

        RaceRunOutcome outcome = await run.RunAsync(
            race, Identity, RequestIds, Silence, timeout.Token);

        await answering;

        Assert.Equal(c.Port, outcome.Selected?.Port);
    }

    /// <summary>
    /// A CANDIDATE IS THE SOURCE ADDRESS OF WHAT ARRIVES. Answering from a port nobody probed does not
    /// credit the candidate that was probed - it takes on a new derived one and that is what wins.
    ///
    /// This is the behaviour a fixture cannot express: the source port is a property of the datagram,
    /// chosen by whoever sent it, and it is how the port discovers the address a NAT actually mapped.
    /// </summary>
    [Fact]
    public async Task AnAnswerFromAnUnprobedPortBecomesANewCandidate()
    {
        using var timeout = new CancellationTokenSource(Patience);

        (Socket probed, Candidate offered) = Stub();
        (Socket elsewhere, Candidate unprobed) = Stub();

        var race = new CandidateRace([offered], RequestIds);

        // The probe goes to `probed`; the answer comes out of `elsewhere`.
        Task answering = AnswerAfterAsync(probed, TimeSpan.Zero, timeout.Token, from: elsewhere);

        using var run = new CandidateRaceRun();
        run.Bind();

        RaceRunOutcome outcome = await run.RunAsync(
            race, Identity, RequestIds, Silence, timeout.Token);

        await answering;

        Assert.Equal(unprobed.Port, outcome.Selected?.Port);
        Assert.Equal(CandidateType.Derived, outcome.Selected?.Type);

        // The offered one was probed and never answered, so it is still on zero.
        Assert.Equal(0, race.ResponsesFrom(0));

        // And the race grew by exactly one.
        Assert.Equal(1, race.ExtraUsed);
        Assert.Equal(race.Offered + 1, race.Candidates.Count);
    }

    /// <summary>
    /// A response echoing the wrong five bytes is ignored and NOT counted, so the race keeps running
    /// and ends in silence rather than selecting.
    /// </summary>
    [Fact]
    public async Task AWrongRequestIdIsIgnoredAndNotCounted()
    {
        using var timeout = new CancellationTokenSource(Patience);

        (Socket stub, Candidate only) = Stub();
        var race = new CandidateRace([only], RequestIds);

        Task answering = Task.Run(
            async () =>
            {
                byte[] buffer = new byte[PunchResponse.Length * 2];
                SocketReceiveFromResult got = await stub.ReceiveFromAsync(
                    buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

                byte[] response = new byte[PunchResponse.Length];
                BinaryPrimitives.WriteUInt32BigEndian(response, PunchResponse.ResponseType);

                // Five bytes that are not the ones sent.
                new byte[] { 1, 2, 3, 4, 5 }.CopyTo(response, PunchResponse.EchoAt);

                await stub.SendToAsync(response, SocketFlags.None, got.RemoteEndPoint, timeout.Token);
            },
            timeout.Token);

        using var run = new CandidateRaceRun();
        run.Bind();

        RaceRunOutcome outcome = await run.RunAsync(
            race, Identity, RequestIds, Silence, timeout.Token);

        await answering;

        Assert.True(outcome.TimedOut);
        Assert.Null(outcome.Selected);
        Assert.Contains(RaceOutcome.WrongRequestId, outcome.Outcomes);
        Assert.Equal(0, race.ResponsesFrom(0));
    }

    /// <summary>Candidates that never answer leave the race to end in silence.</summary>
    [Fact]
    public async Task SilenceFromEveryCandidateSelectsNobody()
    {
        using var timeout = new CancellationTokenSource(Patience);

        (_, Candidate a) = Stub();
        (_, Candidate b) = Stub();

        var race = new CandidateRace([a, b], RequestIds);

        using var run = new CandidateRaceRun();
        run.Bind();

        RaceRunOutcome outcome = await run.RunAsync(
            race, Identity, RequestIds, Silence, timeout.Token);

        Assert.True(outcome.TimedOut);
        Assert.Null(outcome.Selected);
        Assert.Empty(outcome.Outcomes);
    }

    /// <summary>Closes every stub socket the test opened.</summary>
    public void Dispose()
    {
        foreach (Socket stub in stubs)
            stub.Dispose();

        GC.SuppressFinalize(this);
    }
}
