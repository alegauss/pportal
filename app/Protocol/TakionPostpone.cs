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
/// POSTPONING TRANSFERS OWNERSHIP INTO THE ARRAY, AND FAILING TO POSTPONE LOSES IT. Both early returns
/// in takion_postpone_packet - the calloc that failed and the array that is full - return without
/// freeing the buffer they were given. That is a leak of one datagram each, and the full case is
/// reachable by arithmetic: the array is 32 entries, and a stream sending video before the cipher is
/// established sends more than 32 packets in well under a second.
///
/// AND A CRYPT THAT NEVER ARRIVES LOSES ALL OF THEM. The flush is guarded on gkcrypt_remote being
/// present, and the thread's teardown frees the send buffer, both reorder queues and the socket - not
/// this array. So a session that dies before the cipher is established leaks the array and every
/// buffer still in it, which is the most reachable of the three: it is what any failed connect does.
///
/// The leaks are modelled and filed rather than fixed, because a free added in the wrong one of these
/// three places is a double free on a path nothing here can exercise.
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
    /// Whether the packet's buffer survives this outcome - which is to say, whether anything will free
    /// it.
    ///
    /// True where ownership passed into the array. False is a leak, not a drop: the caller has already
    /// let go of it.
    /// </summary>
    public static bool BufferIsOwned(PostponeOutcome outcome)
        => outcome is PostponeOutcome.AllocatedAndBuffered or PostponeOutcome.Buffered;

    /// <summary>The outcomes that lose the buffer. Two of the four.</summary>
    public static IReadOnlyList<PostponeOutcome> LosesTheBuffer { get; } =
        [.. Enum.GetValues<PostponeOutcome>().Where(o => !BufferIsOwned(o))];

    /// <summary>
    /// Whether the array and everything in it is released, given whether the cipher ever arrived.
    ///
    /// Only the flush releases them, and the flush is guarded on the cipher. Nothing on the teardown
    /// path frees the array, so a session that never establishes crypt keeps both.
    /// </summary>
    public static bool ArrayIsReleased(bool cryptArrived) => cryptArrived;

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
    /// Whether both early returns in postpone still leave without freeing.
    ///
    /// Read as the absence of a free before each return, because that absence IS the defect - a
    /// predicate looking for a free would go green the moment somebody fixed one of the two.
    /// </summary>
    public static bool BothEarlyReturnsStillLeak(string postponeBody)
    {
        ArgumentNullException.ThrowIfNull(postponeBody);

        string text = postponeBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int allocFail = text.IndexOf("if(!takion->postponed_packets)", StringComparison.Ordinal);
        int full = text.IndexOf(
            "if(takion->postponed_packets_count >= takion->postponed_packets_size)",
            StringComparison.Ordinal);

        if (allocFail < 0 || full < allocFail)
            return false;

        // Neither stretch frees, and each ends in a return.
        string first = text[allocFail..full];
        string second = text[full..];

        return first.Contains("return;", StringComparison.Ordinal)
            && !first.Contains("free(", StringComparison.Ordinal)
            && second.Contains("return;", StringComparison.Ordinal)
            && !second.Contains("free(buf)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the flush is still the only thing that releases the array, and is still guarded on the
    /// cipher.
    /// </summary>
    public static bool OnlyTheFlushReleasesTheArray(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string text = threadBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int guard = text.IndexOf(
            "if(takion->postponed_packets && takion->gkcrypt_remote)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        // Exactly one free of the array, and it is inside the guarded block.
        return CountOf(text, "free(takion->postponed_packets);") == 1
            && text.IndexOf("free(takion->postponed_packets);", StringComparison.Ordinal) > guard;
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
