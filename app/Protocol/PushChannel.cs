using System.Net.WebSockets;
using System.Text;

namespace ChiakiNg.Protocol;

/// <summary>How opening the channel ended.</summary>
public enum ChannelOpenOutcome
{
    /// <summary>It is open.</summary>
    Open,

    /// <summary>The server refused, or was not there.</summary>
    Refused,

    /// <summary>The caller cancelled before it opened.</summary>
    Cancelled,
}

/// <summary>How the reading ended.</summary>
public enum ChannelEndReason
{
    /// <summary>The other end closed it.</summary>
    ClosedByPeer,

    /// <summary>The caller asked it to stop.</summary>
    Cancelled,

    /// <summary>A pong was expected and the interval passed.</summary>
    PongOverdue,

    /// <summary>The socket failed while reading.</summary>
    Faulted,
}

/// <summary>
/// PP267: the push notification channel, opened and read.
///
/// PP191 has the URL, the subprotocol and the eight fixed headers, and a configure that sets every
/// option on the door it belongs to. PP214 has the frame rules and the keepalive ladder. PP212 has
/// the queue. Nothing connected any of it. This does.
///
/// ONE DELIBERATE DIVERGENCE, ON THE RECORD. PP258 measured that the core's thread signals its
/// waiter only after connecting - a refused connection jumps to a cleanup that clears a different
/// flag, leaving a caller waiting with no timeout and no cancellation. That is a hang rather than a
/// log, and reproducing it into new code nothing depends on yet would trade a working caller for a
/// fidelity nobody can observe. <see cref="OpenAsync"/> REPORTS the refusal.
/// <see cref="SessionCreateSource.TheCleanupStillClearsTheOtherFlag"/> goes on recording that the
/// core does not, so the difference stays visible instead of becoming an assumption.
///
/// AND A SECOND ONE, FOUND BY A TEST. PP214's ladder ends the socket when a pong is overdue. A
/// managed socket cannot run it: ClientWebSocket answers and consumes ping and pong frames itself
/// and never surfaces one to a read. Keeping the bookkeeping would mean setting the expectation,
/// never seeing the answer, and killing every healthy connection at the first interval. So keepalive
/// here is the socket's own, which is what <see cref="PushSocket.KeepAlive"/> already said it was -
/// the ladder stays as the description of the C it was written from. See
/// <see cref="KeepaliveIsTheSockets"/>, which is a value so the delegation is asserted rather than
/// assumed from the absence of code.
///
/// Everything else is carried: a payload that will not parse is dropped rather than queued, which is
/// PP231's asymmetry, and what a frame means is still PP214's answer and not this class's.
/// </summary>
public sealed class PushChannel : IDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly NotificationQueue queue = new();

    /// <summary>What has arrived and not been taken.</summary>
    public NotificationQueue Queue => queue;

    /// <summary>How many frames the loop has acted on, which a test can watch without a clock.</summary>
    public int FramesHandled { get; private set; }

    /// <summary>And how many were dropped for not parsing - PP231's loss, counted.</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// Whether the ping cadence is the socket's rather than this loop's. It is - see the class note.
    ///
    /// Stated as a value because the alternative evidence is an absence of code, and an absence is
    /// what a later reader would fill back in.
    /// </summary>
    public const bool KeepaliveIsTheSockets = true;

    /// <summary>
    /// Opens the channel against a discovered address.
    /// </summary>
    public async Task<ChannelOpenOutcome> OpenAsync(
        string fqdn, string authorizationHeader, Uri? url = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fqdn);
        ArgumentNullException.ThrowIfNull(authorizationHeader);

        PushSocket.Configure(socket, authorizationHeader);

        try
        {
            await socket.ConnectAsync(url ?? PushSocket.UrlFor(fqdn), cancellationToken)
                .ConfigureAwait(false);

            return ChannelOpenOutcome.Open;
        }
        catch (OperationCanceledException)
        {
            return ChannelOpenOutcome.Cancelled;
        }
        catch (WebSocketException)
        {
            // Reported, not swallowed - see the class note.
            return ChannelOpenOutcome.Refused;
        }
    }

    /// <summary>
    /// Reads frames until the far end closes, the caller stops it, or the socket faults.
    ///
    /// NOT until a pong goes overdue - see <see cref="KeepaliveIsTheSockets"/>.
    /// </summary>
    public async Task<ChannelEndReason> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[8192];

        while (true)
        {
            WebSocketReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ChannelEndReason.Cancelled;
            }
            catch (WebSocketException)
            {
                return ChannelEndReason.Faulted;
            }

            FrameAction actions = PushSocketLoop.ActionsFor(KindOf(received.MessageType));
            FramesHandled++;

            if ((actions & FrameAction.Close) != 0)
                return ChannelEndReason.ClosedByPeer;

            if ((actions & FrameAction.Deliver) != 0)
                Deliver(buffer.AsSpan(0, received.Count));
        }
    }

    /// <summary>
    /// One notification: parsed, typed, and queued - or dropped where it will not parse.
    /// </summary>
    private void Deliver(ReadOnlySpan<byte> frame)
    {
        string payload = Encoding.UTF8.GetString(frame);

        // One frame, one document - PP215's rule, and the document is released here because the
        // queue keeps the text rather than the parse.
        PushNotificationType type;
        using (System.Text.Json.JsonDocument? document = FrameParsing.Parse(frame))
        {
            type = document is null
                ? PushNotificationType.Unknown
                : PushNotification.TypeOf(document.RootElement);
        }

        if (type == PushNotificationType.Unknown)
        {
            // PP231: a payload that will not parse is DROPPED rather than queued, and the enqueue
            // sits below the branch that leaves. Counted so the loss is observable.
            Dropped++;
            return;
        }

        queue.Enqueue(new QueuedNotification(type, payload));
    }

    /// <summary>The runtime's message type, in the flags PP214 decides on.</summary>
    public static WebSocketFrameKind KindOf(WebSocketMessageType type) => type switch
    {
        WebSocketMessageType.Text => WebSocketFrameKind.Text,
        WebSocketMessageType.Binary => WebSocketFrameKind.Binary,
        WebSocketMessageType.Close => WebSocketFrameKind.Close,
        _ => WebSocketFrameKind.None,
    };

    /// <summary>Closes the channel if it is open, and releases the socket.</summary>
    public void Dispose()
    {
        socket.Dispose();
        GC.SuppressFinalize(this);
    }
}
