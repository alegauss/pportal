using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP676: the managed feedback payloads, byte for byte against feedback.c.
///
/// The three sends outside PP497's MAC table carry these, and none of them had a managed
/// counterpart - so PP676's criterion had nothing to compare. <see cref="NativeFeedback"/> is the
/// oracle and it is a pure one: no session, no socket, no key, so the whole differential runs in a
/// unit test even though the sends themselves need a console.
///
/// WHAT IT IS LOOKING FOR IS NOT A CRASH. Every wrong answer here is the right size and the wrong
/// value: a quaternion compressed by the largest component's VALUE rather than its magnitude aims a
/// controller differently, a history formatted oldest-first reads as input lag, and a stick written
/// little-endian is a stick that drifts. None of them fails on its own.
/// </summary>
public class FeedbackPayloadTests
{
    /// <summary>States chosen for the edges of the two scales and of the quaternion's choice.</summary>
    public static TheoryData<FeedbackMotion> Motions()
    {
        var data = new TheoryData<FeedbackMotion>();

        void Add(
            float gx, float gy, float gz, float ax, float ay, float az,
            float ox, float oy, float oz, float ow,
            short lx, short ly, short rx, short ry)
            => data.Add(new FeedbackMotion(gx, gy, gz, ax, ay, az, ox, oy, oz, ow, lx, ly, rx, ry));

        // Rest: everything centred, the identity quaternion, sticks at zero.
        Add(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0);

        // Both ends of both scales.
        Add(-30, -30, -30, -5, -5, -5, 0, 0, 0, 1, 0, 0, 0, 0);
        Add(30, 30, 30, 5, 5, 5, 0, 0, 0, 1, 0, 0, 0, 0);

        // Past the ends, which the C does not clamp - the cast wraps and this must wrap the same.
        Add(-45, 45, 0, -9, 9, 0, 0, 0, 0, 1, 0, 0, 0, 0);

        // The largest quaternion component in each of the four slots, and NEGATIVE in two of them:
        // a port comparing values rather than magnitudes picks a different component here.
        Add(1, 2, 3, 0.5f, -0.5f, 0.25f, 0.9f, 0.1f, 0.2f, 0.3f, 1, 2, 3, 4);
        Add(1, 2, 3, 0.5f, -0.5f, 0.25f, 0.1f, -0.9f, 0.2f, 0.3f, 1, 2, 3, 4);
        Add(1, 2, 3, 0.5f, -0.5f, 0.25f, 0.1f, 0.2f, 0.9f, 0.3f, 1, 2, 3, 4);
        Add(1, 2, 3, 0.5f, -0.5f, 0.25f, 0.1f, 0.2f, 0.3f, -0.9f, 1, 2, 3, 4);

        // Components past ±1/√2, which the compression clamps and the rest does not.
        Add(0, 0, 0, 0, 0, 0, 0.99f, -0.99f, 0.7f, -0.7f, 0, 0, 0, 0);

        // A tie on magnitude, where the C keeps the FIRST largest because its test is strict.
        Add(0, 0, 0, 0, 0, 0, 0.5f, -0.5f, 0.5f, -0.5f, 0, 0, 0, 0);

        // Sticks at their extremes, including the negative one that a big-endian write of an
        // unsigned cast has to reproduce exactly.
        Add(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, short.MinValue, short.MaxValue, -1, 1);

        return data;
    }

    /// <summary>THE DIFFERENTIAL: both versions, every state, byte for byte.</summary>
    [Theory]
    [MemberData(nameof(Motions))]
    public void TheStateFormatsExactlyAsTheCDoes(FeedbackMotion motion)
    {
        if (!NativeFeedback.IsAvailable())
            return;

        foreach (bool v12 in new[] { false, true })
        {
            byte[] managed = new byte[FeedbackPayload.StateSize(v12)];
            FeedbackPayload.FormatState(managed, v12, motion);

            Assert.Equal(NativeFeedback.FormatState(v12, motion), managed);
        }
    }

    /// <summary>And the two sizes are the C's, so a right-looking buffer is the right length.</summary>
    [Fact]
    public void TheTwoSizesAreTheCs()
    {
        if (!NativeFeedback.IsAvailable())
            return;

        Assert.Equal(FeedbackPayload.StateSizeV9, NativeFeedback.StateSize(false));
        Assert.Equal(FeedbackPayload.StateSizeV12, NativeFeedback.StateSize(true));
    }

