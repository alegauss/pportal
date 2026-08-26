using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the client is told, if anything, when a display message arrives.</summary>
public enum DisplayTold
{
    /// <summary>Nothing. The flags may still have moved.</summary>
    Nothing,

    /// <summary>The stream cannot be shown - the console is displaying something unstreamable.</summary>
    CannotDisplay,

    /// <summary>It can be shown again.</summary>
    CanDisplay,
}

/// <summary>The flags, which only mean anything together.</summary>
/// <param name="CantA">Raised by DISPLAYA carrying 0x1. Raised silently.</param>
/// <param name="CantB">Raised by DISPLAYB while A is up, and that is what tells the client.</param>
/// <param name="Prohibited">
/// PP359: raised once by RP-Prohibit in the ctrl response, and never lowered. A prohibition is a
/// property of the session the console granted rather than of what is on its screen.
/// </param>
public readonly record struct DisplayFlags(
    bool CantA = false, bool CantB = false, bool Prohibited = false);

/// <summary>One arrival's effect: what the flags become, and what the client hears.</summary>
public readonly record struct DisplayEffect(DisplayFlags Flags, DisplayTold Told);

/// <summary>
/// PP353, under PP294: the two flags that decide whether the client shows the stream.
///
/// DISPLAYA and DISPLAYB share cant_displaya and cant_displayb, and neither flag means anything on
/// its own. PP297's capture holds exactly one DISPLAYB, carrying 01-ff - which is the value that
/// CLEARS the second flag. So the one path a real console was watched taking is the quiet one, and
/// every path that stops a stream is unwitnessed.
///
/// A RAISES SILENTLY. DISPLAYA carrying 0x1 sets the first flag and tells the client nothing. What
/// tells the client is DISPLAYB arriving afterwards with anything other than 01-ff - only then is
/// the sink told the stream cannot be shown, and only then is the second flag raised.
///
/// AND THE TWO DIRECTIONS ARE NOT SYMMETRIC, which is the part worth porting carefully. Clearing
/// the second flag says NOTHING to the client: a 01-ff arrives, cant_displayb goes false, and the
/// sink is not told. The only thing that ever says the stream can be shown again is DISPLAYA
/// carrying 0x0 - and that branch is itself guarded on the second flag being down. So a console
/// that raised both and then sent only 01-ff leaves a client still hiding the stream until a
/// DISPLAYA follows.
///
/// A 0x0 WHILE B IS UP IS IGNORED ENTIRELY - not deferred, not queued: the first flag is not even
/// lowered. So the first flag can be stale while the second is up, which is why this is a table
/// over both rather than two independent booleans.
/// </summary>
public static class CtrlDisplay
{
    /// <summary>The DISPLAYB payload that clears the second flag. Compared as a pair.</summary>
    public static ReadOnlySpan<byte> ClearingPair => [0x01, 0xff];

    /// <summary>What a DISPLAYA payload does.</summary>
    public static DisplayEffect ReceiveDisplayA(DisplayFlags flags, ReadOnlySpan<byte> payload)
    {
        // PP352: the check the handler did not have.
        if (payload.Length < 1)
            return new DisplayEffect(flags, DisplayTold.Nothing);

        if (payload[0] == 0x1)
            return new DisplayEffect(flags with { CantA = true }, DisplayTold.Nothing);

        // The only thing that ever tells the client the stream is back - and it is guarded on the
        // second flag AND on the prohibition (PP359), so it does nothing at all while either is up.
        if (payload[0] == 0x0 && !flags.CantB && !flags.Prohibited)
            return new DisplayEffect(flags with { CantA = false }, DisplayTold.CanDisplay);

        return new DisplayEffect(flags, DisplayTold.Nothing);
    }

    /// <summary>What a DISPLAYB payload does.</summary>
    public static DisplayEffect ReceiveDisplayB(DisplayFlags flags, ReadOnlySpan<byte> payload)
    {
        // PP352: both bytes are read below, so both are required.
        if (payload.Length < 2)
            return new DisplayEffect(flags, DisplayTold.Nothing);

        bool clearing = payload[0] == ClearingPair[0] && payload[1] == ClearingPair[1];

        DisplayFlags next = flags;
        var told = DisplayTold.Nothing;

        // Only while the first flag is up, and only the first time.
        if (flags.CantA && !clearing && !flags.CantB)
        {
            next = next with { CantB = true };
            told = DisplayTold.CannotDisplay;
        }

        // Unconditional on the first flag - and silent. Nothing is told here, which is the
        // asymmetry: only a DISPLAYA 0x0 ever says the stream is back.
        if (next.CantB && clearing)
            next = next with { CantB = false };

        return new DisplayEffect(next, told);
    }

    /// <summary>
    /// PP359: what the ctrl response's RP-Prohibit does, which is the third way into this machine.
    ///
    /// It arrives once, before any display message, and tells the client the stream cannot be shown.
    /// Recording it is what the C did not do: the sink was told while both flags stayed false, so
    /// the client's belief and the machine's state disagreed from the first moment of the session.
    /// </summary>
    public static DisplayEffect ReceiveProhibition(DisplayFlags flags, bool prohibited)
        => prohibited
            ? new DisplayEffect(flags with { Prohibited = true }, DisplayTold.CannotDisplay)
            : new DisplayEffect(flags, DisplayTold.Nothing);

