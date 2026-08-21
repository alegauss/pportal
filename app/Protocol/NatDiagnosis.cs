namespace ChiakiNg.Protocol;

/// <summary>What the offer concludes about the NAT it is behind.</summary>
public enum NatVerdict
{
    /// <summary>The measured increment is usable and the offer guesses from it.</summary>
    Measured,

    /// <summary>
    /// The two ports agree and nothing moves, so no guessing is needed.
    /// </summary>
    Transparent,

    /// <summary>
    /// The ports disagree with an increment of zero: something rewrites ports without varying them.
    /// The offer OVERRULES its own measurement.
    /// </summary>
    Rewriting,

    /// <summary>The same, with forcing switched off - so nothing is done about it.</summary>
    RewritingUnhandled,
}

/// <summary>What is written back into the session, or that nothing is.</summary>
/// <param name="Writes">Whether the diagnosis changes the session.</param>
/// <param name="RandomAllocation">The flag it would set.</param>
/// <param name="Increment">And the increment.</param>
public readonly record struct NatWriteBack(bool Writes, bool RandomAllocation, int Increment);

/// <summary>
/// PP253: the one place in the offer that writes back into what it was handed.
///
/// PP199 measures how far a NAT moves its ports, and PP33 ported both generators that guess from
/// that measurement. This is the branch that decides the measurement is unusable and REPLACES it.
///
/// When the increment measures zero but the local port and the port STUN reported disagree, the NAT
/// is rewriting ports without varying them - a double NAT, or a cone NAT with endpoint-independent
/// mapping on the outermost layer. There is nothing to extrapolate from, so the code sets the
/// random-allocation flag and an increment of one ON THE SESSION and guesses as though the
/// measurement had said so.
///
/// Those two fields are read afterwards by PP244's send loop, PP245's socket ladder and PP249's
/// cleanup. So a conclusion reached while building the offer changes what three later functions do,
/// and nothing downstream can tell a measured increment from an asserted one - which is why
/// <see cref="NatWriteBack.Writes"/> is a separate answer from the values themselves.
///
/// And it only happens with forcing switched on. With it off the same NAT is diagnosed and nothing
/// is done - see <see cref="NatVerdict.RewritingUnhandled"/>.
/// </summary>
public static class NatDiagnosis
{
    /// <summary>The increment the forcing path asserts in place of the measured zero.</summary>
    public const int AssertedIncrement = 1;

    /// <summary>
    /// What the offer concludes.
    /// </summary>
    /// <param name="measuredIncrement">What the allocation test found.</param>
    /// <param name="localPort">The port this side bound.</param>
    /// <param name="reportedPort">And the one STUN said the world sees.</param>
    /// <param name="forcingEnabled">Whether forcing is switched on.</param>
    public static NatVerdict Verdict(
        int measuredIncrement, ushort localPort, ushort reportedPort, bool forcingEnabled)
    {
        if (measuredIncrement != 0)
            return NatVerdict.Measured;

        if (localPort == reportedPort)
            return NatVerdict.Transparent;

        return forcingEnabled ? NatVerdict.Rewriting : NatVerdict.RewritingUnhandled;
    }

    /// <summary>What that verdict writes into the session.</summary>
    public static NatWriteBack WriteBackFor(NatVerdict verdict)
        => verdict == NatVerdict.Rewriting
            ? new NatWriteBack(Writes: true, RandomAllocation: true, Increment: AssertedIncrement)
            : new NatWriteBack(Writes: false, RandomAllocation: false, Increment: 0);

    /// <summary>
    /// The increment the guessing actually uses, given what was measured and what was concluded.
    /// </summary>
    public static int IncrementUsed(int measuredIncrement, NatVerdict verdict)
    {
        NatWriteBack written = WriteBackFor(verdict);
        return written.Writes ? written.Increment : measuredIncrement;
    }

    /// <summary>
    /// The functions that read the two fields afterwards, named so the reach is stated rather than
    /// implied.
    /// </summary>
    public static IReadOnlyList<string> ReadAfterwards { get; } =
    [
        "the probe send loop's random-allocation fan-out",
        "the wait loop's ladder for a guessed-port socket",
        "the cleanup that closes the guessed sockets",
    ];
}

/// <summary>
/// PP253: the diagnosis where the core writes it.
/// </summary>
public static class NatDiagnosisSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// One line per source line, trimmed - these branches sit deep enough that matching the file's
    /// own indentation is matching the nesting rather than the code.
    /// </summary>
    private static string Flat(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return string.Join(
            '|',
            core.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()));
    }

    /// <summary>Whether the three conditions are still what pick this branch.</summary>
    public static bool TheConditionIsStillThat(string core)
    {
        string flat = Flat(core);

        return flat.Contains("if(local_port == stun_port)", StringComparison.Ordinal)
            && flat.Contains("else if(session->force_port_guessing)", StringComparison.Ordinal);
    }

    /// <summary>And whether it still writes both fields, in that order.</summary>
    public static bool ItStillWritesBothFields(string core)
        => Flat(core).Contains(
            $"session->stun_random_allocation = true;|session->stun_allocation_increment = {NatDiagnosis.AssertedIncrement};",
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the write still happens BEFORE the guessing, which is what makes the asserted
    /// increment the one the guesses use.
    /// </summary>
    public static bool TheWriteStillPrecedesTheGuessing(string core)
    {
        string flat = Flat(core);

        int writes = flat.IndexOf("session->stun_random_allocation = true;", StringComparison.Ordinal);
        if (writes < 0)
            return false;

        int guesses = flat.IndexOf("int32_t base_port = candidate_stun->port;", writes, StringComparison.Ordinal);
        return guesses > writes;
    }

    /// <summary>
    /// Whether the fields are still read by the later functions - three sites, all past the offer.
    /// </summary>
    public static bool TheFieldsAreStillReadDownstream(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int written = text.IndexOf("session->stun_random_allocation = true;", StringComparison.Ordinal);
        if (written < 0)
            return false;

        // Every remaining read of the flag comes after the offer has written it.
        int reads = text[written..].Split(
            "session->stun_random_allocation", StringSplitOptions.None).Length - 1;

        return reads >= NatDiagnosis.ReadAfterwards.Count;
    }

    /// <summary>
    /// And whether the branch that does nothing about the same NAT is still there beside it.
    /// </summary>
    public static bool TheUnhandledBranchIsStillBesideIt(string core)
        => Flat(core).Contains(
            "else|{|msg.conn_request->num_candidates = 3;|candidate_remote = &msg.conn_request->candidates[1];",
            StringComparison.Ordinal);
}
