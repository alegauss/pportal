using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// chiaki_audio_header_t: what a STREAMINFO says the sound will be.
/// </summary>
/// <param name="Unknown">
/// The C's own name for it, kept. <c>chiaki_audio_header_set</c> writes 1 and nothing reads it.
/// </param>
public readonly record struct ManagedAudioHeader(
    byte Channels, byte Bits, uint Rate, uint FrameSize, uint Unknown)
{
    /// <summary>CHIAKI_AUDIO_HEADER_SIZE - the only length the receiver's caller accepts.</summary>
    public const int Size = 0xe;

    /// <summary>
    /// chiaki_audio_header_load: channels first, then bits, then three big-endian words.
    /// </summary>
    public static ManagedAudioHeader Load(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < Size)
            throw new ArgumentException($"an audio header is {Size} bytes", nameof(buffer));

        return new ManagedAudioHeader(
            buffer[0],
            buffer[1],
            BinaryPrimitives.ReadUInt32BigEndian(buffer[2..]),
            BinaryPrimitives.ReadUInt32BigEndian(buffer[6..]),
            BinaryPrimitives.ReadUInt32BigEndian(buffer[0xa..]));
    }

    /// <summary>
    /// chiaki_audio_header_save, which puts BITS in the byte load reads as channels.
    ///
    /// PP740: not a transcription slip. The two are not inverses in the C either, and reproducing
    /// that is PP402's rule - the bytes go to a console, so the port's job is what the console sees
    /// and not what reads more sensibly. <see cref="ManagedAudioReceiverSource.LoadAndSaveDisagree"/>
    /// holds the C to it, so the day upstream makes them symmetric the port hears about it.
    /// </summary>
    public void Save(Span<byte> buffer)
    {
        if (buffer.Length < Size)
            throw new ArgumentException($"an audio header is {Size} bytes", nameof(buffer));

        buffer[0] = Bits;
        buffer[1] = Channels;
        BinaryPrimitives.WriteUInt32BigEndian(buffer[2..], Rate);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[6..], FrameSize);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[0xa..], Unknown);
    }

    /// <summary>chiaki_audio_header_set, whose fourth field is the 1 nothing reads.</summary>
    public static ManagedAudioHeader Set(byte channels, byte bits, uint rate, uint frameSize)
        => new(channels, bits, rate, frameSize, 1);
}

/// <summary>
/// Where one audio receiver's output goes: the C's ChiakiAudioSink, as an interface.
/// </summary>
public interface IAudioFrameSink
{
    /// <summary>header_cb, after the receiver has reset its sequencing.</summary>
    void Header(in ManagedAudioHeader header);

    /// <summary>
    /// frame_cb. An EMPTY span is the C's concealed frame - <c>frame_cb(NULL, 0, user)</c> - and
    /// means the index was lost and playback moved past it, not that a frame of silence arrived.
    /// </summary>
    void Frame(ReadOnlySpan<byte> frame);
}

/// <summary>Why an AV packet never reached the jitter buffer, or that it did.</summary>
public enum AudioIntake
{
    /// <summary>Every unit was offered to the buffer.</summary>
    Accepted,

    /// <summary>codec != 5.</summary>
    UnknownCodec,

    /// <summary>data_size was zero.</summary>
    Empty,

    /// <summary>source units + FEC units disagreed with units_in_frame_total.</summary>
    UnitCountMismatch,

    /// <summary>data_size was not unit_size * units_in_frame_total.</summary>
    SizeMismatch,
}

/// <summary>
/// PP740: audioreceiver.c, managed - the eight-slot jitter buffer PP738's census had nothing for.
///
/// PP667's route decrypts an AV packet and says which of the three receivers it was for, and the two
/// audio arms hand it to <see cref="IAudioSink"/>. Nothing outside the test project implemented that
/// interface, so a managed run reached the seam and stopped. This is what stands behind it.
///
/// THIS COPY IS NOT UPSTREAM CHIAKI'S. Upstream hands each unit straight to the sink; this one holds
/// eight slots, waits for three before starting playback, delivers strictly by frame index, and
/// CONCEALS a missing index by emitting an empty frame once enough lookahead has arrived. Porting
/// the simpler shape would have compiled, passed a round-trip test and reordered sound.
///
/// THE FEC UNITS ARE FRAMES, NOT PARITY. Audio's redundancy is repetition: units past
/// source_units_count carry earlier frame indices - <c>frame_index - fec_units_count + fec_index</c>
/// - so a lost frame arrives again inside the next packet. The startup arm skips the ones that would
/// underflow before the stream's first index, which is what frame_index_startup is for.
///
/// ONE ARM PER INSTANCE. The C's stream connection holds two of these and audioreceiver.c is one
/// file used twice; each carries its own frame_index_prev and its own buffer.
/// <see cref="ManagedAudioReceiverPair"/> is what puts two behind the one interface, because sharing
/// this state between sound and haptics compiles and desynchronises both.
///
/// WHAT IT DOES NOT DO is decode or play. The C's frame_cb hands out Opus, and so does this.
/// </summary>
public sealed class ManagedAudioReceiver
{
    /// <summary>CHIAKI_AUDIO_JITTER_PREFILL.</summary>
    public const int JitterPrefill = 3;

