using System.Net.Sockets;

namespace ChiakiNg.Protocol;

/// <summary>Which socket an arm of the send would use, or that there is no arm for it.</summary>
public enum ProbeArm
{
    /// <summary>The IPv4 socket, and the only arm with a random-allocation path behind it.</summary>
    Ipv4,

    /// <summary>The IPv6 socket, which has no such path.</summary>
    Ipv6,

    /// <summary>Neither - the default arm, which logs and sends nothing.</summary>
    Unsupported,
}

/// <summary>What one candidate's turn through the send loop actually did.</summary>
/// <param name="Resolved">Whether the address resolved at all.</param>
/// <param name="ProbeSent">Whether a probe left the machine.</param>
/// <param name="ClearsFailed">Whether this turn cleared the loop's failure flag.</param>
public readonly record struct ProbeAttempt(bool Resolved, bool ProbeSent, bool ClearsFailed);

/// <summary>
/// PP244: the loop that resolves every candidate and probes it.
///
/// FAILED MEANS ONE ADDRESS RESOLVED. The flag starts true and is cleared at the bottom of the
/// iteration - past the switch, so past the default arm that logs an unsupported family and sends
/// nothing, and past the arms whose send was skipped because the socket they would use is invalid.
/// Only a resolution failure skips it, by taking the continue above. So a candidate list of nothing
/// but unsupported families clears the flag and the function waits for answers to probes that were
/// never sent. <see cref="Attempt"/> keeps <c>ProbeSent</c> and <c>ClearsFailed</c> as separate
/// answers for exactly this reason.
///
/// AND ONE CLAUSE IS UNREACHABLE, WHICH IS NOT THE SAME AS INERT. The random-allocation arm asks
/// whether the candidate is static AND nothing has been sent yet. The flag it reads is declared
/// false at the top of each iteration and assigned in one place only - inside the block that
/// condition guards - so it is false every time it is read. As an expression the clause is
/// perfectly meaningful: for a static candidate it would suppress the fan-out. A port dropping it
/// as dead would land on the right behaviour by the wrong reasoning, so this one keeps the clause
/// and states separately which value is reachable - see <see cref="TheOnlyReachableSentValue"/> and
/// <see cref="TheClauseWouldMatterIfItCouldBeReached"/>.
///
/// THE ERROR CODE IS STALE AND DOES NOT ESCAPE. A candidate that fails to resolve leaves a code
/// behind that nothing clears, and a later candidate succeeding does not reset it. It never reaches
/// a caller because the success return is a literal rather than that variable - one line between
/// correct and wrong, with nothing marking it. <see cref="StaleErrorSurvives"/> is where that is
/// written down.
/// </summary>
public static class ProbeSend
{
    /// <summary>Which arm an address family takes.</summary>
    public static ProbeArm ArmFor(AddressFamily family) => family switch
    {
        AddressFamily.InterNetwork => ProbeArm.Ipv4,
        AddressFamily.InterNetworkV6 => ProbeArm.Ipv6,
        _ => ProbeArm.Unsupported,
    };

    /// <summary>
    /// Whether this candidate takes the extra fan-out over the guessed ports.
    ///
    /// Written with the clause the core has. The <paramref name="alreadySent"/> value the core can
    /// actually reach here is always false - see <see cref="TheOnlyReachableSentValue"/>.
    /// </summary>
    public static bool TakesTheRandomAllocationPath(
        ProbeArm arm, CandidateType type, bool randomAllocation, bool alreadySent)
    {
        if (arm != ProbeArm.Ipv4 || !randomAllocation)
            return false;

        return (type == CandidateType.Static && !alreadySent) || type == CandidateType.Stun;
    }

    /// <summary>
    /// The only value the "nothing sent yet" flag can hold when that condition is read.
    ///
    /// False, always: it is declared false at the top of each iteration and assigned in exactly one
    /// place, inside the block the condition guards. The clause is UNREACHABLE rather than inert -
    /// a distinction worth the two methods, because as an expression it is perfectly meaningful and
    /// <see cref="TheClauseWouldMatterIfItCouldBeReached"/> shows what it would do.
    /// </summary>
    public const bool TheOnlyReachableSentValue = false;

    /// <summary>
    /// What the clause would change, if anything could set the flag before it is read.
    ///
    /// It would suppress the fan-out for a static candidate - so a port that "simplified" the
    /// clause away would land on the right behaviour by the wrong reasoning, and a later change
    /// that made the flag reachable would then be silently wrong.
    /// </summary>
    public static bool TheClauseWouldMatterIfItCouldBeReached(
        ProbeArm arm, CandidateType type, bool randomAllocation)
        => TakesTheRandomAllocationPath(arm, type, randomAllocation, alreadySent: false)
            != TakesTheRandomAllocationPath(arm, type, randomAllocation, alreadySent: true);

    /// <summary>
    /// One candidate's turn.
    /// </summary>
    /// <param name="resolved">Whether the address resolved.</param>
    /// <param name="arm">Which arm its family takes.</param>
    /// <param name="socketOpen">Whether that arm's socket is usable.</param>
    /// <param name="sendSucceeded">Whether the send, if attempted, worked.</param>
    public static ProbeAttempt Attempt(bool resolved, ProbeArm arm, bool socketOpen, bool sendSucceeded)
    {
        // A resolution failure is the only exit that skips the clear at the bottom.
        if (!resolved)
            return new ProbeAttempt(Resolved: false, ProbeSent: false, ClearsFailed: false);

        bool attempted = arm != ProbeArm.Unsupported && socketOpen;

        // And a send failure takes the same continue, so it too leaves the flag alone.
        if (attempted && !sendSucceeded)
            return new ProbeAttempt(Resolved: true, ProbeSent: false, ClearsFailed: false);

        // Everything else falls out of the switch and reaches the clear - including the default arm,
        // which sent nothing at all.
        return new ProbeAttempt(Resolved: true, ProbeSent: attempted, ClearsFailed: true);
    }

