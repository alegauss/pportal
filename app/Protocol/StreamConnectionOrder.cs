using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One ordering the run depends on, and what a port loses by not keeping it.</summary>
/// <param name="Lead">What it is, in a sentence.</param>
/// <param name="Costs">What a port that reproduced the functions and not this would do instead.</param>
public readonly record struct StreamOrdering(string Lead, string Costs);

/// <summary>
/// PP640: the sequence PP295's first criterion asks a port to reproduce.
///
/// That criterion says a port reproducing every function and not their order "would pass a
/// message-level comparison and fail a session". It had no oracle: nothing in this tree stated the
/// order, so the port would have been written against a reading and checked against messages.
///
/// SIX ORDERINGS IN <c>chiaki_stream_connection_run</c>, and every one is invisible to a comparison
/// built from what crosses the wire. They are asserted as POSITIONS in the source rather than as
/// prose, which is how <see cref="HolepunchFlow"/> and <see cref="SessionRelease"/> hold the same
/// kind of fact one file over.
///
/// This states them and ports nothing. Porting is the criterion these serve.
/// </summary>
public static class StreamConnectionOrder
{
    /// <summary>The file, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The six, in the order the run reaches them.</summary>
    public static IReadOnlyList<StreamOrdering> All { get; } =
    [
        new("the three receivers are created audio, haptics, video and freed in reverse",
            "a failure at the video receiver would free three where the C frees two"),
        new("the streaminfo early buffer is drained before the wait, and the wait is skipped when it finished",
            "waiting first times out holding the message it is waiting for"),
        new("CONNECTED is sent with the state mutex released and taken again after",
            "sending it held is a lock order this file does not otherwise have"),
        new("input_to_wire is read before the feedback sender is finished",
            "after fini the sender may not be read at all, so the measurement ends with it"),
        new("the stage counters are read after takion closes and before the receivers are freed",
            "read earlier they race the thread that writes them; later they read freed objects"),
        new("the disconnect is sent from the label every failure after the bang reaches",
            "a disconnect that never arrived is why the NEXT attempt is refused with RP in use"),
    ];

    /// <summary>Where a needle sits, or -1. Compacted first, so wrapping is not part of the answer.</summary>
    public static int At(string source, string needle, int from = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(needle);

        return source.IndexOf(needle, from, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the receivers are made in one order and unwound in the other.
    ///
    /// Both halves. The creations alone are an order a port could copy without meaning it; what
    /// makes it behaviour is that each `goto` label frees exactly what was made before the failure,
    /// so the video receiver's failure path frees two.
    /// </summary>
    public static bool ReceiversUnwindInReverse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int audio = At(source, "stream_connection->audio_receiver = chiaki_audio_receiver_new");
        int haptics = At(source, "stream_connection->haptics_receiver = chiaki_audio_receiver_new");
        int video = At(source, "stream_connection->video_receiver = chiaki_video_receiver_new");

        int freeVideo = At(source, "err_video_receiver:");
        int freeHaptics = At(source, "err_haptics_receiver:");
        int freeAudio = At(source, "err_audio_receiver:");

        return audio >= 0 && haptics > audio && video > haptics
            && freeVideo > video && freeHaptics > freeVideo && freeAudio > freeHaptics;
    }

    /// <summary>
    /// Whether the early streaminfo is replayed before the wait, and the wait guarded by it.
    ///
    /// The guard is the half that matters: draining and then waiting anyway would time out on a
    /// message already handled.
    /// </summary>
    public static bool TheEarlyBufferIsDrainedBeforeTheWait(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int state = At(source, "stream_connection->state = STATE_EXPECT_STREAMINFO;");
        if (state < 0)
            return false;

        int drain = At(source, "stream_connection_takion_data_expect_streaminfo(stream_connection, stream_connection->streaminfo_early_buf", state);
        int guard = At(source, "if(!stream_connection->state_finished)", state);

        return drain > state && guard > drain;
    }

    /// <summary>Whether CONNECTED is sent between an unlock and a lock.</summary>
    public static bool ConnectedIsSentUnlocked(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int typed = At(source, "event.type = CHIAKI_EVENT_CONNECTED;");
        if (typed < 0)
            return false;

        int unlock = At(source, "chiaki_mutex_unlock(&stream_connection->state_mutex);", typed);
        int send = At(source, "chiaki_session_send_event(session, &event);", typed);
        int relock = At(source, "chiaki_mutex_lock(&stream_connection->state_mutex);", send);

        return unlock > typed && send > unlock && relock > send;
    }

    /// <summary>Whether the input delay is taken before the sender is finished.</summary>
    public static bool TheDelayIsTakenBeforeFini(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int taken = At(source, "stream_connection->input_to_wire = stream_connection->feedback_sender.input_to_wire;");
        int fini = At(source, "chiaki_feedback_sender_fini(&stream_connection->feedback_sender);");

        return taken >= 0 && fini > taken;
    }

    /// <summary>Whether the stage counters sit between the close and the free.</summary>
    public static bool TheStagesAreReadBetweenCloseAndFree(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int close = At(source, "chiaki_takion_close(&stream_connection->takion);");
        if (close < 0)
            return false;

        int receive = At(source, "stream_connection->stages.receive = stream_connection->takion.stage_receive;", close);
        int free = At(source, "chiaki_video_receiver_free(stream_connection->video_receiver);", close);

        return receive > close && free > receive;
    }

    /// <summary>
    /// Whether the disconnect is sent from the label rather than from the success path.
    ///
    /// Its position is the whole point: every failure after the bang goes to `disconnect`, so the
    /// message is sent on paths that failed as well as on the one that did not.
    /// </summary>
    public static bool TheDisconnectIsOnTheSharedExit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int label = At(source, "\ndisconnect:");
        if (label < 0)
            return false;

        int sent = At(source, "stream_connection_send_disconnect(stream_connection);", label);
        int congestion = At(source, "err_congestion_control:", label);

        return sent > label && congestion > sent;
    }

    /// <summary>Every ordering that does not hold, named so a failure says which.</summary>
    public static IReadOnlyList<string> Broken(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var holds = new[]
        {
            ReceiversUnwindInReverse(source),
            TheEarlyBufferIsDrainedBeforeTheWait(source),
            ConnectedIsSentUnlocked(source),
            TheDelayIsTakenBeforeFini(source),
            TheStagesAreReadBetweenCloseAndFree(source),
            TheDisconnectIsOnTheSharedExit(source),
        };

        return [.. All.Where((_, at) => !holds[at]).Select(one => one.Lead)];
    }
}