    /// <summary>CHIAKI_AUDIO_JITTER_BUFFER_SIZE.</summary>
    public const int JitterBufferSize = 8;

    /// <summary>The one codec the receiver accepts.</summary>
    public const byte OpusCodec = 5;

    private readonly IAudioFrameSink sink;
    private readonly ManagedPacketStats? stats;

    private readonly bool[] occupied = new bool[JitterBufferSize];
    private readonly ushort[] indexes = new ushort[JitterBufferSize];
    private readonly byte[]?[] buffers = new byte[JitterBufferSize][];
    private readonly int[] sizes = new int[JitterBufferSize];

    private ushort framePrev;
    private ushort next;
    private bool nextValid;
    private bool playbackStarted;
    private bool frameIndexStartup = true;
    private int buffered;

    /// <summary>chiaki_audio_receiver_init.</summary>
    /// <param name="sink">Where the frames go.</param>
    /// <param name="stats">The run's packet stats, or null where the C is handed none.</param>
    public ManagedAudioReceiver(IAudioFrameSink sink, ManagedPacketStats? stats = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        this.sink = sink;
        this.stats = stats;
    }

    /// <summary>How many slots hold a frame, which is what the prefill counts.</summary>
    public int Buffered => buffered;

    /// <summary>Whether the prefill has been reached and delivery has begun.</summary>
    public bool PlaybackStarted => playbackStarted;

    /// <summary>The index delivery is waiting for, once there is one.</summary>
    public ushort? NextFrameIndex => nextValid ? next : null;

    /// <summary>chiaki_audio_receiver_stream_info: reset everything, then tell the sink.</summary>
    public void StreamInfo(in ManagedAudioHeader header)
    {
        framePrev = 0;
        next = 0;
        nextValid = false;
        playbackStarted = false;
        frameIndexStartup = true;
        ClearJitterBuffer();

        sink.Header(header);
    }

    /// <summary>chiaki_audio_receiver_fini's half that is not a mutex.</summary>
    public void ClearJitterBuffer()
    {
        Array.Clear(occupied);
        Array.Clear(buffers);
        Array.Clear(sizes);
        buffered = 0;
    }

    /// <summary>The three counts the C packs into units_in_frame_fec.</summary>
    public static (byte Source, byte Fec, byte UnitSize) Units(ushort unitsInFrameFec)
        => ((byte)(unitsInFrameFec & 0xf), (byte)((unitsInFrameFec >> 4) & 0xf), (byte)(unitsInFrameFec >> 8));

    /// <summary>
    /// chiaki_audio_receiver_av_packet: validate, split into units, then push the seq.
    /// </summary>
    /// <param name="packet">The decrypted packet's header.</param>
    /// <param name="payload">Its decrypted payload, which is unit_size * units_in_frame_total long.</param>
    public AudioIntake AvPacket(in AvPacket packet, ReadOnlySpan<byte> payload)
    {
        if (packet.Codec != OpusCodec)
            return AudioIntake.UnknownCodec;

        (byte source, byte fec, byte unitSize) = Units(packet.UnitsInFrameFec);

        if (packet.DataSize == 0)
            return AudioIntake.Empty;

        if (fec + source != packet.UnitsInFrameTotal)
            return AudioIntake.UnitCountMismatch;

        if (packet.DataSize != unitSize * packet.UnitsInFrameTotal)
            return AudioIntake.SizeMismatch;

        // The C reads is_haptics off the packet; the pair above has already chosen the arm, so it
        // is passed rather than re-read - which is what lets one receiver serve either.
        if (packet.FrameIndex > (1 << 15))
            frameIndexStartup = false;

        for (int i = 0; i < source + fec; i++)
        {
            ushort frameIndex;

            if (i < source)
            {
                frameIndex = (ushort)(packet.FrameIndex + i);
            }
            else
            {
                int fecIndex = i - source;

                // Before the stream has run far enough, the repeated indices would sit below the
                // first one ever sent, and the C skips them rather than wrapping into the past.
                if (frameIndexStartup && packet.FrameIndex + fecIndex < fec + 1)
                    continue;

                frameIndex = (ushort)(packet.FrameIndex - fec + fecIndex);
            }

            Frame(frameIndex, packet.IsHaptics, payload.Slice(unitSize * i, unitSize));
        }

        stats?.PushSeq(packet.FrameIndex);
        return AudioIntake.Accepted;
    }