    /// <summary>Whether the loop as a whole reports failure, given every candidate's turn.</summary>
    public static bool ReportsFailure(IEnumerable<ProbeAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        return !attempts.Any(a => a.ClearsFailed);
    }

    /// <summary>And whether any probe actually left, which is a different question.</summary>
    public static bool AnyProbeSent(IEnumerable<ProbeAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        return attempts.Any(a => a.ProbeSent);
    }

    /// <summary>
    /// Whether a stale failure code survives the loop - it does, and is discarded by the return
    /// being a literal rather than by anything resetting it.
    /// </summary>
    public static bool StaleErrorSurvives(IEnumerable<ProbeAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);

        List<ProbeAttempt> turns = [.. attempts];
        return turns.Any(a => !a.Resolved) && turns.Any(a => a.ClearsFailed);
    }

    /// <summary>
    /// The buffer the port is printed into, which fits five digits and a terminator and no more.
    /// </summary>
    public const int PortBuffer = 6;

    /// <summary>Whether a port's decimal form fits it. Every ushort does, with nothing to spare.</summary>
    public static bool PortFits(ushort port)
        => port.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1 <= PortBuffer;
}

/// <summary>
/// PP244: the send loop where the core writes it.
/// </summary>
public static class ProbeSendSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>
    /// Whether the flag is still cleared past the switch rather than where a probe is sent.
    /// </summary>
    public static bool TheFlagIsStillClearedPastTheSwitch(string core)
    {
        string body = Body(core);

        int unsupported = body.IndexOf(
            "CHIAKI_LOGW(session->log, \"Unsupported address family, skipping...\");", StringComparison.Ordinal);
        int cleared = body.IndexOf("\n        failed = false;", StringComparison.Ordinal);

        return unsupported >= 0 && cleared > unsupported;
    }

    /// <summary>And whether it still starts true.</summary>
    public static bool TheFlagStillStartsTrue(string core)
        => Body(core).Contains("bool failed = true;", StringComparison.Ordinal);

    /// <summary>
    /// Whether the clause that cannot be false is still there, and the variable it reads is still
    /// assigned only inside the block it guards.
    /// </summary>
    public static bool TheDeadClauseIsStillThere(string core)
    {
        string body = Body(core);

        int declared = body.IndexOf("bool sent = false;", StringComparison.Ordinal);
        int read = body.IndexOf(
            "(candidate->type == CANDIDATE_TYPE_STATIC && !sent)", StringComparison.Ordinal);
        int assigned = body.IndexOf("sent = true;", StringComparison.Ordinal);

        // Declared, then read, then assigned - and assigned exactly once.
        return declared >= 0
            && read > declared
            && assigned > read
            && body.IndexOf("sent = true;", assigned + 1, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// Whether the success return is still a literal rather than the error variable.
    ///
    /// This one reads the WHOLE function, not the loop: the stale code is set inside the loop and
    /// the return that saves it is four hundred lines further on, which is the distance that makes
    /// it worth an assertion.
    /// </summary>
    public static bool TheSuccessReturnIsStillALiteral(string core)
    {
        string whole = Function(core);

        return Body(core).Contains("err = CHIAKI_ERR_UNKNOWN;", StringComparison.Ordinal)
            && whole.Contains("\n    return CHIAKI_ERR_SUCCESS;\n", StringComparison.Ordinal)
            && whole.Contains("cleanup_sockets:", StringComparison.Ordinal)

            // The variable IS returned - but only from the cleanup label, never from success.
            && whole.IndexOf("\n    return err;", StringComparison.Ordinal)
                > whole.IndexOf("cleanup_sockets:\n    for", StringComparison.Ordinal);
    }

    /// <summary>Whether the port still goes into a buffer with nothing to spare.</summary>
    public static bool ThePortBufferIsStillExact(string core)
        => Body(core).Contains($"char service_remote[{ProbeSend.PortBuffer}];", StringComparison.Ordinal)
            && Body(core).Contains(
                "sprintf(service_remote, \"%d\", candidate->port);", StringComparison.Ordinal);

    /// <summary>
    /// And whether the unsupported-family log is still the only line here that does not name the
    /// function.
    /// </summary>
    public static bool TheUnsupportedLogStillDoesNotNameTheFunction(string core)
    {
        string body = Body(core);

        int lines = body.Split("CHIAKI_LOG", StringSplitOptions.None).Length - 1;
        int named = body.Split("\"check_candidates: ", StringSplitOptions.None).Length - 1;

        return lines - named == 1
            && body.Contains("\"Unsupported address family, skipping...\"", StringComparison.Ordinal);
    }

    /// <summary>The send loop, from its head to the failure test that follows it.</summary>
    private static string Body(string core)
    {
        string whole = Function(core);
        if (whole.Length == 0)
            return "";

        int start = whole.IndexOf("    bool failed = true;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = whole.IndexOf("    // Wait for responses", start, StringComparison.Ordinal);
        return end < 0 ? whole[start..] : whole[start..end];
    }

    /// <summary>The whole of check_candidates, which the stale-code check needs.</summary>
    private static string Function(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason PP213, PP233, PP234, PP236 and PP243 each wrote down.
        int start = text.LastIndexOf(
            "static ChiakiErrorCode check_candidates(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic ", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
