using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>How the lookup for the websocket's host ended.</summary>
public enum FqdnLookupOutcome
{
    /// <summary>An address came back.</summary>
    Ok,

    /// <summary>The server answered with an error status.</summary>
    HttpNotOk,

    /// <summary>The transfer failed.</summary>
    Network,

    /// <summary>The body would not parse.</summary>
    Unreadable,

    /// <summary>The document had no such field.</summary>
    FieldAbsent,

    /// <summary>It had one, and it was not a string.</summary>
    FieldNotAString,

    /// <summary>
    /// A parser could not be allocated - and this is reported as <see cref="Ok"/>, with no address
    /// written. The same branch PP233 measured in the session check, in its second function.
    /// </summary>
    NoTokener,
}

/// <summary>
/// PP254: asking PSN which host to open the websocket against.
///
/// PP206 ported the URL and PP191 the wss address built from the answer. This is the lookup between
/// them, and the branch it shares with PP233.
///
/// THE SAME TOKENER BRANCH, AND WORSE HERE. A parser that cannot be allocated is logged and jumped
/// straight to the cleanup with no error code set, so the variable holding success is still holding
/// it. PP233 found this in the session check, where the answer was thrown away regardless. This
/// function has an OUT-PARAMETER, and on that path never writes it - the session initialises the
/// field to null when it is created, so the caller reads success, keeps the null, and carries it to
/// the connection. <see cref="IsFailure"/> and <see cref="WritesAnAddress"/> are separate questions
/// for exactly that reason.
///
/// THE FIELD IS CHECKED TWICE FOR TWO DIFFERENT THINGS. Absent and present-but-not-a-string are
/// separate branches with separate messages - more care than the allocation above them gets, and
/// worth keeping apart, since folding them loses which of the two happened.
///
/// Checked and not a defect: the string is duplicated before the document is released, so what is
/// handed back outlives what it came from. The success path falls through both cleanup labels to
/// reach that release, which reads like a leak and is the opposite of one.
/// </summary>
public static class WebSocketFqdn
{
    /// <summary>The field the address is read out of.</summary>
    public const string Field = "fqdn";

    /// <summary>The URL asked, which PP206 already named.</summary>
    public static string Url => PsnEndpoints.WebSocketFqdnUrl;

    /// <summary>
    /// Whether an outcome is reported to the caller as a failure.
    ///
    /// <see cref="FqdnLookupOutcome.NoTokener"/> is not - which is the defect, and is the same
    /// answer <see cref="SessionCheck.IsFailure"/> gives for its own.
    /// </summary>
    public static bool IsFailure(FqdnLookupOutcome outcome)
        => outcome is not (FqdnLookupOutcome.Ok or FqdnLookupOutcome.NoTokener);

    /// <summary>
    /// Whether an address is actually written. Only one outcome does.
    ///
    /// The gap between this and <see cref="IsFailure"/> is the whole finding: one outcome reports
    /// success and writes nothing.
    /// </summary>
    public static bool WritesAnAddress(FqdnLookupOutcome outcome) => outcome == FqdnLookupOutcome.Ok;

    /// <summary>
    /// Whether this outcome hands the caller a success it cannot act on.
    /// </summary>
    public static bool SucceedsWithoutAnAddress(FqdnLookupOutcome outcome)
        => !IsFailure(outcome) && !WritesAnAddress(outcome);

    /// <summary>What the session holds for the address before the lookup runs, and after that one.</summary>
    public const string? BeforeTheLookup = null;

    /// <summary>
    /// What the caller ends up holding, given the outcome and what came back.
    /// </summary>
    public static string? AddressAfter(FqdnLookupOutcome outcome, string? found)
        => WritesAnAddress(outcome) ? found : BeforeTheLookup;

    /// <summary>
    /// What the document has to contain, as the two separate questions the core asks.
    /// </summary>
    /// <param name="hasField">Whether the field is present at all.</param>
    /// <param name="isString">Whether it is a string.</param>
    public static FqdnLookupOutcome Read(bool hasField, bool isString)
    {
        if (!hasField)
            return FqdnLookupOutcome.FieldAbsent;

        return isString ? FqdnLookupOutcome.Ok : FqdnLookupOutcome.FieldNotAString;
    }
}

