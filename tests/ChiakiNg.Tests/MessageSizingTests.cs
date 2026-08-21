using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP250: the sizing behind the writer PP191 and PP33 already ported.
///
/// <see cref="TheSizingIsSafeOnlyBecauseTheWrongConstantIsLarger"/> carries the task, and it
/// MEASURES both format strings off the source rather than trusting a transcription.
/// </summary>
public class MessageSizingTests
{
    private static string? Core()
    {
        string? file = MessageSizingSource.Locate();
        return file is null ? null : File.ReadAllText(file);
    }

    /// <summary>
    /// THE SIZING. The buffer is measured from one format and written with another, and the only
    /// thing making that safe is which of the two is bigger - a fact nothing in the core states.
    /// </summary>
    [Fact]
    public void TheSizingIsSafeOnlyBecauseTheWrongConstantIsLarger()
    {
        string? core = Core();
        if (core is null)
            return;

        int sizing = MessageSizingSource.LengthOfEnvelopeFormat(core);
        int written = MessageSizingSource.LengthOfMessageFormat(core);

        Assert.True(sizing > 0, "the envelope format could not be measured");
        Assert.True(written > 0, "the message format could not be measured");

        // The relationship the arithmetic rests on.
        Assert.True(
            sizing > written,
            $"the envelope format is {sizing} bytes and the message format {written}; "
            + "the sizing only works while the first exceeds the second");

        // And with the doubling and the ack's three bytes there is real slack - for now.
        Assert.True(MessageSizing.Fits(
            sizing, written, MessageSizing.AckConnectionRequestLength, substituted: 64));
    }

    /// <summary>Both serializers size it the same wrong way, so it is a habit and not a slip.</summary>
    [Fact]
    public void BothSerializersSizeItThatWay()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.True(MessageSizingSource.TheSizingStillUsesTheOtherFormat(core));
        Assert.Equal(2, MessageSizingSource.HowManySerializersSizeItThatWay(core));
    }

    /// <summary>
    /// THE LENGTH. What is handed back is what would have been written, so a message that did not
    /// fit reports a length longer than itself.
    /// </summary>
    [Fact]
    public void ATruncatedMessageReportsALengthLongerThanItself()
    {
        // It fitted: the two agree apart from the terminator.
        Assert.False(MessageSizing.LengthOverstates(wouldHaveWritten: 50, buffer: 200));

        // It did not: the report is the full length, the string is one short of the buffer.
        Assert.True(MessageSizing.LengthOverstates(wouldHaveWritten: 500, buffer: 200));
        Assert.Equal(500, MessageSizing.ReportedLength(500, 200));
        Assert.Equal(199, MessageSizing.ActualLength(500, 200));
    }

    /// <summary>
    /// And the caller sizes the envelope's stack array from that number - too large rather than too
    /// small, which is the safe direction and still not what it claims.
    /// </summary>
    [Fact]
    public void TheEnvelopeIsSizedFromTheOverstatedLength()
    {
        const int sizing = 200;

        int fromReported = MessageSizing.EnvelopeBufferFor(
            sizing, MessageSizing.ReportedLength(500, 200));
        int fromActual = MessageSizing.EnvelopeBufferFor(
            sizing, MessageSizing.ActualLength(500, 200));

        Assert.True(fromReported > fromActual);
    }

    /// <summary>A failed serializer leaves a null payload, and nothing reads the failure.</summary>
    [Fact]
    public void AFailedSerializerLeavesANullPayload()
    {
        (string? payload, int length) = MessageSizing.AfterAFailure();

        Assert.Null(payload);
        Assert.Equal(0, length);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheSizingIsStillTheCores()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.True(
            MessageSizingSource.TheReportedLengthIsStillWhatWouldHaveBeenWritten(core),
            "the reported length is still snprintf's return, unclamped");
        Assert.True(
            MessageSizingSource.TheAckRequestIsStillThreeBytes(core),
            "the ack's connection request is still three bytes");
        Assert.True(
            MessageSizingSource.TheCallerStillDiscardsTheResult(core),
            "the caller still discards both serializers' results");
        Assert.True(
            MessageSizingSource.TheReasonIsStillStated(core),
            "and the core still explains why a JSON library cannot be used");
    }
}
