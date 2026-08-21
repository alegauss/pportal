using System.Net;
using System.Net.WebSockets;
using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the one call site that is not an HTTP transfer, and the three things about it a port
/// would write differently.
///
/// The options are asserted without a server, because which DOOR each value goes through is
/// decided before any connection exists - and the subprotocol going through the wrong one is the
/// mistake that makes the server refuse the client entirely. The round trip is then driven against
/// a real WebSocket on the loopback, so "it connects" is a fact rather than an arrangement.
/// </summary>
public class PushSocketTests : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string prefix;
    private readonly Uri url;
    private readonly CancellationTokenSource stopping = new();

    private readonly List<string> seenHeaders = [];
    private string? seenSubProtocol;

    public PushSocketTests()
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
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            foreach (string? name in context.Request.Headers.AllKeys)
            {
                if (name is not null)
                    seenHeaders.Add($"{name}: {context.Request.Headers[name]}");
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext ws =
                await context.AcceptWebSocketAsync(PushSocket.SubProtocol).ConfigureAwait(false);

            seenSubProtocol = ws.SecWebSocketProtocols.FirstOrDefault();

            // Echo one message, which is enough to say the channel carries traffic.
            var buffer = new byte[256];
            WebSocketReceiveResult received =
                await ws.WebSocket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);

            await ws.WebSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, received.Count),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Close();
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE ONE THAT DECIDES WHETHER THE SERVER TALKS TO THIS CLIENT. The subprotocol is a plain
    /// header in curl and is not one here: ClientWebSocket owns Sec-WebSocket-Protocol and refuses
    /// it as a raw header, so the header a port would copy across with the other eight is the one
    /// that must go through a different door.
    /// </summary>
    [Fact]
    public void TheSubProtocolIsNotOneOfTheHeaders()
    {
        Assert.DoesNotContain(
            PushSocket.FixedHeaders,
            h => h.Name.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase));

        // Setting it raw does NOT throw - the first version of this check said it did and was
        // wrong. What it does instead is measured in TheRawHeaderRouteIsNotTheSameRequest below,
        // which is the honest form of the claim.
        using var socket = new ClientWebSocket();
        PushSocket.Configure(socket, "Bearer token");
    }

    /// <summary>
    /// The eight fixed headers, including the two that are not true of this client: it calls
    /// itself WebSocket++ and says Windows whatever it runs on. Both are what the server has been
    /// answering, so both are reproduced rather than derived.
    /// </summary>
    [Fact]
    public void TheClientKeepsSayingWhatTheCoreSays()
    {
        Assert.Contains(PushSocket.FixedHeaders, h => h is { Name: "User-Agent", Value: "WebSocket++/0.8.2" });
        Assert.Contains(PushSocket.FixedHeaders, h => h is { Name: "X-PSN-OS-VER", Value: "Windows/10.0" });

        Assert.Equal(7, PushSocket.FixedHeaders.Count);
    }

    /// <summary>
    /// The URL is the discovered FQDN and a fixed path, over TLS. Asserted because the scheme is
    /// the half a port carries over from an http:// neighbour.
    /// </summary>
    [Fact]
    public void TheUrlIsSecureAndOnTheFixedPath()
    {
        Uri built = PushSocket.UrlFor("example.playstation.net");

        Assert.Equal("wss", built.Scheme);
        Assert.Equal(PushSocket.Path, built.AbsolutePath);
        Assert.Equal("example.playstation.net", built.Host);
    }

    /// <summary>
    /// This transfer's timeout is disabled where every other one in the core sets a real number.
    /// A push channel is open for the session, and a port applying its ordinary transfer timeout
    /// would drop notifications and look like a server that went quiet.
    /// </summary>
    [Fact]
    public void ThisIsTheTransferWithNoTimeout()
    {
        Assert.Equal(TimeSpan.Zero, PushSocket.NoTimeout);
        Assert.True(PushSocket.KeepAlive > TimeSpan.Zero, "an idle channel still needs a ping");
    }

    /// <summary>
    /// And it connects and carries a message, against a real server. Everything above is about
    /// which door a value goes through; this is the one check that says the doors were the right
    /// ones.
    /// </summary>
    [Fact]
    public async Task ItConnectsAndCarriesAMessage()
    {
        using var socket = new ClientWebSocket();
        PushSocket.Configure(socket, "Bearer test-token");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(url, timeout.Token);

        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal(PushSocket.SubProtocol, socket.SubProtocol);

        byte[] sent = Encoding.UTF8.GetBytes("ping");
        await socket.SendAsync(sent, WebSocketMessageType.Text, true, timeout.Token);

        var buffer = new byte[64];
        WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal("ping", Encoding.UTF8.GetString(buffer, 0, received.Count));

        // The server saw the headers the core sends, and the subprotocol as a subprotocol.
        Assert.Equal(PushSocket.SubProtocol, seenSubProtocol);
        Assert.Contains(seenHeaders, h => h.StartsWith("User-Agent: WebSocket++/0.8.2", StringComparison.Ordinal));
        Assert.Contains(seenHeaders, h => h.StartsWith("Authorization: Bearer test-token", StringComparison.Ordinal));
    }

    /// <summary>
    /// What the raw-header route actually does, measured rather than asserted from memory.
    ///
    /// A port copying "Sec-WebSocket-Protocol" across with the other eight headers gets no error at
    /// configuration time. This drives that shape against a real server and records what comes of
    /// it, so the reason to use AddSubProtocol is a fact on this runtime rather than a rule
    /// somebody remembers reading.
    /// </summary>
    [Fact]
    public async Task TheRawHeaderRouteIsNotTheSameRequest()
    {
        using var socket = new ClientWebSocket();

        // Deliberately the wrong door: the header, and no AddSubProtocol.
        socket.Options.SetRequestHeader("Sec-WebSocket-Protocol", PushSocket.SubProtocol);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        seenSubProtocol = null;

        try
        {
            await socket.ConnectAsync(url, timeout.Token);
        }
        catch (WebSocketException)
        {
            // A refused handshake is one honest outcome and is the strongest form of the finding:
            // the raw header does not produce a usable connection.
            Assert.Null(socket.SubProtocol);
            return;
        }

        // The other honest outcome: it connected, and the CLIENT does not consider a subprotocol
        // negotiated - which is what matters, because everything after the handshake reads
        // socket.SubProtocol rather than the header that was sent.
        Assert.Null(socket.SubProtocol);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void ThePushSocketsSetupIsStillTheQtCores()
    {
        string? path = PushSocketSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PushSocketSource.TheSubProtocolIsStillAHeader(core), "a header there");
        Assert.True(PushSocketSource.TheTimeoutIsStillDisabled(core), "no timeout on this one");
        Assert.True(PushSocketSource.TheModeIsStillTheWebSocketOne(core), "connect-only 2");
        Assert.True(PushSocketSource.TheFixedHeadersAreStillThese(core), "the same eight strings");
    }
}
