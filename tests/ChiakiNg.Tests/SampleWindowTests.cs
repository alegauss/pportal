using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP526, under PP27: one asked-for length settling the window, the count and the hold.
///
/// What is worth holding is not the three numbers - a rate is a measurement and will move - but the
/// three RELATIONS between them, each of which is a way the feature silently does nothing.
/// </summary>
public class SampleWindowTests
{
    /// <summary>
    /// THE HOLD NEVER ENDS BEFORE THE WINDOW, which is the whole reason these are one type.
    ///
    /// A sixty-second window under a twelve-second hold captures twelve seconds and reports a
    /// window it never reached - a flag that appears to work, and a file that does not say so.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(SampleWindow.MaximumSeconds)]
    public void TheHoldCoversTheWindowAtEveryLength(int seconds)
    {
        SampleBounds bounds = SampleWindow.For(seconds);

        Assert.True(bounds.Hold.TotalMicroseconds >= bounds.WindowMicroseconds);
    }

    /// <summary>
    /// And it covers the opening as well, which the window's own origin cannot.
    ///
    /// PP510 measures arrivals from the FIRST datagram; the hold is measured from the session's
    /// start. Holding for exactly the window would therefore always be short by PP521's opening,
    /// and every sample would end an opening early without anything reporting it.
    /// </summary>
    [Fact]
    public void TheHoldAlsoCoversTheOpening()
    {
        SampleBounds bounds = SampleWindow.For(SampleWindow.MaximumSeconds);

        Assert.True(
            bounds.Hold.TotalMicroseconds >= bounds.WindowMicroseconds + SampleWindow.Opening.TotalMicroseconds);
    }

    /// <summary>
    /// THE DEFAULT LENGTH LEAVES PP297'S EXCHANGE HOLD EXACTLY WHERE IT WAS.
    ///
    /// The hold has two floors and only one of them is about datagrams: the exchange run wants long
    /// enough for the control conversation, which is twelve seconds and has nothing to do with a
    /// window. Deriving the hold from the window alone would have shortened a capture this task was
    /// not about.
    /// </summary>
    [Fact]
    public void TheDefaultStillHoldsWhatTheExchangeNeeds()
    {
        Assert.Equal(SampleWindow.ExchangeHold, ExchangeCapture.Hold);
        Assert.Equal(SampleWindow.ExchangeHold, SampleWindow.Default.Hold);
    }

    /// <summary>
    /// THE COUNT GROWS WITH THE WINDOW, which is the other way a longer sample does nothing.
    ///
    /// PP525's capture closed on the count at 2486ms of its five-second window. A count left at a
    /// constant would close a sixty-second sample in the same two and a half seconds - so the
    /// count is derived, and a longer window is a longer file rather than the same one.
    /// </summary>
    [Fact]
    public void TheCountGrowsWithTheWindow()
    {
        SampleBounds five = SampleWindow.For(5);
        SampleBounds sixty = SampleWindow.For(60);

        Assert.Equal(12 * five.WindowMicroseconds, sixty.WindowMicroseconds);
        Assert.Equal(12 * five.Limit, sixty.Limit);
    }

    /// <summary>The capture built from bounds carries both of them, and not one.</summary>
    [Fact]
    public void ACaptureBuiltFromBoundsCarriesBoth()
    {
        SampleBounds bounds = SampleWindow.For(30);

        var capture = new TakionTimingCapture(bounds);

        Assert.Equal(bounds.Limit, capture.Limit);
        Assert.Equal(bounds.WindowMicroseconds, capture.WindowMicroseconds);
    }

    /// <summary>An absent flag is the default length, and both bounds are the class's own.</summary>
    [Fact]
    public void AnAbsentFlagIsTheDefaultLength()
    {
        SampleBounds bounds = Assert.NotNull(SampleWindow.TryParse(null));

        Assert.Equal(TakionTimingCapture.DefaultLimit, bounds.Limit);
        Assert.Equal(TakionTimingCapture.DefaultWindowMicroseconds, bounds.WindowMicroseconds);
    }

    /// <summary>
    /// A LENGTH THAT IS NOT ONE IS REFUSED, NOT ROUNDED BACK TO THE DEFAULT.
    ///
    /// Every one of these is something a person types. Falling back to five seconds would run the
    /// session, write the file and exit zero, and the only evidence of the mistake would be a
    /// number nobody had a reason to look at.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("5.5")]
    [InlineData("5s")]
    [InlineData("")]
    [InlineData(" 5")]
    public void ALengthThatIsNotOneIsRefused(string text)
        => Assert.Null(SampleWindow.TryParse(text));

    /// <summary>And so is one past the bound, which exists so a typo cannot hold a console open.</summary>
    [Fact]
    public void PastTheMaximumIsRefused()
    {
        Assert.NotNull(SampleWindow.TryParse(
            SampleWindow.MaximumSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Null(SampleWindow.TryParse(
            (SampleWindow.MaximumSeconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleWindow.For(SampleWindow.MaximumSeconds + 1));
    }
}
