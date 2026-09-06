using System.Runtime.InteropServices;
using ChiakiNg.Native;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>Where decoded PCM goes, and what the stream's shape turned out to be.</summary>
public interface IPcmSink
{
    /// <summary>The settings callback: what a STREAMINFO decided the stream is.</summary>
    void Settings(byte channels, uint rate);

    /// <summary>One decoded frame, interleaved, as many samples per channel as the decode returned.</summary>
    void Pcm(ReadOnlySpan<short> pcm, int samplesPerChannel);
}

/// <summary>
/// PP751: decoded PCM into the ring a renderer reads, so this seam ships with something behind it.
///
/// PP741 measured what happens otherwise - PP740 closed one seam and opened another in the same
/// commit, and the census called it success. The ring is sized from the header the way
/// <see cref="AudioRing.CapacityFor"/> says, because frame size is what a STREAMINFO decides and a
/// ring built before one would be built for a guess.
///
/// WHAT IT IS NOT is a device. Handing the ring to WASAPI is the render path's work; what this
/// settles is that the decoder's output has an owner rather than a shape.
/// </summary>
public sealed class AudioRingSink : IPcmSink
{
    private AudioRing? ring;

    /// <summary>The ring, once a header has said how big it should be.</summary>
    public AudioRing? Ring => ring;

    /// <summary>What the stream announced, or null before it did.</summary>
    public (byte Channels, uint Rate)? Announced { get; private set; }

    /// <summary>How many frames the ring took.</summary>
    public int Written { get; private set; }

    /// <summary>And how many the ring had no room for, which is the overflow a listener hears.</summary>
    public int Dropped { get; private set; }

    /// <inheritdoc/>
    public void Settings(byte channels, uint rate)
    {
        Announced = (channels, rate);

        // A frame's worth of interleaved shorts, eight deep - the ring's own rule.
        ring = new AudioRing(AudioRing.CapacityFor((int)rate / 100 * channels * sizeof(short)));
    }

    /// <inheritdoc/>
    public void Pcm(ReadOnlySpan<short> pcm, int samplesPerChannel)
    {
        if (ring is null)
            return;

        Span<byte> bytes = stackalloc byte[pcm.Length * sizeof(short)];
        MemoryMarshal.AsBytes(pcm).CopyTo(bytes);

        if (ring.Write(bytes))
            Written++;
        else
            Dropped++;
    }
}

/// <summary>
/// PP751: opusdecoder.c, managed - the last thing between a received audio frame and sound.
///
/// PP740 ported the jitter buffer and its frames went to <see cref="IAudioFrameSink"/>, which
/// nothing filled because nothing decoded. This fills it.
///
/// THE CONCEALED FRAME IS THE POINT, not an edge case. audioreceiver.c emits a frame with no buffer
/// when it gives up on a missing index, and opusdecoder.c passes exactly that to opus_decode as a
/// NULL packet - which is Opus's own loss concealment and not silence. PP740 built the first half
/// as an empty span with nowhere to go; the two are one behaviour and this is where they meet.
///
/// THE HEADER REBUILDS THE DECODER. Rate and channels are what a decoder is created for, so a
/// STREAMINFO destroys the old one and makes another; the PCM buffer is re-taken at the same time,
/// because frame_size times channels times two bytes is what a decode writes into and a stale one
/// is a decode into memory that is not there.
///
/// A DECODE THAT FAILS IS LOGGED AND DROPPED in the C, and dropped here. The port counts it instead
/// of logging, because a count is what a test can read and the C's log line is not the behaviour.
/// </summary>
public sealed class ManagedOpusDecoder : IAudioFrameSink, IDisposable
{
    private readonly IPcmSink sink;

    private IntPtr decoder;
    private short[] pcm = [];
    private ManagedAudioHeader header;

    /// <summary>Takes where the decoded frames go.</summary>
    public ManagedOpusDecoder(IPcmSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
    }

    /// <summary>
    /// Whether libopus is behind the shim at all, which the build decides.
    ///
    /// A METHOD and not a property, which OracleGuardCensus is the reason for: its sweep counts a
    /// guard by the call it is written as, so a property is invisible to it and the comparisons
    /// behind it would go uncounted on a build without the oracle.
    /// </summary>
    public static bool IsAvailable() => NativeOpusEncoder.IsAvailable();

    /// <summary>Whether a decoder exists, which a STREAMINFO is what makes true.</summary>
    public bool Ready => decoder != IntPtr.Zero;

    /// <summary>How many frames were decoded and handed on.</summary>
    public int Decoded { get; private set; }

    /// <summary>How many were concealed - a frame with no buffer, which Opus fills in.</summary>
    public int Concealed { get; private set; }

    /// <summary>How many the decoder refused, which the C logs and drops.</summary>
    public int Refused { get; private set; }

    /// <summary>How many arrived before a STREAMINFO, which the C logs and drops too.</summary>
    public int BeforeAnyHeader { get; private set; }

    /// <summary>The header the current decoder was built for.</summary>
    public ManagedAudioHeader Announced => header;

    /// <summary>How many shorts a decode may write, which the header decides.</summary>
    public int PcmBufferLength => pcm.Length;

    /// <summary>
    /// chiaki_audio_header_frame_buf_size, in SHORTS rather than bytes.
    ///
    /// The C sizes a byte buffer as frame_size * channels * sizeof(int16_t) and then decodes into
    /// it as int16_t. Managed, the buffer is already shorts, so the two bytes are the element size
    /// rather than a multiplier - and writing it out is what keeps that from being doubled.
    /// </summary>
    public static int FrameBufferShorts(in ManagedAudioHeader header)
        => (int)(header.FrameSize * header.Channels);

    /// <inheritdoc/>
    public void Header(in ManagedAudioHeader announced)
    {
        header = announced;

        Destroy();

        if (!IsAvailable())
            return;

        decoder = OpusDecoderCreate((int)announced.Rate, announced.Channels, out int error);

        if (decoder == IntPtr.Zero || error != 0)
        {
            decoder = IntPtr.Zero;
            return;
        }

        int wanted = FrameBufferShorts(announced);
        if (pcm.Length != wanted)
            pcm = new short[wanted];

        sink.Settings(announced.Channels, announced.Rate);
    }

    /// <inheritdoc/>
    public void Frame(ReadOnlySpan<byte> frame)
    {
        if (decoder == IntPtr.Zero)
        {
            BeforeAnyHeader++;
            return;
        }

        int samples = Decode(frame);

        if (samples < 1)
        {
            Refused++;
            return;
        }

        if (frame.IsEmpty)
            Concealed++;

        Decoded++;

        // The count is per channel, and the buffer holds them interleaved.
        sink.Pcm(pcm.AsSpan(0, Math.Min(pcm.Length, samples * header.Channels)), samples);
    }

    private unsafe int Decode(ReadOnlySpan<byte> frame)
    {
        fixed (short* into = pcm)
        fixed (byte* data = frame)
        {
            // Size zero is what makes it a concealment: the shim passes NULL rather than this
            // pointer, which is a different call to opus_decode and the one the C makes.
            return OpusDecode(decoder, data, frame.Length, into, (int)header.FrameSize);
        }
    }

    private void Destroy()
    {
        if (decoder == IntPtr.Zero)
            return;

        OpusDecoderDestroy(decoder);
        decoder = IntPtr.Zero;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Destroy();
        GC.SuppressFinalize(this);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_decoder_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr OpusDecoderCreate(int rate, int channels, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_decoder_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void OpusDecoderDestroy(IntPtr decoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_decode",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int OpusDecode(
        IntPtr decoder, byte* data, int size, short* pcm, int frameSize);
}
