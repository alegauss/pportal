using System.Globalization;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP239: waking a PS4, which takes two requests - the first one asking where the second should go.
///
/// A fixed bootstrap endpoint answers with a document whose only interesting field is a url, and
/// the wakeup is built from THAT rather than from anything compiled in.
///
/// THE NAME IS SHADOWED IN THE CORE, and both bindings are plausible URLs: at file scope
/// user_profile_url is the bootstrap address, and inside the function a local of the same name
/// holds what the server answered. The wakeup is filled from the local. A port resolving it the
/// other way builds a well-formed wakeup aimed at the discovery service, which will answer
/// something - and nothing in the result says which was used.
///
/// So the two are given DIFFERENT NAMES here: <see cref="DiscoveryUrl"/> and the discovered one
/// passed in. A port cannot carry a bug whose whole mechanism is that two things are called the
/// same, and reproducing the shadowing would be reproducing the trap rather than the behaviour.
/// </summary>
public static class Ps4Wakeup
{
    /// <summary>
    /// The bootstrap. Fixed, compiled in, and the only address in this exchange that is.
    /// </summary>
    public static string DiscoveryUrl => PsnEndpoints.UserProfileUrl;

    /// <summary>Where in the answer the real address is.</summary>
    public const string UrlField = "/url";

    /// <summary>Seconds each request is given, from CURLOPT_TIMEOUT.</summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// The buffer the core copies the answer into. 128 bytes, on the stack, filled by a memcpy of
    /// strlen plus one from a string the SERVER sent.
    ///
    /// Carried as a number rather than as a copy: every other unbounded copy in that file takes a
    /// value the process produced or a file from a repository, and this one takes whatever came
    /// back. <see cref="Fits"/> is what the core does not ask.
    /// </summary>
    public const int HostBuffer = 128;

    /// <summary>Whether an answer would have fitted the buffer the core copies it into.</summary>
    public static bool Fits(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Length + 1 <= HostBuffer;
    }

    /// <summary>
    /// The wakeup address, built from the DISCOVERED base rather than from the bootstrap.
    /// </summary>
    /// <param name="discoveredBase">The url the bootstrap answered with.</param>
    /// <param name="onlineId">The account this is waking a console for.</param>
    public static string UrlFor(string discoveredBase, string onlineId)
    {
        ArgumentNullException.ThrowIfNull(discoveredBase);
        ArgumentNullException.ThrowIfNull(onlineId);

        return string.Format(
            CultureInfo.InvariantCulture, PsnEndpoints.WakeupFormat, discoveredBase, onlineId);
    }

    /// <summary>
    /// The host, as the core arrives at it: the scheme removed from the string rather than parsed
    /// out of it.
    ///
    /// remove_substring deletes the first occurrence of "https://" and then of "http://", wherever
    /// each one appears - so a url carrying either sequence somewhere other than the front loses it
    /// too. Reproduced, because a url that survives this is what ends up in a Host header.
    /// </summary>
    public static string HostOf(string url)
    {
        string stripped = StripScheme(url);

        int slash = stripped.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? stripped : stripped[..slash];
    }

    /// <summary>
    /// The stripping on its own, which is where the damage is.
    ///
    /// The core keeps this string as well as the host it cuts out of it, so what happens here
    /// matters beyond the first slash: the two removals are unanchored, so a url carrying either
    /// scheme sequence anywhere else has that occurrence deleted too. The host usually survives
    /// because the cut happens before the second occurrence - which is why this is separated out
    /// rather than folded in, where the damage would be invisible.
    /// </summary>
    public static string StripScheme(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return RemoveFirst(RemoveFirst(url, "https://"), "http://");
    }

    private static string RemoveFirst(string text, string what)
    {
        int at = text.IndexOf(what, StringComparison.Ordinal);
        return at < 0 ? text : text.Remove(at, what.Length);
    }
}

/// <summary>
/// PP239: the wakeup where the core writes it.
/// </summary>
public static class Ps4WakeupSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the bootstrap is still that address, and still the compiled-in one.</summary>
    public static bool TheBootstrapIsStillCompiledIn(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"static const char user_profile_url[] = \"{Ps4Wakeup.DiscoveryUrl}\"",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the name is still shadowed - the whole reason the port gives the two different ones.
    /// True means a local of the file-scope name still holds the answer.
    /// </summary>
    public static bool TheNameIsStillShadowed(string core)
        => Body(core).Contains(
            "const char *user_profile_url = json_object_get_string(user_profile_url_json);",
            StringComparison.Ordinal);

    /// <summary>Whether the answer is still copied into a fixed buffer with no bound.</summary>
    public static bool TheAnswerStillGoesIntoAFixedBuffer(string core)
    {
        string body = Body(core);

        return body.Contains($"char host_url[{Ps4Wakeup.HostBuffer}];", StringComparison.Ordinal)
            && body.Contains(
                "memcpy(host_url, user_profile_url, strlen(user_profile_url) + 1)",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the scheme is still removed rather than parsed.</summary>
    public static bool TheSchemeIsStillRemovedNotParsed(string core)
    {
        string body = Body(core);

        return body.Contains("remove_substring(host_url, \"https://\")", StringComparison.Ordinal)
            && body.Contains("remove_substring(host_url, \"http://\")", StringComparison.Ordinal);
    }

    /// <summary>http_ps4_session_wakeup's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.LastIndexOf(
            "static ChiakiErrorCode http_ps4_session_wakeup(Session *session)", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf("\n}\n", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
