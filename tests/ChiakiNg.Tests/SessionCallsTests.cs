using System.Net;
using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP266: the five session calls, performed against a listener on the loopback.
///
/// A stub HttpClient handler would assert this test's own arrangement. A real listener answers with
/// a real status and a real body, so what is asserted is what the call did - the same reasoning
/// PP33's transfer tests were written on, and the same listener.
///
/// <see cref="TheCheckStillReportsAnUnreadableAnswerCorrectlyAndTheGapRemains"/> and
/// <see cref="TheLookupReturnsSuccessWithNoAddress"/> are the two that carry measured failures into
/// a call that actually runs.
/// </summary>
public class SessionCallsTests : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string prefix;
    private readonly CancellationTokenSource stopping = new();

    private int status = 200;
    private string body = "{}";
    private readonly List<string> seenHeaders = [];
    private string? seenBody;
    private string? seenMethod;

    public SessionCallsTests()
    {
        int port = FreePort();
        prefix = $"http://127.0.0.1:{port}/";

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

            foreach (string? name in context.Request.Headers.AllKeys)
            {
                if (name is not null)
                    seenHeaders.Add($"{name}: {context.Request.Headers[name]}");
            }

            seenMethod = context.Request.HttpMethod;

            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                seenBody = await reader.ReadToEndAsync().ConfigureAwait(false);

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = bytes.Length;

            try
            {
                await context.Response.OutputStream.WriteAsync(bytes, CancellationToken.None)
                    .ConfigureAwait(false);
                context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // The client went away.
            }
        }
    }

    private const string Bearer = "Authorization: Bearer token";

    /// <summary>The lookup reads the field and hands back what was in it.</summary>
    [Fact]
    public async Task TheLookupReadsTheField()
    {
        body = """{"fqdn":"ps.example.net"}""";

        (FqdnLookupOutcome outcome, string? address) =
            await SessionCalls.WebSocketFqdnAsync(Bearer, prefix);

        Assert.Equal(FqdnLookupOutcome.Ok, outcome);
        Assert.Equal("ps.example.net", address);

        // And the wss URL PP191 builds from it.
        Assert.Equal("wss", PushSocket.UrlFor(address!).Scheme);
    }

    /// <summary>
    /// A document without the field, and one where it is not a string, are told apart - which is
    /// the pair of checks PP254 kept separate.
    /// </summary>
    [Fact]
    public async Task TheTwoFieldFailuresAreToldApart()
    {
        body = """{"other":"x"}""";
        Assert.Equal(
            FqdnLookupOutcome.FieldAbsent,
            (await SessionCalls.WebSocketFqdnAsync(Bearer, prefix)).Outcome);

        body = """{"fqdn":7}""";
        Assert.Equal(
            FqdnLookupOutcome.FieldNotAString,
            (await SessionCalls.WebSocketFqdnAsync(Bearer, prefix)).Outcome);
    }

    /// <summary>
    /// PP254 CARRIED INTO A CALL THAT RUNS. Every outcome that writes no address is one, and the
    /// one that is not a failure is still one of them.
    /// </summary>
    [Fact]
    public async Task TheLookupReturnsSuccessWithNoAddress()
    {
        body = "not json at all";

        (FqdnLookupOutcome outcome, string? address) =
            await SessionCalls.WebSocketFqdnAsync(Bearer, prefix);

        Assert.Equal(FqdnLookupOutcome.Unreadable, outcome);
        Assert.Null(address);

        // The gap PP254 named still exists in the vocabulary this call answers in.
        Assert.True(WebSocketFqdn.SucceedsWithoutAnAddress(FqdnLookupOutcome.NoTokener));
    }

    /// <summary>An error status is a failure, which is what FAILONERROR does.</summary>
    [Fact]
    public async Task AnErrorStatusIsAFailure()
    {
        status = 403;

        Assert.Equal(
            FqdnLookupOutcome.HttpNotOk,
            (await SessionCalls.WebSocketFqdnAsync(Bearer, prefix)).Outcome);
    }

    /// <summary>
    /// PP233 CARRIED INTO A CALL THAT RUNS. The check asserts only that the answer was JSON, and the
    /// outcome its own task calls a non-failure is still reachable in this vocabulary.
    /// </summary>
    [Fact]
    public async Task TheCheckStillReportsAnUnreadableAnswerCorrectlyAndTheGapRemains()
    {
        body = """{"anything":true}""";
        Assert.Equal(SessionCheckOutcome.Ok, await SessionCalls.CheckAsync(Bearer, viewUrl: false, prefix));

        body = "<html>";
        Assert.Equal(
            SessionCheckOutcome.Unreadable,
            await SessionCalls.CheckAsync(Bearer, viewUrl: false, prefix));

        status = 500;
        Assert.Equal(
            SessionCheckOutcome.HttpNotOk,
            await SessionCalls.CheckAsync(Bearer, viewUrl: false, prefix));

        // Reproduced, not fixed: the tokener outcome is still not a failure.
        Assert.False(SessionCheck.IsFailure(SessionCheckOutcome.NoTokener));
    }

    /// <summary>Creating a session posts the body PP210 shapes, with the headers the core sets.</summary>
    [Fact]
    public async Task CreatingPostsTheBodyAndTheHeaders()
    {
        CallResult result = await SessionCalls.CreateAsync(Bearer, "context-id", prefix);

        Assert.True(result.Transferred);
        Assert.Equal("POST", seenMethod);
        Assert.Equal(SessionRequests.Create("context-id"), seenBody);

        Assert.Contains(seenHeaders, h => h.Contains("Bearer token", StringComparison.Ordinal));
        Assert.Contains(
            seenHeaders,
            h => h.StartsWith("Content-Type: " + HttpTransfer.JsonContentType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A message goes to the same place with the envelope as its body.</summary>
    [Fact]
    public async Task AMessageCarriesItsEnvelope()
    {
        string envelope = SessionMessageWriter.Envelope(
            SessionMessageWriter.ShortMessage(SessionMessageAction.Result, 42, 0), 1, "abc", "PS5");

        CallResult result = await SessionCalls.SendMessageAsync(Bearer, "sid", envelope, prefix);

        Assert.True(result.Transferred);
        Assert.Equal(envelope, seenBody);
    }

    /// <summary>
    /// PP235 CARRIED: the DELETE really does send a content type for a body it does not have.
    /// </summary>
    [Fact]
    public async Task TheDeleteSendsAContentTypeWithNoBody()
    {
        CallResult result = await SessionCalls.DeleteAsync(Bearer, "sid", prefix);

        Assert.True(result.Transferred);
        Assert.Equal("DELETE", seenMethod);
        Assert.Equal("", seenBody);

        Assert.Contains(
            seenHeaders,
            h => h.StartsWith("Content-Type: ", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>And the wakeup is built from the answer, not the question.</summary>
    [Fact]
    public async Task TheWakeupIsBuiltFromTheDiscoveredBase()
    {
        CallResult result = await SessionCalls.WakeupAsync(
            Bearer, prefix.TrimEnd('/'), "player-one", "{}");

        Assert.True(result.Transferred);
        Assert.Equal("POST", seenMethod);
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Close();
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
