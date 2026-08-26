using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The takion event kinds the stream connection's callback switches on.</summary>
public enum TakionEvent
{
    /// <summary>Takion came up.</summary>
    Connected,

    /// <summary>Takion went away.</summary>
    Disconnect,

    /// <summary>A data message: what kind is the second question.</summary>
    Data,

    /// <summary>An audio or video packet.</summary>
    Av,

    /// <summary>Anything else, which the callback ignores.</summary>
    Other,
}

/// <summary>What the top layer does with an event.</summary>
public enum TakionRoute
{
    /// <summary>Finish the TAKION_CONNECT state successfully.</summary>
    FinishConnect,

    /// <summary>Fail the TAKION_CONNECT state.</summary>
    FailConnect,

    /// <summary>Hand it to the data layer.</summary>
    ToData,

    /// <summary>Hand it to the AV route.</summary>
    ToAv,

    /// <summary>Nothing at all.</summary>
    Ignored,
}

/// <summary>The data kinds the second layer switches on.</summary>
public enum TakionData
{
    /// <summary>A protobuf, whose meaning depends on the state.</summary>
    Protobuf,

    /// <summary>Rumble: three bytes straight to an event.</summary>
    Rumble,

    /// <summary>Pad info.</summary>
    PadInfo,

    /// <summary>Trigger effects.</summary>
    TriggerEffects,

    /// <summary>Anything else, dropped.</summary>
    Other,
}

/// <summary>Which handler a protobuf reaches, which is decided by the state and nothing else.</summary>
public enum ProtobufHandler
{
    /// <summary>Waiting for the console's bang.</summary>
    ExpectBang,

    /// <summary>Waiting for streaminfo.</summary>
    ExpectStreaminfo,

    /// <summary>A stream exists: the idle handler, which the default arm also reaches.</summary>
    Idle,
}

/// <summary>Where an AV packet goes.</summary>
public enum AvDestination
{
    /// <summary>The video receiver - and PP30's whole leverage.</summary>
    Video,

    /// <summary>The haptics receiver, which is an audio receiver.</summary>
    Haptics,

    /// <summary>The audio receiver.</summary>
    Audio,

    /// <summary>Nowhere: the decrypt failed and the packet is dropped (PP367).</summary>
    Dropped,
}

/// <summary>
/// PP366, under PP295: three layers of dispatch, each asking a different question.
///
/// The same bytes mean different things at different moments, and that is not an accident of this
/// file - it is the design. §PP295's claim that the ordering IS the behaviour is this function.
///
/// LAYER ONE ASKS WHAT KIND OF TAKION EVENT IT IS. CONNECTED and DISCONNECT are acted on ONLY while
/// the state is TAKION_CONNECT, so takion dying during EXPECT_BANG signals nothing here and the wait
/// sits out its whole timeout. Same shape as PP365's dead flag and from the same direction: the
/// machine learns about failures late or not at all.
///
/// LAYER TWO ASKS WHAT KIND OF DATA - protobuf, rumble, pad info, trigger effects. Flat, and the
/// only layer that is.
///
/// LAYER THREE ASKS WHAT STATE THE MACHINE IS IN, and holds the state mutex across the whole
/// handler. That lock is what lets the run function read state_finished immediately after its wait
/// returns: the handler that set it has finished before the waiter can be scheduled. So one protobuf
/// on the wire is three different messages depending on where the walk had got to.
///
/// AND THE AV ROUTE IS TEN LINES, one of which is the whole of PP30's leverage. The packet is
/// decrypted IN PLACE at key_pos PLUS ONE BLOCK - not at key_pos - and then routed by two flags.
/// The single call to chiaki_video_receiver_av_packet is why videoreceiver.c stays, so
/// frameprocessor.c stays, so fec.c stays, and jerasure with them.
/// </summary>
public static class StreamDispatch
{
    /// <summary>
    /// Layer one: what the callback does, given the event and where the walk had got to.
    ///
    /// The state is a parameter because for two of the five kinds it decides everything.
    /// </summary>
    public static TakionRoute Route(TakionEvent kind, StreamState state) => kind switch
    {
        // Both connect answers are heard ONLY in the state that is waiting for one. Anywhere else
        // they are dropped on the floor, and the wait that would have wanted one runs its timeout.
        TakionEvent.Connected => state == StreamState.TakionConnect
            ? TakionRoute.FinishConnect
            : TakionRoute.Ignored,

        TakionEvent.Disconnect => state == StreamState.TakionConnect
            ? TakionRoute.FailConnect
            : TakionRoute.Ignored,

        TakionEvent.Data => TakionRoute.ToData,
        TakionEvent.Av => TakionRoute.ToAv,

        _ => TakionRoute.Ignored,
    };

