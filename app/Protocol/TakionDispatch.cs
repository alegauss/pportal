using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which branch of takion_handle_packet a datagram took.</summary>
public enum TakionDispatchBranch
{
    /// <summary>The MAC gate rejected it, ahead of the switch. The C frees and returns.</summary>
    MacRejected,

    /// <summary>Base type 0: on to the message handler.</summary>
    Control,

    /// <summary>Video or audio arriving before the cipher exists: into the postpone array.</summary>
    Postponed,

    /// <summary>Video: parsed and pushed into the reorder queue.</summary>
    Video,

    /// <summary>Audio: parsed, handed to the callback, and done with inside the handler.</summary>
    Audio,

    /// <summary>Anything else: logged, hexdumped and freed.</summary>
    UnknownType,
}

/// <summary>How long the branch needs the datagram's bytes.</summary>
public enum DatagramLifetime
{
    /// <summary>Only for the length of the call, so the loop's rented buffer may be lent out.</summary>
    Borrowed,

    /// <summary>Past the iteration, so the bytes must be copied out before the buffer is reused.</summary>
    Copied,
}

/// <summary>What the dispatch decided about one datagram.</summary>
/// <param name="Branch">Which of the six.</param>
/// <param name="Lifetime">Borrow or copy, which over a pooled buffer is what ownership became.</param>
public readonly record struct TakionDispatchVerdict(
    TakionDispatchBranch Branch,
    DatagramLifetime Lifetime);

/// <summary>
/// PP490, under PP27: takion_handle_packet's branches, and which of them keeps the datagram past the
/// iteration that read it.
///
/// PP485 put a pooled 1500-byte buffer under the receive thread and PP487 modelled the thread itself;
/// this is the call the thread makes with what arrived. It is the point where the C's one line of
/// doc - "ownership of this buf is taken" - has to be translated, and the translation is not the
/// obvious one.
///
/// A FREE RULE BECOMES A COPY RULE, AND THE FAILURE MODE INVERTS. In the C every branch either frees
/// the malloc'd datagram or hands it to something that will, and getting it wrong leaks one packet.
/// Over PP485's rented buffer there is nothing to free: the loop hands out a span into a buffer it
/// reuses on the next iteration. So the question each branch answers is no longer "who frees this"
/// but "does this outlive the call", and a branch that keeps a borrowed span is reading bytes the
/// next datagram has already overwritten - a use-after-return of pooled memory, which does not fail
/// where it is written and does not fail every time.
///
/// THREE BRANCHES KEEP THE BYTES AND THREE DO NOT, AND THE SPLIT IS NOT WHERE THE SWITCH IS. The
/// switch has three labels and a default; the copy set cuts across them. Control keeps them, because
/// the data path pushes packet_buf into a data_queue entry. Postpone keeps them, which is the array
/// PP473 modelled and PP474 repaired. Video keeps them, in a reorder queue entry. Audio does NOT -
/// its callback runs inside the handler and the buffer is freed on the way out - and audio is a
/// sibling case label of video, sharing every line of the branch above it. A failed MAC and an
/// unknown type keep nothing.
///
/// So the base type alone decides it, video and audio being separate base types, and this class is
/// that decision written down once rather than inferred at each call site.
/// </summary>
public static class TakionDispatch
{
    /// <summary>TAKION_PACKET_BASE_TYPE_MASK - the low nibble of the first byte.</summary>
    public const int BaseTypeMask = 0xf;

    /// <summary>TAKION_PACKET_TYPE_CONTROL.</summary>
    public const int Control = 0;

    /// <summary>TAKION_PACKET_TYPE_VIDEO.</summary>
    public const int Video = 2;

    /// <summary>TAKION_PACKET_TYPE_AUDIO.</summary>
    public const int Audio = 3;

    /// <summary>The base type of a datagram, which is the low nibble and nothing else.</summary>
    public static int BaseTypeOf(ReadOnlySpan<byte> datagram)
    {
        if (datagram.IsEmpty)
            throw new ArgumentException("takion_handle_packet asserts buf_size > 0.", nameof(datagram));

        return datagram[0] & BaseTypeMask;
    }

