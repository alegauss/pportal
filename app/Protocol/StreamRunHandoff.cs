using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One thing PP696's commit adds, and where it goes.</summary>
/// <param name="RelativePath">The file it is added to.</param>
/// <param name="Text">The declaration, spelled as it will be written.</param>
/// <param name="Why">What decides that spelling, because a signature with no reason is a guess.</param>
public readonly record struct HandoffAddition(string RelativePath, string Text, string Why);

/// <summary>
/// PP759, under PP696: HOW the C reaches the managed run, which PP752 decided and did not say.
///
/// PP752 settled that exactly one of PP28's seven steps becomes managed and named the call by its
/// text. PP753 built the handover and PP754 the runner that waits on it. Both are exports of
/// chiaki-shim - and chiaki-shim links chiaki-lib, one way. session.c cannot call either, and the
/// session carries no hook it could be reached through: chiaki_session_set_event_cb and
/// set_video_sample_cb are the only two of that shape and neither covers the run.
///
/// SO THE COMMIT THAT EDITS THE C HAS A LINE TO DELETE AND NOTHING TO PUT THERE. What it needs is a
/// callback field beside the other two, a typedef, a setter, and a trampoline in the shim that
/// installs itself over a handover. All of that is lib, which is the one commit allowed to touch it
/// and the one forbidden from adding an assertion - so the contract is written here first.
///
/// FOUR THINGS THE SPELLING DECIDES, and each is a way to get it quietly wrong:
///
/// THE REASON IS BORROWED. session.c already does <c>strdup(disconnect_reason)</c> on what it reads,
/// so a callback returning owned memory would leak one copy per session. The handover holds its own
/// until it is freed, which outlives the read.
///
/// THE SOCKET CROSSES AND IS NOT USED. The C's run took the data socket senkusha left; the managed
/// runner opens its own through the host it builds. It is passed anyway, because a callback that
/// dropped the parameter would be a different signature the day a runner wants it - and PP746's run
/// over a socket is what says one could.
///
/// THE WAIT IS SLICED. chiaki_shim_stream_handover_await_finish refuses a negative timeout, and the
/// run lasts a whole session - so the trampoline loops over bounded waits rather than asking once.
/// A single wait would end the stream at whatever number was chosen.
///
/// AND THE STOP DOES NOT DISAPPEAR. chiaki_session_stop's fourth wake-up is the stream connection's
/// stop, and something still has to reach a run that is now on the other side of the handover. It
/// becomes the handoff's stop rather than one fewer poke, which is the failure PP338 recorded: a
/// session that sets the flag and waits hangs against whichever blocker was left unpoked.
/// </summary>
public static class StreamRunHandoff
{
    /// <summary>The header the two existing setters live in, which is where the third goes.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\session.h";

    /// <summary>And the body that calls it.</summary>
    public const string SessionRelativePath = FramePathConsumers.SessionRelativePath;

    /// <summary>The shim, which gains the trampoline and the install.</summary>
    public const string ShimRelativePath = FramePathConsumers.ShimRelativePath;

    /// <summary>The two setters of this shape that already exist, which the third is written beside.</summary>
    public static IReadOnlyList<string> SettersBesideIt { get; } =
        ["chiaki_session_set_event_cb", "chiaki_session_set_video_sample_cb"];

    /// <summary>The name of the setter PP696 adds.</summary>
    public const string Setter = "chiaki_session_set_stream_run_cb";

    /// <summary>The line session.c already writes, which is what makes the reason borrowed.</summary>
    public const string SessionCopiesTheReason = "strdup(disconnect_reason)";

    /// <summary>
    /// Everything the commit adds, spelled out.
    ///
    /// Written as text rather than described, because the thing that goes wrong is a signature: a
    /// callback taking the session instead of the socket, or returning the reason instead of writing
    /// it, compiles and is a different contract.
    /// </summary>
    public static IReadOnlyList<HandoffAddition> Additions { get; } =
    [
        new(
            HeaderRelativePath,
            "typedef ChiakiErrorCode (*ChiakiStreamRunCallback)(chiaki_socket_t data_sock, "
                + "const char **disconnect_reason, void *user);",
            "The error is the run's own, and the reason is written out rather than returned so a run "
                + "that had none says so without a second code."),
        new(
            HeaderRelativePath,
            "static inline void " + Setter + "(ChiakiSession *session, ChiakiStreamRunCallback cb, void *user)",
            "Beside the other two and the same shape, so a host that installs one installs all three "
                + "alike."),
        new(
            SessionRelativePath,
            "err = session->stream_run_cb(data_sock, &disconnect_reason, session->stream_run_cb_user);",
            "In place of the run, between the same unlock and lock - PP752's whole point is that the "
                + "state mutex is released for exactly its length."),
        new(
            ShimRelativePath,
            "chiaki_shim_stream_run_install(void *session, void *handover)",
            "The trampoline is C, as every one of libchiaki's callbacks is: a managed delegate here "
                + "would be a function pointer the collector may move."),
    ];

    /// <summary>
    /// Whether the callback's reason is borrowed from the callee rather than owned by the session.
    ///
    /// True, and session.c's own strdup is the evidence rather than this constant.
    /// </summary>
    public static bool TheReasonIsBorrowed => true;

    /// <summary>
    /// Whether the socket crosses even though the managed runner opens its own.
    ///
    /// True. Stated rather than left implicit so that the day a runner wants it, what changes is a
    /// body and not a signature every caller has to be found for.
    /// </summary>
    public static bool TheSocketCrossesUnused => true;

    /// <summary>
    /// How long one slice of the trampoline's wait is.
    ///
    /// A second, which is short enough that a stop is acted on promptly and long enough that a whole
    /// session is not a busy loop. The number matters less than that there IS a loop: the export
    /// refuses a negative timeout, so "wait until it is over" is not something it can be asked.
    /// </summary>
    public const int WaitSliceMs = 1000;

    /// <summary>
    /// What <c>chiaki_session_stop</c>'s fourth wake-up becomes, rather than nothing.
    ///
    /// PP758 modelled the after-flip list as the three that are left, which is a floor and not a
    /// ceiling - the check reads them in order and allows more. This names the fourth so the commit
    /// adds it rather than reading that floor as permission to drop a poke.
    /// </summary>
    public const string StopBecomes = "chiaki_shim_stream_handover_stop";

    /// <summary>The files the commit touches, distinct and in the order the additions name them.</summary>
    public static IReadOnlyList<string> Touches { get; } =
        [.. Additions.Select(one => one.RelativePath).Distinct(StringComparer.Ordinal)];
}

/// <summary>PP759: the claims this contract makes about the tree, held against it.</summary>
public static class StreamRunHandoffSource
{
    /// <summary>The header, or null outside a checkout.</summary>
    public static string? LocateHeader()
        => SanitizerSource.LocateRelative(StreamRunHandoff.HeaderRelativePath);

    /// <summary>Whether the two setters this one is written beside are still there.</summary>
    public static bool TheTwoSettersAreStillThere(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return StreamRunHandoff.SettersBesideIt.All(
            one => header.Contains("static inline void " + one + "(", StringComparison.Ordinal));
    }

    /// <summary>Whether the session still copies the reason it is handed, which is what borrowing rests on.</summary>
    public static bool TheSessionStillCopiesTheReason(string session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return CCall.Code(session).Contains(StreamRunHandoff.SessionCopiesTheReason, StringComparison.Ordinal);
    }

    /// <summary>Whether the hook has been added yet, which is what tells this tree from PP696's.</summary>
    public static bool TheHookIsInstalled(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Contains(StreamRunHandoff.Setter, StringComparison.Ordinal);
    }
}