    /// <summary>
    /// Whether takion going away is noticed at all in this state.
    ///
    /// The question stated on its own, because it is the finding rather than a step: after the
    /// first state, it is not. PP365's dead state_failed is the other half of the same silence.
    /// </summary>
    public static bool ADisconnectIsNoticed(StreamState state)
        => Route(TakionEvent.Disconnect, state) != TakionRoute.Ignored;

    /// <summary>
    /// Layer two: which handler a data message reaches. Flat - nothing here depends on the state.
    /// </summary>
    public static bool IsHandled(TakionData data) => data != TakionData.Other;

    /// <summary>
    /// Layer three: which handler a protobuf reaches, which is the state and nothing else.
    ///
    /// The default arm is idle, so a protobuf arriving in TAKION_CONNECT - before a stream exists -
    /// goes to the idle handler rather than being dropped. Reproduced rather than tightened: it is
    /// the arm the C reaches for everything that is not one of the two named states.
    /// </summary>
    public static ProtobufHandler HandlerFor(StreamState state) => state switch
    {
        StreamState.ExpectBang => ProtobufHandler.ExpectBang,
        StreamState.ExpectStreaminfo => ProtobufHandler.ExpectStreaminfo,
        _ => ProtobufHandler.Idle,
    };

    /// <summary>
    /// Whether the state lock is held across the whole protobuf handler, which it is.
    ///
    /// Not a detail. It is what makes "the wait returned" and "the handler finished" the same
    /// moment for the run function, and a port that took the lock inside each handler instead would
    /// have a window where state_finished is true and the state has not been updated.
    /// </summary>
    public static bool TheStateLockSpansTheHandler => true;

    /// <summary>
    /// The key position an AV packet is decrypted at: the packet's, plus one block.
    ///
    /// Not the packet's own. Getting it wrong does not fail - it produces plausible garbage that
    /// the decoder reports as a corrupt frame, and the fault reads as the network's.
    /// </summary>
    public static ulong DecryptPositionFor(ulong keyPos) => keyPos + GkKeyStream.BlockSize;

    /// <summary>
    /// Where a packet goes, by the two flags and whether it decrypted.
    ///
    /// The order is the C's: video first, then haptics, then everything else to audio. There is no
    /// "neither" arm - audio is what a packet is when it is not one of the two.
    /// </summary>
    public static AvDestination DestinationFor(bool decrypted, bool isVideo, bool isHaptics)
    {
        // PP367: a failed decrypt drops the packet rather than passing ciphertext on.
        if (!decrypted)
            return AvDestination.Dropped;

        if (isVideo)
            return AvDestination.Video;

        return isHaptics ? AvDestination.Haptics : AvDestination.Audio;
    }

    /// <summary>
    /// What the video route holds up, named so PP30's dependency is a fact rather than a memory.
    ///
    /// One call keeps four things in the build. Listed in the order each depends on the last, which
    /// is the order they can leave in and no other.
    /// </summary>
    public static IReadOnlyList<string> KeptAliveByTheVideoRoute { get; } =
        ["videoreceiver.c", "frameprocessor.c", "fec.c", "jerasure"];
}