    /// <summary>
    /// Which branch a datagram takes, and how long that branch needs its bytes.
    /// </summary>
    /// <param name="baseType">The low nibble of the first byte.</param>
    /// <param name="macOk">
    /// Whether takion_handle_packet_mac passed. It is true whenever there is no remote cipher, which
    /// is why a pre-cipher control packet reaches the switch at all.
    /// </param>
    /// <param name="enableCrypt">The C's `takion->enable_crypt`.</param>
    /// <param name="cryptAvailable">Whether `takion->gkcrypt_remote` exists.</param>
    public static TakionDispatchVerdict Decide(
        int baseType, bool macOk, bool enableCrypt, bool cryptAvailable)
    {
        // Ahead of the switch, so a rejected packet never reaches a branch.
        if (!macOk)
            return new TakionDispatchVerdict(TakionDispatchBranch.MacRejected, DatagramLifetime.Borrowed);

        return baseType switch
        {
            // The data sub-branch pushes packet_buf into a data_queue entry, so the bytes outlive the
            // call even though the dispatch cannot see which sub-branch was taken.
            Control => new TakionDispatchVerdict(TakionDispatchBranch.Control, DatagramLifetime.Copied),

            // One guard for both AV types, and it is the pair the postpone flush uses, not the triple
            // the MAC re-check uses - see TakionReceiveLoop.
            Video or Audio when enableCrypt && !cryptAvailable
                => new TakionDispatchVerdict(TakionDispatchBranch.Postponed, DatagramLifetime.Copied),

            Video => new TakionDispatchVerdict(TakionDispatchBranch.Video, DatagramLifetime.Copied),

            // The one AV outcome that does not keep the bytes, sharing a case label with the one that
            // does. Tidying the two into "AV copies" costs a memcpy per audio packet forever.
            Audio => new TakionDispatchVerdict(TakionDispatchBranch.Audio, DatagramLifetime.Borrowed),

            _ => new TakionDispatchVerdict(TakionDispatchBranch.UnknownType, DatagramLifetime.Borrowed),
        };
    }

    /// <summary>The branches that need the bytes copied out of the loop's buffer. Three of the six.</summary>
    public static IReadOnlyList<TakionDispatchBranch> KeepsTheBytes { get; } =
    [
        TakionDispatchBranch.Control,
        TakionDispatchBranch.Postponed,
        TakionDispatchBranch.Video,
    ];

    /// <summary>Whether this branch may be handed a span into the loop's rented buffer.</summary>
    public static bool MayBorrow(TakionDispatchBranch branch) => !KeepsTheBytes.Contains(branch);
}

