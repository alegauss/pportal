using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP293: the six RP-Application-Reason codes session.h names, which are what a refusal says.
///
/// Copied from lib/include/chiaki/session.h, and held against it by
/// <see cref="SessionLadderSource.TheReasonsAreStill"/> - a code renumbered upstream would leave
/// the ladder retrying a refusal and refusing a retry, both silently.
/// </summary>
public enum RpApplicationReason : uint
{
    RegistFailed = 0x80108b09,
    InvalidPsnId = 0x80108b02,
    InUse = 0x80108b10,
    Crash = 0x80108b15,
    RpVersion = 0x80108b11,
    Unknown = 0x80108bff,
}

/// <summary>What one session request attempt resolved to.</summary>
public enum AttemptOutcome
{
    /// <summary>The console granted it and sent a usable nonce.</summary>
    Granted,

    /// <summary>The versions differ. Whether that is retried is <see cref="SessionVersionLadder"/>'s.</summary>
    VersionMismatch,

    /// <summary>Refused for a reason no retry addresses.</summary>
    Refused,
}

/// <summary>One attempt's answer, as the connect sequence reads it.</summary>
/// <param name="Outcome">Granted, a version mismatch, or a refusal.</param>
/// <param name="NextTarget">The target to try next, where one was worked out. Unknown otherwise.</param>
/// <param name="QuitReason">What the session would report if this ends it.</param>
public readonly record struct AttemptResult(
    AttemptOutcome Outcome, ChiakiTarget NextTarget, ChiakiQuitReason QuitReason);

/// <summary>
/// PP293: the RP-Version retry, which is three attempts and two of them are not the same attempt.
///
/// session_thread_func asks for a session, and where the console answers "wrong RP-Version" it asks
/// again with the version the console named - then, if that is refused the same way, once more
/// "even harder", which is upstream's word for it. Reading it is the only way to know it is three
/// and not a loop, because it is written as two `if`/`else if` pairs and a third bare check.
///
/// THE THIRD ATTEMPT CANNOT RE-DETECT, and that is the whole reason there are three rather than a
/// loop. The first two pass a target_out and the last passes NULL, which session.c documents as
/// "version mismatch means to fail the entire session". The branch that would work out a new target
/// tests target_out, so on the third attempt the same reply falls through to the refusal switch -
/// and the SAME answer therefore produces a different quit reason depending on which attempt it
/// arrived on. That is reproduced here rather than smoothed, because a client that retried forever
/// against a console reporting nonsense is what the third attempt exists to stop.
///
/// THE VERSIONS MUST ACTUALLY DIFFER. The mismatch branch is guarded by a strcmp: a console that
/// reports the version we already sent is not a mismatch, and falls to the refusal switch. A port
/// that treated the reason code alone as a mismatch would retry the same request forever.
///
/// AND 5.0 IS TREATED AS NONSENSE. A console reporting RP-Version 5.0 parses to unknown, and rather
/// than give up session.c substitutes PS4 target 9 - "This is probably nonsense, let's try with
/// 9.0". It is the one rung of this ladder that is a guess, and it is upstream's guess.
/// </summary>
public static class SessionVersionLadder
{
    /// <summary>How many requests the connect sequence will ever make. Not a loop bound - a count.</summary>
    public const int Attempts = 3;

    /// <summary>The version a console reports when it means nothing, and what to try instead.</summary>
    public const string NonsenseVersion = "5.0";

    /// <summary>
    /// What one attempt's answer resolves to.
    /// </summary>
    /// <param name="response">The three fields the answer carried.</param>
    /// <param name="ourVersion">The RP-Version this attempt sent.</param>
    /// <param name="ps5">Which family, which is what the reported version is parsed against.</param>
    /// <param name="canRetarget">
    /// Whether this attempt may work out a new target - false on the third, where session.c passes
    /// NULL and a mismatch ends the session instead.
    /// </param>
    public static AttemptResult Read(
        SessionResponseFields response, string? ourVersion, bool ps5, bool canRetarget)
    {
        if (response.Success)
        {
            // The nonce is what success means here: session.c base64-decodes it and fails the
            // request where it is absent or not 16 bytes, with success already reported.
            return string.IsNullOrEmpty(response.Nonce)
                ? new AttemptResult(AttemptOutcome.Refused, ChiakiTarget.Ps4Unknown,
                    ChiakiQuitReason.SessionRequestUnknown)
                : new AttemptResult(AttemptOutcome.Granted, ChiakiTarget.Ps4Unknown, ChiakiQuitReason.None);
        }

        bool versionReason =
            response.ErrorCode == (uint)RpApplicationReason.RpVersion
            || response.ErrorCode == (uint)RpApplicationReason.Unknown;

        // All four conditions, in session.c's order. The strcmp is the one a port drops by accident.
        if (versionReason
            && canRetarget
            && response.RpVersion is { Length: > 0 }
            && !string.Equals(ourVersion, response.RpVersion, StringComparison.Ordinal))
        {
            return Retarget(response.RpVersion, ps5);
        }

        // The refusal switch. RP_VERSION still reports a mismatch out of it - with no target, so
        // the ladder's own guard is what stops it - which is how the third attempt ends a session
        // rather than asking a fourth time.
        return new AttemptResult(
            response.ErrorCode == (uint)RpApplicationReason.RpVersion
                ? AttemptOutcome.VersionMismatch
                : AttemptOutcome.Refused,
            ChiakiTarget.Ps4Unknown,
            QuitFor(response.ErrorCode));
    }

