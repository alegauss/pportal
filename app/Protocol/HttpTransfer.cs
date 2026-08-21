using System.Net.Http;
using System.Net.Http.Headers;

namespace ChiakiNg.Protocol;

/// <summary>What a transfer produced, in the shape curl's callers already read.</summary>
/// <param name="Status">The HTTP status, or 0 when the transfer never got one.</param>
/// <param name="Body">The bytes, or null - which is what a failed transfer hands back.</param>
/// <param name="Failure">Why, or null. Set whenever <paramref name="Body"/> is null.</param>
public readonly record struct TransferResult(int Status, byte[]? Body, string? Failure)
{
    /// <summary>Whether curl would have called this a successful transfer.</summary>
    public bool Ok => Failure is null;
}

/// <summary>
/// PP33: the three curl behaviours HttpClient does not have, as one place.
///
/// PP186 turned 420 call sites into ten options and found three with no plain equivalent. This is
/// those three, so that the translation of each call site is a call to this rather than a fresh
/// decision about what curl was doing:
///
/// 1. FAILONERROR. A response of 400 or above is a FAILED TRANSFER with no body, not a response
///    the caller inspects. Twelve sites are written that way and none re-states it, so a port that
///    returned the response would make twelve call sites wrong in the same invisible direction.
///
/// 2. SHARE. Curl's share handle pools DNS, connections and cookies across easy handles. The
///    equivalent is ONE HttpClient, which is why there is a static one here and no constructor
///    that makes another: a client per request throws the pooling away and exhausts sockets under
///    the hole-punching retry loop, and it is the shape a port reaches for first.
///
/// 3. CONNECT_ONLY=2 is not here at all, deliberately. That value is curl's WebSocket mode and its
///    equivalent is ClientWebSocket - a different type, not an option on this one. Putting it
///    behind the same door would hide that one of the 420 sites is not an HTTP transfer.
/// </summary>
public static class HttpTransfer
{
    /// <summary>
    /// The one client, which is the SHARE handle's equivalent.
    ///
    /// Static and never disposed, which is the documented shape for HttpClient and the opposite of
    /// what a `using` per call would do: the connections it holds are the pool, and disposing it
    /// per transfer is how a retry loop runs out of sockets while looking careful.
    /// </summary>
    private static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        // Curl's share handle keeps a connection across transfers rather than for a fixed time.
        // Two minutes is the runtime's own default and is named here so that changing it is a
        // decision rather than an upgrade.
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });

    /// <summary>
    /// One transfer, answering the way a curl handle with FAILONERROR set answers.
    /// </summary>
    /// <param name="url">The URL, which is CURLOPT_URL.</param>
    /// <param name="timeout">CURLOPT_TIMEOUT, which curl applies to the WHOLE transfer.</param>
    /// <param name="headers">CURLOPT_HTTPHEADER, or none.</param>
    public static async Task<TransferResult> GetAsync(
        string url,
        TimeSpan timeout,
        IReadOnlyList<(string Name, string Value)>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        // The whole transfer, headers and body together - which is what CURLOPT_TIMEOUT bounds.
        // HttpClient.Timeout would do it too, but it is a property of the SHARED client and would
        // make one call site's timeout every call site's.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            foreach ((string name, string value) in headers ?? [])
                request.Headers.TryAddWithoutValidation(name, value);

            using HttpResponseMessage response =
                await Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token)
                    .ConfigureAwait(false);

            int status = (int)response.StatusCode;

            // FAILONERROR, and the body is NOT read: curl does not hand one back for a failed
            // transfer, and reading it here would let a caller start relying on something the Qt
            // client's callers have never had.
            if (CurlSemantics.WouldFailTransfer(status))
                return new TransferResult(status, null, $"HTTP {status}");

            byte[] body = await response.Content.ReadAsByteArrayAsync(bounded.Token).ConfigureAwait(false);
            return new TransferResult(status, body, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout rather than the caller. Told apart because a caller that cancelled knows
            // why and a transfer that timed out is the thing worth logging.
            return new TransferResult(0, null, "timed out after " + timeout);
        }
        catch (HttpRequestException ex)
        {
            return new TransferResult(0, null, ex.Message);
        }
    }

    /// <summary>
    /// The content type every body-carrying transfer in the core sets, WITH the charset.
    ///
    /// It is set as an explicit header on all six of them, and that is load-bearing rather than
    /// polite: CURLOPT_POSTFIELDS makes curl send Content-Type: application/x-www-form-urlencoded
    /// unless told otherwise, so the header is what stops six JSON bodies being announced as form
    /// data. The charset parameter is part of the string the server has been answering, and a port
    /// reaching for JsonContent would send "application/json" without it.
    /// </summary>
    public const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>
    /// A transfer with a body, or with a method curl set through CURLOPT_CUSTOMREQUEST.
    ///
    /// One door for both because that is what the core has: four sites set POSTFIELDS and one sets
    /// CUSTOMREQUEST to DELETE, and every one of them then behaves like the GET above - same
    /// FAILONERROR, same shared client, same whole-transfer bound.
    /// </summary>
    /// <param name="method">POST for the four, DELETE for the one.</param>
    /// <param name="body">The JSON, or null for a bodyless method.</param>
    public static async Task<TransferResult> SendAsync(
        HttpMethod method,
        string url,
        TimeSpan timeout,
        string? body = null,
        IReadOnlyList<(string Name, string Value)>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(url);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(method, url);

            foreach ((string name, string value) in headers ?? [])
                request.Headers.TryAddWithoutValidation(name, value);

            if (body is not null)
            {
                // The type is set on the CONTENT and not through the header list above: a
                // Content-Type in request.Headers is refused by HttpClient, which puts it on the
                // content - so a call site copying the core's header list wholesale would throw.
                request.Content = new StringContent(body, System.Text.Encoding.UTF8);
                request.Content.Headers.ContentType =
                    MediaTypeHeaderValue.Parse(JsonContentType);
            }

            using HttpResponseMessage response =
                await Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token)
                    .ConfigureAwait(false);

            int status = (int)response.StatusCode;

            if (CurlSemantics.WouldFailTransfer(status))
                return new TransferResult(status, null, $"HTTP {status}");

            byte[] received = await response.Content.ReadAsByteArrayAsync(bounded.Token).ConfigureAwait(false);
            return new TransferResult(status, received, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TransferResult(0, null, "timed out after " + timeout);
        }
        catch (HttpRequestException ex)
        {
            return new TransferResult(0, null, ex.Message);
        }
    }

    /// <summary>The four POSTFIELDS sites, which all carry JSON.</summary>
    public static Task<TransferResult> PostJsonAsync(
        string url,
        string json,
        TimeSpan timeout,
        IReadOnlyList<(string Name, string Value)>? headers = null,
        CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, url, timeout, json, headers, cancellationToken);

    /// <summary>
    /// The header list a call site builds, in the order curl's slist keeps them.
    ///
    /// Here so that a site translating CURLOPT_HTTPHEADER has somewhere to put its pairs without
    /// each one inventing a shape; the ORDER is kept because a slist does.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Headers(
        params (string Name, string Value)[] pairs)
        => pairs;
}
