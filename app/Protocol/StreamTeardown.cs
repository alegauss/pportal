using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The stream connection's six exit labels, in the order they cascade.</summary>
public enum StreamExitLabel
{
    /// <summary>Tell the console, decide the run's code, free the early streaminfo buffer.</summary>
    Disconnect,

    /// <summary>Stop congestion control.</summary>
    CongestionControl,

    /// <summary>Unlock, close takion, and JOIN the thread that wrote the frame-path counters.</summary>
    CloseTakion,

    /// <summary>Lift the four stage timings, then free the video receiver.</summary>
    VideoReceiver,

    /// <summary>Free the haptics receiver.</summary>
    HapticsReceiver,

    /// <summary>Free the audio receiver, free the address, return.</summary>
    AudioReceiver,
}

/// <summary>What a measurement is copied out of, and what is destroyed after it.</summary>
/// <param name="Measurement">The field it is copied into.</param>
/// <param name="LiftedFrom">The object that accumulated it.</param>
/// <param name="DestroyedBy">The call that ends that object.</param>
public readonly record struct LiftedMeasurement(
    string Measurement, string LiftedFrom, string DestroyedBy);

/// <summary>
/// PP364, under PP295: six cascading labels, and the numbers rescued between two of them.
///
/// Where session.c has two exit labels (PP336), the stream connection has six and each unwinds
/// exactly ONE thing before falling through to the next. So a construction that failed half way
/// down enters half way down and releases exactly what was built above it. It is the most
/// disciplined teardown in the three files PP28 named, and it is worth porting as a cascade rather
/// than as six independent cleanups.
///
/// TWO MEASUREMENTS ARE LIFTED BEFORE THE THING THAT MADE THEM IS FREED. input_to_wire is copied
/// out of the feedback sender before its fini; the four frame-path stage timings are copied out of
/// takion and the video receiver before that receiver is freed. Both carry a comment in the C
/// saying so, which is unusual enough in this tree to be a signal.
///
/// THE ORDER OF close_takion AND err_video_receiver IS LOAD-BEARING, and not for tidiness. The
/// timings are read BETWEEN them: after the close, because that joins the thread that wrote them
/// and there is then nothing to race with; before the free, because the accumulators live on the
/// objects being destroyed.
///
/// A PORT THAT FREED FIRST WOULD LOSE THEM SILENTLY. There is no sentinel - a stage that was never
/// measured and a stage that measured nothing are both zero - so the failure is a baseline full of
/// plausible numbers rather than an error. That is what makes this the half of PP295 where a
/// rewrite loses measurements rather than behaviour.
///
/// AND streaminfo_early_buf IS FREED TWICE OVER, at the disconnect label as well as where it is
/// replayed. Not redundancy: the replay only happens on the path that reaches EXPECT_STREAMINFO,
/// and every earlier failure leaves it allocated.
/// </summary>
public static class StreamTeardown
{
    /// <summary>The labels, in the order they fall through.</summary>
    public static IReadOnlyList<StreamExitLabel> Cascade { get; } =
    [
        StreamExitLabel.Disconnect,
        StreamExitLabel.CongestionControl,
        StreamExitLabel.CloseTakion,
        StreamExitLabel.VideoReceiver,
        StreamExitLabel.HapticsReceiver,
        StreamExitLabel.AudioReceiver,
    ];

    /// <summary>
    /// Everything that runs from this entry point down, which is the whole of what a cascade means.
    /// </summary>
    public static IReadOnlyList<StreamExitLabel> From(StreamExitLabel entry)
        => [.. Cascade.SkipWhile(l => l != entry)];

    /// <summary>
    /// Where a failure enters, by what had been built when it happened.
    ///
    /// The names are the C's own goto targets. Read as a ladder: each construction that succeeds
    /// moves the entry point one label earlier, so nothing that was not built is released.
    ///
    /// PP295 CORRECTED EVERY RUNG. This table used to enter one label EARLIER than the C at each
    /// step - a haptics failure at HapticsReceiver where the C goes to err_audio_receiver, a connect
    /// failure at CloseTakion where the C goes to err_video_receiver. Three of those were harmless
    /// because the frees are null-safe; the fourth was not, because it would close a takion that
    /// never connected. It was found by writing <see cref="ManagedStreamRun"/> against this table
    /// and reading the C beside it, which is the whole of what PP295's first criterion means by
    /// "the ordering ported, not only the functions": a wrong table is one a message-level
    /// comparison cannot see. <see cref="StreamTeardownSource.GotoTargetsBeforeTheFirstLabel"/> now
    /// holds it against the file.
    ///
    /// The audio receiver failing is the one case with no label at all: the C unlocks and returns.
    /// Entering at AudioReceiver frees a null, which is the same outcome by a different route, and
    /// it is stated here because it is the one rung where the table is a modelling choice.
    /// </summary>
    public static StreamExitLabel EntryAfter(StreamBuilt built) => built switch
    {
        StreamBuilt.Nothing => StreamExitLabel.AudioReceiver,
        StreamBuilt.AudioReceiver => StreamExitLabel.AudioReceiver,
        StreamBuilt.HapticsReceiver => StreamExitLabel.HapticsReceiver,
        StreamBuilt.VideoReceiver => StreamExitLabel.VideoReceiver,
        StreamBuilt.Takion => StreamExitLabel.CloseTakion,
        _ => StreamExitLabel.CongestionControl,
    };

