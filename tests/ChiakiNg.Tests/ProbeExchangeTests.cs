using System.Net;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP268: the probe, sent and answered over a real socket.
///
/// <see cref="TheEchoSurvivesAKernel"/> is the one that could not be written before: both halves of
/// this packet come from the same port, so an offset that is wrong agrees with itself. A datagram
/// through the loopback is what tells them apart.
/// </summary>
public class ProbeExchangeTests : IDisposable
{
    private readonly Socket console = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IPEndPoint consoleAt;
    private readonly CancellationTokenSource stopping = new();

    private static byte[] Id(byte fill) => Enumerable.Repeat(fill, PunchResponse.IdLength).ToArray();

    private static readonly byte[] RequestId = [0xde, 0xad, 0xbe, 0xef, 0x5a];

    public ProbeExchangeTests()
    {
        console.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        consoleAt = (IPEndPoint)console.LocalEndPoint!;
    }

    /// <summary>The console answering one probe the way PP236 says it is answered.</summary>
    private Task AnswerOnce(Func<byte[], byte[]?> reply) => Task.Run(async () =>
    {
        byte[] buffer = new byte[256];

        SocketReceiveFromResult got = await console.ReceiveFromAsync(
            buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), stopping.Token)
            .ConfigureAwait(false);

        byte[]? answer = reply(buffer[..got.ReceivedBytes]);
        if (answer is not null)
        {
            await console.SendToAsync(answer, SocketFlags.None, got.RemoteEndPoint, stopping.Token)
                .ConfigureAwait(false);
        }
    });

    /// <summary>
    /// THE ONE THAT NEEDED A SOCKET. The five bytes go out at one offset and come back from the
    /// same one, through a kernel rather than through the two builders agreeing with each other.
    /// </summary>
    [Fact]
    public async Task TheEchoSurvivesAKernel()
    {
        Task answering = AnswerOnce(request =>
            ProbeExchange.ReplyTo(request, Id(0xc0), Id(0xa1), 0x2222, 0x1111, "127.0.0.1", 9295));

        using var client = new ProbeExchange();
        client.Bind();

        ExchangeResult result = await client.ExchangeAsync(
            consoleAt, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromSeconds(5));

        await answering;

        Assert.Equal(ResponseVerdict.Accepted, result.Verdict);
        Assert.Equal(FollowupStep.Done, result.Step);
        Assert.Equal(RequestId, result.Echo);
    }

    /// <summary>
    /// An answer echoing somebody else's bytes is dropped, and dropped without an error - which is
    /// PP247's loudest-quietest branch, now reached through a socket.
    /// </summary>
    [Fact]
    public async Task AnAnswerEchoingSomethingElseIsDropped()
    {
        Task answering = AnswerOnce(request =>
        {
            // A well-formed reply to a DIFFERENT probe.
            byte[] other = PunchProbe.Build([1, 2, 3, 4, 5], Id(0xa1), Id(0xc0), 0x1111, 0x2222);
            return ProbeExchange.ReplyTo(other, Id(0xc0), Id(0xa1), 0x2222, 0x1111, "127.0.0.1", 9295);
        });

        using var client = new ProbeExchange();
        client.Bind();

        ExchangeResult result = await client.ExchangeAsync(
            consoleAt, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromSeconds(5));

        await answering;

        Assert.Equal(ResponseVerdict.WrongRequestId, result.Verdict);
        Assert.False(ResponseCheck.RecordsAnError(ResponseVerdict.WrongRequestId, CandidateType.Static));
    }

    /// <summary>A console probing us is answered rather than counted.</summary>
    [Fact]
    public async Task AConsoleProbingUsIsAnswered()
    {
        // The console sends a REQUEST of its own instead of a reply.
        Task answering = AnswerOnce(_ =>
            PunchProbe.Build([9, 9, 9, 9, 9], Id(0xc0), Id(0xa1), 0x2222, 0x1111));

        using var client = new ProbeExchange();
        client.Bind();

        ExchangeResult result = await client.ExchangeAsync(
            consoleAt, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromSeconds(5));

        await answering;

        Assert.Equal(ResponseVerdict.ConsoleProbing, result.Verdict);
        Assert.Equal(FollowupStep.Answer, result.Step);
    }

    /// <summary>A datagram of the wrong length is fatal for a named candidate.</summary>
    [Fact]
    public async Task AShortDatagramIsTheWrongSize()
    {
        Task answering = AnswerOnce(_ => new byte[40]);

        using var client = new ProbeExchange();
        client.Bind();

        ExchangeResult result = await client.ExchangeAsync(
            consoleAt, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromSeconds(5));

        await answering;

        Assert.Equal(ResponseVerdict.WrongSize, result.Verdict);

        // Fatal for one the console named, and survivable for one this code found.
        Assert.Equal(
            VerdictAction.Abort, ResponseCheck.Action(ResponseVerdict.WrongSize, CandidateType.Static));
        Assert.NotEqual(
            VerdictAction.Abort, ResponseCheck.Action(ResponseVerdict.WrongSize, CandidateType.Derived));
    }

    /// <summary>
    /// Nothing answering is the timeout, which PP256 calls the ordinary ending. The far end here is
    /// a listener that stays silent rather than a closed port - a closed one is the next test.
    /// </summary>
    [Fact]
    public async Task SilenceIsATimeout()
    {
        using var client = new ProbeExchange();
        client.Bind();

        ExchangeResult result = await client.ExchangeAsync(
            consoleAt, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromMilliseconds(300));

        Assert.Null(result.Verdict);
        Assert.Equal(FollowupStep.TimedOut, result.Step);
        Assert.False(result.Faulted);
    }

    /// <summary>
    /// PP256 FROM THE OTHER SIDE. A datagram to a closed port draws an ICMP rejection, which arrives
    /// as a failed receive on a socket the wait calls readable - the exact condition PP256 measured
    /// the core continuing on with no exit behind it.
    ///
    /// The step reported is the core's. The fault is reported beside it, so a caller here does not
    /// have to spin to find out.
    /// </summary>
    [Fact]
    public async Task AClosedPortIsTheConditionThatSpinsTheCore()
    {
        using var client = new ProbeExchange();
        client.Bind();

        using var nowhere = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        nowhere.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var closed = (IPEndPoint)nowhere.LocalEndPoint!;
        nowhere.Close();

        ExchangeResult result = await client.ExchangeAsync(
            closed, RequestId, Id(0xa1), Id(0xc0), 0x1111, 0x2222, TimeSpan.FromMilliseconds(300));

        // Either the rejection came back or nothing did; both are legitimate here, and only the
        // first exercises the condition. Whichever it was, the caller is told.
        Assert.Null(result.Verdict);

        if (result.Faulted)
        {
            // The core's own answer for this step, which is why it never leaves the loop.
            Assert.Equal(FollowupStep.Retry, result.Step);
            Assert.False(FollowupExchange.Leaves(FollowupStep.Retry));
        }
        else
        {
            Assert.Equal(FollowupStep.TimedOut, result.Step);
        }
    }

    /// <summary>The bind reports the port a local candidate would advertise.</summary>
    [Fact]
    public void TheBindReportsThePortACandidateWouldCarry()
    {
        using var client = new ProbeExchange();

        int port = client.Bind();

        Assert.InRange(port, 1, ushort.MaxValue);
        Assert.Equal(port, client.LocalPort);
    }

    public void Dispose()
    {
        stopping.Cancel();
        console.Dispose();
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
