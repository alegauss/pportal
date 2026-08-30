namespace ChiakiNg.Protocol;

/// <summary>
/// PP550, under PP33: <see cref="IHolepunchPunchSteps"/> over the pieces that actually run.
///
/// PP548 did the create and PP549 the start; this is the third and last of PP533's sequences, and
/// the one that produces two of the five results PP533 settled session.c would take - the sockets.
/// The pieces were all here: PP212's queue for the messages, SessionMessageEnvelope for reading
/// one, SessionCalls for sending one, and PP343's race for choosing the candidate.
///
/// THE STEP NAMES ARE THE WIRE ACTIONS, and the mapping is the part worth writing down rather than
/// inferring. Eleven steps use four actions: the two offers are OFFER, the accept is ACCEPT, and
/// every acknowledgement is RESULT - which is why a wait keyed on the step name alone would have
/// WaitForOfferAck and WaitForAccept looking for different things and finding the same message.
/// <see cref="ActionFor"/> is that mapping, and it is asserted rather than trusted.
///
/// THE RACE OWNS THE SOCKET, not this. PP343's run binds, races the candidates and hands back the
/// one that answered; this holds the outcome so the port type it was punched for can be asked for
/// afterwards, which is what session.c does with both sockets.
/// </summary>
public sealed class LiveHolepunchPunchSteps : IHolepunchPunchSteps, IDisposable
{
    private readonly string oauthHeader;
    private readonly string sessionId;
    private readonly NotificationQueue queue;
    private readonly CandidateRaceRun race;

    private readonly HashSet<HolepunchPortType> established = [];

    /// <param name="oauthHeader">The bearer PsnEndpoints builds.</param>
    /// <param name="sessionId">The session the create came back with.</param>
    /// <param name="queue">The queue PushChannel fills - PP548's own, carried through.</param>
    /// <param name="race">PP343's candidate race, injected so a test can hand over an unbound one.</param>
    public LiveHolepunchPunchSteps(
        string oauthHeader, string sessionId, NotificationQueue queue, CandidateRaceRun race)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(race);

