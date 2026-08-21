using System.Net;
using System.Net.Http;
using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the transfer answering the way a curl handle with FAILONERROR set answers.
///
/// Driven against a real server on the loopback rather than a mocked handler, because what is
/// being asserted is what comes back from an HTTP exchange - a fake that returns whatever it was
/// told to return would be asserting the test's own arrangement. An HttpListener on 127.0.0.1
/// needs no elevation and no network.
/// </summary>
public class HttpTransferTests : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string prefix;
    private readonly CancellationTokenSource stopping = new();

    /// <summary>What the next request is answered with.</summary>
    private int status = 200;
    private string body = "ok";
    private TimeSpan delay = TimeSpan.Zero;
    private readonly List<string> seenHeaders = [];
    private string? seenBody;
    private string? seenMethod;

    public HttpTransferTests()
    {
        // Port 0 is not available to HttpListener, so a free one is found the ordinary way.
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

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

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
                // The client went away, which is what the timeout case does.
            }
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.Close();
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>An ordinary transfer hands back the body.</summary>
    [Fact]
    public async Task ATwoHundredHandsBackTheBody()
    {
        status = 200;
        body = "hello";

        TransferResult result = await HttpTransfer.GetAsync(prefix, TimeSpan.FromSeconds(10));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(200, result.Status);
        Assert.Equal("hello", Encoding.UTF8.GetString(result.Body!));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A 404 is a FAILED TRANSFER with no body, not a response to inspect -
    /// which is what the twelve FAILONERROR call sites are written expecting and what HttpClient
    /// does not do by itself.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task AFourHundredOrAboveIsAFailedTransferWithNoBody(int code)
    {
        status = code;
        body = "a body nobody should read";

        TransferResult result = await HttpTransfer.GetAsync(prefix, TimeSpan.FromSeconds(10));

        Assert.False(result.Ok);
        Assert.Null(result.Body);
        Assert.Equal(code, result.Status);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.Failure!, StringComparison.Ordinal);
    }

    /// <summary>And 399 does not, so the threshold is curl's and not "any non-2xx".</summary>
    [Fact]
    public async Task ThreeNinetyNineIsStillATransfer()
    {
        status = 399;
        body = "still a body";

        TransferResult result = await HttpTransfer.GetAsync(prefix, TimeSpan.FromSeconds(10));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(399, result.Status);
    }

    /// <summary>The headers a call site sets reach the server, in the order it set them.</summary>
    [Fact]
    public async Task TheHeadersReachTheServer()
    {
        status = 200;
        seenHeaders.Clear();

        await HttpTransfer.GetAsync(
            prefix,
            TimeSpan.FromSeconds(10),
            HttpTransfer.Headers(("X-Chiaki-One", "1"), ("X-Chiaki-Two", "2")));

        Assert.Contains(seenHeaders, h => h.StartsWith("X-Chiaki-One: 1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(seenHeaders, h => h.StartsWith("X-Chiaki-Two: 2", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The timeout bounds the WHOLE transfer, which is what CURLOPT_TIMEOUT does - and it is told
    /// apart from a caller's own cancellation, because one of the two is worth logging.
    /// </summary>
    [Fact]
    public async Task ASlowServerTimesOutAndSaysSo()
    {
        status = 200;
        delay = TimeSpan.FromSeconds(5);

        TransferResult result = await HttpTransfer.GetAsync(prefix, TimeSpan.FromMilliseconds(200));

        delay = TimeSpan.Zero;

        Assert.False(result.Ok);
        Assert.Null(result.Body);
        Assert.Contains("timed out", result.Failure!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A caller's own cancellation is not reported as a timeout. Two failures that read alike would
    /// send somebody looking at the network for something the application did.
    /// </summary>
    [Fact]
    public async Task ACallersCancellationIsNotATimeout()
    {
        status = 200;
        delay = TimeSpan.FromSeconds(5);

        using var caller = new CancellationTokenSource();
        caller.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => HttpTransfer.GetAsync(prefix, TimeSpan.FromSeconds(30), null, caller.Token));

        delay = TimeSpan.Zero;
    }

    /// <summary>
    /// The four POSTFIELDS sites carry JSON, and the content type reaches the server WITH its
    /// charset - which is the string the server has been answering.
    ///
    /// It also matters more than politeness: CURLOPT_POSTFIELDS makes curl announce
    /// application/x-www-form-urlencoded unless told otherwise, so the explicit header is what
    /// stops six JSON bodies being sent as form data.
    /// </summary>
    [Fact]
    public async Task APostCarriesJsonWithItsCharset()
    {
        status = 200;
        body = "{}";
        seenHeaders.Clear();
        seenBody = null;

        TransferResult result = await HttpTransfer.PostJsonAsync(
            prefix, """{"a":1}""", TimeSpan.FromSeconds(10));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("""{"a":1}""", seenBody);

        Assert.Contains(
            seenHeaders,
            h => h.StartsWith("Content-Type: " + HttpTransfer.JsonContentType, StringComparison.OrdinalIgnoreCase));

        // Not the form encoding curl would have chosen for a POSTFIELDS body on its own.
        Assert.DoesNotContain(
            seenHeaders,
            h => h.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>FAILONERROR applies to a POST as well - it is set on the same handles.</summary>
    [Fact]
    public async Task APostAlsoFailsAtFourHundred()
    {
        status = 400;
        body = "a body nobody should read";

        TransferResult result = await HttpTransfer.PostJsonAsync(
            prefix, "{}", TimeSpan.FromSeconds(10));

        Assert.False(result.Ok);
        Assert.Null(result.Body);
    }

    /// <summary>
    /// The one CUSTOMREQUEST site is a DELETE with no body, which is what curl's option does:
    /// change the method and nothing else.
    /// </summary>
    [Fact]
    public async Task TheCustomMethodIsADeleteWithNoBody()
    {
        status = 200;
        body = "";
        seenMethod = null;
        seenBody = null;

        TransferResult result = await HttpTransfer.SendAsync(
            HttpMethod.Delete, prefix, TimeSpan.FromSeconds(10));

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("DELETE", seenMethod);
        Assert.True(string.IsNullOrEmpty(seenBody), $"a bodyless method sent: {seenBody}");
    }

    /// <summary>
    /// A host that is not there fails as a transfer rather than throwing, which is how curl's
    /// callers read it: a CURLcode, not an exception.
    /// </summary>
    [Fact]
    public async Task AnUnreachableHostIsAFailedTransfer()
    {
        // Port 1 on the loopback, which nothing listens on.
        TransferResult result =
            await HttpTransfer.GetAsync("http://127.0.0.1:1/", TimeSpan.FromSeconds(5));

        Assert.False(result.Ok);
        Assert.Null(result.Body);
        Assert.NotNull(result.Failure);
    }
}
