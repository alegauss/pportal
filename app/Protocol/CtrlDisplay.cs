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

/// <summary>The two flags, which only mean anything together.</summary>
/// <param name="CantA">Raised by DISPLAYA carrying 0x1. Raised silently.</param>
/// <param name="CantB">Raised by DISPLAYB while A is up, and that is what tells the client.</param>
public readonly record struct DisplayFlags(bool CantA = false, bool CantB = false);

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
        // second flag, so it does nothing at all while that is up.
        if (payload[0] == 0x0 && !flags.CantB)
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
    /// Whether the client currently believes it cannot show the stream.
    ///
    /// It is the second flag and not the pair: the sink is told from that one, and the first can be
    /// stale while it is up.
    /// </summary>
    public static bool ClientIsHidingTheStream(DisplayFlags flags) => flags.CantB;
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
}
