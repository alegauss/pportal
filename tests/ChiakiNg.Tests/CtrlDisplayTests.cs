using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP353, under PP294: the two flags that decide whether the client shows the stream.
///
/// PP297's capture holds exactly one DISPLAYB, carrying 01-ff - the value that CLEARS. So the one
/// path a real console was watched taking is the quiet one, and every path that stops a stream is
/// asserted against ctrl.c and against nothing else.
/// </summary>
public class CtrlDisplayTests
{
    private static readonly byte[] Clearing = [0x01, 0xff];
    private static readonly byte[] NotClearing = [0x02, 0x00];

    /// <summary>DISPLAYA carrying 0x1 raises its flag and tells nobody.</summary>
    [Fact]
    public void ARaisesItsFlagSilently()
    {
        DisplayEffect effect = CtrlDisplay.ReceiveDisplayA(new DisplayFlags(), [0x1]);

        Assert.True(effect.Flags.CantA);
        Assert.Equal(DisplayTold.Nothing, effect.Told);
        Assert.False(CtrlDisplay.ClientIsHidingTheStream(effect.Flags));
    }

    /// <summary>
    /// It takes a DISPLAYB after it to tell the client anything - and not the clearing pair.
    /// </summary>
    [Fact]
    public void BUnderARaisedFlagIsWhatStopsTheStream()
    {
        DisplayEffect effect = CtrlDisplay.ReceiveDisplayB(
            new DisplayFlags(CantA: true), NotClearing);

        Assert.True(effect.Flags.CantB);
        Assert.Equal(DisplayTold.CannotDisplay, effect.Told);
        Assert.True(CtrlDisplay.ClientIsHidingTheStream(effect.Flags));
    }

    /// <summary>A DISPLAYB with the first flag down does nothing at all.</summary>
    [Fact]
    public void BWithoutARaisedFlagDoesNothing()
    {
        DisplayEffect effect = CtrlDisplay.ReceiveDisplayB(new DisplayFlags(), NotClearing);

        Assert.Equal(new DisplayFlags(), effect.Flags);
        Assert.Equal(DisplayTold.Nothing, effect.Told);
    }

    /// <summary>And it only tells the client once, however many arrive.</summary>
    [Fact]
    public void BTellsTheClientOnlyOnce()
    {
        var raised = new DisplayFlags(CantA: true, CantB: true);

        DisplayEffect again = CtrlDisplay.ReceiveDisplayB(raised, NotClearing);

        Assert.Equal(raised, again.Flags);
        Assert.Equal(DisplayTold.Nothing, again.Told);
    }

    /// <summary>
    /// CLEARING THE SECOND FLAG IS SILENT, which is the asymmetry.
    ///
    /// A 01-ff lowers the flag and the sink is not told. So a console that raised both and then sent
    /// only 01-ff leaves the client still hiding the stream, until a DISPLAYA follows.
    /// </summary>
    [Fact]
    public void ClearingTheSecondFlagTellsTheClientNothing()
    {
        DisplayEffect effect = CtrlDisplay.ReceiveDisplayB(
            new DisplayFlags(CantA: true, CantB: true), Clearing);

        Assert.False(effect.Flags.CantB);
        Assert.Equal(DisplayTold.Nothing, effect.Told);

        // The first flag is untouched by this, which is what makes the next DISPLAYA matter.
        Assert.True(effect.Flags.CantA);
    }

    /// <summary>Only a DISPLAYA carrying 0x0 ever says the stream is back.</summary>
    [Fact]
    public void OnlyADisplayAZeroSaysTheStreamIsBack()
    {
        DisplayEffect effect = CtrlDisplay.ReceiveDisplayA(new DisplayFlags(CantA: true), [0x0]);

        Assert.False(effect.Flags.CantA);
        Assert.Equal(DisplayTold.CanDisplay, effect.Told);
    }

