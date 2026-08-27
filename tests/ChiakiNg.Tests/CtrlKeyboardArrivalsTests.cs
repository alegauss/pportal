using System.Buffers.Binary;
using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP409, under PP294: the three keyboard messages the console sends.
///
/// PP351 has the four asks a screen makes. These are the answers, and the empty text that arrives
/// as no text at all.
/// </summary>
public class CtrlKeyboardArrivalsTests
{
    /// <summary>An open carrying text hands the screen that text.</summary>
    [Fact]
    public void AnOpenCarriesTheFieldsText()
    {
        KeyboardArrival arrival = CtrlKeyboardArrivals.ReceiveOpen(Open("Kojima"));

        Assert.Equal(KeyboardMessage.Open, arrival.Message);
        Assert.Equal(KeyboardVerdict.Raised, arrival.Verdict);
        Assert.Equal("Kojima", arrival.Text);
        Assert.False(CtrlKeyboardArrivals.IsIndistinguishableFromAClose(arrival));
    }

    /// <summary>And so does a text change, off a header eight bytes longer.</summary>
    [Fact]
    public void ATextChangeCarriesWhatTheFieldIsNow()
    {
        KeyboardArrival arrival = CtrlKeyboardArrivals.ReceiveTextChange(TextChange("Kojim"));

        Assert.Equal(KeyboardMessage.TextChange, arrival.Message);
        Assert.Equal(KeyboardVerdict.Raised, arrival.Verdict);
        Assert.Equal("Kojim", arrival.Text);
    }

    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. An empty text arrives as no text.
    ///
    /// Both text-carrying handlers allocate only for a non-empty text, so a keyboard opening on an
    /// empty field reaches the screen carrying the same nothing a remote close carries. That is one
    /// value away from the opposite instruction, and this is the assertion that says so out loud.
    /// </summary>
    [Fact]
    public void AnEmptyTextIsIndistinguishableFromAClose()
    {
        KeyboardArrival opened = CtrlKeyboardArrivals.ReceiveOpen(Open(""));
        KeyboardArrival changed = CtrlKeyboardArrivals.ReceiveTextChange(TextChange(""));
        KeyboardArrival closed = CtrlKeyboardArrivals.ReceiveRemoteClose([]);

        // All three raised, all three carrying null - not "".
        Assert.Equal(KeyboardVerdict.Raised, opened.Verdict);
        Assert.Equal(KeyboardVerdict.Raised, changed.Verdict);
        Assert.Equal(KeyboardVerdict.Raised, closed.Verdict);

        Assert.Null(opened.Text);
        Assert.Null(changed.Text);
        Assert.Null(closed.Text);

        Assert.True(CtrlKeyboardArrivals.IsIndistinguishableFromAClose(opened));
        Assert.True(CtrlKeyboardArrivals.IsIndistinguishableFromAClose(changed));

        // And the close itself is not, because a close carrying nothing is a close saying so.
        Assert.False(CtrlKeyboardArrivals.IsIndistinguishableFromAClose(closed));
    }

    /// <summary>A payload shorter than its header is not read at all.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(CtrlKeyboardArrivals.OpenHeaderSize - 1)]
    public void AShortOpenIsRefused(int size)
    {
        KeyboardArrival arrival = CtrlKeyboardArrivals.ReceiveOpen(new byte[size]);

        Assert.Equal(KeyboardVerdict.HeaderTooShort, arrival.Verdict);
        Assert.Null(arrival.Text);
        Assert.False(CtrlKeyboardArrivals.IsIndistinguishableFromAClose(arrival));
    }

    /// <summary>The same for a text change, whose header is the longer of the two.</summary>
    [Fact]
    public void AShortTextChangeIsRefused()
    {
        Assert.Equal(
            KeyboardVerdict.HeaderTooShort,
            CtrlKeyboardArrivals
                .ReceiveTextChange(new byte[CtrlKeyboardArrivals.TextResponseHeaderSize - 1])
                .Verdict);

        // An open-sized payload is short for a text change, which is the eight bytes between them.
        Assert.Equal(
            KeyboardVerdict.HeaderTooShort,
            CtrlKeyboardArrivals
                .ReceiveTextChange(new byte[CtrlKeyboardArrivals.OpenHeaderSize])
                .Verdict);
    }

    /// <summary>
    /// PP357'S CHECK, WHOLE. A message announcing more text than it carries is not read.
    ///
    /// This is the one that used to be an assert, and this tree builds Release with -DNDEBUG - so it
    /// was nothing in the shipped binary, and the header guard above it covers the header only.
    /// </summary>
    [Theory]
    [InlineData(6, 0)]
    [InlineData(6, 5)]
    [InlineData(6, 7)]
    [InlineData(0, 400)]
    [InlineData(6, int.MaxValue)]
    public void AnAnnouncementThePayloadDoesNotMatchIsRefused(int carried, long announced)
    {
        var payload = new byte[CtrlKeyboardArrivals.OpenHeaderSize + carried];
        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(CtrlKeyboardArrivals.OpenTextLengthOffset), (uint)announced);

