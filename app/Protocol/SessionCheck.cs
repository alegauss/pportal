namespace ChiakiNg.Protocol;

/// <summary>How a session check ended, in the outcomes the core distinguishes.</summary>
public enum SessionCheckOutcome
{
    /// <summary>The server answered and the answer was JSON. That is the whole of the check.</summary>
    Ok,

    /// <summary>The server answered with an error status. CURLOPT_FAILONERROR makes that a failure.</summary>
    HttpNotOk,

    /// <summary>The transfer itself failed.</summary>
    Network,

    /// <summary>The body would not parse.</summary>
    Unreadable,

    /// <summary>
    /// A tokener could not be allocated - and this is reported as <see cref="Ok"/>. See
    /// <see cref="SessionCheck.Result"/>: the outcome is named here so the defect can be asserted,
    /// and the mapping is where it is reproduced.
    /// </summary>
    NoTokener,
}

/// <summary>
/// PP233: checking a session, which sends nothing and keeps nothing.
///
/// PP206 ported the two URLs and PP210 the bodies that go to them. This is neither: one function,
/// two endpoints chosen by a bool, and a response parsed only to be logged - the document is
/// released the line after it is turned into a string. The whole assertion this call makes about a
/// session is that the server answered with JSON.
///
/// TWO DEFECTS, NEXT TO EACH OTHER.
///
/// A tokener that cannot be allocated logs and jumps to the cleanup, which returns the error
/// variable - and at that point the error variable still says success. The parse failure directly
/// below it sets one first. So of the two ways this can fail to read a response, one is reported
/// and the other is a check that passed having read nothing, on the path taken when memory is
/// short.
///
/// And every message in it says "Creating holepunch session failed", in a function that creates
/// nothing. Whoever reads that log while a session refuses to start is told about the wrong call -
/// and the call it names is one that did succeed. Reproduced, not fixed.
/// </summary>
public static class SessionCheck
{
    /// <summary>Seconds the request is given, from CURLOPT_TIMEOUT.</summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// Which endpoint a check goes to.
    ///
    /// One bool, two URLs, and the FALSE case is the create URL - so an ordinary check is a GET of
    /// the address a session is created at. The view URL is the same path with `?view=v1.0`.
    /// </summary>
    public static string UrlFor(bool viewUrl)
        => viewUrl ? PsnEndpoints.SessionViewUrl : PsnEndpoints.SessionCreateUrl;

    /// <summary>
    /// What the caller is told, given what happened.
    ///
    /// <paramref name="tokener"/> is the one that matters. The core's branch for it does not set
    /// the error variable before jumping to a cleanup that returns it, so a failure to allocate is
    /// answered with success - which is reproduced here rather than corrected, and asserted so a
    /// reader meets it on purpose.
    /// </summary>
    public static SessionCheckOutcome Result(
        bool transferred, bool httpOk, bool tokener, bool parsed)
    {
        if (!transferred)
            return httpOk ? SessionCheckOutcome.Network : SessionCheckOutcome.HttpNotOk;

        if (!tokener)
            return SessionCheckOutcome.NoTokener;

        return parsed ? SessionCheckOutcome.Ok : SessionCheckOutcome.Unreadable;
    }

    /// <summary>
    /// Whether an outcome is reported to the caller as a FAILURE.
    ///
    /// <see cref="SessionCheckOutcome.NoTokener"/> is not, and that is the defect stated as a
    /// value rather than as prose: the branch reaches a cleanup that returns a variable still
    /// holding success.
    /// </summary>
    public static bool IsFailure(SessionCheckOutcome outcome) => outcome switch
    {
        SessionCheckOutcome.HttpNotOk => true,
        SessionCheckOutcome.Network => true,
        SessionCheckOutcome.Unreadable => true,

        // Ok, and NoTokener with it.
        _ => false,
    };
}

/// <summary>
/// PP233: the check where the core writes it.
/// </summary>
public static class SessionCheckSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether one bool still chooses between the two endpoints.</summary>
    public static bool OneBoolStillChoosesTheUrl(string core)
        => Body(core).Contains(
            "CURLOPT_URL, viewurl ? session_view_url : session_create_url", StringComparison.Ordinal);

    /// <summary>Whether the body is still parsed only to be logged and released.</summary>
    public static bool TheBodyIsStillOnlyLogged(string core)
    {
        string body = Body(core);

        int logged = body.IndexOf("retrieved session data", StringComparison.Ordinal);
        int released = body.IndexOf("json_object_put(json);", StringComparison.Ordinal);

        return logged >= 0 && released > logged;
    }

    /// <summary>
    /// Whether a tokener that cannot be allocated still leaves without setting an error. True means
    /// the defect is still present - the branch below it sets one, and this one does not.
    /// </summary>
    public static bool ANoTokenerStillReturnsSuccess(string core)
    {
        string body = Body(core);

        int missing = body.IndexOf(
            "http_check_session: Couldn't create new json tokener", StringComparison.Ordinal);
        if (missing < 0)
            return false;

        int leaves = body.IndexOf("goto cleanup;", missing, StringComparison.Ordinal);
        if (leaves < 0)
            return false;

        // Nothing sets err between the log and the jump - which is the whole of it.
        string branch = body[missing..leaves];
        if (branch.Contains("err =", StringComparison.Ordinal))
            return false;

        // And the neighbour DOES, which is what makes it an asymmetry rather than a convention.
        return body.Contains("err = CHIAKI_ERR_UNKNOWN;", StringComparison.Ordinal);
    }

    /// <summary>Whether its failures still say "Creating" in a function that creates nothing.</summary>
    public static bool TheMessagesStillSayCreating(string core)
    {
        string body = Body(core);

        return body.Contains(
            "http_check_session: Creating holepunch session failed", StringComparison.Ordinal);
    }

    /// <summary>http_check_session's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // LAST, not first. holepunch.c forward-declares every static function at the top and the
        // declaration is a PREFIX of the definition, so searching forward lands on the semicolon
        // two and a half thousand lines above the body - which is the same miss PP213 made and
        // wrote down.
        int start = core.LastIndexOf(
            "static ChiakiErrorCode http_check_session(Session *session, bool viewurl)",
            StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(
            "static ChiakiErrorCode http_start_session(Session *session)", start, StringComparison.Ordinal);

        return end < 0 ? core[start..] : core[start..end];
    }
}
