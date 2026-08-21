using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the PSN endpoints, and a URL that does not fit the buffer it is built in.
/// </summary>
public class PsnEndpointsTests
{
    /// <summary>
    /// THE WAKE-UP URL DOES NOT FIT ITS BUFFER. Composed into 128 bytes from a fixed base, a fixed
    /// tail and the user's online id, it survives only a SEVEN character id - and PSN online ids
    /// run to sixteen.
    ///
    /// The number is measured from the strings rather than counted by hand, because a number
    /// written down beside a buffer is exactly the kind of thing that stops being true.
    /// </summary>
    [Fact]
    public void TheWakeupUrlOutgrowsItsBufferAtEightCharacters()
    {
        int longest = PsnEndpoints.LongestOnlineIdThatFits();

        Assert.Equal(7, longest);
        Assert.True(PsnEndpoints.WakeupFits(new string('a', longest)));
        Assert.False(PsnEndpoints.WakeupFits(new string('a', longest + 1)));
    }

    /// <summary>
    /// And PSN's own limit is sixteen, so the buffer is short for most accounts rather than for an
    /// unlucky few. The failure would surface as a request to a path that does not exist.
    /// </summary>
    [Fact]
    public void APlausibleOnlineIdDoesNotFit()
    {
        Assert.False(PsnEndpoints.WakeupFits("alexandre_oliv"));
        Assert.False(PsnEndpoints.WakeupFits(new string('x', 16)));

        // Truncated at 127 characters, the URL loses its query string and then its path.
        string whole = PsnEndpoints.Wakeup(new string('x', 16));
        string clipped = whole[..(PsnEndpoints.WakeupUrlBuffer - 1)];

        Assert.DoesNotContain("platform=PS4", clipped, StringComparison.Ordinal);
        Assert.EndsWith("wakeUp?platform=PS4", whole, StringComparison.Ordinal);
    }

    /// <summary>This port composes the whole URL, which is plainly what the core meant to.</summary>
    [Fact]
    public void ThisPortBuildsTheWholeUrl()
    {
        string url = PsnEndpoints.Wakeup("a_sixteen_charid");

        Assert.StartsWith(PsnEndpoints.UserProfileUrl, url, StringComparison.Ordinal);
        Assert.Contains("/v1/users/a_sixteen_charid/remoteConsole/wakeUp", url, StringComparison.Ordinal);
        Assert.True(url.Length > PsnEndpoints.WakeupUrlBuffer);
    }

    /// <summary>
    /// THE WAKE-UP ALWAYS SAYS PS4, while the device list refuses any console that is not a PS5.
    /// Two requests in the same file, disagreeing about what this client is for.
    /// </summary>
    [Fact]
    public void TheWakeupSaysPs4AndTheDeviceListDemandsPs5()
    {
        Assert.Contains("platform=PS4", PsnEndpoints.Wakeup("someone"), StringComparison.Ordinal);
        Assert.Contains("platform=PS5", PsnEndpoints.DeviceList(), StringComparison.Ordinal);
        Assert.Equal("PS5", PsnEndpoints.SupportedPlatform);
    }

    /// <summary>
    /// The device list's buffer, by contrast, fits - with exactly two bytes to spare, so a longer
    /// platform name would truncate it the same way.
    /// </summary>
    [Fact]
    public void TheDeviceListFitsWithTwoBytesToSpare()
    {
        string url = PsnEndpoints.DeviceList();

        Assert.True(url.Length < PsnEndpoints.DeviceListUrlBuffer);
        Assert.Equal(2, PsnEndpoints.DeviceListUrlBuffer - 1 - url.Length);
    }

    /// <summary>And it asks for ten devices and never asks for the next ten.</summary>
    [Fact]
    public void TheDeviceListNeverPaginates()
    {
        string url = PsnEndpoints.DeviceList();

        Assert.Contains("limit=10", url, StringComparison.Ordinal);
        Assert.Contains("offset=0", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND IT ASKS IN JAPANESE. Not a variable and not a setting - and "jp" is not a language code
    /// either, since Japanese is "ja" and "jp" is the country. Wrong twice, and it works because
    /// PSN ignores it.
    /// </summary>
    [Fact]
    public void TheDeviceListAsksInAnInvalidLanguageCode()
    {
        Assert.Equal("Accept-Language: jp", PsnEndpoints.DeviceListLanguage);
        Assert.DoesNotContain("ja", PsnEndpoints.DeviceListLanguage, StringComparison.Ordinal);
    }

    /// <summary>The two composed headers, spelled the way the core spells them.</summary>
    [Fact]
    public void TheComposedHeadersAreWhatTheyLookLike()
    {
        Assert.Equal("Authorization: Bearer abc123", PsnEndpoints.OauthHeader("abc123"));
        Assert.Equal(
            "X-PSN-SESSION-MANAGER-SESSION-IDS: sid", PsnEndpoints.SessionIdHeader("sid"));
    }

    /// <summary>Every URL and buffer above, still stated the same way in the core.</summary>
    [Fact]
    public void TheEndpointsRulesAreStillTheQtCores()
    {
        string? path = PsnEndpointsSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PsnEndpointsSource.TheUrlsAreStillTheseOnes(core), "nine URLs");
        Assert.True(PsnEndpointsSource.TheBuffersAreStillTheseSizes(core), "133 and 128");
        Assert.True(PsnEndpointsSource.TheWakeupIsStillComposedThatWay(core), "profile plus online id");
        Assert.True(
            PsnEndpointsSource.TheDeviceListStillRefusesEverythingButPs5(core), "PS5 or nothing");
        Assert.True(PsnEndpointsSource.TheLanguageHeaderIsStillThere(core), "asked in jp");
    }

    /// <summary>
    /// The agent goes on some JSON requests and not on others that are otherwise identical - a
    /// smaller inconsistency, counted rather than described so it cannot drift unnoticed.
    /// </summary>
    [Fact]
    public void MoreRequestsCarryTheContentTypeThanCarryTheAgent()
    {
        string? path = PsnEndpointsSource.Locate();
        if (path is null)
            return;

        (int contentType, int userAgent) = PsnEndpointsSource.HeaderCounts(File.ReadAllText(path));

        Assert.Equal(6, contentType);
        Assert.Equal(3, userAgent);
    }
}
