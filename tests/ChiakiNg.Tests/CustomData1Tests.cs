using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the sixteen bytes behind base64 twice, and the band around them.
/// </summary>
public class CustomData1Tests
{
    /// <summary>Wraps payload bytes the way the console does: base64, then base64 again.</summary>
    private static string Wrap(byte[] payload)
    {
        string inner = Convert.ToBase64String(payload);
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(inner));
    }

    private static byte[] Bytes(int count)
        => [.. Enumerable.Range(0, count).Select(i => (byte)(i + 1))];

    /// <summary>Sixteen bytes in, the same sixteen out.</summary>
    [Fact]
    public void SixteenBytesSurviveBothRounds()
    {
        byte[] payload = Bytes(16);

        byte[]? decoded = CustomData1.Decode(Wrap(payload), out CustomData1.Result result, out int extras);

        Assert.Equal(CustomData1.Result.Ok, result);
        Assert.Equal(0, extras);
        Assert.Equal(payload, decoded);
    }

    /// <summary>
    /// THE ONE A PORT STOPS AT. After ONE decode the value is still base64 text - printable, of a
    /// plausible length, and not the payload. A port that stopped there would have no reason to
    /// think anything went wrong.
    /// </summary>
    [Fact]
    public void OneRoundLeavesTextRatherThanBytes()
    {
        byte[] payload = Bytes(16);
        string wrapped = Wrap(payload);

        byte[] afterOne = Convert.FromBase64String(wrapped);

        // Twenty-four bytes, every one of them a base64 character - which is why it looks like a
        // payload and is not one.
        Assert.Equal(24, afterOne.Length);
        Assert.All(afterOne, b => Assert.InRange(b, (byte)'+', (byte)'z'));
        Assert.NotEqual(payload, afterOne[..16]);

        // And the real payload only appears after the second.
        Assert.Equal(payload, Convert.FromBase64String(Encoding.ASCII.GetString(afterOne)));
    }

    /// <summary>
    /// Extras are ignored rather than refused: the console appends bytes this client has no use
    /// for, and a port demanding exactly sixteen would reject a session the Qt client accepts.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UpToFourExtraBytesAreThrownAway(int extra)
    {
        byte[] payload = Bytes(16 + extra);

        byte[]? decoded = CustomData1.Decode(Wrap(payload), out CustomData1.Result result, out int extras);

        Assert.Equal(CustomData1.Result.Ok, result);
        Assert.Equal(extra, extras);
        Assert.Equal(16, decoded!.Length);
        Assert.Equal(payload[..16], decoded);
    }

    /// <summary>A fifth extra byte is one too many, which is where the band ends.</summary>
    [Fact]
    public void FiveExtraBytesIsTooLong()
    {
        byte[]? decoded = CustomData1.Decode(Wrap(Bytes(21)), out CustomData1.Result result, out _);

        Assert.Null(decoded);
        Assert.Equal(CustomData1.Result.TooLong, result);
    }

    /// <summary>And fewer than sixteen is refused, because sixteen is what the session needs.</summary>
    [Fact]
    public void FifteenBytesIsTooShort()
    {
        byte[]? decoded = CustomData1.Decode(Wrap(Bytes(15)), out CustomData1.Result result, out _);

        Assert.Null(decoded);
        Assert.Equal(CustomData1.Result.TooShort, result);
    }

    /// <summary>Rubbish at either round is refused as rubbish rather than throwing.</summary>
    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("")]
    public void SomethingThatIsNotBase64IsRefused(string text)
    {
        byte[]? decoded = CustomData1.Decode(text, out CustomData1.Result result, out _);

        Assert.Null(decoded);
        Assert.NotEqual(CustomData1.Result.Ok, result);
    }

    /// <summary>
    /// And valid base64 whose CONTENT is not base64 fails at the second round, not the first -
    /// which is the failure a single-decode port would never reach.
    /// </summary>
    [Fact]
    public void ValidOuterAndRubbishInnerFailsAtTheSecondRound()
    {
        string outer = Convert.ToBase64String(Encoding.ASCII.GetBytes("!!!not base64!!!"));

        byte[]? decoded = CustomData1.Decode(outer, out CustomData1.Result result, out _);

        Assert.Null(decoded);
        Assert.Equal(CustomData1.Result.NotBase64, result);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheFieldsRulesAreStillTheQtCores()
    {
        string? path = CustomData1Source.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(CustomData1Source.ItIsStillDecodedTwice(core), "base64 twice");
        Assert.True(CustomData1Source.TheBandIsStillFourBytes(core), "four extra bytes");
        Assert.True(CustomData1Source.TheSessionStillTakesSixteen(core), "sixteen taken");
        Assert.True(CustomData1Source.ExtrasAreStillIgnored(core), "extras ignored");
    }
}
