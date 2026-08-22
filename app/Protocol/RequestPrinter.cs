namespace ChiakiNg.Protocol;

/// <summary>One buffer the printer encodes into.</summary>
/// <param name="Name">What it holds.</param>
/// <param name="SourceBytes">How many bytes go in.</param>
/// <param name="Size">And how big the destination is.</param>
/// <param name="Initialised">Whether it starts zeroed.</param>
public readonly record struct PrintBuffer(string Name, int SourceBytes, int Size, bool Initialised);

/// <summary>
/// PP261: what a reader is shown when a connection request or a candidate is printed.
///
/// A FAILED ENCODE IS HANDLED, AND UNDONE ONE LINE LATER. Both base64 conversions check their result
/// and log a hex dump when it fails. The next statement prints the destination anyway - and that
/// destination is a bare local, never initialised, which the encoder leaves partly written and
/// WITHOUT a terminator when it runs out of room. The handling exists; the line beneath it reads
/// past whatever the encoder managed.
///
/// IT CANNOT FIRE AS WRITTEN, which is a different thing from harmless. Sixteen bytes encode to
/// twenty-four characters into a buffer of twenty-five; twenty encode to twenty-eight into
/// twenty-nine. Both exact - <see cref="Fits"/> computes it rather than asserting it. This is the
/// shape PP244 named on the send loop: unreachable, and a port that dropped the branch as dead would
/// be right by the wrong reasoning.
///
/// The same function zeroes its third buffer, the one that does not go through base64. Two of three
/// left bare, within eleven lines of each other - see <see cref="Buffers"/>.
///
/// AND THE PRINTER CALLS THE STATIC CANDIDATE REMOTE. PP248 measured that the variable named for the
/// remote end holds this side's address as the outside sees it. The label agrees with the name
/// rather than with the thing, so a reader is told the same wrong thing twice.
/// </summary>
public static class RequestPrinter
{
    /// <summary>The three buffers the request printer fills, in the order it fills them.</summary>
    public static IReadOnlyList<PrintBuffer> Buffers { get; } =
    [
        new("skey", SourceBytes: 16, Size: 25, Initialised: false),
        new("mac_addr", SourceBytes: 6, Size: 18, Initialised: true),
        new("local_hashed_id", SourceBytes: 20, Size: 29, Initialised: false),
    ];

    /// <summary>How many characters base64 makes of this many bytes.</summary>
    public static int EncodedLength(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return (bytes + 2) / 3 * 4;
    }

    /// <summary>Whether the encoding fits the buffer, terminator included.</summary>
    public static bool Fits(PrintBuffer buffer) => EncodedLength(buffer.SourceBytes) + 1 <= buffer.Size;

    /// <summary>
    /// Whether the encoder can fail for this buffer - which decides whether the print beneath the
    /// failure branch is reachable.
    /// </summary>
    public static bool CanFail(PrintBuffer buffer) => !Fits(buffer);

    /// <summary>
    /// What would be printed if it did fail: a buffer neither terminated nor initialised.
    /// </summary>
    public static bool WouldPrintUnterminated(PrintBuffer buffer)
        => !buffer.Initialised && buffer.Name != "mac_addr";

    /// <summary>
    /// Whether the print after a failed encode is guarded. It is not - it runs either way.
    /// </summary>
    public const bool ThePrintIsGuarded = false;

    /// <summary>The label the printer gives each candidate type.</summary>
    public static IReadOnlyDictionary<CandidateType, string> Labels { get; } =
        new Dictionary<CandidateType, string>
        {
            [CandidateType.Local] = "LOCAL CANDIDATE",
            [CandidateType.Static] = "REMOTE CANDIDATE",
            [CandidateType.Derived] = "DERIVED CANDIDATE",
            [CandidateType.Stun] = "STUN CANDIDATE",
        };

    /// <summary>What an unrecognised type is labelled.</summary>
    public const string UnknownLabel = "CANDIDATE TYPE UNKNOWN";

    /// <summary>The label for a type, falling through for anything unnamed.</summary>
    public static string LabelFor(CandidateType type)
        => Labels.TryGetValue(type, out string? label) ? label : UnknownLabel;

