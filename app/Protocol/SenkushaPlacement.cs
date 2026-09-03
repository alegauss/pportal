using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>How chiaki_senkusha_run came back.</summary>
public enum SenkushaRunOutcome
{
    /// <summary>CHIAKI_ERR_SUCCESS: the MTUs and the RTT were measured.</summary>
    Succeeded,

    /// <summary>CHIAKI_ERR_CANCELED: somebody stopped the session while it ran.</summary>
    Canceled,

    /// <summary>Anything else. There is no list; every other code lands here together.</summary>
    Failed,
}

/// <summary>What the session thread does next.</summary>
public enum SenkushaConsequence
{
    /// <summary>On to the stream connection with what senkusha measured.</summary>
    Continue,

    /// <summary>Out through quit_ctrl. The session is over.</summary>
    EndSession,

    /// <summary>On to the stream connection with numbers senkusha did not measure.</summary>
    FallBack,
}

/// <summary>
/// What the session uses when senkusha did not measure it.
/// </summary>
/// <param name="MtuIn">Both MTUs take the same literal.</param>
/// <param name="MtuOut">Likewise.</param>
/// <param name="RttMicroseconds">A flat microsecond figure, not a measurement.</param>
/// <param name="DontFragment">
/// The fourth field, and the one a port guesses wrong. The fallback does not only supply numbers
/// senkusha would have produced - it also turns the don't-fragment bit OFF, which senkusha's success
/// path never touches. A port that copied "the MTU and the RTT" would leave it set and send packets
/// the network may not carry at a size nothing measured.
/// </param>
public readonly record struct SenkushaFallback(
    int MtuIn, int MtuOut, int RttMicroseconds, bool DontFragment);

/// <summary>
/// PP28, the first of the three joins: where senkusha sits between ctrl and the stream connection.
///
/// The port models senkusha's own exchange - its waits, its send results, its participant in the
/// captured corpus - and says nothing about where it runs or what its failing costs. That is this
/// line's subject rather than PP294's or PP295's: it is a fact about the SESSION thread, in the one
/// function that drives all three files.
///
/// THE ORDER, and every step of it is load-bearing:
///
/// 1. It runs only after ctrl reported a session id. The arm above it is ctrl_failed, so senkusha
///    never starts on a session ctrl did not establish.
///
/// 2. init, then run with four out-parameters, then FINI - and the fini is unconditional and comes
///    BEFORE the result is looked at. A port that read the error first and cleaned up per branch
///    would leak on two of the three outcomes.
///
/// 3. Then a stop check, and then ctrl is asked AGAIN whether it failed - "since session started".
///    Senkusha's run is the longest thing between ctrl's start and the stream connection, and ctrl
///    is a thread of its own that can die during it.
///
/// 4. And only then the outcome, which is the part worth porting carefully.
///
/// THREE OUTCOMES, NOT TWO. Success continues. CANCELED ends the session. Every OTHER error is a
/// FALLBACK: the session carries on with 1454, 1454, 1000 and the don't-fragment bit cleared, and
/// the log says so. A port writing <c>if (err != Success) return err;</c> reproduces two of the
/// three and kills sessions upstream survives - which is the failure §PP28 predicts of code written
/// to match observed behaviour rather than designed.
///
/// THE #ifdef IS NOT A BUILD OPTION. ENABLE_SENKUSHA is defined in session.c itself, a few hundred
/// lines above its only use, so it is on in every build this port has ever produced. Modelled as
/// what it is rather than as a switch: a port that promoted it to a setting would be adding a way to
/// skip the only thing that measures the path's MTU.
/// </summary>
public static class SenkushaPlacement
{
    /// <summary>The file the whole order is read from.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>
    /// The numbers the session carries when senkusha failed without being cancelled.
    ///
    /// Named constants rather than a literal at the one call site, because three of the four are
    /// also what a reader would otherwise assume senkusha measured.
    /// </summary>
    public static SenkushaFallback Fallback { get; } = new(1454, 1454, 1000, false);

    /// <summary>
    /// What the session thread does with each outcome.
    ///
    /// The whole of the join, and the reason it is a method rather than a table: the mapping is not
    /// monotone in "how bad the error is". Cancelled is the FATAL one and a plain failure is the
    /// survivable one, which is the opposite of the ordering an error code suggests.
    /// </summary>
    public static SenkushaConsequence After(SenkushaRunOutcome outcome) => outcome switch
    {
        SenkushaRunOutcome.Succeeded => SenkushaConsequence.Continue,
        SenkushaRunOutcome.Canceled => SenkushaConsequence.EndSession,
        _ => SenkushaConsequence.FallBack,
    };

