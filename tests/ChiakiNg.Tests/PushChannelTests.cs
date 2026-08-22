using System.Net;
using System.Net.WebSockets;
using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP267: the channel, opened and read against a listener on the loopback.
///
/// <see cref="ARefusedConnectionIsReportedRatherThanWaitedOn"/> is the divergence PP267 put on the
/// record, and <see cref="APayloadThatWillNotParseIsDropped"/> is PP231's loss, now countable.
/// </summary>
public class PushChannelTests : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string prefix;
    private readonly Uri url;
    private readonly CancellationTokenSource stopping = new();

    private readonly List<string> toSend = [];

    public PushChannelTests()
    {
        int port = FreePort();
        prefix = $"http://127.0.0.1:{port}/";
        url = new Uri($"ws://127.0.0.1:{port}/");

        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(ServeAsync);
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext accepted =
                await context.AcceptWebSocketAsync(PushSocket.SubProtocol).ConfigureAwait(false);

            using WebSocket peer = accepted.WebSocket;

            foreach (string frame in toSend)
            {
                await peer.SendAsync(
                    Encoding.UTF8.GetBytes(frame),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None).ConfigureAwait(false);
            }

            await peer.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private const string Bearer = "Bearer token";

    /// <summary>A frame long enough to be a notification, of the type asked for.</summary>
    private static string Notification(string type)
        => """{"to":{"accountId":"1"},"body":{"data":{}},"dataType":"TYPE"}"""
            .Replace("TYPE", type, StringComparison.Ordinal);

    /// <summary>The channel opens, reads what arrives, and ends when the peer closes.</summary>
    [Fact]
    public async Task ItOpensReadsAndEndsWhenThePeerCloses()
    {
        toSend.Add(Notification("psn:sessionManager:sys:remotePlaySession:created"));

        using var channel = new PushChannel();

        Assert.Equal(ChannelOpenOutcome.Open, await channel.OpenAsync("ignored", Bearer, url));

        ChannelEndReason reason = await channel.ReadAsync();

        Assert.Equal(ChannelEndReason.ClosedByPeer, reason);
        Assert.True(channel.FramesHandled >= 1);
    }

    /// <summary>
    /// THE DIVERGENCE. Nothing is listening for a websocket here, so the connection is refused - and
    /// the channel says so rather than waiting, which the core's thread does not.
    /// </summary>
    [Fact]
    public async Task ARefusedConnectionIsReportedRatherThanWaitedOn()
    {
        using var channel = new PushChannel();

        // A port nothing is on at all.
        var nowhere = new Uri($"ws://127.0.0.1:{FreePort()}/");

        ChannelOpenOutcome outcome = await channel.OpenAsync("ignored", Bearer, nowhere);

        Assert.Equal(ChannelOpenOutcome.Refused, outcome);
    }

    /// <summary>And a caller that cancels is told apart from a server that refused.</summary>
    [Fact]
    public async Task ACancelledOpenIsToldApart()
    {
        using var channel = new PushChannel();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal(
            ChannelOpenOutcome.Cancelled,
            await channel.OpenAsync("ignored", Bearer, url, cancelled.Token));
    }

    /// <summary>
    /// PP231's LOSS, countable. A frame that will not parse is dropped and never reaches the queue.
    /// </summary>
    [Fact]
    public async Task APayloadThatWillNotParseIsDropped()
    {
        toSend.Add("not json at all");
        toSend.Add(Notification("psn:sessionManager:sys:remotePlaySession:created"));

        using var channel = new PushChannel();
        Assert.Equal(ChannelOpenOutcome.Open, await channel.OpenAsync("ignored", Bearer, url));

        await channel.ReadAsync();

        Assert.Equal(1, channel.Dropped);
        Assert.Equal(1, channel.Queue.Count);
    }

    /// <summary>
    /// THE SECOND DIVERGENCE. PP214's ladder is not run here, and that is deliberate: a managed
    /// socket never sees a pong, so keeping the bookkeeping would kill every healthy connection at
    /// the first interval. The ladder itself is still asserted, against the clock and not a socket.
    /// </summary>
    [Fact]
    public void TheKeepaliveLadderIsTheSocketsAndTheRuleStillHolds()
    {
        Assert.True(PushChannel.KeepaliveIsTheSockets);

        // The rule PP214 measured, exercised where it can be: one number, and the pong asked first.
        Assert.Equal(
            KeepaliveStep.PongOverdue,
            PushSocketLoop.Next(PushSocketLoop.PingIntervalUs + 1, 0, expectingPong: true));

        Assert.Equal(
            KeepaliveStep.SendPing,
            PushSocketLoop.Next(PushSocketLoop.PingIntervalUs + 1, 0, expectingPong: false));

        Assert.Equal(KeepaliveStep.Read, PushSocketLoop.Next(1, 0, expectingPong: false));

        // And the cadence the socket is given instead.
        Assert.True(PushSocket.KeepAlive > TimeSpan.Zero);
    }

    /// <summary>The runtime's message types map onto the flags the loop decides with.</summary>
    [Fact]
    public void TheMessageTypesMapOntoTheFlags()
    {
        Assert.Equal(WebSocketFrameKind.Text, PushChannel.KindOf(WebSocketMessageType.Text));
        Assert.Equal(WebSocketFrameKind.Binary, PushChannel.KindOf(WebSocketMessageType.Binary));
        Assert.Equal(WebSocketFrameKind.Close, PushChannel.KindOf(WebSocketMessageType.Close));

        // And what each makes the loop do is PP214's, not this class's.
        Assert.True((PushSocketLoop.ActionsFor(WebSocketFrameKind.Close) & FrameAction.Close) != 0);
        Assert.True((PushSocketLoop.ActionsFor(WebSocketFrameKind.Text) & FrameAction.Deliver) != 0);
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Close();
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
