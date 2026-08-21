namespace ChiakiNg.Protocol;

/// <summary>
/// PP235: deleting a session, and the messages that name a different call.
///
/// The request itself is ordinary: a DELETE by custom request, one URL built from the session id,
/// and a JSON content type on a request that carries no body at all.
///
/// The logging is not. Both failure branches say "http_send_session_message: Sending holepunch
/// session message failed" - in a function that sends no message and deletes a session. Not a stale
/// word inside the right sentence: the wrong function's name and the wrong operation.
///
/// That is the third of these found while porting five calls, and <see cref="MisnamedLogs"/> is
/// where they are counted together.
/// </summary>
public static class SessionDelete
{
    /// <summary>The verb, set through CURLOPT_CUSTOMREQUEST rather than by a method option.</summary>
    public const string Method = "DELETE";

    /// <summary>Seconds it is given, from CURLOPT_TIMEOUT.</summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// The headers it sends: the bearer, and a JSON content type on a request with no body.
    ///
    /// The content type is carried because the core carries it. A DELETE with no entity has nothing
    /// for it to describe, and dropping it would be a change to what PSN receives rather than a
    /// tidy - which is not a thing to find out from a server.
    /// </summary>
    public static IReadOnlyList<string> Headers(string oauthHeader)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        return [oauthHeader, PsnEndpoints.JsonContentType];
    }

    /// <summary>The URL for one session id.</summary>
    public static string UrlFor(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PsnEndpoints.DeleteMessageFormat,
            sessionId);
    }
}

/// <summary>
/// PP235: the log messages in holepunch.c that name the wrong call, counted together.
///
/// Three found while porting five of its HTTP calls, none of which a compiler or a test could have
/// objected to, because a log string is correct by construction. The pattern is the same each time:
/// the message is copied along with the curl block it sits in, and the name inside it is whatever
/// that block was copied FROM.
///
/// It matters because these are what a person reads first when a session will not start, and the
/// thing they check last. A report saying "creating the session failed" when the create succeeded
/// sends whoever wrote it to the wrong half of the flow.
///
/// Asserted here rather than one per task, so the count is visible and the next one has somewhere
/// to go. Reproduced and not fixed: a port that corrected the strings would make its own logs
/// disagree with every report ever written against the Qt client's.
/// </summary>
public static class MisnamedLogs
{
    /// <summary>One message that names the wrong thing, and what it should have named.</summary>
    /// <param name="Function">The function the message actually sits in.</param>
    /// <param name="Says">The text, as the file has it.</param>
    /// <param name="Names">What that text claims is happening.</param>
    public readonly record struct Misnamed(string Function, string Says, string Names);

    /// <summary>
    /// Every one found so far, in the order they were met.
    /// </summary>
    public static IReadOnlyList<Misnamed> All { get; } =
    [
        new(
            "get_stun_servers",
            "Getting IPV6 stun servers from %s failed with HTTP code %ld\", STUN_HOSTS_URL,",
            "the IPv4 list's URL, for a failure fetching the IPv6 one"),

        new(
            "http_check_session",
            "http_check_session: Creating holepunch session failed",
            "creating a session, in a function that creates nothing"),

        new(
            "deleteSession",
            "http_send_session_message: Sending holepunch session message failed",
            "another function entirely, by name"),

    ];

    /// <summary>
    /// PP238: what this list does NOT count, and why.
    ///
    /// PP237 put both reply senders here because they log as check_candidates rather than as
    /// themselves. PP238 found the same prefix on all three messages in the loop that calls them -
    /// and every one of those functions is a HELPER of check_candidates. The prefix names the
    /// operation the reader is following, not the function the line sits in, and across a call tree
    /// that is defensible: a log that changed name three times inside one exchange would be worse.
    ///
    /// That is a different thing from <see cref="All"/>, where a message names a DIFFERENT
    /// operation - a create that is a check, a send that is a delete, an IPv4 URL for an IPv6
    /// fetch. Grouping the two flattened a real distinction, so the correction is here rather than
    /// as a quietly shorter list.
    /// </summary>
    public static IReadOnlyList<string> NamesTheOperationNotTheFunction { get; } =
        ["send_response_ps", "send_responseto_ps", "receive_request_send_response_ps"];

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>
    /// Which of them are still there. Every one, on a tree that has not been corrected - and this
    /// answers with the ones that ARE rather than a bare true, so a message that gets fixed
    /// upstream is named rather than silently dropping the count.
    /// </summary>
    public static IReadOnlyList<Misnamed> StillPresent(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return [.. All.Where(m => core.Contains(m.Says, StringComparison.Ordinal))];
    }
}
