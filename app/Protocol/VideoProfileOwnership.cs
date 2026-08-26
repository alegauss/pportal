using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Who owns the decoded video headers at a point in the streaminfo handler.</summary>
public enum ProfileOwner
{
    /// <summary>Nothing has been decoded yet, so there is nothing to own.</summary>
    Nobody,

    /// <summary>The decode context holds them, and whoever leaves from here must free them.</summary>
    TheContext,

    /// <summary>The video receiver took them, and freeing them here would be a double free.</summary>
    TheReceiver,
}

/// <summary>
/// PP372: a decoded video header has no owner until the receiver takes it, and three paths lost it.
///
/// The resolution callback mallocs a header per resolution and reallocs it with padding. Ownership
/// moves to the video receiver in ONE memcpy, inside `chiaki_video_receiver_stream_info`. Everything
/// before that point, and every path that never reaches it, leaves the headers with the decode
/// context - which had no free anywhere.
///
/// THREE PATHS, ONE SHAPE:
///
/// - the callback reallocs the header BEFORE testing the profile count against the maximum, so every
///   resolution past the maximum was decoded, padded and dropped. The console chooses how many it
///   announces.
/// - the handler's `error` label is reached with resolutions already decoded - an audio header of the
///   wrong size gets there - and freed none of them.
/// - the receiver itself declined on one path, profiles already set, having taken nothing, while its
///   own documentation promised the transfer unconditionally.
///
/// The third is why this is one task and not three: the transfer was a memcpy with no answer, so a
/// caller could not tell whether it still owned what it had passed. It answers now.
///
/// WHAT IS MODELLED HERE is the ownership itself, because that is the part a reader of the C cannot
/// see: which step moves it, and therefore which exits owe a free and which owe none.
/// </summary>
public static partial class VideoProfileOwnership
{
    /// <summary>How many profiles the receiver has room for.</summary>
    public const int ProfilesMax = 8;

    /// <summary>
    /// Who owns the headers once the handler has reached a given step.
    /// </summary>
    /// <param name="decoded">Whether the protobuf decode has run, which is what allocates them.</param>
    /// <param name="receiverAccepted">
    /// Whether the receiver returned success. Null where the handover has not been attempted.
    /// </param>
    public static ProfileOwner OwnerAfter(bool decoded, bool? receiverAccepted)
    {
        if (!decoded)
            return ProfileOwner.Nobody;

        // Declining is not a transfer. The receiver copies the array in one go or not at all, so a
        // refusal leaves every header exactly where it was.
        return receiverAccepted == true ? ProfileOwner.TheReceiver : ProfileOwner.TheContext;
    }

    /// <summary>Whether an exit taken at this point has to free the headers.</summary>
    public static bool MustFree(ProfileOwner owner) => owner == ProfileOwner.TheContext;

    /// <summary>
    /// How many headers survive a console announcing <paramref name="announced"/> resolutions.
    ///
    /// The count is the console's, the room is not. Past the maximum the callback keeps decoding,
    /// because it has to consume the stream, so what matters is that it stops ALLOCATING - which is
    /// what moving the check above the realloc does.
    /// </summary>
    public static int HeadersKept(int announced)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(announced);

        return Math.Min(announced, ProfilesMax);
    }

    /// <summary>
    /// How many headers get padded, which is the allocation the count check now sits above.
    ///
    /// It equals <see cref="HeadersKept"/>. That equality IS the fix: below the realloc, the callback
    /// padded one per announced resolution and kept only the first eight.
    /// </summary>
    public static int HeadersPadded(int announced) => HeadersKept(announced);
}

/// <summary>PP372: the ownership held against the two files it lives in.</summary>
public static class VideoProfileOwnershipSource
{
    /// <summary>Where the handler and the callback live.</summary>
    public const string StreamRelativePath = @"lib\src\streamconnection.c";

    /// <summary>Where the handover lives.</summary>
    public const string ReceiverRelativePath = @"lib\src\videoreceiver.c";

    /// <summary>Where the promise is written down.</summary>
    public const string ReceiverHeaderRelativePath = @"lib\include\chiaki\videoreceiver.h";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Whether the profile-count check still sits ABOVE the realloc it used to sit below.
    ///
    /// Order is the whole of this one: below it, a header is padded for a profile there is no room
    /// for and then dropped.
    /// </summary>
    public static bool TheCountIsCheckedBeforeTheHeaderIsPadded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = CFunction.Body(core, "static bool pb_decode_resolution(");
        if (body is null)
            return false;

        int check = body.IndexOf("video_profiles_count >= CHIAKI_VIDEO_PROFILES_MAX", StringComparison.Ordinal);
        int pad = body.IndexOf("realloc(header_buf.buf", StringComparison.Ordinal);
        if (check < 0 || pad < 0 || check > pad)
            return false;

        // And the check frees before it leaves, since the header is already decoded by then.
        return body[check..pad].Contains("free(header_buf.buf);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the handover still answers whether it took them.
    ///
    /// A void return is the defect: the caller cannot distinguish a transfer from a refusal, which is
    /// the reason the refusal path leaked in silence.
    /// </summary>
    public static bool TheHandoverAnswers(string receiverCore, string headerCore)
    {
        ArgumentNullException.ThrowIfNull(receiverCore);
        ArgumentNullException.ThrowIfNull(headerCore);

        return receiverCore.Contains(
                "ChiakiErrorCode chiaki_video_receiver_stream_info(", StringComparison.Ordinal)
            && headerCore.Contains(
                "CHIAKI_EXPORT ChiakiErrorCode chiaki_video_receiver_stream_info(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every exit from the streaminfo handler that leaves while the context still owns the headers,
    /// and does not free them.
    ///
    /// The handover is the dividing line. Before it the context owns them, so every `return` and
    /// `goto error` owes a free; after a SUCCESSFUL one the receiver owns them, so a free there would
    /// be a double free. The exits before the decode are covered too - freeing an empty context is a
    /// no-op, which is why one helper at every exit is simpler to hold than a case analysis.
    /// </summary>
    /// <returns>The exit text of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> ExitsThatLoseTheHeaders(string streamCore)
    {
        ArgumentNullException.ThrowIfNull(streamCore);

        string? body = CFunction.Body(
            streamCore, "static void stream_connection_takion_data_expect_streaminfo(");
        if (body is null)
            return ["the streaminfo handler was not found at all"];

        // Everything up to the handover: past it, ownership has moved.
        int handover = body.IndexOf("chiaki_video_receiver_stream_info(", StringComparison.Ordinal);
        if (handover < 0)
            return ["the handover was not found, so ownership cannot be reasoned about"];

        string beforeHandover = body[..handover];
        var losing = new List<string>();

        foreach (Match exit in Regex.Matches(beforeHandover, @"^[ \t]*(return;|goto error;)", RegexOptions.Multiline))
        {
            // The free has to be ADJACENT to the exit, not merely somewhere above it. A window wide
            // enough to reach past an intervening branch would pass a `return` the free never runs
            // before - which is how a leak on one arm of an if hides behind a free on the other. Two
            // lines of slack, so a log line between the free and the leaving is allowed.
            int from = Math.Max(0, exit.Index - 200);
            string leadUp = beforeHandover[from..exit.Index];

            if (!leadUp.Contains("decode_resolutions_context_free(", StringComparison.Ordinal))
                losing.Add(exit.Value.Trim());
        }

        return losing;
    }
}