    /// <summary>
    /// Whether a label describes whose address it is. The static one does not - PP248 showed it
    /// carries this side's address as the outside sees it.
    /// </summary>
    public static bool LabelDescribesWhoseItIs(CandidateType type) => type != CandidateType.Static;

    /// <summary>
    /// Whether the MAC block runs, given what this client sends. It does not - PP259's finding.
    /// </summary>
    public static bool ThePrintsMac(string macSent)
    {
        ArgumentNullException.ThrowIfNull(macSent);
        return macSent.Length > 0;
    }
}

/// <summary>
/// PP261: the printers where the core writes them.
/// </summary>
public static class RequestPrinterSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// THE FINDING. Whether the print after each failure branch is still unconditional.
    /// </summary>
    public static bool ThePrintAfterAFailureIsStillUnconditional(string core)
    {
        string body = Body(core);

        foreach ((string logged, string printed) in new[]
        {
            ("Error with base64 encoding of skey: %s", "CHIAKI_LOGV(log, \"skey: %s\", skey);"),
            ("Error with base64 encoding of local hashed id: %s",
             "CHIAKI_LOGV(log, \"local hashed id %s\", local_hashed_id);"),
        })
        {
            int fails = body.IndexOf(logged, StringComparison.Ordinal);
            int prints = body.IndexOf(printed, StringComparison.Ordinal);

            // Logged, the branch closes, and the print follows it outside.
            if (fails < 0 || prints <= fails)
                return false;

            if (body[fails..prints].Contains("if(err == CHIAKI_ERR_SUCCESS)", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>And whether the two encoded buffers are still declared bare.</summary>
    public static bool TheTwoEncodedBuffersAreStillBare(string core)
    {
        string body = Body(core);

        return body.Contains("char skey[25];", StringComparison.Ordinal)
            && body.Contains("char local_hashed_id[29];", StringComparison.Ordinal)

            // While the third, which is not encoded, is zeroed.
            && body.Contains("char mac_addr[18] = {0};", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the encoder still gives up without terminating - which is what makes the print a
    /// read past the end rather than an empty string.
    /// </summary>
    public static bool TheEncoderStillLeavesItUnterminated(string encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        string text = encoder.Replace("\r\n", "\n", StringComparison.Ordinal);

        int guard = text.IndexOf("if(result_index >= out_size)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int gives = text.IndexOf("return CHIAKI_ERR_BUF_TOO_SMALL;", guard, StringComparison.Ordinal);

        return gives > guard
            && !text[guard..gives].Contains("= '\\0'", StringComparison.Ordinal);
    }

    /// <summary>Where the encoder lives, so the check above has something to read.</summary>
    public static string? LocateEncoder()
        => ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\src\base64.c");

    /// <summary>Whether the source sizes are still the ones that make the branch unreachable.</summary>
    public static bool TheSizesStillMakeItUnreachable(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("uint8_t skey[16];", StringComparison.Ordinal)
            && text.Contains("uint8_t local_hashed_id[20];", StringComparison.Ordinal);
    }

    /// <summary>Whether the static candidate is still labelled for the remote end.</summary>
    public static bool TheStaticIsStillLabelledRemote(string core)
    {
        string body = CandidateBody(core);

        return body.Contains("case CANDIDATE_TYPE_STATIC:", StringComparison.Ordinal)
            && body.Contains(
                $"--{RequestPrinter.LabelFor(CandidateType.Static)}--", StringComparison.Ordinal);
    }

    /// <summary>And whether every type still has a label, with a fall-through for the rest.</summary>
    public static bool EveryTypeStillHasALabel(string core)
    {
        string body = CandidateBody(core);

        foreach (CandidateType type in Enum.GetValues<CandidateType>())
        {
            if (!body.Contains($"--{RequestPrinter.LabelFor(type)}--", StringComparison.Ordinal))
                return false;
        }

        return body.Contains($"--{RequestPrinter.UnknownLabel}--", StringComparison.Ordinal);
    }

    /// <summary>print_session_request's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, and spelled as the definition spells it - PP258's lesson.
        int start = text.LastIndexOf(
            "static void print_session_request(ChiakiLog *log", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\n/**", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>And print_candidate's.</summary>
    private static string CandidateBody(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(
            "static void print_candidate(ChiakiLog *log", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\n/**", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