    /// <summary>The target a reported version resolves to, including the 5.0 guess.</summary>
    private static AttemptResult Retarget(string reported, bool ps5)
    {
        ChiakiTarget target = RpVersion.Parse(reported, ps5);

        if (!RpVersion.IsUnknown(target))
            return new AttemptResult(AttemptOutcome.VersionMismatch, target, ChiakiQuitReason.None);

        if (string.Equals(reported, NonsenseVersion, StringComparison.Ordinal))
            return new AttemptResult(AttemptOutcome.VersionMismatch, ChiakiTarget.Ps4_9, ChiakiQuitReason.None);

        // Unknown and not the nonsense one: still a mismatch, but with nothing to try next, so the
        // ladder's own guard on an unknown target is what stops it.
        return new AttemptResult(
            AttemptOutcome.VersionMismatch, target, ChiakiQuitReason.SessionRequestRpVersionMismatch);
    }

    /// <summary>The refusal switch, by application reason.</summary>
    private static ChiakiQuitReason QuitFor(uint errorCode) => errorCode switch
    {
        (uint)RpApplicationReason.InUse => ChiakiQuitReason.SessionRequestRpInUse,
        (uint)RpApplicationReason.Crash => ChiakiQuitReason.SessionRequestRpCrash,
        (uint)RpApplicationReason.RpVersion => ChiakiQuitReason.SessionRequestRpVersionMismatch,
        _ => ChiakiQuitReason.SessionRequestUnknown,
    };

    /// <summary>
    /// Whether the ladder asks again after this attempt.
    ///
    /// Both guards, which is the pair session.c tests: a mismatch AND a target that is not unknown.
    /// A console reporting a version nothing parses is refused rather than retried with nothing.
    /// </summary>
    public static bool AsksAgain(AttemptResult result, int attemptsSoFar)
        => attemptsSoFar < Attempts
            && result.Outcome == AttemptOutcome.VersionMismatch
            && !RpVersion.IsUnknown(result.NextTarget);
}

/// <summary>
/// PP293: the ladder held against session_thread_func, so the count and the guards cannot drift.
/// </summary>
public static class SessionLadderSource
{
    /// <summary>Where the ladder lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether there are still exactly two retries after the first attempt, the second of them
    /// passing NULL so it cannot re-detect.
    /// </summary>
    public static bool TheLadderIsStillThreeAttempts(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("Attempting to re-request session with Server's RP-Version", StringComparison.Ordinal)
            && core.Contains("even harder with Server's RP-Version", StringComparison.Ordinal)
            && core.Contains("session_thread_request_session(session, NULL)", StringComparison.Ordinal);
    }

    /// <summary>Whether the mismatch branch is still guarded by the versions actually differing.</summary>
    public static bool TheMismatchStillNeedsTheVersionsToDiffer(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("target_out && response.rp_version && strcmp(rp_version_str, response.rp_version)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether 5.0 is still the version session.c refuses to believe.</summary>
    public static bool FiveIsStillNonsense(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("Reported Server RP-Version is 5.0", StringComparison.Ordinal)
            && core.Contains("*target_out = CHIAKI_TARGET_PS4_9;", StringComparison.Ordinal);
    }

    /// <summary>Where the reason codes are defined.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\session.h";

    /// <summary>That header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>
    /// Whether every reason the port copied is still the number session.h defines.
    ///
    /// The join is the name, so a code renumbered upstream fails here rather than silently making
    /// the ladder retry a refusal - which is a session that hangs on a console reporting something
    /// this no longer recognises.
    /// </summary>
    public static bool TheReasonsAreStill(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        foreach (RpApplicationReason reason in Enum.GetValues<RpApplicationReason>())
        {
            string name = Enum.GetName(reason) switch
            {
                "RegistFailed" => "REGIST_FAILED",
                "InvalidPsnId" => "INVALID_PSN_ID",
                "InUse" => "IN_USE",
                "Crash" => "CRASH",
                "RpVersion" => "RP_VERSION",
                _ => "UNKNOWN",
            };

            if (!header.Contains(
                    $"CHIAKI_RP_APPLICATION_REASON_{name}", StringComparison.Ordinal)
                || !header.Contains(
                    $"0x{(uint)reason:x8}", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
