using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP345, under PP294: the handover of a login PIN from the session thread to ctrl, and the failure
/// that used to arrive as an accusation.
///
/// Three functions carry a PIN from the person who typed it to the console, and only two of them
/// could report a failure. chiaki_session_set_login_pin returns ChiakiErrorCode and answers
/// CHIAKI_ERR_MEMORY where its malloc fails. The session thread then forwarded the PIN with
/// chiaki_ctrl_set_login_pin, which returned VOID - and whose first statement is a malloc that
/// returned early on failure, before login_pin_entered was set and before the notify pipe was poked.
/// Nothing anywhere learned that the PIN had been dropped.
///
/// WHAT THE PERSON SAW IS THE DEFECT. The ctrl thread never sent the PIN, so the console never
/// accepted it and asked again; PP335 established that a second request IS the refusal as far as
/// this loop can tell, so the next prompt said the last PIN was wrong. It was not wrong. There was
/// no memory - and the early return did not log either, so nothing said otherwise anywhere.
///
/// THE PIN IS SPENT BEFORE THE FAILURE IS READ, and that is what settles what to do about it. The
/// session thread frees its copy immediately after the call, so by the time the code reads a failure
/// there is nothing left to retry with. Prompting a third time would repeat the accusation; ending
/// the session with a reason naming memory says what happened.
///
/// THE REASON IS APPENDED, NOT FILED WITH THE OTHER CTRL ONES. Quit reasons cross the shim as
/// ordinals and <see cref="ChiakiQuitReason"/> counts from None, so inserting a value beside
/// CTRL_UNKNOWN would silently renumber the six below it. <see cref="TheReasonAgreesAcrossTheShim"/>
/// is the assertion that keeps the two enums honest about that.
/// </summary>
public static class LoginPinHandover
{
    /// <summary>Where the handover's near side lives.</summary>
    public const string CtrlRelativePath = @"lib\src\ctrl.c";

    /// <summary>Where its caller lives.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>ctrl.c, or null outside a checkout.</summary>
    public static string? LocateCtrl() => SanitizerSource.LocateRelative(CtrlRelativePath);

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>The function whose signature was the defect.</summary>
    public const string Handover = "chiaki_ctrl_set_login_pin";

    /// <summary>What a dropped PIN ends the session with.</summary>
    public const ChiakiQuitReason Reason = ChiakiQuitReason.CtrlMemory;

    /// <summary>
    /// Whether the handover can report a failure at all, which is the whole of the symptom.
    ///
    /// Read off the declaration rather than the body: a body that returns a code from a function
    /// declared void does not compile, and a body that does not is what a caller cannot see.
    /// </summary>
    public static bool ItCanReportAFailure(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf($"ChiakiErrorCode {Handover}(", StringComparison.Ordinal);
        return at >= 0 && !source.Contains($"void {Handover}(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the allocation failure both says so and answers with a code.
    ///
    /// Both, because either alone leaves the same session. A log with a silent return tells a reader
    /// afterwards and the caller nothing; a code with no log tells the caller and leaves no trace of
    /// a failure that happens once in a very long time.
    /// </summary>
    public static bool TheAllocationFailureIsReported(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, $"ChiakiErrorCode {Handover}(");
        if (body is null)
            return false;

        int check = body.IndexOf("if(!buf)", StringComparison.Ordinal);
        if (check < 0)
            return false;

        int log = body.IndexOf("CHIAKI_LOGE", check, StringComparison.Ordinal);
        int answer = body.IndexOf("return CHIAKI_ERR_MEMORY;", check, StringComparison.Ordinal);

        return log > check && answer > log;
    }

    /// <summary>
    /// Whether the session thread reads the answer and ends on it, rather than carrying on into the
    /// wait that produces the third prompt.
    /// </summary>
    public static bool TheCallerActsOnIt(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int call = source.IndexOf($"= {Handover}(&session->ctrl", StringComparison.Ordinal);
        if (call < 0)
            return false;

        int check = source.IndexOf("if(pin_err != CHIAKI_ERR_SUCCESS)", call, StringComparison.Ordinal);
        int reason = source.IndexOf("CHIAKI_QUIT_REASON_CTRL_MEMORY", call, StringComparison.Ordinal);
        int exit = source.IndexOf("goto ctrl_failed;", call, StringComparison.Ordinal);

        // And the loop's own wait is only reached after all of that, so a failure cannot fall into it.
        int wait = source.IndexOf("session_check_state_pred_ctrl_start", call, StringComparison.Ordinal);

        return check > call && reason > check && exit > reason && wait > exit;
    }

    /// <summary>
    /// Whether the PIN really is spent before the failure is read, which is what rules out a retry.
    ///
    /// Stated as an assertion rather than as prose because it is the reason the branch ends the
    /// session: a future edit that moved the free below the check would make a retry possible again
    /// and leave this branch looking gratuitous.
    /// </summary>
    public static bool ThePinIsSpentBeforeTheCheck(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source); // PP388

        int call = CCall.Mark(compact, $"= {Handover}(&session->ctrl");
        if (call < 0)
            return false;

        int freed = CCall.At(compact, "free(session->login_pin)", call);
        int cleared = CCall.Mark(compact, "session->login_pin = NULL;", call);
        int check = CCall.Mark(compact, "if(pin_err != CHIAKI_ERR_SUCCESS)", call);

        return freed > call && cleared > freed && check > cleared;
    }

    /// <summary>
    /// Whether the new reason carries a sentence of its own rather than falling to "Unknown".
    ///
    /// This is the join between the two enums, and it is a RUNTIME call for that reason: the managed
    /// value is an ordinal this port assigned and the sentence comes from the C switch, so the two
    /// agreeing is the only evidence the append landed at the same index on both sides.
    /// </summary>
    public static bool TheReasonAgreesAcrossTheShim()
    {
        string? sentence = ChiakiSession.QuitReasonString((int)Reason);

        return !string.IsNullOrEmpty(sentence)
            && !sentence.Equals("Unknown", StringComparison.Ordinal)
            && sentence.Contains("memory", StringComparison.OrdinalIgnoreCase);
    }
}
