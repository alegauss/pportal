using System.Net.Http;
using System.Text.Json;

namespace ChiakiNg.Protocol;

/// <summary>What one session call did, in the vocabulary the matching task already named.</summary>
/// <param name="Status">The HTTP status, or zero where the transfer never got one.</param>
/// <param name="Failure">What went wrong, or null.</param>
/// <param name="Body">What came back, where the call reads one.</param>
public readonly record struct CallResult(int Status, string? Failure, byte[]? Body)
{
    /// <summary>Whether the transfer itself worked.</summary>
    public bool Transferred => Failure is null;
}

/// <summary>
/// PP266: the five session calls, performed rather than described.
///
/// PP206 ported the URLs, PP210 the bodies, PP250 the message envelope, and PP33's
/// <see cref="HttpTransfer"/> is a real HttpClient carrying the curl handle's semantics. The
/// outcomes were ported too - <see cref="SessionCheckOutcome"/>, <see cref="FqdnLookupOutcome"/>,
/// <see cref="SessionDelete"/> - each an enum with nothing behind it. This is what was missing: the
/// call sites.
///
/// Each function here takes what a caller holds, performs the transfer, and answers in the enum its
/// own task defined. Nothing invents a new vocabulary, and the measured failures are carried rather
/// than repaired:
///
///   The CHECK still reports a parser it could not allocate as success (PP233), so
///   <see cref="CheckAsync"/> can return an outcome <see cref="SessionCheck.IsFailure"/> calls fine.
///
///   The LOOKUP still returns success with no address written (PP254), so
///   <see cref="WebSocketFqdnAsync"/> hands back a null address beside a non-failure.
///
///   The DELETE still sends a JSON content type on a request with no body (PP235).
///
/// None of this talks to a console. There is no account, token or session id in here - every value
/// comes from the caller, which is what lets a test point these at a local listener.
/// </summary>
public static class SessionCalls
{
    /// <summary>How long a check is given, which is the only call with its own timeout.</summary>
    public static TimeSpan CheckTimeout => TimeSpan.FromSeconds(SessionCheck.TimeoutSeconds);

    /// <summary>And what everything else gets.</summary>
    public static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

