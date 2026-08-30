using System.Text.Json;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP549, under PP533: <see cref="IHolepunchStartSteps"/> over the pieces that actually run.
///
/// PP548 did this for the create. Everything the start needs was already here and unjoined:
/// SessionRequests builds both the start payload and its envelope, PsnEndpoints has the command URL
/// the C posts them to, SessionCalls carries the transfer, and PP212's queue holds the notification
/// the start finishes on. There is no StartAsync on SessionCalls and none is needed - the start IS
/// a message to the command URL, which SendMessageAsync already takes a URL for.
///
/// THE IDENTITY CHECK IS THE POINT OF THIS ONE. PP257 found that the C shadows its error variable
/// inside the branch handling the console's arrival, so a device id that will not convert from hex
/// and a device id naming a DIFFERENT console both write the inner variable, break, and return the
/// success the wait left behind. PP546's sequence reports whatever failure it is told; this is what
/// tells it, so the check has to actually run here or the departure PP546 declared is empty.
/// </summary>
public sealed class LiveHolepunchStartSteps : IHolepunchStartSteps
{
    private readonly string oauthHeader;
    private readonly string sessionId;
    private readonly NotificationQueue queue;
    private readonly string expectedDeviceUid;

    /// <param name="oauthHeader">The bearer PsnEndpoints builds.</param>
    /// <param name="sessionId">The session the create came back with.</param>
    /// <param name="queue">The queue PushChannel fills - PP548 hands over its own.</param>
    /// <param name="expectedDeviceUid">
    /// The console asked for, as 64 hex characters. What the identity check compares against, and
    /// the whole reason a wrong console can be told from a right one.
    /// </param>
    public LiveHolepunchStartSteps(
        string oauthHeader, string sessionId, NotificationQueue queue, string expectedDeviceUid)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(expectedDeviceUid);

