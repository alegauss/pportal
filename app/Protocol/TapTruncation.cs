using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP612, under PP27: where the eighteen bytes are decided, and why moving them is not this line's
/// to do.
///
/// PP510 chose to keep eighteen bytes a datagram and gave the reason - enough for the dispatch and
/// the MAC layout, no frame of anybody's screen. That reads as a decision the port made and could
/// unmake. It is not: the truncation happens in the C, at the tap's emit, before a managed byte is
/// involved. <see cref="HeadConstant"/> is defined in lib/include/chiaki/messagetap.h and applied
/// in takion.c, and both are vendored.
///
/// SO A CAPTURE WITH PAYLOADS IS THE PATCH THE NON-GOAL REFUSES. PP601 read that rule against this
/// line: only PP33's deletion and PP30's port are named as what it does not reach, and PP593 put
/// both there. PP27 is not among them, so raising the constant - or emitting the whole buffer
/// beside it - is a local patch to the vendored C, and the drift checks would then agree with a
/// libchiaki nobody else runs.
///
/// THE TWO DOORS, so the next reader does not go looking for a third. Narrow the non-goal, which is
/// a decision and one line of `non-goal amend`; or put a managed relay between the console and the
/// C, which sees every byte because it forwards them - the shape PP607 already runs on loopback,
/// and which needs a session the host can start, so PP600.
///
/// Neither is code this line can write without being told which.
/// </summary>
public static class TapTruncation
{
    /// <summary>The constant that decides how much of a datagram the tap emits.</summary>
    public const string HeadConstant = "CHIAKI_MESSAGE_TAP_TAKION_HEAD";

    /// <summary>Where it is defined.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\messagetap.h";

    /// <summary>Where it is applied, at the emit.</summary>
    public const string SourceRelativePath = @"lib\src\takion.c";

    /// <summary>How many bytes it names, which is what PP510's capture keeps.</summary>
    public const int Head = TakionTimingCapture.HeadBytes;

    /// <summary>messagetap.h, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateRelative(SourceRelativePath);

    /// <summary>
    /// Whether the header still defines the constant at the width the managed capture assumes.
    ///
    /// Both halves of one fact: the C decides the width, and the managed side records what it is
    /// handed. A header that raised it without the capture noticing would leave every head longer
    /// than the model says and every claim about "eighteen" quietly false.
    /// </summary>
    public static bool TheHeaderDefinesTheHead(string messageTapHeader)
    {
        ArgumentNullException.ThrowIfNull(messageTapHeader);

        return messageTapHeader.Contains(
            $"#define {HeadConstant} {Head}", StringComparison.Ordinal);
    }

    /// <summary>Whether takion.c still applies it, rather than emitting some other length.</summary>
    public static bool TheEmitAppliesIt(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        foreach (string line in takionSource.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                continue;

            if (trimmed.Contains(HeadConstant, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