    /// <summary>
    /// v12 is v9 plus three bytes, which is the C's own shape rather than a second format.
    ///
    /// Asserted directly because the differential above would pass on two implementations that both
    /// wrote a whole separate v12 - and the next field added to v9 would then be added twice.
    /// </summary>
    [Fact]
    public void TheV12StateIsTheV9StateWithThreeBytesAfterIt()
    {
        var motion = new FeedbackMotion(1, 2, 3, 0.5f, 0.5f, 0.5f, 0.1f, 0.2f, 0.3f, 0.9f, 7, 8, 9, 10);

        byte[] v9 = new byte[FeedbackPayload.StateSizeV9];
        byte[] v12 = new byte[FeedbackPayload.StateSizeV12];

        FeedbackPayload.FormatState(v9, v12: false, motion);
        FeedbackPayload.FormatState(v12, v12: true, motion);

        Assert.Equal(v9, v12[..FeedbackPayload.StateSizeV9]);
        Assert.Equal<byte[]>([0x0, 0x0, 0x1], v12[FeedbackPayload.StateSizeV9..]);
    }

    /// <summary>Every button the C has a code for, pressed and released, against the C.</summary>
    [Theory]
    [InlineData(ChiakiControllerButton.Cross)]
    [InlineData(ChiakiControllerButton.Moon)]
    [InlineData(ChiakiControllerButton.Box)]
    [InlineData(ChiakiControllerButton.Pyramid)]
    [InlineData(ChiakiControllerButton.DpadLeft)]
    [InlineData(ChiakiControllerButton.DpadRight)]
    [InlineData(ChiakiControllerButton.DpadUp)]
    [InlineData(ChiakiControllerButton.DpadDown)]
    [InlineData(ChiakiControllerButton.L1)]
    [InlineData(ChiakiControllerButton.R1)]
    [InlineData(ChiakiControllerButton.L2)]
    [InlineData(ChiakiControllerButton.R2)]
    [InlineData(ChiakiControllerButton.L3)]
    [InlineData(ChiakiControllerButton.R3)]
    [InlineData(ChiakiControllerButton.Options)]
    [InlineData(ChiakiControllerButton.Share)]
    [InlineData(ChiakiControllerButton.Touchpad)]
    [InlineData(ChiakiControllerButton.Ps)]
    public void EveryButtonEventIsTheCs(ChiakiControllerButton button)
    {
        if (!NativeFeedback.IsAvailable())
            return;

        // 0x7f as well as the two ends: the six two-byte buttons test any non-zero, and the twelve
        // three-byte ones carry the value through, so a mid-range state tells the two apart.
        foreach (byte state in new byte[] { 0x00, 0x7f, 0xff })
        {
            byte[] buf = new byte[FeedbackPayload.HistoryEventSizeMax];
            ChiakiError error = FeedbackPayload.ButtonEvent(buf, button, state, out int written);

            Assert.Equal(ChiakiError.Success, error);
            Assert.Equal(NativeFeedback.ButtonEvent(button, state), buf[..written]);
        }
    }

    /// <summary>A button with no code is refused by both, which is a shape and not a failure.</summary>
    [Fact]
    public void AButtonWithNoCodeIsRefusedByBoth()
    {
        var none = (ChiakiControllerButton)(1u << 20);

        byte[] buf = new byte[FeedbackPayload.HistoryEventSizeMax];

        Assert.Equal(ChiakiError.InvalidData, FeedbackPayload.ButtonEvent(buf, none, 0xff, out int written));
        Assert.Equal(0, written);

        if (NativeFeedback.IsAvailable())
            Assert.Null(NativeFeedback.ButtonEvent(none, 0xff));
    }

