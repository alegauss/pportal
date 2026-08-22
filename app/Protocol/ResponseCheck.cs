namespace ChiakiNg.Protocol;

/// <summary>What one response is judged to be.</summary>
public enum ResponseVerdict
{
    /// <summary>It is the answer to the probe, and counts.</summary>
    Accepted,

    /// <summary>It is the console probing us, and gets a reply rather than a count.</summary>
    ConsoleProbing,

    /// <summary>Not the length a probe answer is.</summary>
    WrongSize,

    /// <summary>Neither a request nor a response.</summary>
    WrongType,

    /// <summary>The right shape, but not echoing the bytes this side sent.</summary>
    WrongRequestId,
}

/// <summary>What the loop does with a verdict.</summary>
public enum VerdictAction
{
    /// <summary>Count it, and either select this candidate or probe it again.</summary>
    Count,

    /// <summary>Answer the console and go round again.</summary>
    Reply,

    /// <summary>Drop this one and go round again, leaving no error behind.</summary>
    DropQuietly,

    /// <summary>Drop this one and go round again, having recorded an error nobody will read.</summary>
    DropRecording,

    /// <summary>End the punch.</summary>
    Abort,
}

/// <summary>
/// PP247: deciding whether a response answers the probe, and picking the candidate.
///
/// A DERIVED CANDIDATE IS EXEMPT FROM EVERY ABORT. Three checks would end the punch - a response of
/// the wrong size, one of an unrecognised type, and a failure replying to the console's own probe -
/// and each carries the same escape clause: a candidate this code DISCOVERED from incoming traffic
/// rather than one the console offered skips the abort and the loop carries on. An address that
/// arrived unannounced is therefore trusted further than one that was named, which is the reverse
/// of what a reader would assume. <see cref="Action"/> takes the type for exactly this reason.
///
/// A WRONG REQUEST ID IS DROPPED IN SILENCE. Six log lines, three of them hexdumps, and no error
/// recorded at all - the loudest branch in the function and the only one leaving nothing a caller
/// could act on.
///
/// AND THE COMMENT ABOVE IT ASKS FOR WHAT THE NEXT LINE DOES. It wonders whether "the weird data at
/// 0x4b" wants validating; the line below it compares that field against the five bytes this side
/// generated. PP243 measured where those bytes are written and PP236 where they come back, so this
/// port can name it: not weird data - the only thing in the packet proving the answer is ours.
///
/// THE RETRANSMIT BRANCH CANNOT RUN. Selection fires when the count passes one less than the probe
/// count, and the probe count is one, so a first valid response always selects. The branch is
/// correct for a larger count and unreachable at this one, and is kept rather than simplified for
/// the reason PP244 kept its clause.
/// </summary>
public static class ResponseCheck
{
    /// <summary>Where the echoed bytes sit, and how many - PP243's offset, PP236's length.</summary>
    public const int EchoAt = PunchResponse.EchoAt;

    /// <summary>How many.</summary>
    public const int EchoLength = PunchResponse.EchoLength;

    /// <summary>
    /// What the comment above the check calls that field, and what it actually is.
    /// </summary>
    public const string WhatTheCommentCallsIt = "the weird data at 0x4b";

    /// <summary>Judges one response.</summary>
    /// <param name="length">How many bytes arrived.</param>
    /// <param name="messageType">The word at the front.</param>
    /// <param name="echo">The five bytes at the echo offset.</param>
    /// <param name="sent">The five bytes this side put in the probe.</param>
    public static ResponseVerdict Verdict(
        int length, uint messageType, ReadOnlySpan<byte> echo, ReadOnlySpan<byte> sent)
    {
        // The size is checked first, and it is checked against the whole packet - so a request from
        // the console has to be full length too.
        if (length != PunchProbe.Length)
            return ResponseVerdict.WrongSize;

        if (messageType == PunchProbe.RequestType)
            return ResponseVerdict.ConsoleProbing;

        if (messageType != PunchResponse.ResponseType)
            return ResponseVerdict.WrongType;

        return echo.SequenceEqual(sent) ? ResponseVerdict.Accepted : ResponseVerdict.WrongRequestId;
    }

    /// <summary>
    /// What the loop does with that verdict, which depends on how the candidate was arrived at.
    /// </summary>
    /// <param name="verdict">The judgement.</param>
    /// <param name="type">How this candidate was arrived at.</param>
    /// <param name="replySucceeded">For a console probe, whether answering it worked.</param>
    public static VerdictAction Action(
        ResponseVerdict verdict, CandidateType type, bool replySucceeded = true)
    {
        bool derived = type == CandidateType.Derived;

        return verdict switch
        {
            ResponseVerdict.Accepted => VerdictAction.Count,

            // The reply's failure is fatal - unless this candidate was discovered rather than named.
            ResponseVerdict.ConsoleProbing when replySucceeded => VerdictAction.Reply,
            ResponseVerdict.ConsoleProbing => derived ? VerdictAction.Reply : VerdictAction.Abort,

            ResponseVerdict.WrongSize => derived ? VerdictAction.DropQuietly : VerdictAction.Abort,
            ResponseVerdict.WrongType => derived ? VerdictAction.DropRecording : VerdictAction.Abort,

            // No exemption needed - this one never aborts for anybody, and records nothing either.
            _ => VerdictAction.DropQuietly,
        };
    }

    /// <summary>Whether a verdict leaves an error code a caller could read.</summary>
    public static bool RecordsAnError(ResponseVerdict verdict, CandidateType type)
        => Action(verdict, type) is VerdictAction.Abort or VerdictAction.DropRecording;