    /// <summary>
    /// chiaki_audio_receiver_frame: one unit in, and everything it makes deliverable out.
    ///
    /// The C's <c>while(true)</c> is here for the reason it is there: delivering the awaited index
    /// can make the one after it deliverable from the same buffer, and the loop drains until it
    /// cannot. Public because the units of a packet are the interesting input, and a test that has
    /// to build a whole AV header to place one frame tests the header.
    /// </summary>
    public void Frame(ushort frameIndex, bool haptics, ReadOnlySpan<byte> unit)
    {
        // Haptics is never buffered: newer than the last one goes straight out, older is dropped.
        if (haptics)
        {
            if (SeqNum.Gt(frameIndex, framePrev))
            {
                framePrev = frameIndex;
                sink.Frame(unit);
            }

            return;
        }

        bool unstored = true;

        while (true)
        {
            if (unstored)
            {
                if (nextValid && SeqNum.Lt(frameIndex, next))
                    return;

                if (!Store(frameIndex, unit))
                    return;

                unstored = false;
            }

            if (!playbackStarted && buffered >= JitterPrefill)
            {
                int oldest = Oldest();
                if (oldest >= 0)
                {
                    next = indexes[oldest];
                    nextValid = true;
                    playbackStarted = true;
                }
            }

            if (!playbackStarted || !nextValid)
                return;

            int slot = Find(next);
            if (slot >= 0)
            {
                byte[]? held = buffers[slot];
                int size = sizes[slot];

                occupied[slot] = false;
                buffers[slot] = null;
                sizes[slot] = 0;
                buffered--;

                framePrev = next;
                next++;

                sink.Frame(held is null ? default : held.AsSpan(0, size));
                continue;
            }

            if (buffered == 0 || !CanConcealLoss())
                return;

            framePrev = next;
            next++;

            sink.Frame(default);
        }
    }

    /// <summary>
    /// Whether the awaited index can be given up on, which is the C's can_conceal_loss.
    ///
    /// Two conditions, and the second is the one worth naming: something NEWER has to be buffered,
    /// or the gap is the head of the stream rather than a hole in it - and once the buffer is at the
    /// prefill, the newest has to be a whole prefill ahead, so a run of arrivals cannot walk playback
    /// past frames still in flight.
    /// </summary>
    private bool CanConcealLoss()
    {
        int oldest = Oldest();
        int newest = Newest();

        if (oldest < 0 || newest < 0 || !SeqNum.Gt(indexes[oldest], next))
            return false;

        if (buffered < JitterPrefill)
            return true;

        return SeqNum.Gt(indexes[newest], (ushort)(next + JitterPrefill - 1));
    }

    /// <summary>
    /// chiaki_audio_receiver_store_audio_frame_locked, which refuses more than it accepts.
    /// </summary>
    /// <returns>False where the index is already held, or where a full buffer holds only newer.</returns>
    private bool Store(ushort frameIndex, ReadOnlySpan<byte> unit)
    {
        if (Find(frameIndex) >= 0)
            return false;

        int free = -1;
        for (int i = 0; i < JitterBufferSize; i++)
        {
            if (!occupied[i])
            {
                free = i;
                break;
            }
        }

        if (free < 0)
        {
            // Full: the newest is evicted for an older arrival, and nothing else is.
            int newest = Newest();
            if (newest < 0 || !SeqNum.Lt(frameIndex, indexes[newest]))
                return false;

            occupied[newest] = false;
            buffers[newest] = null;
            sizes[newest] = 0;
            buffered--;
            free = newest;
        }

        occupied[free] = true;
        indexes[free] = frameIndex;
        buffers[free] = unit.Length > 0 ? unit.ToArray() : null;
        sizes[free] = unit.Length;
        buffered++;
        return true;
    }

