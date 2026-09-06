using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What becomes of one of the session thread's seven steps when the port takes the run.</summary>
public enum HandoverFate
{
    /// <summary>The C keeps it, unchanged. Six of the seven.</summary>
    StaysInTheC,

    /// <summary>The C's call is replaced by one into the managed run. Exactly one.</summary>
    BecomesManaged,
}

/// <summary>One step, and what PP696's commit does to it.</summary>
/// <param name="Step">The step, as PP28 named it.</param>
/// <param name="Fate">Kept or replaced.</param>
/// <param name="Why">What decides it.</param>
public readonly record struct HandoverDecision(HandoverStep Step, HandoverFate Fate, string Why);

/// <summary>
/// PP752, under PP707: where the handoff sits, decided so the commit that edits the C can aim at it.
///
/// PP707's third criterion holds a decision rather than a piece of code, and PP696 - the one commit
/// that edits lib - waits on PP707. So the decision comes first or that commit has nothing to work
/// from.
///
/// WHAT IS NOT AVAILABLE DECIDES MOST OF IT. <see cref="CtrlLoop"/> and the senkusha classes are
/// models: Next, WaitsFirst, After. Neither runs, so a managed session cannot reach the stream phase
/// on its own, and the C session remains the only way there after this. That is why the handoff is
/// one step rather than a replacement of the session thread.
///
/// SO EXACTLY ONE OF PP28's SEVEN BECOMES MANAGED. The handshake key, the ecdh's two calls and the
/// three mutex operations are the session thread's own and stay; the RUN is what the port has a
/// counterpart for, and PP746 has already run that counterpart over a socket.
///
/// THE LOCK DISCIPLINE IS NOT NEGOTIABLE and is the reason this is written down rather than left to
/// the commit. The C unlocks before the run and relocks after, because ctrl's thread, the stop path
/// and every event handler take that mutex - so a replacement that ran under the lock would be a
/// session nothing could stop, and it would look correct until somebody tried to quit one.
///
/// AND THE C THREAD WAITS RATHER THAN RETURNS. Steps five to seven still have to happen on it, so
/// what replaces the call has to block for the length of the session and answer the same error code
/// the C's run answers - which <see cref="ManagedStreamRun.Run"/> already does.
/// </summary>
public static class StreamHandoverPlan
{
    /// <summary>The call PP696's commit replaces, spelled as session.c writes it.</summary>
    public const string ReplacedCall = "chiaki_stream_connection_run(&session->stream_connection,";

    /// <summary>What replaces it, on the managed side.</summary>
    public const string Replacement = nameof(ManagedStreamRun) + "." + nameof(ManagedStreamRun.Run);

    /// <summary>Every step, with what the edit does to it.</summary>
    public static IReadOnlyList<HandoverDecision> Decisions { get; } =
    [
        new(
            HandoverStep.HandshakeKey, HandoverFate.StaysInTheC,
            "The session's own material, filled under the state mutex before anything is handed over."),
        new(
            HandoverStep.EcdhInit, HandoverFate.StaysInTheC,
            "Created as late as it can be so every earlier exit has nothing to free; moving it would leak on each."),
        new(
            HandoverStep.Unlock, HandoverFate.StaysInTheC,
            "Released across the run because ctrl, the stop path and every handler take this mutex."),
        new(
            HandoverStep.Run, HandoverFate.BecomesManaged,
            "The one step the port has a counterpart for, and PP746 ran it over a socket."),
        new(
            HandoverStep.Relock, HandoverFate.StaysInTheC,
            "Retaken to write the quit reason, which is still the session's to record."),
        new(
            HandoverStep.UnlockAgain, HandoverFate.StaysInTheC,
            "Released before anything is freed, which the fini below depends on."),
        new(
            HandoverStep.EcdhFini, HandoverFate.StaysInTheC,
            "Outside the lock deliberately, so a free never holds the state mutex."),
    ];

    /// <summary>The steps the edit replaces, which is one.</summary>
    public static IReadOnlyList<HandoverStep> Managed { get; } =
        [.. Decisions.Where(one => one.Fate == HandoverFate.BecomesManaged).Select(one => one.Step)];

    /// <summary>
    /// Whether the replacement has to block for the session's length.
    ///
    /// True, and it is the property that makes the edit small. The C's call does not return until
    /// the session is over and steps five to seven run on the same thread afterwards; a replacement
    /// that returned early would run them while the stream was still going.
    /// </summary>
    public static bool TheReplacementBlocks => true;

    /// <summary>
    /// Whether a managed session could reach the stream phase without the C.
    ///
    /// False, and this is what keeps the handoff to one step. Stated as a value rather than as
    /// prose so the day ctrl and senkusha are ported, the sentence that stops being true is one a
    /// check can find.
    /// </summary>
    public static bool AManagedSessionCanReachTheStreamPhase => false;
}

/// <summary>PP752: the claims this plan makes about session.c, held against it.</summary>
public static class StreamHandoverPlanSource
{
    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? Locate() => SessionStreamHandover.Locate();

    /// <summary>
    /// Whether the call this plan replaces is still the one session.c makes.
    ///
    /// The plan names a call by its text, so a signature that moved would leave PP696 editing a
    /// line that is not there - and nothing else in the port would notice.
    /// </summary>
    public static bool TheReplacedCallIsStillThere(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return CCall.Code(sessionSource).Contains(StreamHandoverPlan.ReplacedCall, StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether it still sits between the two mutex calls, which is the discipline that matters.
    ///
    /// Read rather than trusted: the plan's whole argument for keeping six steps in the C is that
    /// the run is unlocked across, and a commit that moved the unlock would make the plan wrong
    /// about the one thing it is for.
    /// </summary>
    public static bool TheRunIsStillUnlockedAcross(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string code = CCall.Code(sessionSource);

        int run = code.IndexOf(StreamHandoverPlan.ReplacedCall, StringComparison.Ordinal);
        if (run < 0)
            return false;

        int unlockBefore = code.LastIndexOf("chiaki_mutex_unlock(&session->state_mutex);", run, StringComparison.Ordinal);
        int lockAfter = code.IndexOf("chiaki_mutex_lock(&session->state_mutex);", run, StringComparison.Ordinal);

        return unlockBefore >= 0 && lockAfter > run;
    }
}
