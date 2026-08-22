using System.Text;
using System.Text.Json;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP215: one frame, one document, and no parser state in between.
///
/// A four-line class carrying one rule, and the rule is the whole task. The core's websocket loop
/// makes a json-c tokener before its frame loop and feeds it every frame for the life of the
/// socket. Measured (see FrameParsingTests, and <see cref="NativeJsonTokener"/> which is what does
/// the measuring): that is not per-frame state which a bad frame spoils and the next frame
/// refreshes. It is a STREAM. Once any frame fails to complete a document, no later frame on that
/// tokener produces one - a truncated frame swallows the next as its continuation, garbage stops it
/// at once, and only json_tokener_reset clears it. That call appears nowhere in holepunch.c.
///
/// What the core does with a frame that parsed to nothing is read the next one, which reads as
/// "that one was bad". It is not: it is "and every one after it".
///
/// So this port needs no decision about whether to reproduce that - it needs a rule that keeps it
/// unreachable, and this is the rule. Nothing here is held between calls. The frames could arrive
/// in any order, on any thread, with any amount of garbage between them, and the one after a bad
/// one still parses.
///
/// The parse itself is <see cref="JsonC.Parse"/> rather than a second reader, because what a
/// document MEANS must stay json-c's answer even where how it is fed does not.
/// </summary>
public static class FrameParsing
{
    /// <summary>
    /// One frame's bytes as one document, or null where they are not one.
    ///
    /// Bytes and not a string, because that is how a frame arrives and because the length is the
    /// frame's rather than a terminator's - the core reads exactly the received length out of a
    /// buffer it zeroed, and a port that stopped at the first NUL would truncate a frame that
    /// happened to contain one.
    /// </summary>
    public static JsonDocument? Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length > PushSocketLoop.MaxFrameSize)
            return null;

        // .NET's default decoder substitutes a replacement character for bytes that are not UTF-8
        // rather than refusing them. What json-c does with the same bytes is NOT measured here and
        // so is not claimed: PSN sends UTF-8, and a frame that is not is out of this task's scope.
        return JsonC.Parse(Encoding.UTF8.GetString(frame));
    }
}

/// <summary>
/// PP215: the one reused tokener where the core writes it.
/// </summary>
public static class FrameParsingSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>
    /// Whether the loop still makes ONE tokener outside its loop and feeds it inside. The three
    /// positions are the whole shape: created, then looped, then fed.
    /// </summary>
    public static bool TheLoopStillKeepsOneTokener(string core)
    {
        string body = Body(core);

        int created = body.IndexOf("json_tokener *tok = json_tokener_new();", StringComparison.Ordinal);
        int loop = body.IndexOf("while (true)", StringComparison.Ordinal);
        int fed = body.IndexOf("json_tokener_parse_ex(tok, buf, rlen)", StringComparison.Ordinal);

        return created >= 0 && loop > created && fed > loop;
    }

    /// <summary>Whether the reset that would clear it is still absent from the whole file.</summary>
    public static bool TheTokenerIsStillNeverReset(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // PP272: there has to be a tokener before "never reset" says anything about one.
        return core.Contains("json_tokener_new", StringComparison.Ordinal)
            && !core.Contains("json_tokener_reset", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether every OTHER tokener in the file is still one document long. Counted rather than
    /// located: as many tokeners as parses means each was made for its own, and the loop's pair is
    /// the one that breaks the correspondence by being fed repeatedly.
    /// </summary>
    public static bool EveryOtherTokenerIsStillOneDocumentLong(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int created = Count(core, "json_tokener_new()");
        int fed = Count(core, "json_tokener_parse_ex(");

        return created > 0 && created == fed;
    }

    /// <summary>Whether a frame that parsed to nothing still only costs that frame, as written.</summary>
    public static bool ABadFrameStillOnlyReadsTheNextOne(string core)
    {
        string body = Body(core);

        int refused = body.IndexOf("if (json == NULL)", StringComparison.Ordinal);
        if (refused < 0)
            return false;

        int carryOn = body.IndexOf("continue;", refused, StringComparison.Ordinal);
        return carryOn > refused;
    }

    private static int Count(string core, string needle)
    {
        int found = 0;
        for (int at = core.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = core.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    /// <summary>websocket_thread_func's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(
            "uint64_t timeout = WEBSOCKET_PING_INTERVAL_SEC * 1000;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf("cleanup_json:", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