    private int Find(ushort frameIndex)
    {
        for (int i = 0; i < JitterBufferSize; i++)
        {
            if (occupied[i] && indexes[i] == frameIndex)
                return i;
        }

        return -1;
    }

    private int Oldest()
    {
        int oldest = -1;
        for (int i = 0; i < JitterBufferSize; i++)
        {
            if (occupied[i] && (oldest < 0 || SeqNum.Lt(indexes[i], indexes[oldest])))
                oldest = i;
        }

        return oldest;
    }

    private int Newest()
    {
        int newest = -1;
        for (int i = 0; i < JitterBufferSize; i++)
        {
            if (occupied[i] && (newest < 0 || SeqNum.Gt(indexes[i], indexes[newest])))
                newest = i;
        }

        return newest;
    }
}

/// <summary>
/// PP740: the two receivers the C's stream connection holds, behind the one seam PP667 hands to.
///
/// audioreceiver.c is one file used twice, and the two instances share nothing - each has its own
/// frame_index_prev and its own eight slots. <see cref="IAudioSink"/> is one object with two
/// methods, so the obvious implementation puts both arms on one receiver and lets a haptics packet
/// move the sound path's sequence. This is the shape that does not: two receivers, one interface,
/// the same split the C gets from having two pointers.
/// </summary>
public sealed class ManagedAudioReceiverPair : IAudioSink
{
    /// <summary>The sound arm.</summary>
    public ManagedAudioReceiver AudioArm { get; }

    /// <summary>The haptics arm, which shares no state with it.</summary>
    public ManagedAudioReceiver HapticsArm { get; }

    /// <summary>Both arms, each with its own sink and the run's stats.</summary>
    public ManagedAudioReceiverPair(IAudioFrameSink audio, IAudioFrameSink haptics, ManagedPacketStats? stats = null)
    {
        AudioArm = new ManagedAudioReceiver(audio, stats);
        HapticsArm = new ManagedAudioReceiver(haptics, stats);
    }

    /// <inheritdoc/>
    void IAudioSink.Audio(in AvPacket packet, ReadOnlySpan<byte> payload) => AudioArm.AvPacket(packet, payload);

    /// <inheritdoc/>
    void IAudioSink.Haptics(in AvPacket packet, ReadOnlySpan<byte> payload) => HapticsArm.AvPacket(packet, payload);
}

/// <summary>
/// PP740: what the port copied out of the C, asserted where it was copied from.
///
/// The jitter buffer's two numbers and the header's two byte orders are the whole of what a reader
/// of this port has to trust, and all four are values in the vendored C rather than shapes a
/// differential could catch. So they are read back, which is PP58's rule: a constant transcribed
/// from a file the port also ships is a claim about that file.
/// </summary>
public static class ManagedAudioReceiverSource
{
    /// <summary>Where the receiver is.</summary>
    public const string RelativePath = @"lib\src\audioreceiver.c";

    /// <summary>And where the header's two byte orders are.</summary>
    public const string HeaderRelativePath = @"lib\src\audio.c";

    /// <summary>The receiver's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The header's file, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The C's CHIAKI_AUDIO_JITTER_PREFILL, or null where the define is gone.</summary>
    public static long? PrefillIn(string source) => CDefine.Value(source, "CHIAKI_AUDIO_JITTER_PREFILL");

    /// <summary>The C's CHIAKI_AUDIO_JITTER_BUFFER_SIZE.</summary>
    public static long? BufferSizeIn(string source) => CDefine.Value(source, "CHIAKI_AUDIO_JITTER_BUFFER_SIZE");

    /// <summary>
    /// Whether load and save still disagree about the first two bytes.
    ///
    /// True is the state the port reproduces. This is not a check that the C is RIGHT - it is a
    /// check that the port and the C are wrong in the same way, which is the only claim
    /// <see cref="ManagedAudioHeader.Save"/> makes.
    /// </summary>
    public static bool LoadAndSaveDisagree(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? load = CFunction.Body(source, "chiaki_audio_header_load");
        string? save = CFunction.Body(source, "chiaki_audio_header_save");

        if (load is null || save is null)
            return false;

        return load.Contains("channels = buf[0]", StringComparison.Ordinal)
            && load.Contains("bits = buf[1]", StringComparison.Ordinal)
            && save.Contains("buf[0] = audio_header->bits", StringComparison.Ordinal)
            && save.Contains("buf[1] = audio_header->channels", StringComparison.Ordinal);
    }
}
