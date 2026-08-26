using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP399, under PP340: a clamp that permitted four times the buffer it was clamping against.
///
/// bytes_to_hex tested <c>len &gt; max_len * 2</c> where max_len is the size of the destination.
/// Two characters go out per input byte and the last write leaves a terminator, so what fits is
/// <c>(max_len - 1) / 2</c>. The test allowed four times that and read as a bounds check.
///
/// NOTHING OVERFLOWED, which is the whole reason it survived. All three callers pass a buffer of
/// exactly 2n+1, so the clamp never fired and never had to be right.
/// </summary>
public class HexEncodingBoundTests
{
    /// <summary>THE PROPERTY. What fits, encoded, never exceeds the room it was measured against.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(4096)]
    public void WhatFitsAlwaysFits(int destination)
    {
        int fits = HexEncodingBound.Fits(destination);

        Assert.True(
            HexEncodingBound.Writes(fits) <= destination,
            $"{fits} bytes encode to {HexEncodingBound.Writes(fits)} in a buffer of {destination}");
    }

    /// <summary>
    /// And one more byte does not, so the bound is the largest that fits rather than a safe
    /// under-estimate.
    /// </summary>
    [Theory]
    [InlineData(65)]
    [InlineData(129)]
    public void OneMoreThanItSaysWouldNotFit(int destination)
    {
        int oneMore = HexEncodingBound.Fits(destination) + 1;

        Assert.True(HexEncodingBound.Writes(oneMore) > destination);
    }

    /// <summary>
    /// THE DEFECT. What the old test permitted, against the same buffer.
    ///
    /// Four times, and the overflow is stated as bytes rather than as a factor because that is what
    /// a caller would have written past the end.
    /// </summary>
    [Fact]
    public void TheOldTestPermittedFourTimesTheBuffer()
    {
        const int Destination = 65;

        int permitted = HexEncodingBound.PermittedAsItWas(Destination);

        Assert.Equal(130, permitted);
        Assert.Equal(4 * HexEncodingBound.Fits(Destination) + 2, permitted);

        // What that would have written into 65 bytes.
        Assert.Equal(261, HexEncodingBound.Writes(permitted));
    }

    /// <summary>
    /// The three callers were always right, which is why this never showed.
    ///
    /// Each passes exactly 2n+1, so the clamp had nothing to do - and a rule that only checked the
    /// callers would have found nothing wrong.
    /// </summary>
    [Theory]
    [InlineData(32, 65)]
    public void EveryCallerAlreadyPassesEnough(int inputBytes, int destination)
    {
        Assert.Equal(destination, HexEncodingBound.Writes(inputBytes));
        Assert.True(inputBytes <= HexEncodingBound.Fits(destination));
    }

    /// <summary>A zero-sized destination holds nothing, and says so rather than wrapping.</summary>
    [Fact]
    public void AZeroDestinationHoldsNothing()
    {
        Assert.Equal(0, HexEncodingBound.Fits(0));
        Assert.Equal(0, HexEncodingBound.Writes(0));
    }

    /// <summary>And the C clamps against the room it has.</summary>
    [Fact]
    public void TheCClampsAgainstTheRoomItHas()
    {
        if (HexEncodingBound.Locate() is not { } path)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            HexEncodingBound.TheClampIsAgainstTheRoomItHas(core),
            "bytes_to_hex no longer clamps against (max_len - 1) / 2");
    }

    /// <summary>
    /// AND ANSWERS A ZERO DESTINATION FIRST, which is the trap in correcting the arithmetic.
    ///
    /// max_len is a size_t. <c>(0 - 1) / 2</c> is half of SIZE_MAX, so a clamp written correctly
    /// but without regard to the type would permit everything on the one input the old one
    /// happened to handle.
    /// </summary>
    [Fact]
    public void TheCAnswersAZeroDestinationBeforeSubtracting()
    {
        if (HexEncodingBound.Locate() is not { } path)
            return;

        Assert.True(
            HexEncodingBound.AZeroDestinationLeavesFirst(File.ReadAllText(path)),
            "bytes_to_hex subtracts from max_len without answering zero first");
    }

    /// <summary>The readers see the shape they were written for, and read the file (PP272).</summary>
    [Fact]
    public void TheReadersSeeTheOldClamp()
    {
        const string AsItWas = """
            static void bytes_to_hex(const uint8_t* bytes, size_t len, char* hex_str, size_t max_len) {
                if (len > max_len * 2) {
                    len = max_len * 2;
                }
                for (size_t i = 0; i < len; i++) {
                    snprintf(hex_str + i * 2, 3, "%02x", bytes[i]);
                }
            }
            """;

        Assert.False(HexEncodingBound.TheClampIsAgainstTheRoomItHas(AsItWas));
        Assert.False(HexEncodingBound.AZeroDestinationLeavesFirst(AsItWas));

        Assert.False(HexEncodingBound.TheClampIsAgainstTheRoomItHas(""));
        Assert.False(HexEncodingBound.AZeroDestinationLeavesFirst(""));
    }
}
