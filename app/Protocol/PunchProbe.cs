using System.Buffers.Binary;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP243: the probe sent to every candidate address, and the arrays sized to answer it.
///
/// THE PROBE IS PP236'S PACKET WITH A DIFFERENT FRONT. Same eighty-eight bytes, same identifiers at
/// 0x04 and 0x24 in their thirty-two byte slots, same session ids at 0x44 - and five random bytes at
/// 0x4b, which is the offset PP236 measured the response echoing back. That task could port the echo
/// without knowing what was echoed. This is what was echoed, and the two halves now assert against
/// the same constants rather than against two independently-read numbers that happen to agree.
///
/// What the probe does NOT carry is the key: no second copy of the ids at 0x50, no address hidden
/// under them. The obfuscation is the answer's, one direction only.
///
/// AND THE COUNT IS THE OTHER END'S. The number of candidates comes off a JSON array in a session
/// message. Four arrays are sized from it - addresses, their lengths, the candidates, a tally of
/// responses - and in the core all four are variable-length arrays, which is to say stack. The heap
/// allocation that holds the parsed candidates is checked for failure because it can report one; a
/// stack array cannot, so there is nothing there to check and nothing missing that a reader would
/// notice. Reproduced, not fixed - <see cref="StackBytesFor"/> is the measurement, so the port can
/// state the size rather than describe it.
///
/// One more thing dimensioned to a constant: the probe count is 1. The loop that fills the buffers,
/// both dimensions of them, and the test deciding a candidate answered are all written in terms of
/// it, so raising it changes every one of those places at once.
/// </summary>
public static class PunchProbe
{
    /// <summary>The probe's size, which is the reply's.</summary>
    public const int Length = PunchResponse.Length;

    /// <summary>MSG_TYPE_REQ, at the front where the reply puts MSG_TYPE_RESP.</summary>
    public const uint RequestType = PunchResponse.RequestType;

    /// <summary>Where the five random bytes go - the offset the reply echoes from.</summary>
    public const int RequestIdAt = PunchResponse.EchoAt;

    /// <summary>How many.</summary>
    public const int RequestIdLength = PunchResponse.EchoLength;

    /// <summary>How many probes are built and sent. One.</summary>
    public const int RequestCount = 1;

    /// <summary>How many addresses beyond the console's list are made room for.</summary>
    public const int ExtraAddresses = 3;

    /// <summary>
    /// Builds one probe.
    /// </summary>
    /// <param name="requestId">The five random bytes this probe will be recognised by.</param>
    /// <param name="localId">The local hashed identifier, twenty bytes.</param>
    /// <param name="consoleId">The console's.</param>
    /// <param name="sidLocal">This side's session id.</param>
    /// <param name="sidConsole">The console's.</param>
    public static byte[] Build(
        ReadOnlySpan<byte> requestId,
        ReadOnlySpan<byte> localId,
        ReadOnlySpan<byte> consoleId,
        ushort sidLocal,
        ushort sidConsole)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(requestId.Length, RequestIdLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(localId.Length, PunchResponse.IdLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(consoleId.Length, PunchResponse.IdLength);

        byte[] packet = new byte[Length];

        BinaryPrimitives.WriteUInt32BigEndian(packet, RequestType);

        localId.CopyTo(packet.AsSpan(PunchResponse.LocalIdAt, PunchResponse.IdLength));
        consoleId.CopyTo(packet.AsSpan(PunchResponse.ConsoleIdAt, PunchResponse.IdLength));

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PunchResponse.SessionIdsAt), sidLocal);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PunchResponse.SessionIdsAt + 2), sidConsole);

        // And the thing the answer will hand back. Nothing else - the key is the reply's alone.
        requestId.CopyTo(packet.AsSpan(RequestIdAt, RequestIdLength));

        return packet;
    }

    /// <summary>
    /// Whether a candidate that answered this many times counts as having answered.
    ///
    /// Written as "more than one less than the probe count" rather than "more than none", which with
    /// a probe count of one is the same test and with any other number is not.
    /// </summary>
    public static bool Answered(int responses) => responses > RequestCount - 1;

    /// <summary>How long each of the four arrays is, for a count the console chose.</summary>
    public static int SlotsFor(int candidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateCount);
        return candidateCount + ExtraAddresses;
    }

    /// <summary>How many arrays are on the stack. Four.</summary>
    public const int StackArrays = 4;

    /// <summary>
    /// What one slot costs across all four, on the ABI the core is built for.
    ///
    /// A sockaddr_storage, a socklen_t, a Candidate and an int. The exact total moves with the
    /// platform; what does not is that it is per-candidate and the candidate count is not ours.
    /// </summary>
    public const int BytesPerSlot = 128 + 4 + 96 + 4;

    /// <summary>
    /// The stack these four arrays take for a given count - the measurement, not a judgement.
    /// </summary>
    public static long StackBytesFor(int candidateCount)
        => (long)SlotsFor(candidateCount) * BytesPerSlot;

    /// <summary>
    /// The smallest count whose arrays do not fit a one-megabyte thread stack.
    ///
    /// Stated as a number so the absence of a bound is a size rather than an adjective. Nothing in
    /// the core keeps the count below it.
    /// </summary>
    public static int CountThatOverrunsAMegabyte()
    {
        const long stack = 1024 * 1024;

        int count = 0;
        while (StackBytesFor(count) <= stack)
            count++;

        return count;
    }
}