    /// <summary>
    /// A 0x0 WHILE THE SECOND FLAG IS UP IS IGNORED ENTIRELY - the first flag is not even lowered.
    ///
    /// Not deferred and not queued. So the first flag can be stale while the second is up, which is
    /// why this is a table over both rather than two independent booleans.
    /// </summary>
    [Fact]
    public void AZeroIsIgnoredWhileTheSecondFlagIsUp()
    {
        var both = new DisplayFlags(CantA: true, CantB: true);

        DisplayEffect effect = CtrlDisplay.ReceiveDisplayA(both, [0x0]);

        Assert.Equal(both, effect.Flags);
        Assert.Equal(DisplayTold.Nothing, effect.Told);
    }

    /// <summary>
    /// THE WHOLE SEQUENCE, because the asymmetry only shows across arrivals.
    ///
    /// Raise, stop the stream, clear quietly, and then - only then - a DISPLAYA brings it back. Two
    /// messages after the console stopped covering the screen, which is what the C does.
    /// </summary>
    [Fact]
    public void TheWholeSequenceNeedsADisplayAToRecover()
    {
        var flags = new DisplayFlags();

        flags = CtrlDisplay.ReceiveDisplayA(flags, [0x1]).Flags;
        Assert.False(CtrlDisplay.ClientIsHidingTheStream(flags));

        DisplayEffect stopped = CtrlDisplay.ReceiveDisplayB(flags, NotClearing);
        flags = stopped.Flags;
        Assert.Equal(DisplayTold.CannotDisplay, stopped.Told);
        Assert.True(CtrlDisplay.ClientIsHidingTheStream(flags));

        DisplayEffect cleared = CtrlDisplay.ReceiveDisplayB(flags, Clearing);
        flags = cleared.Flags;
        Assert.Equal(DisplayTold.Nothing, cleared.Told);
        Assert.False(CtrlDisplay.ClientIsHidingTheStream(flags));

        DisplayEffect back = CtrlDisplay.ReceiveDisplayA(flags, [0x0]);
        Assert.Equal(DisplayTold.CanDisplay, back.Told);
        Assert.False(back.Flags.CantA);
    }

    /// <summary>PP352: a short payload moves nothing, in either handler.</summary>
    [Fact]
    public void AShortPayloadMovesNothing()
    {
        var both = new DisplayFlags(CantA: true);

        Assert.Equal(both, CtrlDisplay.ReceiveDisplayA(both, []).Flags);
        Assert.Equal(both, CtrlDisplay.ReceiveDisplayB(both, [0x01]).Flags);
        Assert.Equal(DisplayTold.Nothing, CtrlDisplay.ReceiveDisplayB(both, [0x01]).Told);
    }

    /// <summary>A DISPLAYA byte that is neither 0 nor 1 does nothing.</summary>
    [Fact]
    public void AnUnknownDisplayAByteDoesNothing()
    {
        var flags = new DisplayFlags(CantA: true);

        Assert.Equal(flags, CtrlDisplay.ReceiveDisplayA(flags, [0x7]).Flags);
    }

    /// <summary>And ctrl.c still behaves the way this table says.</summary>
    [Fact]
    public void CtrlStillDeclaresTheMachine()
    {
        string? path = CtrlDisplaySource.Locate();
        if (path is null)
            return;

        string? a = ChiakiNg.Session.CFunction.BodyIn(path, "ctrl_message_received_displaya");
        string? b = ChiakiNg.Session.CFunction.BodyIn(path, "ctrl_message_received_displayb");

        Assert.NotNull(a);
        Assert.NotNull(b);

        Assert.True(CtrlDisplaySource.ARaisesSilently(a), "DisplayA now tells the client when it raises");
        Assert.True(
            CtrlDisplaySource.TheCanDisplayBranchIsStillGuarded(a),
            "the can-display branch is no longer guarded on the second flag");
        Assert.True(
            CtrlDisplaySource.BOnlyRaisesUnderAAndOnlyOnce(b),
            "DisplayB now raises without the first flag, or more than once");
        Assert.True(
            CtrlDisplaySource.ClearingIsStillSilent(b),
            "clearing the second flag now tells the client, which the C does not");
    }
}
