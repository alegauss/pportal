using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP351, under PP294: the four asks a screen makes of the control channel.
///
/// None of them is in PP297's capture - that session opened no keyboard and nobody pressed the
/// power button - so all of this is asserted against ctrl.c.
/// </summary>
public class CtrlKeyboardTests
{
    /// <summary>
    /// ACCEPT AND REJECT ARE THE SAME MESSAGE, one byte apart.
    ///
    /// Swapping them sends the console the opposite of what the user pressed, and nothing anywhere
    /// would catch it - which is the argument for naming the two payloads rather than writing four
    /// bytes at each call site.
    /// </summary>
    [Fact]
    public void AcceptAndRejectAreOneMessageAndOneByte()
    {
        QueuedCtrlMessage accept = CtrlKeyboard.AcceptText();
        QueuedCtrlMessage reject = CtrlKeyboard.RejectText();

        Assert.Equal(accept.Type, reject.Type);
        Assert.Equal((ushort)CtrlMessage.KeyboardCloseReq, accept.Type);

        Assert.Equal<byte[]>([0, 0, 0, 0], accept.Payload);
        Assert.Equal<byte[]>([0, 0, 0, 1], reject.Payload);
    }

    /// <summary>goto-bed carries nothing at all.</summary>
    [Fact]
    public void GoingToBedCarriesNothing()
    {
        QueuedCtrlMessage bed = CtrlKeyboard.GotoBed();

        Assert.Equal((ushort)CtrlMessage.GotoBed, bed.Type);
        Assert.Empty(bed.Payload);
    }

    /// <summary>
    /// The header is 36 bytes and the text follows it.
    ///
    /// 36 rather than 12 because of two unknown blocks - eight bytes and sixteen - that are zeroed
    /// and never written. Their sizes are as load-bearing as the fields with names.
    /// </summary>
    [Fact]
    public void TheTextFollowsAThirtySixByteHeader()
    {
        byte[] payload = CtrlKeyboard.TextRequest("hello", 1);

        Assert.Equal(36 + 5, payload.Length);
        Assert.Equal("hello", CtrlKeyboard.TextIn(payload));
    }

    /// <summary>
    /// THE LENGTH IS WRITTEN TWICE, and both copies say the same thing.
    ///
    /// Nothing in the tree says why there are two. A port writing one and leaving the other zero
    /// sends a message the console reads differently, and finds out at the far end.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a longer piece of text than that")]
    public void TheLengthIsWrittenTwiceAndAgrees(string text)
    {
        byte[] payload = CtrlKeyboard.TextRequest(text, 7);

        (uint first, uint second) = CtrlKeyboard.LengthsIn(payload);

        Assert.Equal((uint)text.Length, first);
        Assert.Equal(first, second);
    }

    /// <summary>The 24 bytes between the two lengths are zero, because the whole payload is.</summary>
    [Fact]
    public void TheUnknownBytesBetweenTheLengthsAreZero()
    {
        byte[] payload = CtrlKeyboard.TextRequest("x", 1);

        Assert.All(payload[8..32], b => Assert.Equal(0, b));
    }

    /// <summary>
    /// THE COUNTER IS PRE-INCREMENTED, so a session's first text carries one and not zero.
    ///
    /// That number is how the console orders edits, so starting at zero would make the first edit
    /// look like the absence of one.
    /// </summary>
    [Fact]
    public void TheCounterStartsAtOne()
    {
        Assert.Equal(1u, CtrlKeyboard.CounterIn(CtrlKeyboard.TextRequest("first", 1)));
        Assert.Equal(2u, CtrlKeyboard.CounterIn(CtrlKeyboard.TextRequest("second", 2)));
    }

    /// <summary>Everything in the header is big-endian, like the message header in front of it.</summary>
    [Fact]
    public void TheHeaderIsBigEndian()
    {
        byte[] payload = CtrlKeyboard.TextRequest(new string('a', 0x0102), 0x0304);

        Assert.Equal<byte[]>([0x00, 0x00, 0x03, 0x04], payload[0..4]);
        Assert.Equal<byte[]>([0x00, 0x00, 0x01, 0x02], payload[4..8]);
        Assert.Equal<byte[]>([0x00, 0x00, 0x01, 0x02], payload[32..36]);
    }

    /// <summary>An empty edit is a valid request: 36 bytes and two zero lengths.</summary>
    [Fact]
    public void AnEmptyEditIsAValidRequest()
    {
        byte[] payload = CtrlKeyboard.TextRequest("", 1);

        Assert.Equal(36, payload.Length);
        Assert.Equal((0u, 0u), CtrlKeyboard.LengthsIn(payload));
        Assert.Equal("", CtrlKeyboard.TextIn(payload));
    }

    /// <summary>And it queues under the request type, not the close type.</summary>
    [Fact]
    public void SettingTextQueuesUnderTheRequestType()
    {
        Assert.Equal(
            (ushort)CtrlMessage.KeyboardTextChangeReq,
            CtrlKeyboard.SetText("hi", 1).Type);
    }

    /// <summary>And ctrl.c still declares the header and writes both lengths.</summary>
    [Fact]
    public void CtrlStillDeclaresTheRequest()
    {
        string? path = CtrlKeyboardSource.Locate();
        if (path is null)
            return;

        string source = File.ReadAllText(path);
        string? setText = ChiakiNg.Session.CFunction.BodyIn(path, "chiaki_ctrl_keyboard_set_text");

        Assert.NotNull(setText);

        Assert.True(
            CtrlKeyboardSource.TheRequestHeaderIsStill(source),
            "the keyboard text request header has changed shape");
        Assert.True(
            CtrlKeyboardSource.BothLengthsAreStillWritten(setText),
            "the text length is no longer written into both fields");
        Assert.True(
            CtrlKeyboardSource.TheCounterIsStillPreIncremented(setText),
            "the counter is no longer pre-incremented, so the first request's number has changed");
        Assert.True(
            CtrlKeyboardSource.AcceptAndRejectStillDifferByOneByte(source),
            "accept and reject no longer differ only in their last byte");
    }
}