/// <summary>
/// PP254: the lookup where the core writes it.
/// </summary>
public static class WebSocketFqdnSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// THE FINDING. Whether the tokener branch still jumps to cleanup without setting a code.
    ///
    /// True means the defect is present, which is what this asserts rather than a fix.
    /// </summary>
    public static bool TheTokenerBranchStillSetsNoCode(string core)
    {
        string body = Body(core);

        // PP388: one space for the two marks and the slice between them.
        string compact = CCall.Compact(body);

        int logs = CCall.Mark(compact, "Couldn't create new json tokener");
        if (logs < 0)
            return false;

        int leaves = CCall.Mark(compact, "goto cleanup;", logs);
        if (leaves < 0)
            return false;

        // Nothing between the message and the jump assigns the error variable.
        return CCall.Mark(compact[logs..leaves], "err =") < 0;
    }

    /// <summary>
    /// And whether the parse failure directly below it does set one - which is the contrast that
    /// makes the branch above an omission rather than a convention.
    /// </summary>
    public static bool TheParseFailureBelowStillSetsOne(string core)
    {
        string body = Body(core);

        string compact = CCall.Compact(body); // PP388

        int parse = CCall.Mark(compact, "get_websocket_fqdn: Parsing JSON failed");
        if (parse < 0)
            return false;

        int leaves = CCall.Mark(compact, "goto cleanup_json_tokener;", parse);
        return leaves > parse && CCall.Mark(compact[parse..leaves], "err =") >= 0;
    }

    /// <summary>Whether the address is still written only on the way past both checks.</summary>
    public static bool TheAddressIsStillWrittenOnlyAtTheEnd(string core)
    {
        string body = Body(core);

        return body.Split("*fqdn =", StringSplitOptions.None).Length - 1 == 1
            && body.Contains(
                $"*fqdn = strdup(json_object_get_string({WebSocketFqdn.Field}_json));",
                StringComparison.Ordinal);
    }

    /// <summary>And whether the caller still reads only the code, against a field it set to null.</summary>
    public static bool TheCallerStillReadsOnlyTheCode(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("session->ws_fqdn = NULL;", StringComparison.Ordinal)
            && text.Contains(
                """
                ChiakiErrorCode err = get_websocket_fqdn(session, &session->ws_fqdn);
                    if (err != CHIAKI_ERR_SUCCESS)
                        return err;
                """.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal);
    }

    /// <summary>Whether the field is still checked twice, for two different things.</summary>
    public static bool TheFieldIsStillCheckedTwice(string core)
    {
        string body = Body(core);

        return body.Contains(
                $"JSON does not contain \\\"{WebSocketFqdn.Field}\\\" field", StringComparison.Ordinal)
            && body.Contains(
                $"JSON \\\"{WebSocketFqdn.Field}\\\" field is not a string", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the copy is still taken before the document is released - the part that is correct.
    /// </summary>
    public static bool TheCopyStillPrecedesTheRelease(string core)
    {
        string body = Body(core);

        string compact = CCall.Compact(body); // PP388

        int copied = CCall.Mark(compact, "*fqdn = strdup(");
        int released = CCall.At(compact, "json_object_put(json)");

        return copied >= 0 && released > copied;
    }

    /// <summary>
    /// Whether the two unnamed messages are still exactly the two ALLOCATION failures.
    ///
    /// Twelve of the fourteen log lines name the function. The two that do not are the curl handle
    /// and the parser - the only two things here that can fail for want of memory. And of those two
    /// only one reports it: see <see cref="TheOtherAllocationFailureStillReportsIt"/>.
    /// </summary>
    public static bool TheUnnamedMessagesAreStillTheAllocations(string core)
    {
        string body = Body(core);

        int lines = body.Split("CHIAKI_LOG", StringSplitOptions.None).Length - 1;
        int named = body.Split("\"get_websocket_fqdn: ", StringSplitOptions.None).Length - 1;

        return lines - named == 2
            && body.Contains("\"Couldn't create new json tokener\"", StringComparison.Ordinal)
            && body.Contains("\"Curl could not init\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the other allocation failure still returns a code - which is the contrast that
    /// makes the tokener's silence an omission.
    /// </summary>
    public static bool TheOtherAllocationFailureStillReportsIt(string core)
        => Body(core).Contains(
            """
            CHIAKI_LOGE(session->log, "Curl could not init");
                    return CHIAKI_ERR_MEMORY;
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>get_websocket_fqdn's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason nine earlier tasks each wrote down.
        int start = text.LastIndexOf(
            "static ChiakiErrorCode get_websocket_fqdn(Session *session", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic inline size_t curl_write_cb(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
