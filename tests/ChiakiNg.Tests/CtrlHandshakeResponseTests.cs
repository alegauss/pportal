using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP360, under PP294: the answer to the ctrl request, and the counter it spends.
///
/// PP297's capture cannot judge any of it - the tap's first ctrl entry is a LOGIN, and all of this
/// happened before that.
/// </summary>
public class CtrlHandshakeResponseTests
{
    /// <summary>
    /// THE REMOTE COUNTER STARTS AT ONE OR AT ZERO, and which depends on what the console sent.
    ///
    /// PP356 is the same trap on the local counter, without the branch. A port picking either
    /// unconditionally is wrong against half the consoles it meets, and wrong silently.
    /// </summary>
    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void TheRemoteCounterDependsOnWhetherTheServerTypeWasDecrypted(bool decrypted, uint at)
    {
        Assert.Equal(at, CtrlHandshakeResponse.RemoteCounterAfterResponse(decrypted));
    }

    /// <summary>A server type of any length but sixteen is unusable, and that is not an error.</summary>
    [Theory]
    [InlineData(16, true)]
    [InlineData(15, false)]
    [InlineData(17, false)]
    [InlineData(0, false)]
    public void TheServerTypeIsUsableOnlyAtSixteenBytes(int decoded, bool usable)
    {
        Assert.Equal(usable, CtrlHandshakeResponse.ServerTypeIsUsable(decoded));
    }

    /// <summary>Success is the HTTP code and nothing else.</summary>
    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    [InlineData(403, false)]
    [InlineData(500, false)]
    public void SuccessIsTheCodeAlone(int code, bool success)
    {
        Assert.Equal(success, CtrlHandshakeResponse.IsSuccess(code));
    }

    /// <summary>The request is tried twice at most, and never a third time.</summary>
    [Fact]
    public void TheRequestIsTriedTwiceAtMost()
    {
        Assert.True(CtrlHandshakeResponse.RetriesAfter(1));
        Assert.False(CtrlHandshakeResponse.RetriesAfter(2));
        Assert.Equal(2, CtrlHandshakeResponse.Attempts);
    }

    /// <summary>And a timed-out TCP socket is rebuilt rather than reused.</summary>
    [Fact]
    public void ATimedOutTcpSocketIsRebuilt()
    {
        Assert.True(CtrlHandshakeResponse.ReconnectsBeforeRetrying(overRudp: false));
        Assert.False(CtrlHandshakeResponse.ReconnectsBeforeRetrying(overRudp: true));
    }

    /// <summary>A regular PS4 asked for 1080p is dropped to 720p.</summary>
    [Fact]
    public void ARegularPs4CannotDoTenEighty()
    {
        ProfileAfterServerType after = CtrlHandshakeResponse.Downgrade(
            CtrlServerType.Ps4, ChiakiVideoResolution.P1080, ChiakiCodec.H264, autoDowngrade: true);

        Assert.Equal(ChiakiVideoResolution.P720, after.Resolution);
        Assert.True(after.Downgraded);
    }

    /// <summary>But not where the session refused a downgrade.</summary>
    [Fact]
    public void TheResolutionIsKeptWhereADowngradeWasRefused()
    {
        ProfileAfterServerType after = CtrlHandshakeResponse.Downgrade(
            CtrlServerType.Ps4, ChiakiVideoResolution.P1080, ChiakiCodec.H264, autoDowngrade: false);

        Assert.Equal(ChiakiVideoResolution.P1080, after.Resolution);
        Assert.False(after.Downgraded);
    }

    /// <summary>
    /// THE CODEC DOWNGRADE IS NOT GATED ON auto-downgrade, which is the asymmetry.
    ///
    /// A session that refused a resolution downgrade still gets a codec one. Both branches are four
    /// lines apart and only one names the flag.
    /// </summary>
    [Theory]
    [InlineData(CtrlServerType.Ps4)]
    [InlineData(CtrlServerType.Ps4Pro)]
    public void TheCodecIsForcedEvenWhereADowngradeWasRefused(CtrlServerType serverType)
    {
        ProfileAfterServerType after = CtrlHandshakeResponse.Downgrade(
            serverType, ChiakiVideoResolution.P720, ChiakiCodec.H265, autoDowngrade: false);

        Assert.Equal(ChiakiCodec.H264, after.Codec);
        Assert.True(after.Downgraded);
    }

    /// <summary>A PS4 Pro keeps 1080p - only the regular one loses it.</summary>
    [Fact]
    public void APs4ProKeepsTenEighty()
    {
        ProfileAfterServerType after = CtrlHandshakeResponse.Downgrade(
            CtrlServerType.Ps4Pro, ChiakiVideoResolution.P1080, ChiakiCodec.H264, autoDowngrade: true);

        Assert.Equal(ChiakiVideoResolution.P1080, after.Resolution);
        Assert.False(after.Downgraded);
    }

    /// <summary>And a PS5 keeps everything.</summary>
    [Theory]
    [InlineData(ChiakiCodec.H264)]
    [InlineData(ChiakiCodec.H265)]
    [InlineData(ChiakiCodec.H265Hdr)]
    public void APs5KeepsWhatWasAskedFor(ChiakiCodec codec)
    {
        ProfileAfterServerType after = CtrlHandshakeResponse.Downgrade(
            CtrlServerType.Ps5, ChiakiVideoResolution.P1080, codec, autoDowngrade: true);

        Assert.Equal(ChiakiVideoResolution.P1080, after.Resolution);
        Assert.Equal(codec, after.Codec);
        Assert.False(after.Downgraded);
    }

    /// <summary>And ctrl.c still answers the way this reproduces.</summary>
    [Fact]
    public void CtrlStillDeclaresTheResponse()
    {
        string? path = CtrlHandshakeResponseSource.Locate();
        if (path is null)
            return;

        string? connect = CtrlHandshakeResponseSource.ConnectBody(path);
        string? parser = CtrlHandshakeResponseSource.ParserBody(path);

        Assert.NotNull(connect);
        Assert.NotNull(parser);

        Assert.True(
            CtrlHandshakeResponseSource.TheRetryIsStillOneShot(connect),
            "the ctrl request retry is no longer one-shot");
        Assert.True(
            CtrlHandshakeResponseSource.TheSocketIsStillRebuiltBeforeTheRetry(connect),
            "a timed-out TCP socket is now reused for the retry");
        Assert.True(
            CtrlHandshakeResponseSource.TheRemoteCounterIsStillSpentConditionally(connect),
            "the remote counter is no longer spent only where the server type is decrypted");
        Assert.True(
            CtrlHandshakeResponseSource.TheCodecDowngradeIsStillUngated(connect),
            "the codec downgrade is now gated on auto-downgrade, which changes what a PS4 is asked for");
        Assert.True(
            CtrlHandshakeResponseSource.SuccessIsStillTheCodeAlone(parser),
            "success is no longer decided by the HTTP code alone");
        Assert.True(
            CtrlHandshakeResponseSource.TheServerTypeIsStillLengthChecked(parser),
            "the server type is no longer refused at the wrong length");
    }
}
