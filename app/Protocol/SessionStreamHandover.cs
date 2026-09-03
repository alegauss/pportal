using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One step of the handover, in the order the session thread takes it.</summary>
public enum HandoverStep
{
    /// <summary>chiaki_random_bytes_crypt fills the handshake key.</summary>
    HandshakeKey,

    /// <summary>chiaki_ecdh_init, and the last thing created before the run.</summary>
    EcdhInit,

    /// <summary>The state mutex is released.</summary>
    Unlock,

    /// <summary>chiaki_stream_connection_run, which does not return until the session is over.</summary>
    Run,

    /// <summary>The state mutex is retaken, to write the quit reason.</summary>
    Relock,

    /// <summary>Released again, before anything is freed.</summary>
    UnlockAgain,

    /// <summary>chiaki_ecdh_fini, outside the lock.</summary>
    EcdhFini,
}

/// <summary>
/// PP28, the third join: how the session thread hands over to the stream connection and takes the
/// answer back.
///
/// PP336 already models what the RESULT means - cancelled is success, the exact "Server shutting
/// down", the reason that survives a later generic failure - and PP371 that the reason can be null.
/// None of them models the handover itself, which is the session thread's own and the last thing
/// PP28 owns.
///
/// THE LOCK IS RELEASED ACROSS THE RUN. Everything else in this function happens under the state
/// mutex; the run is unlocked before and relocked after, and it is the only call in the session
/// thread long enough for that to matter. It has to be: the run lasts the whole session, and ctrl's
/// thread, the stop path and every event handler take that mutex. A port that held it across the run
/// would be a session nothing could stop.
///
/// THE ECDH IS CREATED AS LATE AS IT CAN BE, and that is a resource decision rather than a tidiness
/// one. It is initialised on the line before the unlock and freed on the line after the second
/// unlock - so every earlier exit from this function, and there are many, has nothing to free. Move
/// the init up beside the other setup and each of those exits leaks a key; the only thing keeping
/// them correct is that the object does not exist yet.
///
/// AND THE FINI IS OUTSIDE THE LOCK. The second unlock comes first, deliberately: freeing under the
/// state mutex would hold it for the length of a free on a path where nothing else needs it.
/// </summary>
public static class SessionStreamHandover
{
    /// <summary>The file the order is read from.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>
    /// The handover, in order.
    ///
    /// A list rather than a set, because every claim this file makes is about the sequence: which
    /// side of the unlock the run is on, and which side of it the ecdh's two calls are.
    /// </summary>
    public static IReadOnlyList<HandoverStep> Order { get; } =
    [
        HandoverStep.HandshakeKey,
        HandoverStep.EcdhInit,
        HandoverStep.Unlock,
        HandoverStep.Run,
        HandoverStep.Relock,
        HandoverStep.UnlockAgain,
        HandoverStep.EcdhFini,
    ];

    /// <summary>Whether the session holds the state mutex while a given step runs.</summary>
    public static bool HoldsTheStateMutex(HandoverStep step) => step switch
    {
        HandoverStep.HandshakeKey or HandoverStep.EcdhInit or HandoverStep.Relock => true,
        _ => false,
    };

    /// <summary>
    /// What an exit taken before <see cref="HandoverStep.EcdhInit"/> has to release.
    ///
    /// Nothing, and that is the whole reason the init sits where it does. Every QUIT above this
    /// point jumps to a label that stops ctrl and sends the quit event, and none of them frees an
    /// ecdh - which is correct only while there is not one yet.
    /// </summary>
    public static bool AnEarlierExitMustFreeTheEcdh => false;

    /// <summary>
    /// The two calls that must stay on opposite sides of the run, named for the checks below.
    /// </summary>
    public static IReadOnlyList<string> EcdhCalls { get; } = ["chiaki_ecdh_init(", "chiaki_ecdh_fini("];
}

