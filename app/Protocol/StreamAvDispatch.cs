using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where an AV packet went, or why it did not.</summary>
public enum AvRoute
{
    /// <summary>The decrypt failed, and the packet was dropped rather than handed on as noise.</summary>
    Dropped,

    /// <summary>To the video receiver.</summary>
    Video,

    /// <summary>To the haptics receiver, tested before the audio fallback.</summary>
    Haptics,

    /// <summary>To the audio receiver, which is everything that is neither.</summary>
    Audio,
}

/// <summary>
/// The two receivers this port has no managed counterpart for, as a seam.
///
/// audioreceiver.c is one file used twice - once for sound and once for haptics, told apart only by
/// which instance the packet is handed to. Nothing managed plays either yet, so the seam takes the
/// decrypted payload and says which of the two it was for; what a host does with it is the audio
/// path's business (PP652 and the criterion it left open).
/// </summary>
public interface IAudioSink
{
    /// <summary>One decrypted audio packet, bound for the speakers.</summary>
    void Audio(in AvPacket packet, ReadOnlySpan<byte> payload);

    /// <summary>One decrypted haptics packet, bound for the pad.</summary>
    void Haptics(in AvPacket packet, ReadOnlySpan<byte> payload);
}

/// <summary>
/// PP667, under PP295: stream_connection_takion_av, the one call that holds four files in the build.
///
/// PP366 modelled the AV route as three checks on the C - the decrypt adds a block, video reaches
/// the native receiver, haptics is tested before the audio fallback. This is the route itself,
/// managed, driving the receiver PP291 built: <see cref="ManagedVideoReceiver"/> takes a four-method
/// outbound seam precisely so whatever drives it need not be a session pointer, and this is what
/// drives it. That is PP295's second criterion.
///
/// THE DECRYPT IS THE C's ARITHMETIC EXACTLY, and it is written out rather than delegated because
/// the two halves are easy to get wrong separately. The key position handed in is the packet's plus
/// one block - PP366's first check - and it need not be block-aligned: chiaki_gkcrypt_decrypt rounds
/// DOWN to the block before it for the stream and reads from the padding in, then rounds the length
/// UP to whole blocks. <see cref="GkKeyStream.Generate"/> demands both alignments, so this is where
/// the C's padding_pre and full_size live on the managed side.
///
/// A FAILED DECRYPT DROPS THE PACKET. PP367 found the C discarding that result and handing
/// ciphertext to the receiver as a frame, which decodes into noise and blames the network; the C
/// reads it now and so does this. The managed key stream cannot fail for the reason the C's can -
/// an allocation - so the drop here is a key that is the wrong size, which is the one way a
/// managed caller can hand this something unusable.
///
/// HAPTICS IS TESTED BEFORE AUDIO, which is PP366's third check and the order that matters: both
/// arms go to an audio receiver, so an inversion compiles and sends every haptics packet to the
/// speakers as silence. <see cref="AvPacket.IsHaptics"/> is false on every packet the port parses
/// today, and <see cref="StreamAvDispatchSource"/> holds why.
/// </summary>
public static class StreamAvDispatch
{
    /// <summary>
    /// chiaki_gkcrypt_decrypt: the key stream for an unaligned position, applied in place.
    /// </summary>
    /// <param name="keyBase">The AES key derived at the handshake.</param>
    /// <param name="iv">The session's IV.</param>
    /// <param name="keyPos">Where in the stream - the packet's key_pos PLUS one block, per the C.</param>
    /// <param name="buffer">The payload, xor'd in place.</param>
    public static void Decrypt(ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv, ulong keyPos, Span<byte> buffer)
    {
        int block = GkKeyStream.BlockSize;

        // padding_pre and full_size, as gkcrypt.c names them: the stream starts at the block
        // before the position, and covers whole blocks to the end of the payload.
        ulong paddingPre = keyPos % (ulong)block;
        int fullSize = (int)((paddingPre + (ulong)buffer.Length + (ulong)block - 1) / (ulong)block) * block;

        byte[] stream = GkKeyStream.Generate(keyBase, iv, keyPos - paddingPre, fullSize);

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] ^= stream[(int)paddingPre + i];
    }

    /// <summary>
    /// The route: decrypt or drop, then video, haptics, audio - in that order.
    /// </summary>
    /// <param name="packet">The parsed header.</param>
    /// <param name="datagram">The buffer the header's offsets name into; the payload is decrypted in place.</param>
    /// <param name="keyBase">The remote key, or empty where the caller has none - which is a drop.</param>
    /// <param name="iv">The session's IV.</param>
    /// <param name="video">PP291's receiver, driven for the video arm.</param>
    /// <param name="audio">The seam the two audio arms go to.</param>
    public static AvRoute Dispatch(
        in AvPacket packet, Span<byte> datagram,
        ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv,
        ManagedVideoReceiver video, IAudioSink audio)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);

        if (packet.DataOffset < 0 || packet.DataSize < 0 || packet.DataOffset + packet.DataSize > datagram.Length)
            return AvRoute.Dropped;

        Span<byte> payload = datagram.Slice(packet.DataOffset, packet.DataSize);

        // PP367: the one way this can fail managed is a key of the wrong size, and the answer is the
        // C's - drop it. An undecrypted frame is not data, and the receiver already knows how to
        // report a gap.
        if (keyBase.Length != GkKeyStream.BlockSize || iv.Length != GkKeyStream.BlockSize)
            return AvRoute.Dropped;

        Decrypt(keyBase, iv, packet.KeyPos + (ulong)GkKeyStream.BlockSize, payload);

        if (packet.IsVideo)
        {
            video.AvPacket(
                packet.FrameIndex, packet.UnitIndex, packet.UnitsInFrameTotal, packet.UnitsInFrameFec,
                payload, packet.AdaptiveStreamIndex);
            return AvRoute.Video;
        }

        if (packet.IsHaptics)
        {
            audio.Haptics(packet, payload);
            return AvRoute.Haptics;
        }

        audio.Audio(packet, payload);
        return AvRoute.Audio;
    }
}

/// <summary>
/// PP295: the one fact about the AV route that PP366's three checks do not hold - where the haptics
/// bit comes from, and therefore why it is false on every packet the port parses.
/// </summary>
public static class StreamAvDispatchSource
{
    /// <summary>takion.c, which sets the bit; streamconnection.c only reads it.</summary>
    public const string TakionRelativePath = @"lib\src\takion.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionRelativePath);

    /// <summary>
    /// Whether is_haptics is still set on the v12 audio layout and nowhere else.
    ///
    /// Both halves. The guard is what makes the mirror's default honest - a v9 parse cannot see the
    /// bit because the C never writes it there - and the single assignment is what makes "nowhere
    /// else" a count rather than a hope.
    /// </summary>
    public static bool HapticsIsStillAV12AudioBit(string takion)
    {
        ArgumentNullException.ThrowIfNull(takion);

        string compact = CCall.Compact(CCall.Code(takion));

        return CCall.Count(compact, "packet->is_haptics =") == 1
            && CCall.InOrder(compact, "if(v12 && !packet->is_video)", "packet->is_haptics = *av == 0x02;");
    }
}