    /// <summary>
    /// The goto each failure takes in the C, in the order the failures can happen, so the table
    /// above can be held against the file rather than believed.
    /// </summary>
    public static IReadOnlyList<(StreamBuilt Built, string Goto)> GotosByRung { get; } =
    [
        (StreamBuilt.AudioReceiver, "err_audio_receiver"),
        (StreamBuilt.HapticsReceiver, "err_haptics_receiver"),
        (StreamBuilt.VideoReceiver, "err_video_receiver"),
        (StreamBuilt.Takion, "close_takion"),
        (StreamBuilt.CongestionControl, "err_congestion_control"),
    ];

    /// <summary>The label a goto target names.</summary>
    public static StreamExitLabel LabelOf(string gotoTarget) => gotoTarget switch
    {
        "disconnect" => StreamExitLabel.Disconnect,
        "err_congestion_control" => StreamExitLabel.CongestionControl,
        "close_takion" => StreamExitLabel.CloseTakion,
        "err_video_receiver" => StreamExitLabel.VideoReceiver,
        "err_haptics_receiver" => StreamExitLabel.HapticsReceiver,
        "err_audio_receiver" => StreamExitLabel.AudioReceiver,
        _ => throw new ArgumentOutOfRangeException(nameof(gotoTarget), gotoTarget, "not one of the six labels"),
    };

    /// <summary>
    /// The two measurements, and what destroys the object each is lifted from.
    ///
    /// A list rather than two checks, because a third accumulator added to either object would be
    /// a third one to rescue and this is where that is stated.
    /// </summary>
    public static IReadOnlyList<LiftedMeasurement> Lifted { get; } =
    [
        new("input_to_wire", "feedback_sender", "chiaki_feedback_sender_fini"),
        new("stages.receive", "takion", "chiaki_takion_close"),
        new("stages.reorder", "takion", "chiaki_takion_close"),
        new("stages.reassemble", "video_receiver", "chiaki_video_receiver_free"),
        new("stages.correct", "video_receiver", "chiaki_video_receiver_free"),
    ];

    /// <summary>
    /// Whether a lost measurement would be visible, which it would not.
    ///
    /// Stated as a constant because it is the reason the order is asserted at all. Nothing
    /// distinguishes an accumulator that was never copied from one that measured zero, so freeing
    /// first produces a baseline that looks like a measurement of a very fast session.
    /// </summary>
    public static bool ALostMeasurementIsDistinguishable => false;

    /// <summary>
    /// Whether the takion counters may be read at this point in the cascade.
    ///
    /// Only after the close, which joins the thread that writes them. Before it, the read races
    /// the writer - and the two labels are adjacent precisely so that window is one statement wide.
    /// </summary>
    public static bool TakionCountersAreSafeToRead(StreamExitLabel at)
        => at >= StreamExitLabel.VideoReceiver;

    /// <summary>
    /// Whether the early streaminfo buffer still needs freeing at the disconnect label.
    ///
    /// Only the path that reached EXPECT_STREAMINFO replays it, and the replay is what frees it
    /// there. Every earlier failure leaves it allocated, which is why the label frees it too.
    /// </summary>
    public static bool TheEarlyBufferOutlivesTheReplay(StreamState reached)
        => reached != StreamState.ExpectStreaminfo && reached != StreamState.Idle;
}

/// <summary>How far construction got before something failed.</summary>
public enum StreamBuilt
{
    /// <summary>Not even the audio receiver.</summary>
    Nothing,

    /// <summary>The audio receiver exists.</summary>
    AudioReceiver,

    /// <summary>And the haptics receiver.</summary>
    HapticsReceiver,

    /// <summary>And the video receiver.</summary>
    VideoReceiver,

    /// <summary>And takion is up.</summary>
    Takion,

    /// <summary>And congestion control is running: everything past here exits at disconnect.</summary>
    CongestionControl,
}

/// <summary>
/// PP364: the cascade held against streamconnection.c, because none of it is observable from
/// outside - a teardown that lost a number returns the same code as one that did not.
/// </summary>
public static class StreamTeardownSource
{
    /// <summary>Where the cascade lives.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The run's body, or null.</summary>
    public static string? RunBody(string filePath)
        => CFunction.BodyIn(filePath, "chiaki_stream_connection_run");