/// <summary>
/// PP243: the probe where the core writes it.
/// </summary>
public static class PunchProbeSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the probe is still laid out where the reply expects it.</summary>
    public static bool TheProbeIsStillLaidOutThatWay(string core)
    {
        string body = Body(core);

        return body.Contains($"request_buf[CHECK_CANDIDATES_REQUEST_NUMBER][{PunchProbe.Length}]", StringComparison.Ordinal)
            && body.Contains("&request_buf[i][0x00] = htonl(MSG_TYPE_REQ)", StringComparison.Ordinal)
            && body.Contains("memcpy(&request_buf[i][0x04], session->hashed_id_local", StringComparison.Ordinal)
            && body.Contains("memcpy(&request_buf[i][0x24], session->hashed_id_console", StringComparison.Ordinal)
            && body.Contains("&request_buf[i][0x44] = htons(session->sid_local)", StringComparison.Ordinal)
            && body.Contains("&request_buf[i][0x46] = htons(session->sid_console)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the random bytes still go where the reply echoes from - the pairing this task exists
    /// to nail down.
    /// </summary>
    public static bool TheRandomBytesStillGoWhereTheReplyEchoesFrom(string core)
        => Body(core).Contains(
            $"memcpy(&request_buf[i][0x{PunchProbe.RequestIdAt:x}], request_id[i]", StringComparison.Ordinal);

    /// <summary>And whether the probe still carries no key, which is what makes it one-directional.</summary>
    public static bool TheProbeStillCarriesNoKey(string core)
    {
        string body = Body(core);

        return !body.Contains("request_buf[i][0x50]", StringComparison.Ordinal)
            && !body.Contains("request_buf[i][0x54]", StringComparison.Ordinal);
    }

    /// <summary>Whether the four arrays are still sized by the count and still on the stack.</summary>
    public static bool TheFourArraysAreStillStackSizedByTheCount(string core)
    {
        string body = Body(core);

        // A VLA is what these are: the length is an expression, not a constant.
        return body.Contains(
                "struct sockaddr_storage addrs[num_candidates + EXTRA_CANDIDATE_ADDRESSES];", StringComparison.Ordinal)
            && body.Contains(
                "socklen_t lens[num_candidates + EXTRA_CANDIDATE_ADDRESSES];", StringComparison.Ordinal)
            && body.Contains(
                "Candidate candidates[num_candidates + EXTRA_CANDIDATE_ADDRESSES];", StringComparison.Ordinal)
            && body.Contains(
                "int responses_received[num_candidates + EXTRA_CANDIDATE_ADDRESSES];", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the count still arrives off a parsed array with nothing between it and the use.
    ///
    /// The heap allocation right beside it IS checked, which is the contrast worth keeping: the
    /// author checked what could report a failure.
    /// </summary>
    public static bool TheCountStillArrivesUnbounded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                """
                size_t num_candidates = json_object_array_length(obj);
                        msg->conn_request->num_candidates = num_candidates;
                        msg->conn_request->candidates = calloc(num_candidates, sizeof(Candidate));
                        if(!msg->conn_request->candidates)
                """.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal)
            && text.Contains(
                "check_candidates(session, session->local_candidates, console_req->candidates, console_req->num_candidates,",
                StringComparison.Ordinal);
    }

    /// <summary>And whether the probe count is still one, spelled as a constant everywhere.</summary>
    public static bool TheProbeCountIsStillOne(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
                $"#define CHECK_CANDIDATES_REQUEST_NUMBER {PunchProbe.RequestCount}", StringComparison.Ordinal)
            && core.Contains(
                $"#define EXTRA_CANDIDATE_ADDRESSES {PunchProbe.ExtraAddresses}", StringComparison.Ordinal)
            && Body(core).Contains(
                "responses > (CHECK_CANDIDATES_REQUEST_NUMBER - 1)", StringComparison.Ordinal);
    }

    /// <summary>check_candidates, from its definition on.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason PP213, PP233, PP234 and PP236 each wrote down: the forward
        // declaration at the top of the file is a prefix of the definition.
        int start = text.LastIndexOf("static ChiakiErrorCode check_candidates(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic ", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