/// <summary>
/// PP366: the three layers held against streamconnection.c.
///
/// PP297's capture reaches none of this - the tap sits on ctrl and the session request, and takion
/// crosses neither - so every layer here is a reading of the file.
/// </summary>
public static class StreamDispatchSource
{
    /// <summary>Where the dispatch lives.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>One of the three dispatch functions' bodies, or null.</summary>
    public static string? LayerBody(string source, string function)
        => CFunction.Body(source, function);

    /// <summary>
    /// Whether the connect answers are still heard only in the state that waits for one.
    ///
    /// The guard is the assertion. Without it the two flags would be written from any state, which
    /// is better behaviour and different behaviour.
    /// </summary>
    public static bool ConnectAnswersAreStillStateGuarded(string callbackBody)
    {
        ArgumentNullException.ThrowIfNull(callbackBody);

        int guard = callbackBody.IndexOf(
            "if(stream_connection->state == STATE_TAKION_CONNECT)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int finished = callbackBody.IndexOf("state_finished =", guard, StringComparison.Ordinal);
        int failed = callbackBody.IndexOf("state_failed =", guard, StringComparison.Ordinal);

        return finished > guard && failed > finished;
    }

    /// <summary>
    /// Whether the protobuf layer still holds the state lock across the whole switch.
    ///
    /// Lock, switch, unlock, and no unlock inside an arm - which is the edit that would open the
    /// window the run function relies on being closed.
    /// </summary>
    public static bool TheStateLockStillSpansTheSwitch(string protobufBody)
    {
        ArgumentNullException.ThrowIfNull(protobufBody);

        int locked = protobufBody.IndexOf(
            "chiaki_mutex_lock(&stream_connection->state_mutex);", StringComparison.Ordinal);
        int switched = protobufBody.IndexOf(
            "switch(stream_connection->state)", locked < 0 ? 0 : locked, StringComparison.Ordinal);
        int unlocked = protobufBody.IndexOf(
            "chiaki_mutex_unlock(&stream_connection->state_mutex);",
            switched < 0 ? 0 : switched, StringComparison.Ordinal);

        if (locked < 0 || switched <= locked || unlocked <= switched)
            return false;

        // Exactly one unlock, so no arm releases it early.
        return protobufBody.IndexOf(
            "chiaki_mutex_unlock(&stream_connection->state_mutex);",
            unlocked + 1, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// Whether the AV route still decrypts at the packet's key position PLUS a block.
    ///
    /// The `+ CHIAKI_GKCRYPT_BLOCK_SIZE` is the whole of it. Dropping it decrypts with the wrong
    /// key stream and produces bytes that look like a damaged frame.
    /// </summary>
    public static bool TheAvDecryptStillAddsABlock(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        return avBody.Contains(
            "packet->key_pos + CHIAKI_GKCRYPT_BLOCK_SIZE", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the AV route still sends video to the video receiver, which is PP30's premise.
    ///
    /// The one call that keeps four dependencies in the build. If this ever stops being here, PP30
    /// has moved and the task's own reasoning needs rereading.
    /// </summary>
    public static bool TheVideoRouteStillReachesTheNativeReceiver(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        int guard = avBody.IndexOf("if(packet->is_video)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int call = avBody.IndexOf(
            "chiaki_video_receiver_av_packet(", guard, StringComparison.Ordinal);

        return call > guard;
    }

    /// <summary>
    /// Whether haptics is still tested before the audio fallback.
    ///
    /// Both go to an audio receiver, so an inverted order compiles and sends every haptics packet
    /// to the speakers - which is silence rather than an error, on a path nothing measures.
    /// </summary>
    public static bool HapticsIsStillTestedBeforeTheAudioFallback(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        int haptics = avBody.IndexOf("else if(packet->is_haptics)", StringComparison.Ordinal);
        if (haptics < 0)
            return false;

        int hapticsCall = avBody.IndexOf("haptics_receiver", haptics, StringComparison.Ordinal);
        int audioCall = avBody.IndexOf("stream_connection->audio_receiver", haptics, StringComparison.Ordinal);

        return hapticsCall > haptics && audioCall > hapticsCall;
    }
}
