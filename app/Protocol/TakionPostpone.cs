using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What postponing one AV packet did.</summary>
public enum PostponeOutcome
{
    /// <summary>The array was made and the packet is the first in it.</summary>
    AllocatedAndBuffered,

    /// <summary>Buffered into an array that already existed.</summary>
    Buffered,

    /// <summary>The array could not be allocated. The packet is dropped.</summary>
    AllocationFailed,

    /// <summary>The array is full. The packet is dropped, with a log.</summary>
    NoSpace,
}

/// <summary>
/// PP473, under PP27: the packets takion holds back until the stream cipher exists, and the three ways
/// their buffers are lost.
///
/// PP27's remainder is "the socket, the receive thread, the handshake and the resend loop". PP449 did
/// the thread's timer and PP450 the handshake; this is the thread's other half - what it does with a
/// video or audio packet that arrives before gkcrypt_remote is there to decrypt it.
///
/// THE RULE IS OWNERSHIP, AND IT IS WRITTEN DOWN IN THE C. takion_handle_packet's doc comment says
/// "ownership of this buf is taken", and every branch of it honours that: a failed MAC frees, an
/// unknown type frees, a control message hands it on, and an AV packet with crypt available hands it
/// on. The fifth branch postpones it.
///
/// POSTPONING TRANSFERS OWNERSHIP INTO THE ARRAY, AND FAILING TO POSTPONE USED TO LOSE IT. Both early
/// returns in takion_postpone_packet - the calloc that failed and the array that is full - returned
/// without freeing the buffer they were given. That was a leak of one datagram each, and the full case
/// is reachable by arithmetic: the array is 32 entries, and a stream sending video before the cipher is
/// established sends more than 32 packets in well under a second.
///
/// AND A CIPHER THAT NEVER ARRIVED LOST ALL OF THEM. The flush is guarded on gkcrypt_remote being
/// present, and the thread's teardown freed the send buffer, both reorder queues and the socket - not
/// this array. So a session that died before the cipher was established leaked the array and every
/// buffer still in it, which was the most reachable of the three: it is what any failed connect does.
///
/// PP474 FIXED ALL THREE, and what made that safe to do on a path nothing here can exercise was
/// reading the caller rather than estimating the risk: takion_handle_packet does nothing with buf after
/// the postpone call but break, so a free inside the postpone is the only one on that path and cannot
/// be a double free. The teardown's release sits at `beach` behind a bare null test, and the flush
/// nulls the pointer, so the two cannot both run.
/// </summary>
public static class TakionPostpone
{
    /// <summary>TAKION_POSTPONE_PACKETS_SIZE - how many packets may be held back.</summary>
    public const int Size = 32;

    /// <summary>
    /// What postponing does, given the array's state.
    /// </summary>
    /// <param name="hasArray">Whether the array has been allocated yet.</param>
    /// <param name="count">How many are already in it.</param>
    /// <param name="allocationSucceeds">Whether the calloc would succeed, where one is needed.</param>
    public static PostponeOutcome Postpone(bool hasArray, int count, bool allocationSucceeds = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (!hasArray)
        {
            if (!allocationSucceeds)
                return PostponeOutcome.AllocationFailed;

            // The array is made with a count of zero, so the packet that triggered it is the first.
            return PostponeOutcome.AllocatedAndBuffered;
        }

        return count >= Size ? PostponeOutcome.NoSpace : PostponeOutcome.Buffered;
    }

    /// <summary>
    /// Whether ownership of the packet's buffer passed into the array.
    ///
    /// True where it did. False no longer means the buffer is lost: PP474 gave both failing outcomes a
    /// free, so a packet that cannot be postponed is DROPPED rather than leaked. The distinction is
    /// kept because it is still the thing a port has to get right - the caller has let go either way.
    /// </summary>
    public static bool BufferIsOwned(PostponeOutcome outcome)
        => outcome is PostponeOutcome.AllocatedAndBuffered or PostponeOutcome.Buffered;

    /// <summary>The outcomes that drop the packet rather than holding it. Two of the four.</summary>
    public static IReadOnlyList<PostponeOutcome> DropsThePacket { get; } =
        [.. Enum.GetValues<PostponeOutcome>().Where(o => !BufferIsOwned(o))];