    /// <summary>How many lines the silent drop prints before dropping.</summary>
    public const int LinesTheSilentDropPrints = 6;

    /// <summary>Whether this many counted responses settles on the candidate.</summary>
    public static bool Selects(int responses) => PunchProbe.Answered(responses);

    /// <summary>
    /// Whether the retransmit branch can be reached, given the probe count.
    ///
    /// It cannot at one: a first valid response takes the count to one, which already selects.
    /// </summary>
    public static bool RetransmitIsReachable(int probeCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(probeCount, 1);

        // The branch runs for counts strictly between zero and the probe count.
        return probeCount > 1;
    }
}

/// <summary>
/// PP247: the checking where the core writes it.
/// </summary>
public static class ResponseCheckSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>
    /// Whether all three aborts still carry the same escape for a discovered candidate.
    /// </summary>
    public static bool AllThreeAbortsStillExemptADerivedCandidate(string core)
    {
        string body = Body(core);

        // The size check and the type check each ask the same question and continue.
        int escapes = body.Split(
            "if(candidate->type == CANDIDATE_TYPE_DERIVED)\n                continue;",
            StringSplitOptions.None).Length - 1;

        // And the reply's failure asks it the other way round, in the same statement as the abort.
        bool reply = body.Contains(
            "if(err != CHIAKI_ERR_SUCCESS && candidate->type != CANDIDATE_TYPE_DERIVED)",
            StringComparison.Ordinal);

        return escapes == 2 && reply;
    }

    /// <summary>
    /// Whether the two aborts still put their error code on opposite sides of the escape.
    ///
    /// The size check sets it AFTER, so a discovered candidate leaves nothing behind. The type
    /// check sets it BEFORE, so one leaves a stale code that PP244 measured surviving to the end of
    /// the function. Two branches written to look alike, differing in the one line's placement that
    /// decides it.
    /// </summary>
    public static bool TheErrorSitsOnOppositeSidesOfTheEscape(string core)
    {
        string body = Body(core);

        int sizeEscape = body.IndexOf(
            "if(candidate->type == CANDIDATE_TYPE_DERIVED)", StringComparison.Ordinal);
        int sizeError = body.IndexOf("err = CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);

        int typeError = body.IndexOf("err = CHIAKI_ERR_UNKNOWN;", StringComparison.Ordinal);

        // PP272: answered rather than thrown. Searching from a position of minus one raises, and a
        // check that raises on an empty file is telling a reader about the check rather than about
        // the file.
        if (sizeEscape < 0 || sizeError <= sizeEscape || typeError <= sizeError)
            return false;

        int typeEscape = body.IndexOf(
            "if(candidate->type == CANDIDATE_TYPE_DERIVED)", typeError, StringComparison.Ordinal);

        return typeEscape > typeError;
    }

    /// <summary>Whether the wrong-id branch still records nothing.</summary>
    public static bool TheWrongIdBranchStillRecordsNothing(string core)
    {
        string body = Body(core);

        int starts = body.IndexOf(
            "Received response with unexpected request ID from", StringComparison.Ordinal);
        if (starts < 0)
            return false;

        int ends = body.IndexOf("\n            continue;\n        }", starts, StringComparison.Ordinal);
        if (ends < 0)
            return false;

        string branch = body[starts..ends];

        return !branch.Contains("err =", StringComparison.Ordinal)
            && branch.Split("CHIAKI_LOG", StringSplitOptions.None).Length - 1
                == ResponseCheck.LinesTheSilentDropPrints;
    }

    /// <summary>
    /// Whether the comment still asks for the validation the next line performs.
    /// </summary>
    public static bool TheCommentStillAsksForWhatFollows(string core)
    {
        string body = Body(core);

        int asks = body.IndexOf(ResponseCheck.WhatTheCommentCallsIt, StringComparison.Ordinal);
        int does = body.IndexOf(
            $"if(memcmp(response_buf + 0x{ResponseCheck.EchoAt:x}, request_id[responses],",
            StringComparison.Ordinal);

        // Asked, and answered by the very next statement.
        return asks >= 0 && does > asks && does - asks < 200;
    }

    /// <summary>Whether selection is still the count passing one less than the probe count.</summary>
    public static bool SelectionIsStillThatTest(string core)
        => Body(core).Contains(
                "if(responses > (CHECK_CANDIDATES_REQUEST_NUMBER - 1))", StringComparison.Ordinal)
            && Body(core).Contains("selected_candidate = candidate;", StringComparison.Ordinal);

    /// <summary>
    /// And whether the retransmit branch still indexes with a value that stays in bounds for any
    /// probe count - which it does, and is why the port keeps it rather than calling it a defect.
    /// </summary>
    public static bool TheRetransmitStillIndexesInBounds(string core)
        => Body(core).Contains(
            "sendto(candidate_sock, (CHIAKI_SOCKET_BUF_TYPE) request_buf[responses], sizeof(request_buf[responses])",
            StringComparison.Ordinal);

    /// <summary>The checking, from the type test to the end of the wait loop.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason seven earlier tasks each wrote down.
        int function = text.LastIndexOf(
            "static ChiakiErrorCode check_candidates(", StringComparison.Ordinal);
        if (function < 0)
            return "";

        // From the size check, so both DERIVED escapes are inside.
        int start = text.IndexOf(
            "        if (response_len != sizeof(response_buf))", function, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("    *out = selected_sock;", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