        Assert.Equal(
            KeyboardVerdict.TextLengthMismatch,
            CtrlKeyboardArrivals.ReceiveOpen(payload).Verdict);
    }

    /// <summary>
    /// AND A LENGTH ABOVE WHAT AN INT HOLDS IS REFUSED RATHER THAN NARROWED.
    ///
    /// The announced length is a uint32 and the sum is compared as a long, so 0xFFFFFFFF is four
    /// gigabytes of text nothing carries. Signed-narrowing it would make the same value -1, and a
    /// header plus -1 is a length a payload can have.
    /// </summary>
    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(0x80000000u)]
    public void AnAnnouncementTooLargeForAnIntIsRefused(uint announced)
    {
        var payload = new byte[CtrlKeyboardArrivals.OpenHeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(CtrlKeyboardArrivals.OpenTextLengthOffset), announced);

        Assert.Equal(
            KeyboardVerdict.TextLengthMismatch,
            CtrlKeyboardArrivals.ReceiveOpen(payload).Verdict);
        Assert.Equal(announced, CtrlKeyboardArrivals.AnnouncedLength(
            payload, CtrlKeyboardArrivals.OpenTextLengthOffset));
    }

    /// <summary>
    /// THE SECOND LENGTH IS THE COMPARISON THE C NEVER MAKES.
    ///
    /// The response carries its length twice, like the request PP351 builds, and the handler reads
    /// the first alone. A disagreement is reported here and refuses nothing - which is what the C
    /// does with it, since it does not look.
    /// </summary>
    [Fact]
    public void TheTwoLengthsAreComparedWithoutRefusingADisagreement()
    {
        byte[] agreeing = TextChange("Kojim");
        Assert.True(CtrlKeyboardArrivals.TheTwoLengthsAgree(agreeing));

        byte[] disagreeing = TextChange("Kojim");
        BinaryPrimitives.WriteUInt32BigEndian(
            disagreeing.AsSpan(CtrlKeyboardArrivals.ResponseSecondLengthOffset), 99);

        Assert.False(CtrlKeyboardArrivals.TheTwoLengthsAgree(disagreeing));

        // And it is still raised, with the text the FIRST length describes.
        KeyboardArrival arrival = CtrlKeyboardArrivals.ReceiveTextChange(disagreeing);
        Assert.Equal(KeyboardVerdict.Raised, arrival.Verdict);
        Assert.Equal("Kojim", arrival.Text);

        // A payload too short to hold both fields does not claim they agree.
        Assert.False(CtrlKeyboardArrivals.TheTwoLengthsAgree(new byte[8]));
    }

    /// <summary>The remote close looks at nothing, so every payload is accepted.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    public void TheRemoteCloseAcceptsAnyPayload(int size)
    {
        KeyboardArrival arrival = CtrlKeyboardArrivals.ReceiveRemoteClose(new byte[size]);

        Assert.Equal(KeyboardMessage.RemoteClose, arrival.Message);
        Assert.Equal(KeyboardVerdict.Raised, arrival.Verdict);
        Assert.Null(arrival.Text);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheHandlersRulesAreStillTheQtCores()
    {
        string? path = CtrlKeyboardArrivalsSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.NotNull(CtrlKeyboardArrivalsSource.OpenBody(core));
        Assert.NotNull(CtrlKeyboardArrivalsSource.TextChangeBody(core));
        Assert.NotNull(CtrlKeyboardArrivalsSource.RemoteCloseBody(core));

        Assert.True(
            CtrlKeyboardArrivalsSource.TheHeaderStructsAreStillThese(core),
            "a header field moved, so the offsets this port reads are somewhere else");
        Assert.True(
            CtrlKeyboardArrivalsSource.TheExactSizeCheckStillStands(core),
            "PP357's exact-size check no longer follows the header check in both handlers");
        Assert.True(
            CtrlKeyboardArrivalsSource.AnEmptyTextStillCarriesNothing(core),
            "an empty text no longer reaches the event as NULL, so the port reproduces a fix");
        Assert.True(
            CtrlKeyboardArrivalsSource.TheSecondLengthIsStillNeverRead(core),
            "text_length2 is read now, and the comparison this port only reports is a guard");
        Assert.True(
            CtrlKeyboardArrivalsSource.TheRemoteCloseStillReadsNothing(core),
            "the remote close grew a guard, and this port accepts what it now refuses");
        Assert.True(
            CtrlKeyboardArrivalsSource.TheEventsAreStillThese(core),
            "one of the three raises a different event than the one it is named for");
    }

    /// <summary>PP272: and every reader answers no to an empty file.</summary>
    [Fact]
    public void EveryReaderAnswersNoToAnEmptyFile()
    {
        Assert.Null(CtrlKeyboardArrivalsSource.OpenBody(""));
        Assert.Null(CtrlKeyboardArrivalsSource.TextChangeBody(""));
        Assert.Null(CtrlKeyboardArrivalsSource.RemoteCloseBody(""));
        Assert.False(CtrlKeyboardArrivalsSource.TheHeaderStructsAreStillThese(""));
        Assert.False(CtrlKeyboardArrivalsSource.TheExactSizeCheckStillStands(""));
        Assert.False(CtrlKeyboardArrivalsSource.AnEmptyTextStillCarriesNothing(""));
        Assert.False(CtrlKeyboardArrivalsSource.TheSecondLengthIsStillNeverRead(""));
        Assert.False(CtrlKeyboardArrivalsSource.TheRemoteCloseStillReadsNothing(""));
        Assert.False(CtrlKeyboardArrivalsSource.TheEventsAreStillThese(""));
    }

    private static byte[] Open(string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        var payload = new byte[CtrlKeyboardArrivals.OpenHeaderSize + bytes.Length];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(CtrlKeyboardArrivals.OpenTextLengthOffset), (uint)bytes.Length);
        bytes.CopyTo(payload, CtrlKeyboardArrivals.OpenHeaderSize);

        return payload;
    }

    private static byte[] TextChange(string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        var payload = new byte[CtrlKeyboardArrivals.TextResponseHeaderSize + bytes.Length];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(CtrlKeyboardArrivals.ResponseFirstLengthOffset), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(CtrlKeyboardArrivals.ResponseSecondLengthOffset), (uint)bytes.Length);
        bytes.CopyTo(payload, CtrlKeyboardArrivals.TextResponseHeaderSize);

        return payload;
    }
}