/// <summary>
/// PP490: the C's own spelling of the dispatch, so the table above is asserted rather than remembered.
/// </summary>
public static class TakionDispatchSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>TAKION_PACKET_BASE_TYPE_MASK as the C defines it.</summary>
    public static long? MaskIn(string source) => CDefine.Value(source, "TAKION_PACKET_BASE_TYPE_MASK");

    /// <summary>The dispatch itself.</summary>
    public static string? HandleBody(string source)
        => CFunction.Body(source, "static void takion_handle_packet");

    /// <summary>The AV handler, where video and audio stop being one branch.</summary>
    public static string? AvBody(string source)
        => CFunction.Body(source, "void takion_handle_packet_av");

    /// <summary>The control handler, whose data case is the one that keeps the buffer.</summary>
    public static string? MessageBody(string source)
        => CFunction.Body(source, "static void takion_handle_packet_message");

    /// <summary>The data handler, which is what the data case hands the buffer to.</summary>
    public static string? DataBody(string source)
        => CFunction.Body(source, "static void takion_handle_packet_message_data");

    /// <summary>
    /// PP491: whether BOTH early returns in the data handler free the packet before leaving.
    ///
    /// The data case is the only arm of the message handler that does not free after its call, so
    /// this function owns what it was given until the queue entry does. Its two early returns - a
    /// payload under the nine-byte data header, and a failed malloc of the entry - used to return
    /// without freeing, losing the whole datagram each time.
    ///
    /// Read as two stretches rather than counted, for the reason PP474 gives one level up: a count
    /// of two is also what one branch freeing twice looks like, and that is the shape a fix on this
    /// path can actually have.
    /// </summary>
    public static bool BothEarlyReturnsFreeThePacket(string dataBody)
    {
        ArgumentNullException.ThrowIfNull(dataBody);

        string text = dataBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int shortPayload = text.IndexOf("if(payload_size < 9)", StringComparison.Ordinal);
        int noEntry = text.IndexOf("if(!entry)", StringComparison.Ordinal);

        if (shortPayload < 0 || noEntry < shortPayload)
            return false;

        string first = text[shortPayload..noEntry];
        string second = text[noEntry..];

        return Frees(first) == 1 && Frees(second) == 1;

        static int Frees(string stretch)
        {
            const string needle = "free(packet_buf);";
            var found = 0;
            for (int at = stretch.IndexOf(needle, StringComparison.Ordinal);
                 at >= 0;
                 at = stretch.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                found++;
            }

            return found;
        }
    }

    /// <summary>
    /// Whether the MAC gate is still ahead of the switch rather than inside a branch of it.
    ///
    /// The ordering is the reason MacRejected is a verdict and not a case label: a packet that fails
    /// it is never classified at all.
    /// </summary>
    public static bool TheMacGateIsBeforeTheSwitch(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        int gate = handleBody.IndexOf(
            "if(takion_handle_packet_mac(takion, base_type, buf, buf_size) != CHIAKI_ERR_SUCCESS)",
            StringComparison.Ordinal);
        int dispatch = handleBody.IndexOf("switch(base_type)", StringComparison.Ordinal);

        return gate >= 0 && dispatch > gate;
    }

    /// <summary>Whether the base type is still the low nibble and nothing else.</summary>
    public static bool TheBaseTypeIsTheMaskedFirstByte(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);
        return handleBody.Contains(
            "uint8_t base_type = (uint8_t)(buf[0] & TAKION_PACKET_BASE_TYPE_MASK);",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether video and audio still share one case label and one guard.
    ///
    /// If a later edit splits them, the postpone guard could drift on one side only, and this port
    /// would be modelling a branch that no longer exists.
    /// </summary>
    public static bool VideoAndAudioShareTheOneGuard(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        string text = handleBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                "case TAKION_PACKET_TYPE_VIDEO:\n\t\tcase TAKION_PACKET_TYPE_AUDIO:",
                StringComparison.Ordinal)
            && text.Contains(
                "if(takion->enable_crypt && !takion->gkcrypt_remote)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the audio path still frees inside the AV handler where the video path queues.
    ///
    /// This is the one asymmetry the dispatch cannot see, and the reason Audio borrows while Video
    /// copies. The video half ends by pushing the entry; the audio half ends by freeing.
    /// </summary>
    public static bool AudioIsFreedWhereVideoIsQueued(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        string text = avBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int isVideo = text.IndexOf("bool is_video = (base_type == TAKION_PACKET_TYPE_VIDEO);",
            StringComparison.Ordinal);
        if (isVideo < 0)
            return false;

        string after = text[isVideo..];

        // The non-video arm hands the packet to the callback and frees on the way out; the entry that
        // keeps the buffer is built below it.
        int audioArm = after.IndexOf("if(!is_video)", StringComparison.Ordinal);
        int queued = after.IndexOf("entry->buf = buf;", StringComparison.Ordinal);

        return audioArm >= 0
            && queued > audioArm
            && after[audioArm..queued].Contains("free(buf);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the control handler still frees on the ack case and not on the data case.
    ///
    /// That difference is why Control is a copy: the data case is the one that keeps packet_buf, and
    /// it is the only one of the three that leaves without a free.
    /// </summary>
    public static bool OnlyTheDataCaseKeepsTheBuffer(string messageBody)
    {
        ArgumentNullException.ThrowIfNull(messageBody);

        string text = messageBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int data = text.IndexOf("case TAKION_CHUNK_TYPE_DATA:", StringComparison.Ordinal);
        int ack = text.IndexOf("case TAKION_CHUNK_TYPE_DATA_ACK:", StringComparison.Ordinal);

        if (data < 0 || ack < data)
            return false;

        string dataArm = text[data..ack];
        string ackArm = text[ack..];

        return !dataArm.Contains("free(buf);", StringComparison.Ordinal)
            && ackArm.Contains("free(buf);", StringComparison.Ordinal)
            && dataArm.Contains("takion_handle_packet_message_data(takion, buf, buf_size",
                StringComparison.Ordinal);
    }
}