    /// <summary>The touchpad's twelve-bit packing, at the corners and where the two share a byte.</summary>
    [Theory]
    [InlineData(true, 0, 0, 0)]
    [InlineData(false, 0, 0, 0)]
    [InlineData(true, 1, 1920, 942)]
    [InlineData(true, 127, 1919, 941)]
    [InlineData(true, 0xff, 0x555, 0xaaa)]
    [InlineData(false, 3, 0xfff, 0xfff)]
    public void TheTouchpadEventIsTheCs(bool down, byte pointerId, ushort x, ushort y)
    {
        if (!NativeFeedback.IsAvailable())
            return;

        byte[] buf = new byte[FeedbackPayload.HistoryEventSizeMax];
        int written = FeedbackPayload.TouchpadEvent(buf, down, pointerId, x, y);

        Assert.Equal(NativeFeedback.TouchpadEvent(down, pointerId, x, y), buf[..written]);
    }

    /// <summary>
    /// THE HISTORY IS NEWEST FIRST, which is the ring pushing backwards.
    ///
    /// Distinct one-byte events so the ORDER is readable in the output: a port that appended would
    /// produce the same bytes in the opposite order and nothing else would differ.
    /// </summary>
    [Theory]
    [InlineData(4, 1)]
    [InlineData(4, 3)]
    [InlineData(4, 4)]
    [InlineData(4, 7)]
    [InlineData(1, 3)]
    [InlineData(16, 5)]
    public void TheHistoryFormatsInTheCsOrder(int size, int count)
    {
        if (!NativeFeedback.IsAvailable())
            return;

        byte[][] events = [.. Enumerable.Range(0, count).Select(i => new byte[] { (byte)(0xa0 + i) })];

        byte[] managed = new byte[256];
        ChiakiError error = FeedbackPayload.FormatHistory(size, events, managed, out int written);

        Assert.Equal(ChiakiError.Success, error);
        Assert.Equal(NativeFeedback.FormatHistory(size, events, 256), managed[..written]);
    }

    /// <summary>And with events of different lengths, which is what a real history is.</summary>
    [Fact]
    public void TheHistoryFormatsMixedLengthEventsInTheCsOrder()
    {
        if (!NativeFeedback.IsAvailable())
            return;

        byte[][] events =
        [
            [0x80, 0x88, 0xff],
            [0xd0, 0x01, 0x10, 0x20, 0x30],
            [0x80, 0xaf],
            [0x80, 0x82, 0x00],
        ];

        byte[] managed = new byte[256];
        ChiakiError error = FeedbackPayload.FormatHistory(3, events, managed, out int written);

        Assert.Equal(ChiakiError.Success, error);
        Assert.Equal(NativeFeedback.FormatHistory(3, events, 256), managed[..written]);
    }

    /// <summary>
    /// A buffer too small stops where the C stops, having written nothing a caller may use.
    ///
    /// The C returns BUF_TOO_SMALL from inside the loop, so some bytes are already in the caller's
    /// buffer and the written size is never set - which is why the error is the answer and the
    /// buffer is not.
    /// </summary>
    [Fact]
    public void ABufferTooSmallIsRefusedByBoth()
    {
        byte[][] events = [[0x80, 0x88, 0xff], [0x80, 0x89, 0xff]];

        byte[] managed = new byte[4];
        Assert.Equal(
            ChiakiError.BufTooSmall, FeedbackPayload.FormatHistory(4, events, managed, out _));

        if (NativeFeedback.IsAvailable())
            Assert.Null(NativeFeedback.FormatHistory(4, events, 4));
    }

    /// <summary>
    /// The compression picks the largest by MAGNITUDE and carries its index and sign.
    ///
    /// Stated directly as well as compared, because a differential passes on two implementations
    /// wrong the same way - and this is the rule the C's `fabs` encodes.
    /// </summary>
    [Theory]
    [InlineData(0.9f, 0.1f, 0.2f, 0.3f, 0u, 0u)]
    [InlineData(-0.9f, 0.1f, 0.2f, 0.3f, 0u, 1u)]
    [InlineData(0.1f, -0.9f, 0.2f, 0.3f, 1u, 1u)]
    [InlineData(0.1f, 0.2f, 0.9f, 0.3f, 2u, 0u)]
    [InlineData(0.1f, 0.2f, 0.3f, -0.9f, 3u, 1u)]
    public void TheLargestComponentIsChosenByMagnitude(
        float x, float y, float z, float w, uint index, uint negative)
    {
        uint packed = FeedbackPayload.CompressQuaternion(x, y, z, w);

        Assert.Equal(negative, packed & 1u);
        Assert.Equal(index, (packed >> 1) & 3u);
    }
}
