using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP8: which motor a rumble reaches, and in which units - the last of the input path.
///
/// One 16-bit strength comes out of the haptics fold and goes two ways: through the DualSense
/// effects report, whose rumble fields are bytes, or through SDL's own rumble, which takes the
/// whole value and a duration. Two decisions, neither of which fails loudly.
/// </summary>
public class RumbleRoutingTests
{
    /// <summary>
    /// A shift, not a scale. Dividing by 257 maps 0..65535 onto 0..255 more evenly and differs by
    /// one across most of the range - nothing for a motor, except that the Qt client shifts, and
    /// two clients differing by one on every haptic frame is unmeasurable and unarguable.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 0)]        // a shift loses the low byte entirely
    [InlineData(256, 1)]
    [InlineData(32768, 128)]
    [InlineData(65535, 255)]
    public void TheDualSenseAmplitudeIsTheHighByte(int strength, int expected)
        => Assert.Equal(expected, RumbleRouting.ToDualSenseAmplitude((ushort)strength));

    /// <summary>
    /// The two arithmetics really do differ, so the choice above is a choice. 65535/257 is 255 and
    /// so is the shift, but 511 divides to 1 and shifts to 1 while 256 divides to 0 and shifts to
    /// 1 - which is the range where a rumble is quiet enough for one step to be the whole signal.
    /// </summary>
    [Fact]
    public void AScaleWouldNotAgreeWithTheShift()
    {
        int disagreements = 0;
        for (int v = 0; v <= ushort.MaxValue; v++)
        {
            if (RumbleRouting.ToDualSenseAmplitude((ushort)v) != v / 257)
                disagreements++;
        }

        Assert.True(disagreements > 0, "a scale and a shift agreed everywhere, so the choice is moot");
    }

    /// <summary>
    /// SDL's rumble stops on its own when the duration expires. That is not a formality: a
    /// session that stopped re-sending has the pad fall silent rather than keep buzzing.
    /// </summary>
    [Fact]
    public void SdlRumbleCarriesADurationSoItStopsOnItsOwn()
        => Assert.Equal(5000u, RumbleRouting.SdlRumbleDurationMs);

    /// <summary>Both rules are still the Qt client's own.</summary>
    [Fact]
    public void TheRoutingRulesAreStillTheQtClients()
    {
        string? file = RumbleRoutingSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(RumbleRoutingSource.DualSenseAmplitudeIsShifted(text), "shifted by eight");
        Assert.True(
            RumbleRoutingSource.OtherPadsGetTheWholeValue(text, RumbleRouting.SdlRumbleDurationMs),
            "whole value and the same duration");
    }
}