    /// <summary>
    /// Whether the array and everything in it is released, given whether the cipher ever arrived.
    ///
    /// TRUE EITHER WAY SINCE PP474. The flush releases them when the cipher arrives, and the thread's
    /// teardown releases them when it does not - which nothing used to do, so a session dying before
    /// the cipher was agreed left the array and every datagram in it behind.
    /// </summary>
    /// <param name="cryptArrived">
    /// Kept, and deliberately ignored: it used to be the answer. A caller passing false is asking the
    /// question PP474 removed, and the parameter is what lets the test ask it.
    /// </param>
    public static bool ArrayIsReleased(bool cryptArrived) => true;

    /// <summary>takion.c.</summary>
    public const string RelativePath = @"lib\src\takion.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>TAKION_POSTPONE_PACKETS_SIZE as the C defines it.</summary>
    public static long? SizeIn(string source) => CDefine.Value(source, "TAKION_POSTPONE_PACKETS_SIZE");

    /// <summary>The postpone function's body.</summary>
    public static string? PostponeBody(string source)
        => CFunction.Body(source, "static void takion_postpone_packet");

    /// <summary>The dispatcher that owns the buffer.</summary>
    public static string? HandleBody(string source)
        => CFunction.Body(source, "static void takion_handle_packet");

    /// <summary>The thread, where the flush and the teardown are.</summary>
    public static string? ThreadBody(string source)
        => CFunction.Body(source, "static void *takion_thread_func");

    /// <summary>
    /// Whether takion_handle_packet still frees on every branch that keeps the buffer, which is what
    /// makes postponing the only one that can lose it.
    ///
    /// Two frees - the failed MAC and the unknown type - and the postpone call between them.
    /// </summary>
    public static bool TheDispatcherStillOwnsTheBuffer(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        return CountOf(handleBody, "free(buf);") == 2
            && handleBody.Contains("takion_postpone_packet(takion, buf, buf_size)", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP474: whether BOTH early returns in postpone free the buffer before leaving.
    ///
    /// Both, because either alone still leaks. Each stretch is checked for its own free rather than the
    /// function being checked for two - a predicate counting frees would pass for one branch freeing
    /// twice, which is the failure a fix on this path can actually have.
    /// </summary>
    public static bool BothEarlyReturnsFreeTheBuffer(string postponeBody)
    {
        ArgumentNullException.ThrowIfNull(postponeBody);

        string text = postponeBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int allocFail = text.IndexOf("if(!takion->postponed_packets)", StringComparison.Ordinal);
        int full = text.IndexOf(
            "if(takion->postponed_packets_count >= takion->postponed_packets_size)",
            StringComparison.Ordinal);

        if (allocFail < 0 || full < allocFail)
            return false;

        string first = text[allocFail..full];
        string second = text[full..];

        return CountOf(first, "free(buf);") == 1
            && first.Contains("return;", StringComparison.Ordinal)
            && CountOf(second, "free(buf);") == 1
            && second.Contains("return;", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP474: whether the array is released on BOTH exits - the flush when the cipher arrives, and the
    /// teardown when it does not.
    ///
    /// Two frees now, where there was one. The flush's is inside the cipher's guard and the teardown's
    /// is not, which is the whole of the fix: the guard is what used to make the release conditional.
    /// </summary>
    public static bool TheArrayIsReleasedOnBothExits(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string text = threadBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int guard = text.IndexOf(
            "if(takion->postponed_packets && takion->gkcrypt_remote)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        if (CountOf(text, "free(takion->postponed_packets);") != 2)
            return false;

        // The teardown's release is behind a bare null test, not the cipher's, and it frees what is
        // still buffered rather than only the array.
        int teardown = text.LastIndexOf("if(takion->postponed_packets)", StringComparison.Ordinal);

        return teardown > guard
            && text[teardown..].Contains("free(takion->postponed_packets[i].buf);", StringComparison.Ordinal);
    }

    private static int CountOf(string haystack, string needle)
    {
        var found = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
