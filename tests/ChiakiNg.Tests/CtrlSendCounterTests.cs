using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP448: the counter a ctrl send encrypts at, including the one type that does not use the one it
/// consumed.
///
/// PP356 covered what the connect spends. This covers what each send then picks - and CtrlConnect
/// already states the cost of getting it wrong: "every message decrypts to nothing, at the far end,
/// with no local error".
/// </summary>
public class CtrlSendCounterTests
{
    private static string? Ctrl()
    {
        string? path = CtrlSendCounter.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE RULE, and the whole reason for the task: over rudp a PIN reply encrypts ONE BEHIND.
    /// </summary>
    [Theory]
    [InlineData((ushort)5, (ushort)4)]
    [InlineData((ushort)3, (ushort)2)]
    [InlineData((ushort)100, (ushort)99)]
    public void APinReplyOverRudpEncryptsOneBehind(ushort counter, ushort expected)
    {
        CtrlSendSpend spend = CtrlSendCounter.Spend(
            counter, CtrlSendCounter.LoginPinRep, rudp: true, hasPayload: true);

        Assert.Equal(expected, spend.EncryptAt);

        // And it still consumes a value, so the next message is not affected by the step back.
        Assert.Equal((ushort)(counter + 1), spend.Next);
        Assert.True(spend.Spent);
    }

    /// <summary>The same type OFF rudp uses the value it consumed, like everything else.</summary>
    [Fact]
    public void APinReplyOffRudpDoesNotStepBack()
    {
        CtrlSendSpend spend = CtrlSendCounter.Spend(
            5, CtrlSendCounter.LoginPinRep, rudp: false, hasPayload: true);

        Assert.Equal(5ul, spend.EncryptAt);
        Assert.Equal((ushort)6, spend.Next);
    }

    /// <summary>And every other type on rudp uses the value it consumed.</summary>
    [Theory]
    [InlineData((ushort)0x0000)] // SESSION_ID
    [InlineData((ushort)0x8003)] // LOGIN_PIN_REQ's neighbour
    [InlineData((ushort)0x8005)] // one past the quirk
    public void EveryOtherTypeOnRudpUsesWhatItConsumed(ushort type)
    {
        CtrlSendSpend spend = CtrlSendCounter.Spend(5, type, rudp: true, hasPayload: true);

        Assert.Equal(5ul, spend.EncryptAt);
        Assert.Equal((ushort)6, spend.Next);
    }

    /// <summary>
    /// A message with no payload spends nothing: the encryption is behind `if(payload)`.
    ///
    /// A managed loop that incremented per message rather than per encrypted payload would drift by
    /// exactly the number of bare messages it sent.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABarePayloadlessMessageSpendsNothing(bool rudp)
    {
        CtrlSendSpend spend = CtrlSendCounter.Spend(
            7, CtrlSendCounter.LoginPinRep, rudp, hasPayload: false);

        Assert.Equal((ushort)7, spend.Next);
        Assert.False(spend.Spent);
    }

    /// <summary>
    /// The underflow, as the C computes it and not as it looks: at counter zero the subtraction
    /// happens in int, reaches -1, and converts to a uint64_t of all ones - NOT 0xFFFF.
    ///
    /// Unreachable, which the test below asserts separately. Modelled so a managed port that clamped
    /// to 0xFFFF would differ from the C rather than agree with it by accident.
    /// </summary>
    [Fact]
    public void AtZeroTheCComputesAllOnesAndNotSixteenBitsOfOnes()
    {
        CtrlSendSpend spend = CtrlSendCounter.Spend(
            0, CtrlSendCounter.LoginPinRep, rudp: true, hasPayload: true);

        Assert.Equal(ulong.MaxValue, spend.EncryptAt);
        Assert.NotEqual(0xFFFFul, spend.EncryptAt);
    }

    /// <summary>
    /// And why that is traced rather than filed: the connect zeroes the counter and then spends at
    /// least three values before any ctrl message goes out, so a PIN reply never sees zero.
    ///
    /// PP356's own number is the guard here - if the connect ever stopped spending, this would fail
    /// and the underflow would become reachable.
    /// </summary>
    [Fact]
    public void TheConnectSpendsEnoughThatZeroIsUnreachable()
    {
        if (CtrlSendCounter.Locate() is not { } path)
            return;

        // On CtrlConnectSource, not CtrlConnect - the file holds both - and it takes the path rather
        // than the text, so this reads the file itself.
        string? body = CtrlConnectSource.ConnectBody(path);
        if (body is null)
            return;

        Assert.True(
            CtrlConnectSource.CounterSpendsIn(body) >= 1,
            "the connect spends nothing, so a PIN reply could be the first encryption and would "
                + "underflow the counter");
    }

    /// <summary>The quirk is still written the way this models it.</summary>
    [Fact]
    public void TheQuirkIsStillInTheC()
    {
        if (Ctrl() is not { } source)
            return;

        Assert.True(
            CtrlSendCounter.TheQuirkIsStillThere(source),
            "ctrl_message_send no longer steps the PIN reply's counter back, so this model is ahead "
                + "of the C");
    }

    /// <summary>And the payload test still gates the encryption.</summary>
    [Fact]
    public void ThePayloadTestStillGatesTheEncryption()
    {
        if (Ctrl() is not { } source)
            return;

        Assert.True(CtrlSendCounter.EncryptionIsStillBehindAPayloadTest(source));
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.False(CtrlSendCounter.TheQuirkIsStillThere(""));
        Assert.False(CtrlSendCounter.EncryptionIsStillBehindAPayloadTest(""));
    }
}
