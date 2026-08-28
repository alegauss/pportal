using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP466, PP294: which ctrl message types arrive, and what receiving one costs.
///
/// PP440 censused all 22 and which class answers each, so a part of this file could be picked. The
/// direction was the column it did not have: without it a reader cannot tell a missing handler from a
/// type no console sends, and both look like a gap.
///
/// The other half is the counter, and it is PP448's rule read backwards - a message arriving with no
/// payload advances nothing, so a managed loop counting per message rather than per encrypted payload
/// drifts by exactly the number of bare messages it received.
/// </summary>
public class CtrlDispatchTableTests
{
    private static string? Dispatch()
    {
        if (CtrlDispatchTable.Locate() is not { } path)
            return null;

        return CtrlDispatchTable.DispatchBody(File.ReadAllText(path));
    }

    /// <summary>
    /// TEN ARRIVE AND TWELVE DO NOT, and the two lists account for the census exactly once each.
    /// </summary>
    [Fact]
    public void EveryCensusedTypeIsEitherReceivedOrSendOnly()
    {
        Assert.Equal(10, CtrlDispatchTable.Received.Count);
        Assert.Equal(12, CtrlDispatchTable.SendOnly.Count);

        Assert.Equal(
            CtrlMessageCensus.Rows.Count,
            CtrlDispatchTable.Received.Count + CtrlDispatchTable.SendOnly.Count);

        // And nothing is in both, which the derivation guarantees and this states.
        Assert.Empty(CtrlDispatchTable.Received.Intersect(CtrlDispatchTable.SendOnly, StringComparer.Ordinal));

        // Every received name is a censused one - a typo here would otherwise sit in Received and
        // silently move a type into SendOnly.
        foreach (string name in CtrlDispatchTable.Received)
            Assert.Contains(name, CtrlMessageCensus.Rows.Select(r => r.CName));
    }

    /// <summary>
    /// The switch's cases are exactly those ten, in that order, read out of ctrl.c.
    ///
    /// This is what makes the list above a claim about the file rather than a copy of itself.
    /// </summary>
    [Fact]
    public void TheSwitchsCasesAreExactlyThoseTen()
    {
        if (Dispatch() is not { } body)
            return;

        Assert.Equal(CtrlDispatchTable.Received.ToArray(), CtrlDispatchTable.CasesIn(body).ToArray());
    }

    /// <summary>
    /// A handful of the send-only ones, named so the split is legible rather than only counted.
    /// </summary>
    [Theory]
    [InlineData("GOTO_BED")]
    [InlineData("GO_HOME")]
    [InlineData("MIC_CONNECT")]
    [InlineData("HEARTBEAT_REP")]
    [InlineData("KEYBOARD_TEXT_CHANGE_REQ")]
    [InlineData("LOGIN_PIN_REP")]
    public void TheseAreSentAndNeverReceived(string cName)
    {
        Assert.False(CtrlDispatchTable.Arrives(cName));
        Assert.Contains(cName, CtrlDispatchTable.SendOnly);
    }

    /// <summary>And the request/reply pairs sit on opposite sides, which is the shape of the split.</summary>
    [Fact]
    public void AHeartbeatRequestArrivesAndItsReplyDoesNot()
    {
        Assert.True(CtrlDispatchTable.Arrives("HEARTBEAT_REQ"));
        Assert.False(CtrlDispatchTable.Arrives("HEARTBEAT_REP"));

        // The keyboard's text change is the same pair the other way round: this side sends the REQ
        // and the console answers with the RES.
        Assert.False(CtrlDispatchTable.Arrives("KEYBOARD_TEXT_CHANGE_REQ"));
        Assert.True(CtrlDispatchTable.Arrives("KEYBOARD_TEXT_CHANGE_RES"));
    }

    /// <summary>
    /// PP448'S RULE FROM THE OTHER SIDE: a message arriving with no payload advances no counter.
    /// </summary>
    [Fact]
    public void ABarePayloadlessMessageSpendsNothing()
    {
        CtrlReceiveSpend bare = CtrlDispatchTable.Receive(7, payloadSize: 0);

        Assert.Equal((ushort)7, bare.Next);
        Assert.False(bare.Spent);

        // And one with a payload decrypts at the value it consumes, with no step back - that quirk is
        // the send side's alone.
        CtrlReceiveSpend paid = CtrlDispatchTable.Receive(7, payloadSize: 1);

        Assert.Equal((ushort)7, paid.DecryptAt);
        Assert.Equal((ushort)8, paid.Next);
        Assert.True(paid.Spent);
    }

    /// <summary>
    /// The two sides agree on what a payload costs, which is the property that keeps the counters in
    /// step - and the one place they differ is named rather than left to be found.
    /// </summary>
    [Fact]
    public void TheSendAndReceiveSidesAgreeExceptForThePinReply()
    {
        CtrlSendSpend sent = CtrlSendCounter.Spend(7, type: 0x33, rudp: false, hasPayload: true);
        CtrlReceiveSpend received = CtrlDispatchTable.Receive(7, payloadSize: 1);

        Assert.Equal(sent.EncryptAt, received.DecryptAt);
        Assert.Equal(sent.Next, received.Next);
        Assert.Equal(sent.Spent, received.Spent);

        // The exception, and it is the send's: over rudp a PIN reply encrypts one behind. Nothing on
        // the receive side steps back, and LOGIN_PIN_REP does not arrive here anyway.
        CtrlSendSpend quirk = CtrlSendCounter.Spend(
            7, CtrlSendCounter.LoginPinRep, rudp: true, hasPayload: true);

        Assert.NotEqual(received.DecryptAt, quirk.EncryptAt);
        Assert.False(CtrlDispatchTable.Arrives("LOGIN_PIN_REP"));
    }

    /// <summary>The counter is still moved only for a message with a payload, in the C.</summary>
    [Fact]
    public void TheCounterStillMovesOnlyForAPayload()
    {
        if (Dispatch() is not { } body)
            return;

        Assert.True(
            CtrlDispatchTable.TheCounterStillMovesOnlyForAPayload(body),
            "the remote counter moved outside the payload guard, so a bare message now spends one and "
                + "the two sides drift");
    }

    /// <summary>And a failed decrypt still stops before the tap records anything.</summary>
    [Fact]
    public void AFailedDecryptStopsBeforeTheTap()
    {
        if (Dispatch() is not { } body)
            return;

        Assert.True(CtrlDispatchTable.AFailedDecryptStillStopsBeforeTheTap(body));
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(CtrlDispatchTable.DispatchBody(""));
        Assert.Empty(CtrlDispatchTable.CasesIn(""));
        Assert.False(CtrlDispatchTable.TheCounterStillMovesOnlyForAPayload(""));
        Assert.False(CtrlDispatchTable.AFailedDecryptStillStopsBeforeTheTap(""));
    }
}
