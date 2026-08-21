using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the PSN endpoints the hole punching talks to, and the fixed-size buffers they are built in.
///
/// THE WAKE-UP URL DOES NOT FIT ITS BUFFER. It is composed into <c>char url[128]</c> from a
/// seventy-six character base, a fixed thirty-four character tail and the user's online id in the
/// middle - so anything past a SEVEN character id is silently truncated by snprintf, which returns
/// the length it wanted and is not checked. PSN online ids run to sixteen characters. The URL is
/// not built wrong for some users; it is built wrong for most of them, and the failure surfaces as
/// a request to a path that does not exist rather than as anything naming the buffer.
///
/// This port composes the same URL without a length limit, and the arithmetic is pinned by a test
/// that measures the two strings rather than trusting a number written down beside them. Truncating
/// to match would be reproducing a defect with no behaviour worth having - the PP194 line - and the
/// core's own intent is plainly the whole URL.
///
/// THE WAKE-UP ALWAYS SAYS PS4. The platform is written into the format string as a literal, while
/// the device list REFUSES any console that is not a PS5 a few hundred lines earlier. The two
/// requests in the same file disagree about what this client is for.
///
/// AND THE DEVICE LIST ASKS IN JAPANESE. <c>Accept-Language: jp</c> is appended to that request and
/// to no other - not a variable, not a setting. "jp" is not a language code either; Japanese is
/// "ja" and "jp" is the country, so the header is wrong twice and works because PSN ignores it.
///
/// The other headers are inconsistent in a smaller way: <c>User-Agent: RpNetHttpUtilImpl</c> goes
/// on some JSON requests and not on others that are otherwise identical.
/// </summary>
public static class PsnEndpoints
{
    /// <summary>The device list, which asks for ten and never asks for the next ten.</summary>
    public const string DeviceListFormat =
        "https://web.np.playstation.com/api/cloudAssistedNavigation/v2/users/me/clients?platform={0}&includeFields=device&limit=10&offset=0";

    /// <summary>The buffer the device list URL is composed into.</summary>
    public const int DeviceListUrlBuffer = 133;

    /// <summary>The only console this client will ask about.</summary>
    public const string SupportedPlatform = "PS5";

    /// <summary>Where the push notification server's address comes from.</summary>
    public const string WebSocketFqdnUrl =
        "https://mobile-pushcl.np.communication.playstation.net/np/serveraddr?version=2.1&fields=keepAliveStatus&keepAliveStatusType=3";

    /// <summary>Creating a session.</summary>
    public const string SessionCreateUrl =
        "https://web.np.playstation.com/api/sessionManager/v1/remotePlaySessions";

    /// <summary>And looking at one.</summary>
    public const string SessionViewUrl =
        "https://web.np.playstation.com/api/sessionManager/v1/remotePlaySessions?view=v1.0";

    /// <summary>The base the wake-up is hung off.</summary>
    public const string UserProfileUrl =
        "https://asm.np.community.playstation.net/asm/v1/apps/me/baseUrls/userProfile";

    /// <summary>The wake-up, whose platform is a literal - see the class note.</summary>
    public const string WakeupFormat = "{0}/v1/users/{1}/remoteConsole/wakeUp?platform=PS4";

    /// <summary>The buffer the wake-up URL is composed into, and does not fit.</summary>
    public const int WakeupUrlBuffer = 128;

    /// <summary>Sending a command.</summary>
    public const string SessionCommandUrl =
        "https://web.np.playstation.com/api/cloudAssistedNavigation/v2/users/me/commands";

    /// <summary>Sending a session message.</summary>
    public const string SessionMessageFormat =
        "https://web.np.playstation.com/api/sessionManager/v1/remotePlaySessions/{0}/sessionMessage";

    /// <summary>And leaving.</summary>
    public const string DeleteMessageFormat =
        "https://web.np.playstation.com/api/sessionManager/v1/remotePlaySessions/{0}/members/me";

    /// <summary>The language the device list asks in, which is not a language code.</summary>
    public const string DeviceListLanguage = "Accept-Language: jp";

    /// <summary>The agent the JSON requests carry, where they carry one at all.</summary>
    public const string JsonUserAgent = "User-Agent: RpNetHttpUtilImpl";

    /// <summary>And the content type they all carry.</summary>
    public const string JsonContentType = "Content-Type: application/json; charset=utf-8";

    /// <summary>The bearer header, built from an OAuth token.</summary>
    public static string OauthHeader(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return $"Authorization: Bearer {token}";
    }