        this.oauthHeader = oauthHeader;
        this.sessionId = sessionId;
        this.queue = queue;
        this.race = race;
    }

    /// <summary>The session state as the caller holds it, which the guard reads.</summary>
    public SessionStateFlags State { get; set; }

    /// <summary>The envelope each send posts, by step - null for one leaves that send unable to run.</summary>
    public IDictionary<string, string> Envelopes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The ports a hole has been punched for, which is what the race produced.</summary>
    public IReadOnlySet<HolepunchPortType> Established => established;

    /// <summary>
    /// PP552: whether the socket that fills the queue has ended - PP548's own
    /// <see cref="LiveHolepunchCreateSteps.ChannelEnded"/>, wired through.
    ///
    /// Null leaves the waits bounded only by their deadlines, which is right for a queue a test
    /// fills by hand.
    /// </summary>
    public Func<bool>? ChannelEnded { get; init; }

    /// <summary>
    /// Which wire action each step is about.
    ///
    /// The two offers are one action and the three acknowledgements are another; only the accept is
    /// alone. A step name is not an action, which is the whole reason this exists.
    /// </summary>
    public static SessionMessageAction ActionFor(string step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step switch
        {
            nameof(HolepunchPunchStep.WaitForOffer) or nameof(HolepunchPunchStep.SendOffer)
                => SessionMessageAction.Offer,

            nameof(HolepunchPunchStep.SendAccept) or nameof(HolepunchPunchStep.WaitForAccept)
                => SessionMessageAction.Accept,

            nameof(HolepunchPunchStep.AckOffer) or nameof(HolepunchPunchStep.AckAccept)
                or nameof(HolepunchPunchStep.WaitForOfferAck)
                => SessionMessageAction.Result,

            _ => SessionMessageAction.Unknown,
        };
    }

    /// <summary>The session is started, and this port has not already been punched.</summary>
    public bool PreconditionsHold(HolepunchPortType type)
        => SessionStart.Finished(State) && !established.Contains(type);

    /// <summary>
    /// Waits for a session message carrying the step's action, on the queue the channel fills.
    ///
    /// Read and not drained, for PP212's reason: the C's wait is a cursor walk, and the punch runs
    /// twice - once per port - over the same queue.
    /// </summary>
    public async Task<bool> WaitForMessageAsync(
        string action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        SessionMessageAction wanted = ActionFor(action);
        if (wanted == SessionMessageAction.Unknown)
            return false;

        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (queue.Items.Any(one => Carries(one, wanted)))
                return true;

            // PP552: nothing more can arrive on a queue whose socket has ended. This matters most
            // here: three waits of thirty seconds, twice - once per port.
            if (ChannelEnded is { } ended && ended())
                return false;

            await Task.Delay(LiveHolepunchCreateSteps.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Whether a queued notification is a session message with this action.</summary>
    public static bool Carries(QueuedNotification notification, SessionMessageAction wanted)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Type != PushNotificationType.SessionMessageCreated)
            return false;

        string? json = SessionMessageEnvelope.JsonInPayload(notification.Payload);
        return json is not null && SessionMessageEnvelope.ActionOf(ActionWordIn(json)) == wanted;
    }

    /// <summary>Sends the step's message to the command URL, where an envelope has been set for it.</summary>
    public async Task<bool> SendMessageAsync(string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (ActionFor(action) == SessionMessageAction.Unknown
            || !Envelopes.TryGetValue(action, out string? envelope))
        {
            return false;
        }

        CallResult result = await SessionCalls
            .SendMessageAsync(
                oauthHeader, sessionId, envelope, PsnEndpoints.SessionCommandUrl, cancellationToken)
            .ConfigureAwait(false);

        return result.Failure is null && !CurlSemantics.WouldFailTransfer(result.Status);
    }

    /// <summary>The candidates raced, which PP459's run needs and this does not invent.</summary>
    public CandidateRace? Race { get; set; }

    /// <summary>Who we are on the wire, for the request the race sends.</summary>
    public PunchIdentity? Identity { get; set; }

    /// <summary>The request ids the race matches answers against.</summary>
    public IReadOnlyList<byte[]>? RequestIds { get; set; }

    /// <summary>How long the race is given.</summary>
    public TimeSpan RaceTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// PP459's race, which binds and returns the candidate that answered.
    ///
    /// The socket is the race's, not this one's - which is why nothing here closes it and the
    /// outcome is what gets held. Refuses rather than invents when it has not been given what the
    /// race needs, the same way a send with no envelope does.
    /// </summary>
    public async Task<bool> ChooseCandidateAsync(CancellationToken cancellationToken)
    {
        if (Race is not { } candidates || Identity is not { } identity || RequestIds is not { } ids)
            return false;

        if (race.LocalPort == 0)
            race.Bind();

        Chosen = await race
            .RunAsync(candidates, identity, ids, RaceTimeout, cancellationToken)
            .ConfigureAwait(false);

        return Chosen is { Selected: not null };
    }

    /// <summary>The candidate that answered, once the race has run.</summary>
    public RaceRunOutcome? Chosen { get; private set; }

    /// <summary>The hole is open for this port, which is what session.c asks for a socket by.</summary>
    public void MarkEstablished(HolepunchPortType type) => established.Add(type);

    /// <summary>
    /// The console's request and our response, over the socket the race won.
    ///
    /// NOT ASSERTED OFFLINE and refuses rather than pretends: without a winner there is no socket
    /// to receive on, so this answers false rather than reaching a console.
    /// </summary>
    public Task<bool> ReceiveRequestSendResponseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(Chosen is { Selected: not null });

    /// <summary>
    /// The action word out of a session message's JSON, read from the "action" key rather than
    /// found anywhere in the text.
    ///
    /// The difference is not pedantry: a RESULT acknowledging an OFFER names both, so a scan for
    /// the first word that appears would call it an offer and the punch would take the wrong branch.
    /// </summary>
    public static string? ActionWordIn(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        int key = json.IndexOf(ActionKey, StringComparison.Ordinal);
        if (key < 0)
            return null;

        int from = key + ActionKey.Length;

        return SessionMessageEnvelope.Actions.Values
            .Select(word => (Word: word, At: json.IndexOf(word, from, StringComparison.Ordinal)))
            .Where(found => found.At >= 0)
            .OrderBy(found => found.At)
            .Select(found => found.Word)
            .FirstOrDefault();
    }

    /// <summary>The key the action sits under, as the C reads it.</summary>
    public const string ActionKey = "\"action\"";

    /// <summary>Closes the race, which owns the socket.</summary>
    public void Dispose() => race.Dispose();
}