    /// <summary>
    /// Whether an init failure ends the session, which is the one outcome with no fallback.
    ///
    /// Separate from <see cref="After"/> because it is a different call: senkusha that could not be
    /// built produces no error code to classify, and the session leaves through the same label a
    /// cancelled run does.
    /// </summary>
    public static SenkushaConsequence AfterInitFailed() => SenkushaConsequence.EndSession;
}

/// <summary>
/// PP28: the same order where session.c states it, so the model above cannot drift off it.
///
/// Every check here reads the compacted source - comments and string literals removed by
/// <see cref="CCall.Compact"/> - so a paragraph mentioning a call is not the call.
/// </summary>
public static class SenkushaPlacementSource
{
    /// <summary>
    /// Whether senkusha's guard is defined in this same file, which is what makes it not a switch.
    ///
    /// Both halves asked. The <c>#ifdef</c> alone would be a build option somebody could turn off;
    /// the <c>#define</c> beside it is what says every build has senkusha in it.
    /// </summary>
    public static bool TheGuardIsDefinedInThisFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains("#define ENABLE_SENKUSHA", StringComparison.Ordinal)
            && source.Contains("#ifdef ENABLE_SENKUSHA", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether fini still runs before the result is read.
    ///
    /// The order that matters and the one a tidy-up would invert: run, fini, and only then the
    /// branch on err. Asserted as a sequence rather than as three presences, because all three are
    /// present in any arrangement of them.
    /// </summary>
    public static bool FiniRunsBeforeTheOutcomeIsRead(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.InOrder(
            CCall.Compact(source),
            "chiaki_senkusha_init(",
            "chiaki_senkusha_run(",
            "chiaki_senkusha_fini(",
            "if(err == CHIAKI_ERR_SUCCESS)");
    }

    /// <summary>
    /// Whether ctrl is asked again after senkusha, between the fini and the outcome.
    ///
    /// Its own check rather than a fifth element of the sequence above: this one is about a
    /// DIFFERENT thread having died during the longest step, and folding it into the ordering claim
    /// would let one failure read as the other.
    /// </summary>
    public static bool CtrlIsAskedAgainAfterSenkusha(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int fini = CCall.At(compact, "chiaki_senkusha_fini(");
        if (fini < 0)
            return false;

        int outcome = CCall.Mark(compact, "if(err == CHIAKI_ERR_SUCCESS)", fini);
        if (outcome < 0)
            return false;

        return CCall.Mark(compact[fini..outcome], "if(session->ctrl_failed)") >= 0;
    }

    /// <summary>
    /// Whether cancelled is still the only error code named, and the rest still fall back.
    ///
    /// Both halves, because either alone is satisfiable by the wrong code. A file that named
    /// CANCELED and quit on everything would pass the first; one that fell back on everything
    /// including a cancel would pass the second.
    /// </summary>
    public static bool CanceledIsTheOnlyFatalOutcome(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        return CCall.Mark(compact, "else if(err == CHIAKI_ERR_CANCELED)") >= 0
            && CCall.InOrder(
                compact,
                "if(err == CHIAKI_ERR_SUCCESS)",
                "else if(err == CHIAKI_ERR_CANCELED)",
                "session->mtu_in = 1454;");
    }

    /// <summary>
    /// Whether the fallback still sets all four fields, the don't-fragment bit included.
    ///
    /// The fourth is why this is a check and not a comment. Three of them are numbers a reader would
    /// look for; <c>dontfrag</c> is a boolean the success path never writes, so a port that ported
    /// "the fallback values" would carry three of four and leave the fourth at whatever the connect
    /// info set.
    /// </summary>
    public static bool TheFallbackSetsFourFields(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        return CCall.InOrder(
            compact,
            $"session->mtu_in = {SenkushaPlacement.Fallback.MtuIn};",
            $"session->mtu_out = {SenkushaPlacement.Fallback.MtuOut};",
            $"session->rtt_us = {SenkushaPlacement.Fallback.RttMicroseconds};",
            "session->dontfrag = false;");
    }
}
