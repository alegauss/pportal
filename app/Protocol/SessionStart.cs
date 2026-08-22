namespace ChiakiNg.Protocol;

/// <summary>Why a start attempt ended, before anything is reported.</summary>
public enum StartFailure
{
    /// <summary>Nothing went wrong.</summary>
    None,

    /// <summary>The notification held no member with a device id string.</summary>
    MemberFieldMissing,

    /// <summary>It held one of the wrong length.</summary>
    MemberIdWrongLength,

    /// <summary>The id would not convert from hex.</summary>
    MemberIdNotHex,

    /// <summary>It converted, and named a different console than this session asked for.</summary>
    WrongConsole,

    /// <summary>The custom data field was absent or not a string.</summary>
    CustomDataFieldMissing,

    /// <summary>It was the wrong length.</summary>
    CustomDataWrongLength,

    /// <summary>It would not decode.</summary>
    CustomDataUndecodable,

    /// <summary>A notification of a type this loop does not handle.</summary>
    UnexpectedNotification,
}

/// <summary>
/// PP257: starting a session, and the second variable that swallows two failures.
///
/// TWO FAILURES RETURN SUCCESS THROUGH A SHADOWED VARIABLE. The function declares its error variable
/// at the top and returns it at the bottom. Inside the branch handling the console's arrival a
/// SECOND variable of the same name and type is declared to hold one call's result, and from there
/// on every assignment writes the inner one. Two branches do exactly that - the device id that will
/// not convert from hex, and the device id that names a different console - and both then break,
/// leaving the outer variable holding the success the wait put there.
///
/// The second of those is the identity check. A session that joined the WRONG console is reported as
/// started. <see cref="Reported"/> is where that is written down, against <see cref="IsFailure"/>.
///
/// AND THE SUCCESS PATH UNLOCKS TWICE. The state mutex is taken at the top of each turn and released
/// as the loop's last statement, then released again after the loop. An exit by break leaves it
/// held, so the second release is right for that one and one too many for the other - and the other
/// is the exit every working session takes. See <see cref="UnlocksAfterTheLoop"/>.
///
/// The file names its own third problem, in a comment: the two notifications do not share a timeout,
/// so a first that takes twenty-nine seconds and a second that takes fifteen do not exceed a limit
/// of thirty. <see cref="SharesOneTimeout"/> carries that.
/// </summary>
public static class SessionStart
{
    /// <summary>How long each wait is given, in seconds.</summary>
    public const int TimeoutSeconds = 30;

    /// <summary>Whether the two waits share that budget. They do not, and the core says so.</summary>
    public const bool SharesOneTimeout = false;

    /// <summary>How long a device id is, as text.</summary>
    public const int DeviceIdTextLength = 64;

    /// <summary>And in bytes.</summary>
    public const int DeviceIdLength = 32;

    /// <summary>How long the custom data field is, as text.</summary>
    public const int CustomDataTextLength = 32;

    /// <summary>
    /// Which failures assign the SHADOWED variable - the ones declared after it comes into scope.
    /// </summary>
    public static IReadOnlyList<StartFailure> WriteTheInnerVariable { get; } =
        [StartFailure.MemberIdNotHex, StartFailure.WrongConsole];

    /// <summary>Whether something actually went wrong.</summary>
    public static bool IsFailure(StartFailure failure) => failure != StartFailure.None;

    /// <summary>
    /// What the function actually returns for it.
    ///
    /// Success for the two that wrote the inner variable - which is the defect, reproduced.
    /// </summary>
    public static string Reported(StartFailure failure)
        => !IsFailure(failure) || WriteTheInnerVariable.Contains(failure)
            ? "CHIAKI_ERR_SUCCESS"
            : "CHIAKI_ERR_UNKNOWN";

    /// <summary>Whether a failure is lost - reported as success despite being one.</summary>
    public static bool IsLost(StartFailure failure)
        => IsFailure(failure)
            && string.Equals(Reported(failure), "CHIAKI_ERR_SUCCESS", StringComparison.Ordinal);

    /// <summary>Every failure that is lost. Two.</summary>
    public static IReadOnlyList<StartFailure> Lost { get; } =
        [.. Enum.GetValues<StartFailure>().Where(IsLost)];

    /// <summary>
    /// How many times the state mutex is released on the way out, given how the loop ended.
    /// </summary>
    /// <param name="brokeOut">Whether a failure broke out of the loop.</param>
    /// <returns>One for a break, two for the ordinary completion.</returns>
    public static int UnlocksAfterTheLoop(bool brokeOut) => brokeOut ? 1 : 2;

    /// <summary>Whether the mutex is still held when the release after the loop runs.</summary>
    public static bool StillHeldAfterTheLoop(bool brokeOut) => brokeOut;

