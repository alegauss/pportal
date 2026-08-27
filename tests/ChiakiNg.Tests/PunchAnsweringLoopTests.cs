using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP456, PP340: PP238's answering loop, run over a socket.
///
/// PP238 ported the decision one step at a time and PP455 the reply it sends. The claim neither could
/// demonstrate is the one about the whole run: there is no path in which receiving something returns
/// success, and the only success is a timeout after at least one request was answered. A decision
/// function can say what one step is; only a run can show that the caller is told "done" by an absence
/// of traffic.
///
/// So a stub console drives it over loopback, and the two cases worth telling apart are here: a
/// console that ASKS and falls quiet succeeds, and a console that ANSWERS and falls quiet fails.
/// </summary>
public class PunchAnsweringLoopTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Short, because several tests wait for one of these to expire.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);

    private const ushort SidLocal = 0x1234;
    private const ushort SidConsole = 0xabcd;

    private static PunchIdentity Identity => new(
        [.. Enumerable.Range(1, PunchResponse.IdLength).Select(i => (byte)i)],
        [.. Enumerable.Range(101, PunchResponse.IdLength).Select(i => (byte)i)],
        SidLocal,
        SidConsole);

    private static byte[] Request(byte marker)
        => PunchProbe.Build([marker, 2, 3, 4, 5], Identity.LocalId, Identity.ConsoleId, SidLocal, SidConsole);

    /// <summary>An 88-byte datagram of the RESPONSE type, which the loop is meant to wait past.</summary>
    private static byte[] ExtraResponse()
    {
        byte[] packet = new byte[PunchResponse.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet, PunchResponse.ResponseType);
        return packet;
    }

    /// <summary>
    /// Runs the loop while a stub console sends <paramref name="datagrams"/> and then falls quiet.
    /// </summary>
    /// <returns>The outcome, and every datagram the console received back.</returns>
    private static async Task<(PunchAnsweringOutcome Outcome, List<byte[]> Replies)> DrivenByAsync(
        params byte[][] datagrams)
    {
        using var timeout = new CancellationTokenSource(Patience);

        using var loop = new PunchAnsweringLoop();
        int port = loop.Bind();

        using var console = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        console.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var to = new IPEndPoint(IPAddress.Loopback, port);
        var replies = new List<byte[]>();

        Task<PunchAnsweringOutcome> running = loop.RunAsync(
            Identity, "203.0.113.7", 51234, Silence, timeout.Token);

        foreach (byte[] datagram in datagrams)
        {
            await console.SendToAsync(datagram, SocketFlags.None, to, timeout.Token);

            // Only a request draws a reply, so only wait for one where a reply is due. The type is
            // the console's own test, not the loop's answer being trusted.
            if (BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(0, 4)) != PunchResponse.RequestType
                || datagram.Length != PunchResponse.Length)
            {
                continue;
            }

            byte[] buffer = new byte[PunchResponse.Length * 2];
            SocketReceiveFromResult got = await console.ReceiveFromAsync(
                buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

            replies.Add(buffer[..got.ReceivedBytes]);
        }

        return (await running, replies);
    }

    /// <summary>
    /// THE RULE: a console that asks once and falls quiet is a success, reported by the silence.
    /// </summary>
    [Fact]
    public async Task AskingOnceAndFallingQuietSucceeds()
    {
        (PunchAnsweringOutcome outcome, List<byte[]> replies) = await DrivenByAsync(Request(0xaa));

        Assert.Equal(PunchStep.Done, outcome.Step);
        Assert.True(PunchExchange.IsSuccess(outcome.Step));
        Assert.Equal(1, outcome.Answered);
        Assert.Equal(0, outcome.Faulted);

        Assert.Single(replies);
        Assert.Equal(PunchResponse.Length, replies[0].Length);
    }

    /// <summary>Every request is answered, and the loop goes back to waiting after each.</summary>
    [Fact]
    public async Task EveryRequestIsAnsweredAndTheWaitIsReentered()
    {
        (PunchAnsweringOutcome outcome, List<byte[]> replies) =
            await DrivenByAsync(Request(0xaa), Request(0xbb), Request(0xcc));

        Assert.Equal(PunchStep.Done, outcome.Step);
        Assert.Equal(3, outcome.Answered);
        Assert.Equal(3, replies.Count);

        // Each reply echoes its own request's five bytes, so the answers are not one packet resent.
        Assert.Equal(
            new byte[] { 0xaa, 0xbb, 0xcc },
            replies.Select(r => r[PunchResponse.EchoAt]).ToArray());
    }

    /// <summary>
    /// Nothing at all is the timeout error, not a success - the absence only means "done" once
    /// something has been answered.
    /// </summary>
    [Fact]
    public async Task SilenceWithNothingAnsweredIsTheTimeoutError()
    {
        (PunchAnsweringOutcome outcome, _) = await DrivenByAsync();

        Assert.Equal(PunchStep.TimedOut, outcome.Step);
        Assert.False(PunchExchange.IsSuccess(outcome.Step));
        Assert.Equal(0, outcome.Answered);
    }

    /// <summary>
    /// AND THE PAIR THAT MATTERS: an extra response is waited past and does NOT count as answering,
    /// so a console that answers and falls quiet gets a failure where one that asks gets a success.
    ///
    /// Same two datagram counts, same silence, opposite outcomes - which is the distinction a step
    /// function cannot show, because both steps stay in the loop.
    /// </summary>
    [Fact]
    public async Task AnsweringAndFallingQuietFailsWhereAskingSucceeds()
    {
        (PunchAnsweringOutcome ignored, List<byte[]> noReplies) =
            await DrivenByAsync(ExtraResponse());

        Assert.Equal(PunchStep.TimedOut, ignored.Step);
        Assert.Equal(1, ignored.Ignored);
        Assert.Equal(0, ignored.Answered);
        Assert.Empty(noReplies);

        (PunchAnsweringOutcome asked, _) = await DrivenByAsync(Request(0xaa));

        Assert.Equal(PunchStep.Done, asked.Step);
    }

    /// <summary>A datagram of the wrong size is fatal, and leaves the loop at once.</summary>
    [Fact]
    public async Task AWrongSizedDatagramIsFatal()
    {
        (PunchAnsweringOutcome outcome, _) = await DrivenByAsync(new byte[PunchResponse.Length - 1]);

        Assert.Equal(PunchStep.Fatal, outcome.Step);
        Assert.True(PunchExchange.Leaves(outcome.Step));
        Assert.Equal(0, outcome.Answered);
    }

    /// <summary>
    /// And a full-sized datagram of a type this does not know is fatal too - which is a different
    /// branch from the size, on the same eighty-eight bytes.
    /// </summary>
    [Fact]
    public async Task AnUnknownTypeIsFatalAtTheRightSize()
    {
        byte[] unknown = new byte[PunchResponse.Length];
        BinaryPrimitives.WriteUInt32BigEndian(unknown, 0x0a000000);

        (PunchAnsweringOutcome outcome, _) = await DrivenByAsync(unknown);

        Assert.Equal(PunchStep.Fatal, outcome.Step);
    }

    /// <summary>
    /// A request answered before a fatal one still counts, so the fatal step reports what had already
    /// been done rather than discarding it.
    /// </summary>
    [Fact]
    public async Task WorkAlreadyDoneSurvivesAFatalDatagram()
    {
        (PunchAnsweringOutcome outcome, List<byte[]> replies) =
            await DrivenByAsync(Request(0xaa), new byte[4]);

        Assert.Equal(PunchStep.Fatal, outcome.Step);
        Assert.Equal(1, outcome.Answered);
        Assert.Single(replies);
    }

    /// <summary>
    /// PP457: a flood of datagrams the loop discards no longer keeps it alive.
    ///
    /// Both discard paths re-arm the FULL timeout and leave `answered` alone, so before the bound
    /// anything able to reach the port could hold the loop open indefinitely by sending extra
    /// responses - no socket failure needed, and the caller never got the timeout it asked for. This
    /// sends more than the limit and asserts the loop leaves, which it could not have done before.
    /// </summary>
    [Fact]
    public async Task AFloodOfDiscardedDatagramsIsBounded()
    {
        byte[][] flood = [.. Enumerable
            .Range(0, PunchAnsweringLoop.MaxConsecutiveDiscards + 2)
            .Select(_ => ExtraResponse())];

        (PunchAnsweringOutcome outcome, _) = await DrivenByAsync(flood);

        Assert.Equal(PunchStep.Fatal, outcome.Step);
        Assert.True(PunchExchange.Leaves(outcome.Step));
        Assert.Equal(0, outcome.Answered);

        // It gave up on the count and not on the clock: the run was never silent long enough to
        // time out, which is the whole reason the timeout could not end it.
        Assert.True(
            outcome.Ignored > PunchAnsweringLoop.MaxConsecutiveDiscards,
            $"only {outcome.Ignored} were discarded, so the bound was not what ended the run");
    }

    /// <summary>
    /// PP457: and a request resets the budget, so no legitimate flow reaches the bound however many
    /// extras it interleaves.
    ///
    /// This is the half that says the fix is a bound on consecutive discards rather than on the run.
    /// Bounding the run would have cut this off.
    /// </summary>
    [Fact]
    public async Task ARequestGivesTheDiscardBudgetBack()
    {
        var interleaved = new List<byte[]>();
        for (var round = 0; round < 3; round++)
        {
            // Most of the budget, then a request, three times over - well past the limit in total.
            for (var i = 0; i < PunchAnsweringLoop.MaxConsecutiveDiscards - 1; i++)
                interleaved.Add(ExtraResponse());

            interleaved.Add(Request((byte)(0xa0 + round)));
        }

        (PunchAnsweringOutcome outcome, List<byte[]> replies) = await DrivenByAsync([.. interleaved]);

        Assert.Equal(PunchStep.Done, outcome.Step);
        Assert.Equal(3, outcome.Answered);
        Assert.Equal(3, replies.Count);

        // Far more discards in total than the limit, and none of them consecutive enough to trip it.
        Assert.Equal(3 * (PunchAnsweringLoop.MaxConsecutiveDiscards - 1), outcome.Ignored);
    }

    /// <summary>The bound is the C's, read from its define rather than trusted here.</summary>
    [Fact]
    public void TheBoundIsStillTheCs()
    {
        if (NatProbeSource.Locate() is not { } path)
            return;

        Assert.Equal(
            (long?)PunchAnsweringLoop.MaxConsecutiveDiscards,
            ChiakiNg.Session.CDefine.Value(File.ReadAllText(path), "MAX_CONSECUTIVE_DISCARDS"));
    }

    /// <summary>
    /// The replies really carry the masked tail PP455 un-masks, so this loop is sending the packet and
    /// not a placeholder.
    /// </summary>
    [Fact]
    public async Task TheRepliesCarryTheMaskedTail()
    {
        (_, List<byte[]> replies) = await DrivenByAsync(Request(0xaa));

        MaskedTail tail = PunchResponse.ReadMaskedTail(replies[0], SidLocal, SidConsole)!.Value;

        Assert.Equal(IPAddress.Parse("203.0.113.7"), tail.AsIpv4());
        Assert.Equal((ushort)51234, tail.Port);
    }
}
