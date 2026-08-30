namespace ChiakiNg.Protocol;

/// <summary>
/// PP548, under PP533: <see cref="IHolepunchCreateSteps"/> over the pieces that actually run.
///
/// PP545 put the create's steps in order behind an interface so the sequence could be tested
/// without a network. This is the other side of that interface: SessionCalls for the two HTTP
/// calls, PushChannel for the websocket and the queue it fills, and NotificationWait for the wait
/// on that queue. Between them the create is a thing that can be run rather than described.
///
/// THE OPEN IS STARTED AND AWAITED SEPARATELY, which keeps the C's two steps two. The C creates a
/// thread that connects and a caller that waits on the session state; managed, ClientWebSocket's
/// connect is one await, and folding the pair would have left PP545's bounded wait with nothing to
/// bound. So <see cref="OpenWebSocketAsync"/> starts the connect and
/// <see cref="WaitForOpenAsync"/> awaits it against the deadline - which is where PP545's declared
/// departure from the C's unbounded wait actually takes effect.
///
/// THE READ LOOP IS THE C'S WEBSOCKET THREAD. Once open, <see cref="PushChannel.ReadAsync"/> runs
/// until the channel ends, delivering each notification into the channel's own queue. Nothing here
/// joins it: it ends when the socket does, which is what the C's thread does too.
/// </summary>
public sealed class LiveHolepunchCreateSteps : IHolepunchCreateSteps, IDisposable
{
    private readonly string oauthHeader;
    private readonly string pushContextId;
    private readonly PushChannel channel;

    private Task<ChannelOpenOutcome>? opening;
    private Task<ChannelEndReason>? reading;

    /// <param name="oauthHeader">The bearer PsnEndpoints builds.</param>
    /// <param name="pushContextId">The id the create request carries.</param>
    /// <param name="channel">The channel, injected so a test can hand over a closed one.</param>
    public LiveHolepunchCreateSteps(string oauthHeader, string pushContextId, PushChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        ArgumentNullException.ThrowIfNull(pushContextId);

        this.oauthHeader = oauthHeader;
        this.pushContextId = pushContextId;
        this.channel = channel ?? new PushChannel();
    }

    /// <summary>The host the lookup found, or null before it has run.</summary>
    public string? Fqdn { get; private set; }

    /// <summary>The queue the channel fills, which is what the waits below read.</summary>
    public NotificationQueue Queue => channel.Queue;

    /// <summary>PP254's lookup, performed rather than modelled.</summary>
    public async Task<bool> LookUpFqdnAsync(CancellationToken cancellationToken)
    {
        (FqdnLookupOutcome outcome, string? address) = await SessionCalls
            .WebSocketFqdnAsync(oauthHeader, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // PP254's own reading: an outcome that succeeds without an address is not an address.
        Fqdn = WebSocketFqdn.AddressAfter(outcome, address);
        return Fqdn is not null;
    }

    /// <summary>
    /// Starts the connect. Deliberately not awaited here - see the note on the type.
    /// </summary>
    public Task<bool> OpenWebSocketAsync(CancellationToken cancellationToken)
    {
        if (Fqdn is not { } host)
            return Task.FromResult(false);

        opening = channel.OpenAsync(host, oauthHeader, cancellationToken: cancellationToken);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Awaits the connect against PP545's deadline, and starts the read loop once it is open.
    ///
    /// This is where the departure lives: the C waits on a condition nothing sets when the connect
    /// fails, and this waits on the connect itself with a bound.
    /// </summary>
    public async Task<bool> WaitForOpenAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (opening is not { } connect)
            return false;

        Task finished = await Task.WhenAny(connect, Task.Delay(timeout, cancellationToken))
            .ConfigureAwait(false);

        if (!ReferenceEquals(finished, connect))
            return false;

        if (await connect.ConfigureAwait(false) != ChannelOpenOutcome.Open)
            return false;

        // The C's websocket thread: it runs until the socket ends and fills the queue as it goes.
        reading = channel.ReadAsync(cancellationToken);
        return true;
    }

    /// <summary>PP266's create call.</summary>
    public async Task<bool> CreateSessionAsync(CancellationToken cancellationToken)
    {
        CallResult result = await SessionCalls
            .CreateAsync(oauthHeader, pushContextId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Failure is null && !CurlSemantics.WouldFailTransfer(result.Status);
    }

    /// <summary>
    /// Waits for the two notifications the create is finished by, on the queue the channel fills.
    ///
    /// PP212's wait is a cursor walk that removes nothing, so this asks it rather than draining:
    /// the notifications stay for whatever reads next, which is what the C's queue does.
    /// </summary>
    public async Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Queue.Items.Any(Finishes))
                return true;

            if (reading is { IsCompleted: true })
                return false;

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// How often the queue is looked at while waiting. The C blocks on a condition the websocket
    /// thread signals; polling is the managed shape here because the queue PP212 ported has no
    /// signal of its own, and 25 ms against a thirty second deadline is not a cost worth a
    /// condition variable.
    /// </summary>
    public static TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(25);

    /// <summary>Which notifications end the create's wait.</summary>
    public static bool Finishes(QueuedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.Type is PushNotificationType.SessionCreated
            or PushNotificationType.MemberCreated;
    }

    /// <summary>Closes the channel, which ends the read loop with it.</summary>
    public void Dispose() => channel.Dispose();
}