    /// <summary>
    /// The two states a start is waiting for; it finishes only when both have arrived.
    /// </summary>
    public static bool Finished(SessionStateFlags state)
        => state.HasFlag(SessionStateFlags.ConsoleJoined)
            && state.HasFlag(SessionStateFlags.CustomData1Received);
}

/// <summary>
/// PP257: the start where the core writes it.
/// </summary>
public static class SessionStartSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// THE SHADOW. Whether a second variable of the same name is still declared inside the branch.
    /// </summary>
    public static bool TheInnerVariableIsStillDeclared(string core)
    {
        string body = Body(core);

        int outer = body.IndexOf("    ChiakiErrorCode err;\n", StringComparison.Ordinal);
        int inner = body.IndexOf(
            "ChiakiErrorCode err = hex_to_bytes(member_duid, duid_bytes,", StringComparison.Ordinal);

        return outer >= 0 && inner > outer;
    }

    /// <summary>
    /// And whether the two failures after it still assign and break, with the function returning
    /// the outer one.
    /// </summary>
    public static bool TheTwoFailuresStillWriteTheInnerOne(string core)
    {
        string body = Body(core);

        int inner = body.IndexOf(
            "ChiakiErrorCode err = hex_to_bytes(", StringComparison.Ordinal);
        if (inner < 0)
            return false;

        // The scope the shadow covers runs to the end of that branch.
        int scopeEnds = body.IndexOf(
            "session->state |= SESSION_STATE_CONSOLE_JOINED;", inner, StringComparison.Ordinal);
        if (scopeEnds < 0)
            return false;

        string shadowed = body[inner..scopeEnds];

        return shadowed.Contains("Could not convert member duid to bytes", StringComparison.Ordinal)
            && shadowed.Contains("holepunch session does not contain console", StringComparison.Ordinal)
            && shadowed.Split("err = CHIAKI_ERR_UNKNOWN;", StringSplitOptions.None).Length - 1 == 2
            && body.Contains("\n    return err;\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the failures BEFORE the shadow still reach the outer variable - the contrast that
    /// makes the two after it a consequence of where the declaration sits.
    /// </summary>
    public static bool TheEarlierFailuresStillReachTheOuterOne(string core)
    {
        string body = Body(core);

        int inner = body.IndexOf("ChiakiErrorCode err = hex_to_bytes(", StringComparison.Ordinal);
        if (inner < 0)
            return false;

        string before = body[..inner];

        return before.Contains(
                "JSON does not contain member with a deviceUniqueId string field!", StringComparison.Ordinal)
            && before.Contains("has unexpected length, got %zu, expected 64", StringComparison.Ordinal)
            && before.Split("err = CHIAKI_ERR_UNKNOWN;", StringSplitOptions.None).Length - 1 == 2;
    }

    /// <summary>
    /// THE SECOND UNLOCK. Whether the release inside the loop is still followed by another outside
    /// it.
    /// </summary>
    public static bool TheMutexIsStillReleasedTwice(string core)
        => Body(core).Contains(
            """
                    chiaki_mutex_unlock(&session->state_mutex);
                }
                chiaki_mutex_unlock(&session->state_mutex);
                return err;
            """.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\n'),
            StringComparison.Ordinal);

    /// <summary>
    /// And whether the loop still ends by both states having arrived.
    ///
    /// Compared with the indentation taken out: this condition is wrapped across two lines and its
    /// continuation is aligned to the opening paren, which is spacing rather than code.
    /// </summary>
    public static bool TheLoopStillEndsOnBothStates(string core)
    {
        string flat = string.Join(
            '|', Body(core).Split('\n').Select(l => l.Trim()));

        return flat.Contains(
            "finished = (session->state & SESSION_STATE_CONSOLE_JOINED) &&|"
            + "(session->state & SESSION_STATE_CUSTOMDATA1_RECEIVED);",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the core still states the unshared timeout in its own words.</summary>
    public static bool TheUnsharedTimeoutIsStillStated(string core)
        => Body(core).Contains(
            "FIXME: We're currently not using a shared timeout for both", StringComparison.Ordinal);

    /// <summary>And whether the lengths it checks are still these.</summary>
    public static bool TheLengthsAreStillThese(string core)
    {
        string body = Body(core);

        return body.Contains(
                $"if (strlen(member_duid) != {SessionStart.DeviceIdTextLength})", StringComparison.Ordinal)
            && body.Contains(
                $"if (strlen(custom_data1) != {SessionStart.CustomDataTextLength})", StringComparison.Ordinal);
    }

    /// <summary>chiaki_holepunch_session_start's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_start(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "static ChiakiErrorCode http_ps4_session_wakeup(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