        this.oauthHeader = oauthHeader;
        this.sessionId = sessionId;
        this.queue = queue;
        this.expectedDeviceUid = expectedDeviceUid;
    }

    /// <summary>The session state as the caller holds it, which the two guards read.</summary>
    public SessionStateFlags State { get; set; }

    /// <summary>The envelope the start posts, or null to have one built.</summary>
    public string? StartEnvelope { get; init; }

    /// <summary>The PS4 wakeup's envelope and base, where the console is one.</summary>
    public string? WakeupEnvelope { get; init; }

    /// <summary>Where the wakeup is sent, which discovery found.</summary>
    public string? DiscoveredBase { get; init; }

    /// <summary>The account the wakeup names.</summary>
    public string? OnlineId { get; init; }

    /// <summary>
    /// PP552: whether the socket that fills the queue has ended - PP548's own
    /// <see cref="LiveHolepunchCreateSteps.ChannelEnded"/>, wired through.
    ///
    /// Null leaves the wait bounded only by its deadline, which is what it was before and is right
    /// for a queue a test fills by hand.
    /// </summary>
    public Func<bool>? ChannelEnded { get; init; }

    /// <summary>The C's two guards: created, and not already started.</summary>
    public bool PreconditionsHold(out bool created)
    {
        created = State.HasFlag(SessionStateFlags.Created);
        return created && !SessionStart.Finished(State);
    }

    /// <summary>The PS4 wakeup, which only a PS4 reaches.</summary>
    public async Task<bool> WakeUpPs4Async(CancellationToken cancellationToken)
    {
        if (DiscoveredBase is not { } discovered || OnlineId is not { } online
            || WakeupEnvelope is not { } envelope)
        {
            return false;
        }

        CallResult result = await SessionCalls
            .WakeupAsync(oauthHeader, discovered, online, envelope, cancellationToken)
            .ConfigureAwait(false);

        return Succeeded(result);
    }

    /// <summary>
    /// http_start_session: the start envelope, posted to the command URL.
    /// </summary>
    public async Task<bool> StartSessionAsync(CancellationToken cancellationToken)
    {
        if (StartEnvelope is not { } envelope)
            return false;

        CallResult result = await SessionCalls
            .SendMessageAsync(
                oauthHeader, sessionId, envelope, PsnEndpoints.SessionCommandUrl, cancellationToken)
            .ConfigureAwait(false);

        return Succeeded(result);
    }

    /// <summary>
    /// Waits for the console to join and identify itself, answering PP257's name for what went
    /// wrong.
    ///
    /// The queue is read and not drained, for PP212's reason and PP548's: a notification the punch
    /// is about to want must still be there.
    /// </summary>
    public async Task<StartFailure?> WaitForMemberAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (QueuedNotification notification in queue.Items)
            {
                StartFailure failure = Consider(notification);

                // Any failure ends the loop, which is the C's break out of `while (!finished)`.
                if (failure != StartFailure.None)
                    return failure;
            }

            // PP557: BOTH HALVES, which is what the C's loop runs until. The member joining is not
            // a started session on its own - SessionStart.Finished wants the custom data too.
            if (SessionStart.Finished(Seen))
                return StartFailure.None;

            // PP552: the socket that fills this queue has ended, so nothing more can arrive. The
            // create's wait has always done this; these did not, and served out the full deadline.
            if (ChannelEnded is { } ended && ended())
                return null;

            await Task.Delay(LiveHolepunchCreateSteps.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        // Nobody joined. Not one of PP257's failures - see the interface's note.
        return null;
    }

    /// <summary>
    /// Which halves of the start have arrived, which the loop above runs until both have.
    /// </summary>
    public SessionStateFlags Seen { get; private set; } = SessionStateFlags.Created;

    /// <summary>
    /// PP557: one notification, considered as the C's loop body considers it.
    ///
    /// The C handles TWO types and this handled one. A member joining sets ConsoleJoined; the
    /// custom data sets CustomData1Received; the loop ends when both are set - which is exactly
    /// what SessionStart.Finished reads, and what nothing here was ever going to satisfy.
    ///
    /// Each half is checked once. A notification already accounted for is skipped rather than
    /// re-checked, so the wait does not decide the same thing twice on a queue that never drains.
    /// </summary>
    public StartFailure Consider(QueuedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        switch (notification.Type)
        {
            case PushNotificationType.MemberCreated when !Seen.HasFlag(SessionStateFlags.ConsoleJoined):
            {
                StartFailure failure = CheckIdentity(notification.Payload);
                if (failure == StartFailure.None)
                    Seen |= SessionStateFlags.ConsoleJoined;

                return failure;
            }

            case PushNotificationType.CustomData1Updated
                when !Seen.HasFlag(SessionStateFlags.CustomData1Received):
            {
                StartFailure failure = CheckCustomData(notification.Payload);
                if (failure == StartFailure.None)
                    Seen |= SessionStateFlags.CustomData1Received;

                return failure;
            }

            default:
                return StartFailure.None;
        }
    }

    /// <summary>
    /// PP557: the custom data half, in the C's order - the field, its length, then the decode.
    ///
    /// The three CustomData failures were declared by PP257 and nothing could produce one, because
    /// nothing looked at this notification at all.
    /// </summary>
    public StartFailure CheckCustomData(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string? text = ValueAt(payload, CustomDataPointer);
        if (text is null)
            return StartFailure.CustomDataFieldMissing;

        if (text.Length != SessionStart.CustomDataTextLength)
            return StartFailure.CustomDataWrongLength;

        return HolepunchIdentifiers.HexToBytes(text, SessionStart.CustomDataTextLength / 2) is null
            ? StartFailure.CustomDataUndecodable
            : StartFailure.None;
    }

    /// <summary>The C's pointer to the custom data: <c>/body/data/customData1</c>.</summary>
    public static IReadOnlyList<string> CustomDataPointer { get; } = ["body", "data", "customData1"];

    /// <summary>
    /// PP257's identity check, in the order the C makes it: the field, then its length, then that
    /// it converts from hex, then that it is the console that was asked for.
    ///
    /// The last two are the ones the C loses to the shadowed variable. Answering them by name here
    /// is what gives PP546's departure something to report.
    /// </summary>
    public StartFailure CheckIdentity(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string? uid = DeviceUidIn(payload);
        if (uid is null)
            return StartFailure.MemberFieldMissing;

        if (uid.Length != SessionStart.DeviceIdTextLength)
            return StartFailure.MemberIdWrongLength;

        // The C converts rather than inspects, and a conversion is what can fail - so this asks
        // PP247's decoder the same question rather than testing the characters itself.
        if (HolepunchIdentifiers.HexToBytes(uid, SessionStart.DeviceIdLength) is null)
            return StartFailure.MemberIdNotHex;

        return string.Equals(uid, expectedDeviceUid, StringComparison.OrdinalIgnoreCase)
            ? StartFailure.None
            : StartFailure.WrongConsole;
    }

    /// <summary>
    /// The C's own JSON pointer, followed by hand: <c>/body/data/members/0/deviceUniqueId</c>.
    ///
    /// Null covers everything the C's guard covers - no such path, and a value at it that is not a
    /// string - because both leave it without a device id and it says so with one message.
    /// </summary>
    public static string? DeviceUidIn(string payload) => ValueAt(payload, MemberPointer);

    /// <summary>
    /// PP557: a JSON pointer followed by hand, which both halves of the start need.
    ///
    /// Null covers everything the C's guards cover - no such path, and a value at it that is not a
    /// string - because both leave the caller without the field and it says so with one message.
    /// </summary>
    public static string? ValueAt(string payload, IReadOnlyList<string> pointer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(pointer);

        try
        {
            using var document = JsonDocument.Parse(payload);
            JsonElement element = document.RootElement;

            foreach (string step in pointer)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    if (!int.TryParse(step, out int index) || index >= element.GetArrayLength())
                        return null;

                    element = element[index];
                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty(step, out element))
                {
                    return null;
                }
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The pointer's steps, as the C writes them.</summary>
    public static IReadOnlyList<string> MemberPointer { get; } =
        ["body", "data", "members", "0", "deviceUniqueId"];

    /// <summary>A transfer that neither failed nor came back with a refusing status.</summary>
    private static bool Succeeded(CallResult result)
        => result.Failure is null && !CurlSemantics.WouldFailTransfer(result.Status);
}