    /// <summary>
    /// Whether an RP-Prohibit header value means prohibited, by the reading ctrl.c gives it.
    ///
    /// <c>atoi(value) == 1</c>, REPRODUCED AND NOT CORRECTED. It is a fail-open: anything that is
    /// not a leading integer 1 means not prohibited, and that includes the empty string, a value
    /// that failed to decrypt, and "true". Nothing in this tree knows what a console actually sends
    /// here, so the parse stays as it is and the shape is asserted rather than improved.
    /// </summary>
    public static bool ReadsAsProhibited(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
            return false;

        var at = 0;
        while (at < headerValue.Length && char.IsWhiteSpace(headerValue[at]))
            at++;

        var negative = false;
        if (at < headerValue.Length && (headerValue[at] == '+' || headerValue[at] == '-'))
            negative = headerValue[at++] == '-';

        var digits = 0;
        var value = 0;
        while (at < headerValue.Length && char.IsAsciiDigit(headerValue[at]))
        {
            // Bounded rather than accumulated forever: anything past 1 is already not 1.
            value = value >= 10 ? value : (value * 10) + (headerValue[at] - '0');
            at++;
            digits++;
        }

        return digits > 0 && !negative && value == 1;
    }

    /// <summary>
    /// Whether the client currently believes it cannot show the stream.
    ///
    /// The second flag or the prohibition, and never the first: the sink is told from those two,
    /// and the first can be stale while either is up.
    /// </summary>
    public static bool ClientIsHidingTheStream(DisplayFlags flags) => flags.CantB || flags.Prohibited;
}

/// <summary>
/// PP353: the two handlers held against ctrl.c, since the capture holds only the quiet path.
/// </summary>
public static class CtrlDisplaySource
{
    /// <summary>Where they live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether DISPLAYA still raises its flag without telling anybody.</summary>
    public static bool ARaisesSilently(string displayABody)
    {
        ArgumentNullException.ThrowIfNull(displayABody);

        int raise = displayABody.IndexOf("cant_displaya = true;", StringComparison.Ordinal);
        if (raise < 0)
            return false;

        // The callback must come after the else, not in the branch that raises.
        int callback = displayABody.IndexOf("cantdisplay_cb", StringComparison.Ordinal);
        return callback < 0 || callback > raise;
    }

    /// <summary>
    /// Whether telling the client the stream is back is still guarded on the second flag.
    ///
    /// Without that guard a DISPLAYA 0x0 would un-hide a stream the console is still covering.
    /// </summary>
    public static bool TheCanDisplayBranchIsStillGuarded(string displayABody)
    {
        ArgumentNullException.ThrowIfNull(displayABody);

        return displayABody.Contains("payload[0] == 0x0 && !ctrl->cant_displayb", StringComparison.Ordinal);
    }

    /// <summary>Whether DISPLAYB still only raises while the first flag is up, and only once.</summary>
    public static bool BOnlyRaisesUnderAAndOnlyOnce(string displayBBody)
    {
        ArgumentNullException.ThrowIfNull(displayBBody);

        return displayBBody.Contains("if(ctrl->cant_displaya == true)", StringComparison.Ordinal)
            && displayBBody.Contains("&& !ctrl->cant_displayb", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether clearing the second flag is still silent.
    ///
    /// This is the asymmetry, and it is asserted so a port that "fixed" it by telling the client
    /// would show a difference from the C rather than a tidier design.
    /// </summary>
    public static bool ClearingIsStillSilent(string displayBBody)
    {
        ArgumentNullException.ThrowIfNull(displayBBody);

        int clear = displayBBody.IndexOf("cant_displayb = false;", StringComparison.Ordinal);
        if (clear < 0)
            return false;

        // No callback after the clear: the last thing that arm does is lower the flag.
        return !displayBBody[clear..].Contains("cantdisplay_cb", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP359: whether the connect RECORDS the prohibition as well as reporting it.
    ///
    /// The order is the assertion. The flag has to be raised before the sink is told, so no reader
    /// of the machine can observe a client hiding the stream over a state that says it should not
    /// be - which is what the whole session used to look like.
    /// </summary>
    public static bool TheProhibitionIsRecordedBeforeItIsReported(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int branch = connectBody.IndexOf("if(response.rp_prohibit)", StringComparison.Ordinal);
        if (branch < 0)
            return false;

        int raise = connectBody.IndexOf("ctrl->rp_prohibit = true;", branch, StringComparison.Ordinal);
        int told = connectBody.IndexOf("cantdisplay_cb", branch, StringComparison.Ordinal);

        return raise > branch && told > raise;
    }

    /// <summary>
    /// Whether the branch that says the stream is back also guards on the prohibition.
    ///
    /// This is the defect itself: that branch was guarded on cant_displayb alone, and RP-Prohibit
    /// never raised cant_displayb - so a prohibited session was un-hidden by the first unrelated
    /// DisplayA 0x0 the console sent.
    /// </summary>
    public static bool TheCanDisplayBranchAlsoGuardsOnTheProhibition(string displayABody)
    {
        ArgumentNullException.ThrowIfNull(displayABody);

        return displayABody.Contains(
            "payload[0] == 0x0 && !ctrl->cant_displayb && !ctrl->rp_prohibit",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the prohibition is still never lowered, which is what makes it a session property.
    ///
    /// Exactly one assignment of false, and it is the init. A second would be a way back out that
    /// nothing in the ctrl response justifies: the header is read once and never sent again.
    /// </summary>
    public static bool TheProhibitionIsOnlyClearedAtInit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        const string Cleared = "ctrl->rp_prohibit = false;";

        var count = 0;
        for (int at = source.IndexOf(Cleared, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(Cleared, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count == 1
            && CFunction.Body(source, "chiaki_ctrl_init") is { } init
            && init.Contains(Cleared, StringComparison.Ordinal);
    }
}
