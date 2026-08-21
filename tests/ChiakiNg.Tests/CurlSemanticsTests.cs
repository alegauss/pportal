using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the curl inventory, and the three behaviours a port would get wrong for free.
///
/// 420 call sites is the number that stops "cheapest task in the block" being read as "small". It
/// is not the number a translation is planned from: that one is ten, because ten distinct options
/// are what those sites set. This holds the ten, and asserts the three that HttpClient does not do
/// by itself - decided once here rather than rediscovered at each call site.
/// </summary>
public class CurlSemanticsTests
{
    /// <summary>
    /// Curl fails a transfer at 400 and not before, and hands back no body when it does. The
    /// twelve sites that set FAILONERROR are all written expecting that; HttpClient returns the
    /// response instead, so the same 404 is an error in one client and a success in the other.
    /// </summary>
    [Theory]
    [InlineData(200, false)]
    [InlineData(204, false)]
    [InlineData(301, false)]
    [InlineData(399, false)]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(500, true)]
    public void CurlFailsTheTransferFromFourHundredUp(int status, bool fails)
        => Assert.Equal(fails, CurlSemantics.WouldFailTransfer(status));

    /// <summary>
    /// CONNECT_ONLY is not a boolean, and the core uses the value a reader is most likely to read
    /// as one. Two is curl's WebSocket mode, so that call site's equivalent is ClientWebSocket and
    /// not anything on HttpClient.
    /// </summary>
    [Fact]
    public void ConnectOnlyTwoIsAWebSocketAndNotABoolean()
    {
        Assert.Equal(2, CurlSemantics.ConnectOnlyWebSocket);

        Assert.Contains(
            "WebSocket",
            CurlSemantics.ConnectOnlyMeaning(CurlSemantics.ConnectOnlyWebSocket),
            StringComparison.Ordinal);

        // One is a different thing entirely, which is the confusion the value invites.
        Assert.DoesNotContain(
            "WebSocket", CurlSemantics.ConnectOnlyMeaning(1), StringComparison.Ordinal);
    }

    /// <summary>
    /// The inventory itself: ten options and no more. An eleventh appearing in the core is a
    /// behaviour nobody has decided about, and it should surface here rather than halfway through
    /// the translation.
    /// </summary>
    [Fact]
    public void TheCoreSetsTenDistinctOptions()
    {
        string? core = CurlSemanticsSource.Locate();
        if (core is null)
            return;

        IReadOnlyDictionary<string, int> options = CurlSemanticsSource.OptionsUsed(core);

        Assert.Equal(10, options.Count);

        // The ordinary seven, which HttpClient does without being asked.
        foreach (string ordinary in new[]
        {
            "CURLOPT_URL", "CURLOPT_TIMEOUT", "CURLOPT_WRITEFUNCTION", "CURLOPT_WRITEDATA",
            "CURLOPT_HTTPHEADER", "CURLOPT_POSTFIELDS", "CURLOPT_CUSTOMREQUEST",
        })
        {
            Assert.True(options.ContainsKey(ordinary), $"{ordinary} is no longer set by the core");
        }
    }

    /// <summary>
    /// And the three that are the actual work, still there and still used as often as the plan
    /// assumes. A count that halved would mean somebody already did part of this.
    /// </summary>
    [Fact]
    public void TheThreeWithoutAPlainEquivalentAreStillThere()
    {
        string? core = CurlSemanticsSource.Locate();
        if (core is null)
            return;

        IReadOnlyDictionary<string, int> options = CurlSemanticsSource.OptionsUsed(core);

        foreach (string hard in CurlSemanticsSource.WithoutAPlainEquivalent)
            Assert.True(options.ContainsKey(hard), $"{hard} is no longer set by the core");

        Assert.Equal(12, options["CURLOPT_FAILONERROR"]);
        Assert.Equal(9, options["CURLOPT_SHARE"]);

        // Exactly one WebSocket, which is what makes it a single decision rather than a pattern.
        Assert.Equal(1, options["CURLOPT_CONNECT_ONLY"]);
    }

    /// <summary>
    /// The scan can find something, so the counts above are not passing over an empty directory.
    /// </summary>
    [Fact]
    public void TheScanReadsTheCore()
    {
        string? core = CurlSemanticsSource.Locate();
        if (core is null)
            return;

        IReadOnlyDictionary<string, int> options = CurlSemanticsSource.OptionsUsed(core);

        Assert.NotEmpty(options);
        Assert.True(options.Values.Sum() > 50, "the scan found suspiciously few option sites");
    }
}