/// <summary>
/// PP28: the handover where session.c states it.
/// </summary>
public static class SessionStreamHandoverSource
{
    // The four calls every check below locates. Named once: a transposed character in one copy of
    // five would make one check answer about a call the others could not find.
    private const string Unlock = "chiaki_mutex_unlock(&session->state_mutex);";
    private const string Lock = "chiaki_mutex_lock(&session->state_mutex);";
    private const string Run = "chiaki_stream_connection_run(&session->stream_connection,";
    private const string EcdhInit = "chiaki_ecdh_init(&session->ecdh)";
    private const string EcdhFini = "chiaki_ecdh_fini(&session->ecdh)";

    /// <summary>
    /// Whether the whole sequence is still in this order.
    ///
    /// One ordering check over seven calls rather than seven presence checks: all seven are in the
    /// file under any arrangement, and the arrangement is the entire content of this model.
    /// </summary>
    public static bool TheHandoverIsInOrder(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.InOrder(
            CCall.Compact(source),
            "chiaki_random_bytes_crypt(session->handshake_key,",
            EcdhInit,
            Unlock,
            Run,
            Lock,
            Unlock,
            EcdhFini);
    }

    /// <summary>
    /// Whether the run still sits between an unlock and a lock with nothing else between them.
    ///
    /// Stronger than the ordering above and it has to be: a sequence check passes on a version that
    /// unlocked, did something else, and then ran. What this asserts is that the unlock is the last
    /// thing before the run and the lock is the first thing after it, which is what makes the mutex
    /// released for exactly the run's duration and no longer.
    /// </summary>
    public static bool TheRunIsBracketedByTheLock(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int run = CCall.At(compact, Run);
        if (run < 0)
            return false;

        int unlock = compact.LastIndexOf(
            Unlock, run, StringComparison.Ordinal);
        if (unlock < 0)
            return false;

        // No statement boundary between the unlock and the run. The assignment of the run's result
        // is allowed to sit there and nothing else can: a second semicolon means a whole statement
        // ran with the mutex released that this model says did not.
        string between = compact[(unlock + Unlock.Length)..run];
        if (between.Contains(';', StringComparison.Ordinal))
            return false;

        // And the relock is the next statement after the run's own. Measured from where the run's
        // statement ENDS rather than from where its name does - the rest of the call carries the
        // socket argument and the semicolon that closes it, and counting that one as a statement
        // between the two is what the first draft of this check did.
        int ends = compact.IndexOf(';', run + Run.Length);
        if (ends < 0)
            return false;

        int relock = CCall.At(compact, Lock, ends);
        if (relock < 0)
            return false;

        return !compact[(ends + 1)..relock].Contains(';', StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the ecdh's two calls still straddle the run.
    ///
    /// Its own check, because it is a different claim from the ordering: this one is about the
    /// object not existing during the exits above, and it is what would go red if somebody moved
    /// the init up to sit with the rest of the session's setup.
    /// </summary>
    public static bool TheEcdhIsCreatedImmediatelyBeforeTheRun(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int init = CCall.At(compact, EcdhInit);
        int run = CCall.At(compact, Run);
        int fini = CCall.At(compact, EcdhFini);

        if (init < 0 || run < 0 || fini < 0 || init > run || fini < run)
            return false;

        // One init and one fini in the whole file: a second of either would mean the object outlives
        // this span somewhere, and the claim above would be about one of two lifetimes.
        return CCall.Count(compact, EcdhInit) == 1
            && CCall.Count(compact, EcdhFini) == 1;
    }

    /// <summary>Whether the fini is still outside the lock, after the second unlock.</summary>
    public static bool TheEcdhIsFreedOutsideTheLock(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int fini = CCall.At(compact, EcdhFini);
        if (fini < 0)
            return false;

        int unlock = compact.LastIndexOf(
            Unlock, fini, StringComparison.Ordinal);
        int relock = compact.LastIndexOf(
            Lock, fini, StringComparison.Ordinal);

        return unlock >= 0 && unlock > relock;
    }
}