    /// <summary>The session id header, which is plural in its name and singular in its use.</summary>
    public static string SessionIdHeader(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return $"X-PSN-SESSION-MANAGER-SESSION-IDS: {sessionId}";
    }

    /// <summary>The device list URL for the one platform this client asks about.</summary>
    public static string DeviceList(string platform = SupportedPlatform)
        => string.Format(CultureInfo.InvariantCulture, DeviceListFormat, platform);

    /// <summary>
    /// The wake-up URL, WHOLE - not clipped to the buffer the core composes it into.
    /// </summary>
    public static string Wakeup(string onlineId)
    {
        ArgumentNullException.ThrowIfNull(onlineId);
        return string.Format(CultureInfo.InvariantCulture, WakeupFormat, UserProfileUrl, onlineId);
    }

    /// <summary>
    /// The longest online id that fits the core's buffer, worked out from the strings themselves
    /// rather than from a number written down beside them.
    /// </summary>
    public static int LongestOnlineIdThatFits()
    {
        // The URL with an empty id, plus the terminator the buffer also has to hold.
        int fixedLength = Wakeup("").Length;
        return Math.Max(0, WakeupUrlBuffer - 1 - fixedLength);
    }

    /// <summary>Whether an id of this length survives the core's composition intact.</summary>
    public static bool WakeupFits(string onlineId)
    {
        ArgumentNullException.ThrowIfNull(onlineId);
        return Wakeup(onlineId).Length < WakeupUrlBuffer;
    }
}

/// <summary>
/// PP33: the endpoints' rules where the Qt core states them.
/// </summary>
public static class PsnEndpointsSource
{
    /// <summary>Where they are declared.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Every URL this port copied, spelled as the core spells it.</summary>
    public static IReadOnlyList<string> Urls { get; } =
    [
        PsnEndpoints.DeviceListFormat.Replace("{0}", "%s", StringComparison.Ordinal),
        PsnEndpoints.WebSocketFqdnUrl,
        PsnEndpoints.SessionCreateUrl,
        PsnEndpoints.SessionViewUrl,
        PsnEndpoints.UserProfileUrl,
        PsnEndpoints.WakeupFormat.Replace("{0}", "%s", StringComparison.Ordinal).Replace("{1}", "%s", StringComparison.Ordinal),
        PsnEndpoints.SessionCommandUrl,
        PsnEndpoints.SessionMessageFormat.Replace("{0}", "%s", StringComparison.Ordinal),
        PsnEndpoints.DeleteMessageFormat.Replace("{0}", "%s", StringComparison.Ordinal),
    ];

    /// <summary>Whether every one of them is still the URL this port was built against.</summary>
    public static bool TheUrlsAreStillTheseOnes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string url in Urls)
        {
            if (!core.Contains($"\"{url}\"", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether the two buffers are still the sizes the arithmetic is about.</summary>
    public static bool TheBuffersAreStillTheseSizes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"char url[{PsnEndpoints.DeviceListUrlBuffer}];", StringComparison.Ordinal)
            && core.Contains($"char url[{PsnEndpoints.WakeupUrlBuffer}] = {{0}};", StringComparison.Ordinal);
    }

    /// <summary>Whether the wake-up is still composed from the profile URL and the online id.</summary>
    public static bool TheWakeupIsStillComposedThatWay(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "snprintf(url, sizeof(url), wakeup_url_fmt, user_profile_url, session->online_id);",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the device list still refuses anything that is not a PS5.</summary>
    public static bool TheDeviceListStillRefusesEverythingButPs5(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if (console_type != CHIAKI_HOLEPUNCH_CONSOLE_TYPE_PS5) {", StringComparison.Ordinal)
            && core.Contains($"snprintf(platform, sizeof(platform), \"%s\", \"{PsnEndpoints.SupportedPlatform}\");", StringComparison.Ordinal);
    }

    /// <summary>Whether the device list still asks in a language code that is not one.</summary>
    public static bool TheLanguageHeaderIsStillThere(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"curl_slist_append(headers, \"{PsnEndpoints.DeviceListLanguage}\")", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many requests carry the JSON content type, and how many of those also carry the agent.
    /// The two numbers differing is the inconsistency.
    /// </summary>
    public static (int ContentType, int UserAgent) HeaderCounts(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return (Count(core, PsnEndpoints.JsonContentType), Count(core, PsnEndpoints.JsonUserAgent));
    }

    private static int Count(string core, string header)
    {
        int count = 0;
        int at = 0;
        string needle = $"curl_slist_append(headers, \"{header}\")";

        while ((at = core.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at++;
        }

        return count;
    }
}
