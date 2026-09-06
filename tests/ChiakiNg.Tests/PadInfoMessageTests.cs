using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP689, under PP295: the message the console drives the controller with, in both its layouts.
///
/// What is asserted is the two things a port gets wrong quietly: reading a field at the other
/// layout's offset, and reporting the five in the order they were parsed rather than the order the C
/// sends them. The second is PP295's first criterion in miniature.
/// </summary>
public class PadInfoMessageTests(ITestOutputHelper output)
{
    /// <summary>A message of the given layout, with every field placed where that layout puts it.</summary>
    private static byte[] Message(
        PadInfoLayout layout,
        byte playerIndex = 0,
        byte[]? led = null,
        byte haptic = 0,
        byte trigger = 0,
        bool motionReset = false,
        ushort feedbackSeqNum = 0,
        uint streamSeconds = 0)
    {
        byte[] message = new byte[
            layout == PadInfoLayout.Wide ? PadInfoMessage.WideSize : PadInfoMessage.NarrowSize];

        message[PadInfoMessage.PlayerIndexOffset(layout)] = playerIndex;
        (led ?? [0, 0, 0]).CopyTo(message, PadInfoMessage.LedOffset(layout));
        message[PadInfoMessage.HapticOffset(layout)] = haptic;
        message[PadInfoMessage.TriggerOffset(layout)] = trigger;
        message[PadInfoMessage.MotionResetOffset(layout)] = motionReset ? (byte)1 : (byte)0;

        if (layout == PadInfoLayout.Wide)
        {
            BinaryPrimitives.WriteUInt16BigEndian(message, feedbackSeqNum);
            BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), streamSeconds);
        }

        return message;
    }

    /// <summary>The handler's body, or null outside a checkout.</summary>
    private static string? Body()
        => PadInfoMessageSource.Locate() is { } path
            ? PadInfoMessageSource.HandlerBody(File.ReadAllText(path))
            : null;

    /// <summary>The length is the whole of how a layout is recognised.</summary>
    [Theory]
    [InlineData(0x19, PadInfoLayout.Wide)]
    [InlineData(0x11, PadInfoLayout.Narrow)]
    [InlineData(0x18, PadInfoLayout.Unknown)]
    [InlineData(0x1a, PadInfoLayout.Unknown)]
    [InlineData(0, PadInfoLayout.Unknown)]
    public void TheLengthChoosesTheLayout(int length, PadInfoLayout layout)
        => Assert.Equal(layout, PadInfoMessage.LayoutOf(length));

    /// <summary>
    /// BOTH LAYOUTS CARRY THE SAME VALUES AT DIFFERENT PLACES, which is the thing a port reads at
    /// one offset for both and gets right half the time.
    /// </summary>
    [Theory]
    [InlineData(PadInfoLayout.Wide)]
    [InlineData(PadInfoLayout.Narrow)]
    public void EachLayoutIsReadAtItsOwnOffsets(PadInfoLayout layout)
    {
        byte[] message = Message(
            layout, playerIndex: 3, led: [0x11, 0x22, 0x33], haptic: 2, trigger: 1);

        PadInfoReading reading = PadInfoMessage.Read(message, PadState.Initial);

        Assert.Equal(layout, reading.Layout);
        Assert.Equal(3, reading.State.PlayerIndex);
        Assert.Equal(new PadLed(0x11, 0x22, 0x33), reading.State.Led);
        Assert.Equal(2, reading.State.HapticIntensity);
        Assert.Equal(1, reading.State.TriggerIntensity);
    }

    /// <summary>
    /// And the offsets are not the same two, so a reader using one layout's for the other is caught
    /// rather than agreeing by coincidence.
    /// </summary>
    [Fact]
    public void TheTwoLayoutsPutTheFieldsInDifferentPlaces()
    {
        Assert.NotEqual(
            PadInfoMessage.PlayerIndexOffset(PadInfoLayout.Wide),
            PadInfoMessage.PlayerIndexOffset(PadInfoLayout.Narrow));
        Assert.NotEqual(
            PadInfoMessage.LedOffset(PadInfoLayout.Wide),
            PadInfoMessage.LedOffset(PadInfoLayout.Narrow));
        Assert.NotEqual(
            PadInfoMessage.MotionResetOffset(PadInfoLayout.Wide),
            PadInfoMessage.MotionResetOffset(PadInfoLayout.Narrow));
        Assert.NotEqual(
            PadInfoMessage.HapticOffset(PadInfoLayout.Wide),
            PadInfoMessage.HapticOffset(PadInfoLayout.Narrow));

        // And the triggers are always the byte after the haptics, in both.
        Assert.Equal(
            PadInfoMessage.HapticOffset(PadInfoLayout.Wide) + 1,
            PadInfoMessage.TriggerOffset(PadInfoLayout.Wide));
        Assert.Equal(
            PadInfoMessage.HapticOffset(PadInfoLayout.Narrow) + 1,
            PadInfoMessage.TriggerOffset(PadInfoLayout.Narrow));
    }

    /// <summary>
    /// A length that is neither reports nothing AND leaves what is held, which is the safe half of
    /// a message this side could not read.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0x10)]
    [InlineData(0x12)]
    [InlineData(0x18)]
    [InlineData(0x40)]
    public void AnUnknownLengthReportsNothingAndChangesNothing(int length)
    {
        var held = new PadState(4, new PadLed(1, 2, 3), 2, 1);

        PadInfoReading reading = PadInfoMessage.Read(new byte[length], held);

        Assert.Equal(PadInfoLayout.Unknown, reading.Layout);
        Assert.Empty(reading.Reports);
        Assert.Equal(held, reading.State);
    }

    /// <summary>
    /// THE ORDER, which is the criterion: motion reset, haptic, trigger, light bar, player index -
    /// and not the order the fields sit in.
    ///
    /// Everything moves at once, so what the sequence can be is decided by the code rather than by
    /// which field happened to change.
    /// </summary>
    [Theory]
    [InlineData(PadInfoLayout.Wide)]
    [InlineData(PadInfoLayout.Narrow)]
    public void AllFiveGoOutInTheCsOrder(PadInfoLayout layout)
    {
        byte[] message = Message(
            layout, playerIndex: 2, led: [9, 8, 7], haptic: 1, trigger: 3, motionReset: true);

        PadInfoReading reading = PadInfoMessage.Read(message, PadState.Initial);
        output.WriteLine(string.Join(" -> ", reading.Reports));

        Assert.Equal(PadInfoMessage.ReportOrder, reading.Reports);
    }

    /// <summary>
    /// NOTHING THAT DID NOT MOVE IS REPORTED. The same message twice reports its second self
    /// nothing, which is what stops a client rewriting the light bar as fast as the console sends.
    /// </summary>
    [Fact]
    public void ASecondIdenticalMessageReportsNothing()
    {
        byte[] message = Message(
            PadInfoLayout.Wide, playerIndex: 1, led: [4, 5, 6], haptic: 2, trigger: 2);

        PadInfoReading first = PadInfoMessage.Read(message, PadState.Initial);
        PadInfoReading second = PadInfoMessage.Read(message, first.State);

        Assert.NotEmpty(first.Reports);
        Assert.Empty(second.Reports);
        Assert.Equal(first.State, second.State);
    }

    /// <summary>
    /// PP689: the held state compares by VALUE, which is what makes the four comparisons work.
    ///
    /// The C memcmps three bytes; a managed state holding them as an array would compare by
    /// reference, so two identical light bars would read as a change and the report would fire on
    /// every message. The type is three bytes for exactly that reason.
    /// </summary>
    [Fact]
    public void TheHeldStateComparesByValue()
    {
        Assert.Equal(new PadLed(1, 2, 3), PadLed.From([1, 2, 3]));
        Assert.NotEqual(new PadLed(1, 2, 3), new PadLed(1, 2, 4));

        Assert.Equal(
            new PadState(1, new PadLed(4, 5, 6), 2, 2),
            new PadState(1, PadLed.From([4, 5, 6]), 2, 2));

        Assert.Equal<byte[]>([4, 5, 6], new PadLed(4, 5, 6).ToBytes());
        Assert.Throws<ArgumentException>(() => PadLed.From([1, 2]));
    }

    /// <summary>And one field moving reports exactly that one.</summary>
    [Fact]
    public void OneFieldMovingReportsOne()
    {
        var held = new PadState(1, new PadLed(4, 5, 6), 2, 2);

        Assert.Equal(
            [PadReportKind.LedColor],
            PadInfoMessage.Read(
                Message(PadInfoLayout.Wide, playerIndex: 1, led: [4, 5, 7], haptic: 2, trigger: 2),
                held).Reports);

        Assert.Equal(
            [PadReportKind.PlayerIndex],
            PadInfoMessage.Read(
                Message(PadInfoLayout.Wide, playerIndex: 2, led: [4, 5, 6], haptic: 2, trigger: 2),
                held).Reports);

        Assert.Equal(
            [PadReportKind.HapticIntensity],
            PadInfoMessage.Read(
                Message(PadInfoLayout.Wide, playerIndex: 1, led: [4, 5, 6], haptic: 3, trigger: 2),
                held).Reports);

        Assert.Equal(
            [PadReportKind.TriggerIntensity],
            PadInfoMessage.Read(
                Message(PadInfoLayout.Wide, playerIndex: 1, led: [4, 5, 6], haptic: 2, trigger: 3),
                held).Reports);
    }

    /// <summary>
    /// THE MOTION RESET IS NOT A COMPARISON. It fires every time its byte is set, because it is the
    /// console asking for something rather than telling this side what changed.
    ///
    /// A port that treated it like the other four would answer the first ask and ignore every one
    /// after it, and motion control would drift with nothing in a log about it.
    /// </summary>
    [Fact]
    public void TheMotionResetFiresEveryTimeItIsAsked()
    {
        byte[] message = Message(PadInfoLayout.Narrow, motionReset: true);

        PadInfoReading first = PadInfoMessage.Read(message, PadState.Initial);
        PadInfoReading second = PadInfoMessage.Read(message, first.State);
        PadInfoReading third = PadInfoMessage.Read(message, second.State);

        Assert.Equal([PadReportKind.MotionReset], first.Reports);
        Assert.Equal([PadReportKind.MotionReset], second.Reports);
        Assert.Equal([PadReportKind.MotionReset], third.Reports);
    }

    /// <summary>Any non-zero byte is the ask, not just one.</summary>
    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)0xff)]
    public void AnyNonZeroByteIsTheAsk(byte value)
    {
        byte[] message = Message(PadInfoLayout.Wide);
        message[PadInfoMessage.MotionResetOffset(PadInfoLayout.Wide)] = value;

        Assert.Contains(PadReportKind.MotionReset, PadInfoMessage.Read(message, PadState.Initial).Reports);
    }

    /// <summary>
    /// The wide layout's two diagnostics are read at their own widths - and the timestamp's is four
    /// bytes, which is PP374's repair reproduced rather than inherited.
    ///
    /// A value above 65535 is what tells the two widths apart: swapped as two, the low half is
    /// discarded before anything happens and the number moves once per 65536 units.
    /// </summary>
    [Fact]
    public void TheWideLayoutsDiagnosticsAreReadAtTheirOwnWidths()
    {
        byte[] message = Message(
            PadInfoLayout.Wide, feedbackSeqNum: 0x1234, streamSeconds: 0x0001_0002);

        Assert.Equal((ushort)0x1234, PadInfoMessage.FeedbackSeqNum(message));
        Assert.Equal(0x0001_0002u, PadInfoMessage.StreamSeconds(message));

        // The narrow layout carries neither.
        byte[] narrow = Message(PadInfoLayout.Narrow);
        Assert.Null(PadInfoMessage.FeedbackSeqNum(narrow));
        Assert.Null(PadInfoMessage.StreamSeconds(narrow));
    }

    /// <summary>THE C STILL SENDS THEM IN THIS ORDER, which is what the port is reproducing.</summary>
    [Fact]
    public void TheCStillSendsThemInThisOrder()
    {
        if (Body() is not { } body)
            return;

        IReadOnlyList<string> order = PadInfoMessageSource.ReportOrderIn(body);
        output.WriteLine(string.Join(" -> ", order));

        Assert.Equal(
            ["MOTION_RESET", "HAPTIC_INTENSITY", "TRIGGER_INTENSITY", "LED_COLOR", "PLAYER_INDEX"],
            order);

        Assert.Equal(PadInfoMessage.ReportOrder.Count, order.Count);
    }

    /// <summary>
    /// PP744: and it is read whatever the C calls the local, which is what the copy could not do.
    ///
    /// This sweep used to start at "event.type" - correct only because every raiser in the handler
    /// it reads is named event. PP722 found the same pattern reporting two of session.c's four
    /// raisers for exactly that reason, so the shared sweep now starts at the member access. A
    /// handler that renamed its local would otherwise read as sending nothing at all, and an order
    /// check over an empty list passes every claim about a sequence.
    /// </summary>
    [Fact]
    public void TheOrderIsReadWhateverTheCCallsItsLocal()
    {
        const string renamed = """
                ChiakiEvent pad_event = { 0 };
                pad_event.type = CHIAKI_EVENT_MOTION_RESET;
                chiaki_session_send_event(session, &pad_event);
                ChiakiEvent second = { 0 };
                second.type = CHIAKI_EVENT_LED_COLOR;
                chiaki_session_send_event(session, &second);
            """;

        Assert.Equal(["MOTION_RESET", "LED_COLOR"], PadInfoMessageSource.ReportOrderIn(renamed));
    }

    /// <summary>And still names these two lengths, and still returns on anything else.</summary>
    [Fact]
    public void TheCStillNamesTheseTwoLengthsAndReturnsOnOthers()
    {
        if (Body() is not { } body)
            return;

        Assert.True(PadInfoMessageSource.TheTwoLayoutsAreStillTheseLengths(body));
        Assert.True(
            PadInfoMessageSource.AnUnknownLengthStillReturns(body),
            "an unknown pad info length no longer returns, so it now falls into the five sends");
    }

    /// <summary>And the timestamp is still read four bytes wide, which is PP374's repair.</summary>
    [Fact]
    public void TheTimestampIsStillReadAtItsOwnWidth()
    {
        if (Body() is not { } body)
            return;

        Assert.True(PadInfoMessageSource.TheTimestampIsStillReadAtItsOwnWidth(body));
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(PadInfoMessageSource.HandlerBody(""));
        Assert.Empty(PadInfoMessageSource.ReportOrderIn(""));
        Assert.False(PadInfoMessageSource.TheTwoLayoutsAreStillTheseLengths(""));
        Assert.False(PadInfoMessageSource.AnUnknownLengthStillReturns(""));
        Assert.False(PadInfoMessageSource.TheTimestampIsStillReadAtItsOwnWidth(""));
    }
}