    /// <summary>The C's own name for each label, in cascade order.</summary>
    public static IReadOnlyList<string> LabelNames { get; } =
        ["disconnect:", "err_congestion_control:", "close_takion:", "err_video_receiver:",
         "err_haptics_receiver:", "err_audio_receiver:"];

    /// <summary>
    /// Whether all six labels are still there, in order.
    ///
    /// Order rather than presence: a label moved is a label that releases something built after it,
    /// and the compiler has nothing to say about that.
    /// </summary>
    public static bool TheSixLabelsAreStillInOrder(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        var previous = -1;
        foreach (string label in LabelNames)
        {
            int at = runBody.IndexOf(label, StringComparison.Ordinal);
            if (at <= previous)
                return false;

            previous = at;
        }

        return true;
    }

    /// <summary>
    /// Whether the cascade still falls through - no label returns before the last one.
    ///
    /// This is what makes it a cascade rather than six cleanups: the only `return` is past the
    /// final label. One inserted anywhere above leaks everything below it, silently.
    /// </summary>
    public static bool TheCascadeStillFallsThrough(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int first = runBody.IndexOf(LabelNames[0], StringComparison.Ordinal);
        int last = runBody.IndexOf(LabelNames[^1], StringComparison.Ordinal);
        if (first < 0 || last <= first)
            return false;

        return !runBody[first..last].Contains("return ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the four stage timings are still lifted between the close and the free.
    ///
    /// Both bounds, because each guards against a different mistake: reading before the close races
    /// the takion thread, and reading after the free reads freed memory or - the likelier edit -
    /// is simply dropped.
    /// </summary>
    public static bool TheStageTimingsAreLiftedBetweenCloseAndFree(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int closed = runBody.IndexOf("chiaki_takion_close(", StringComparison.Ordinal);
        if (closed < 0)
            return false;

        int freed = runBody.IndexOf("chiaki_video_receiver_free(", closed, StringComparison.Ordinal);
        if (freed < 0)
            return false;

        foreach (string stage in (string[])["receive", "reorder", "reassemble", "correct"])
        {
            int lift = runBody.IndexOf(
                $"stream_connection->stages.{stage} =", closed, StringComparison.Ordinal);

            if (lift < 0 || lift > freed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether input_to_wire is still copied out before the feedback sender's fini.
    ///
    /// The other rescue, and the one with no cascade around it: it happens at the end of the idle
    /// loop rather than at a label, which is why it is asked separately.
    /// </summary>
    public static bool InputToWireIsLiftedBeforeTheFini(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int lift = runBody.IndexOf(
            "stream_connection->input_to_wire = stream_connection->feedback_sender.input_to_wire;",
            StringComparison.Ordinal);
        if (lift < 0)
            return false;

        int fini = runBody.IndexOf("chiaki_feedback_sender_fini(", lift, StringComparison.Ordinal);

        return fini > lift;
    }

    /// <summary>
    /// Whether the early streaminfo buffer is still freed at the disconnect label as well.
    ///
    /// The replay's own free covers only the path that got that far; this covers every earlier
    /// failure, and it is the one a reader deletes as a duplicate.
    /// </summary>
    public static bool TheEarlyBufferIsStillFreedAtTheLabel(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int label = runBody.IndexOf("disconnect:", StringComparison.Ordinal);
        if (label < 0)
            return false;

        int guard = runBody.IndexOf(
            "if(stream_connection->streaminfo_early_buf)", label, StringComparison.Ordinal);
        int freed = runBody.IndexOf(
            "free(stream_connection->streaminfo_early_buf);", guard < 0 ? label : guard,
            StringComparison.Ordinal);
        int cleared = runBody.IndexOf(
            "stream_connection->streaminfo_early_buf = NULL;", freed < 0 ? label : freed,
            StringComparison.Ordinal);

        return guard > label && freed > guard && cleared > freed;
    }

    /// <summary>
    /// Which label each construction failure still jumps to, read off the gotos.
    ///
    /// Returned as the set found rather than as a verdict, so a test can say WHICH one moved.
    /// </summary>
    public static IReadOnlyList<string> GotoTargetsBeforeTheFirstLabel(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int firstLabel = runBody.IndexOf("disconnect:", StringComparison.Ordinal);
        if (firstLabel < 0)
            return [];

        var targets = new List<string>();
        string head = runBody[..firstLabel];

        for (int at = head.IndexOf("goto ", StringComparison.Ordinal);
             at >= 0;
             at = head.IndexOf("goto ", at + 1, StringComparison.Ordinal))
        {
            int end = head.IndexOf(';', at);
            if (end < 0)
                break;

            targets.Add(head[(at + 5)..end].Trim());
        }

        return targets;
    }
}