    /// <summary>The headers a JSON request carries, as the core sets them.</summary>
    public static IReadOnlyList<(string Name, string Value)> JsonHeaders(string oauthHeader)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        return Split([oauthHeader, PsnEndpoints.JsonContentType]);
    }

    /// <summary>
    /// Asking which host to open the websocket against.
    /// </summary>
    /// <returns>The outcome, and the address - which is null on more outcomes than one.</returns>
    public static async Task<(FqdnLookupOutcome Outcome, string? Address)> WebSocketFqdnAsync(
        string oauthHeader, string? url = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);

        TransferResult transfer = await HttpTransfer.GetAsync(
            url ?? WebSocketFqdn.Url, DefaultTimeout, JsonHeaders(oauthHeader), cancellationToken)
            .ConfigureAwait(false);

        if (!transfer.Ok)
        {
            return (
                CurlSemantics.WouldFailTransfer(transfer.Status)
                    ? FqdnLookupOutcome.HttpNotOk
                    : FqdnLookupOutcome.Network,
                null);
        }

        if (!TryRead(transfer.Body, out JsonElement document))
            return (FqdnLookupOutcome.Unreadable, null);

        if (!document.TryGetProperty(WebSocketFqdn.Field, out JsonElement field))
            return (FqdnLookupOutcome.FieldAbsent, null);

        if (field.ValueKind != JsonValueKind.String)
            return (FqdnLookupOutcome.FieldNotAString, null);

        return (FqdnLookupOutcome.Ok, field.GetString());
    }

    /// <summary>
    /// Checking a session, which sends nothing and keeps nothing but whether the answer was JSON.
    /// </summary>
    public static async Task<SessionCheckOutcome> CheckAsync(
        string oauthHeader, bool viewUrl, string? url = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);

        TransferResult transfer = await HttpTransfer.GetAsync(
            url ?? SessionCheck.UrlFor(viewUrl), CheckTimeout, JsonHeaders(oauthHeader), cancellationToken)
            .ConfigureAwait(false);

        // The three arguments PP233 measured, in its own order - and the tokener is always
        // allocatable here, which is the one input a managed runtime cannot make false.
        return SessionCheck.Result(
            transferred: transfer.Ok,
            httpOk: !CurlSemantics.WouldFailTransfer(transfer.Status),
            tokener: true,
            parsed: TryRead(transfer.Body, out _));
    }

    /// <summary>Creating a session.</summary>
    public static Task<CallResult> CreateAsync(
        string oauthHeader, string pushContextId, string? url = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pushContextId);

        return PostAsync(
            url ?? PsnEndpoints.SessionCreateUrl,
            SessionRequests.Create(pushContextId),
            oauthHeader,
            cancellationToken);
    }

    /// <summary>And sending it a message, which is the envelope PP250 sized.</summary>
    public static Task<CallResult> SendMessageAsync(
        string oauthHeader, string sessionId, string envelope, string? url = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(envelope);

        return PostAsync(
            url ?? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                PsnEndpoints.SessionMessageFormat,
                sessionId),
            envelope,
            oauthHeader,
            cancellationToken);
    }

    /// <summary>
    /// Leaving one - the DELETE that carries a content type for a body it does not have.
    /// </summary>
    public static async Task<CallResult> DeleteAsync(
        string oauthHeader, string sessionId, string? url = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        ArgumentNullException.ThrowIfNull(sessionId);

        TransferResult transfer = await HttpTransfer.SendAsync(
            new HttpMethod(SessionDelete.Method),
            url ?? SessionDelete.UrlFor(sessionId),
            DefaultTimeout,

            // No body, and the content type below is sent regardless - PP235's finding, carried.
            body: null,
            Split(SessionDelete.Headers(oauthHeader)),
            cancellationToken)
            .ConfigureAwait(false);

        return new CallResult(transfer.Status, transfer.Failure, transfer.Body);
    }

    /// <summary>And waking a PS4, whose URL comes from the answer and not the question.</summary>
    public static Task<CallResult> WakeupAsync(
        string oauthHeader, string discoveredBase, string onlineId, string envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveredBase);
        ArgumentNullException.ThrowIfNull(onlineId);
        ArgumentNullException.ThrowIfNull(envelope);

        return PostAsync(
            Ps4Wakeup.UrlFor(discoveredBase, onlineId), envelope, oauthHeader, cancellationToken);
    }

    private static async Task<CallResult> PostAsync(
        string url, string body, string oauthHeader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);

        TransferResult transfer = await HttpTransfer.PostJsonAsync(
            url, body, DefaultTimeout, JsonHeaders(oauthHeader), cancellationToken)
            .ConfigureAwait(false);

        return new CallResult(transfer.Status, transfer.Failure, transfer.Body);
    }

    /// <summary>
    /// The core's headers are one string each; the transfer wants a name and a value.
    /// </summary>
    private static IReadOnlyList<(string Name, string Value)> Split(IReadOnlyList<string> headers)
    {
        var split = new List<(string, string)>(headers.Count);
        foreach (string header in headers)
        {
            int at = header.IndexOf(':', StringComparison.Ordinal);
            if (at < 0)
                continue;

            split.Add((header[..at].Trim(), header[(at + 1)..].Trim()));
        }

        return split;
    }

    /// <summary>Whether a body parses as JSON at all, which is the whole of what a check asks.</summary>
    private static bool TryRead(byte[]? body, out JsonElement document)
    {
        document = default;
        if (body is null || body.Length == 0)
            return false;

        try
        {
            using var parsed = JsonDocument.Parse(body);
            document = parsed.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
