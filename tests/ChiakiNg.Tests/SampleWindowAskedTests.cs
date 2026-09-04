using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP701: a length is read off a command line without asking which run flag it accompanies.
///
/// The parse used to be gated on the two capture flags that existed when it was written. Two run
/// flags added since - --measure-decoder, then --show-stream - read the resulting bounds without
/// joining that condition, so both silently took the default: a session asked to hold for 120
/// seconds held for 8.8 and wrote a row saying 8801ms.
///
/// That is the exact failure the parse's own remarks warn about - "a run asked for sixty seconds
/// and silently given five is a measurement about the wrong thing, and the file it leaves says
/// nothing about which length it holds" - and it was reintroduced by adding a consumer rather than
/// by changing the rule.
/// </summary>
public class SampleWindowAskedTests
{
    /// <summary>THE DEFECT: the flag that showed a stream took the default and said nothing.</summary>
    [Fact]
    public void AStreamAsksForALengthLikeEverythingElse()
    {
        SampleWindow.Asked answer = SampleWindow.From(
            ["--show-stream", "vulkan", "--capture-seconds", "120"], out SampleBounds bounds);

        Assert.Equal(SampleWindow.Asked.Parsed, answer);
        Assert.Equal(SampleWindow.For(120).Hold, bounds.Hold);
    }

    /// <summary>And so does the decoder measurement, which had the same gap.</summary>
    [Fact]
    public void SoDoesTheDecoderMeasurement()
    {
        SampleWindow.Asked answer = SampleWindow.From(
            ["--measure-decoder", "cuda", "--capture-seconds", "60"], out SampleBounds bounds);

        Assert.Equal(SampleWindow.Asked.Parsed, answer);
        Assert.Equal(SampleWindow.For(60).Hold, bounds.Hold);
    }

    /// <summary>The two captures it always worked for still work.</summary>
    [Theory]
    [InlineData("--capture-exchange")]
    [InlineData("--capture-datagrams")]
    public void TheCapturesAreUnchanged(string flag)
    {
        SampleWindow.Asked answer = SampleWindow.From(
            [flag, "out.bin", "--capture-seconds", "30"], out SampleBounds bounds);

        Assert.Equal(SampleWindow.Asked.Parsed, answer);
        Assert.Equal(SampleWindow.For(30).Hold, bounds.Hold);
    }

    /// <summary>No flag is the default and no complaint, which is what an absent length means.</summary>
    [Fact]
    public void NoFlagIsTheDefault()
    {
        Assert.Equal(
            SampleWindow.Asked.Absent,
            SampleWindow.From(["--show-stream", "vulkan"], out SampleBounds bounds));

        Assert.Equal(SampleWindow.Default.Hold, bounds.Hold);
    }

    /// <summary>
    /// A length that cannot be read is a refusal WHATEVER it accompanies.
    ///
    /// Including a command line with no run flag at all: refusing a malformed value there costs
    /// nothing, and it is the reason there is no list of flags to be missing from.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("121")]
    [InlineData("abc")]
    [InlineData("")]
    public void ALengthThatCannotBeReadIsRefused(string text)
    {
        Assert.Equal(
            SampleWindow.Asked.Malformed,
            SampleWindow.From(["--show-stream", "vulkan", "--capture-seconds", text], out _));

        Assert.Equal(SampleWindow.Asked.Malformed, SampleWindow.From(["--capture-seconds", text], out _));
    }
}
