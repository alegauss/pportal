using Concentus;
using Concentus.Enums;

namespace ChiakiNg.Session;

/// <summary>What chiaki_opus_encoder_frame did with one microphone frame.</summary>
public enum OpusFrameOutcome
{
    /// <summary>No encoder. The C logs and returns, which is a frame lost with a line in the log.</summary>
    NotInitialised,

    /// <summary>opus_encode returned below one, which is its error convention.</summary>
    EncodeFailed,

    /// <summary>
    /// Encoded, and to a length the protocol does not carry - so the frame is DROPPED.
    ///
    /// The outcome a port leaves out. The C's own words are "dropping packet as protocol
    /// violation", and it logs it at verbose: a session losing every frame this way is silent at
    /// ordinary log levels.
    /// </summary>
    UnexpectedSize,

    /// <summary>Exactly the buffer's size, which is the only thing the audio sender is given.</summary>
    Sent,
}

/// <summary>
/// PP694, under PP32: opusencoder.c in managed code, which is libopus's second consumer.
///
/// PP32 measured that the dependency does not leave with the decoder. opusdecoder.c is the playback
/// path and opusencoder.c is the microphone's, and both call into the library - so porting one
/// removes no DLL from the package. Its own sentence named the blocker: the encoder is on the path
/// that had no input, and the dependency leaves when the microphone question is answered. PP652
/// answered it, and <see cref="WasapiCapture"/> now delivers whole 960-byte units in exactly the
/// format streamconnection.c announces.
///
/// THE FORTY-BYTE BUFFER IS THE PROTOCOL, not an allocation detail. The C sizes its output buffer
/// at forty, hands that size to opus_encode as the maximum, and then DROPS any frame whose result
/// is not exactly forty. Two things follow, and a port that treated the number as a capacity gets
/// both wrong: libopus reads a small maximum as a hard constraint and pads to fill it, so forty is
/// a bitrate - 32 kbps at a hundred frames a second - and the equality test is a protocol check
/// rather than a sanity one.
///
/// IT IS NOT BIT-EXACT AND CANNOT BE. PP651 measured the decode side at 386,460 of 480,000 samples
/// differing from libopus, and the encoder is the same story: over two hundred frames of a
/// deterministic signal, Concentus and libopus agree on the LENGTH every time and on the first byte
/// - the TOC, which carries the mode, the bandwidth and the frame count - every time, and on the
/// payload never. So the differential is written to what the protocol reads and not to the bytes,
/// which is a weaker claim honestly made rather than a stronger one quietly abandoned.
///
/// THE APPLICATION MODE COMES FROM THE C. OPUS_APPLICATION_RESTRICTED_LOWDELAY is 2051 and this
/// does not say so: <see cref="NativeOpusEncoder.Application"/> asks the shim, and the test holds
/// the two together. A number typed here would be right until libopus renumbered it.
/// </summary>
public sealed class ManagedOpusEncoder : IDisposable
{
    private IOpusEncoder? encoder;
    private byte[] frameBuffer = [];
    private bool disposed;

    /// <summary>
    /// The output buffer's size, which the C computes as a required size and then insists on.
    /// </summary>
    /// <remarks>
    /// <see cref="OpusEncoderSource.FrameBytesIn"/> reads the same number out of opusencoder.c, so
    /// this is a copy that cannot drift rather than a constant somebody remembered.
    /// </remarks>
    public const int FrameBytes = 40;

    /// <summary>What the encoder was built for, or null before a header arrived.</summary>
    public (int Rate, int Channels)? Format { get; private set; }

    /// <summary>Whether an encoder exists, which is the C's own first test in the frame path.</summary>
    public bool Initialised => encoder is not null;

    /// <summary>
    /// chiaki_opus_encoder_header: a new encoder for the announced format, replacing any before it.
    ///
    /// The C destroys the old encoder BEFORE it tries to build the new one and leaves the field
    /// null on failure, so a second header that cannot be honoured takes the working encoder with
    /// it. Reproduced: a port that built first and swapped afterwards would keep encoding at the
    /// old rate, which is worse - the console announced a change and the microphone ignored it.
    /// </summary>
    /// <returns>Whether an encoder now exists.</returns>
    public bool Header(int rate, int channels)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        encoder = null;
        Format = null;

        if (rate <= 0 || channels <= 0)
            return false;

        try
        {
            encoder = OpusCodecFactory.CreateEncoder(
                rate, channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        }
        catch (ArgumentException)
        {
            // opus_encoder_create's OPUS_BAD_ARG, which the C logs and leaves the encoder null for.
            return false;
        }

        // The C reallocs only when the size changed, and the size never changes. Kept as an
        // allocation that happens on a header rather than on a frame, which is the property that
        // matters: nothing here allocates per frame.
        if (frameBuffer.Length != FrameBytes)
            frameBuffer = new byte[FrameBytes];

        Format = (rate, channels);
        return true;
    }

    /// <summary>
    /// chiaki_opus_encoder_frame: one PCM frame in, and either an Opus frame or a reason.
    /// </summary>
    /// <param name="pcm">
    /// Interleaved 16-bit samples. Its length divided by the channel count is the frame size the C
    /// takes from the audio header, so the caller's unit decides it rather than a field here.
    /// </param>
    /// <param name="frame">
    /// The bytes, valid until the next call, or empty for anything but <see cref="OpusFrameOutcome.Sent"/>.
    /// Handed back as a span into the encoder's own buffer, the way the C hands the audio sender a
    /// pointer into its.
    /// </param>
    public OpusFrameOutcome Frame(ReadOnlySpan<short> pcm, out ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        frame = default;

        if (encoder is null || Format is not { } format)
            return OpusFrameOutcome.NotInitialised;

        int frameSize = pcm.Length / format.Channels;
        if (frameSize <= 0)
            return OpusFrameOutcome.EncodeFailed;

        int written;
        try
        {
            written = encoder.Encode(pcm, frameSize, frameBuffer.AsSpan(), FrameBytes);
        }
        catch (OpusException)
        {
            // Concentus THROWS where libopus returns a negative code, and the C's test is `r < 1` -
            // so a refused frame reaches the same arm by a different road. Caught here rather than
            // left to the caller: a port that let it out would turn a frame the C logs and drops
            // into an exception on the capture thread.
            return OpusFrameOutcome.EncodeFailed;
        }
        catch (ArgumentException)
        {
            // And the argument checks it makes before it reaches libopus's own, which libopus
            // answers with OPUS_BAD_ARG rather than by refusing to run.
            return OpusFrameOutcome.EncodeFailed;
        }

        if (written < 1)
            return OpusFrameOutcome.EncodeFailed;

        if (written != FrameBytes)
            return OpusFrameOutcome.UnexpectedSize;

        frame = frameBuffer.AsSpan(0, written);
        return OpusFrameOutcome.Sent;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        encoder?.Dispose();
        encoder = null;
        disposed = true;
    }
}

/// <summary>
/// PP694: opusencoder.c's own numbers, so the port above cannot drift off them.
///
/// Three of the four things this port had to copy are literals in that file and no header publishes
/// any of them: the buffer size, the equality test that drops a frame, and the error convention the
/// C reads a return code by. The application mode is the fourth and it comes from the shim instead,
/// because it is libopus's constant rather than this module's.
/// </summary>
public static class OpusEncoderSource
{
    /// <summary>The file, which is the whole subject.</summary>
    public const string RelativePath = @"lib\src\opusencoder.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The buffer size the C requires, read out of its one assignment, or null where it has moved.
    /// </summary>
    public static int? FrameBytesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Compacted, because the compaction welds `required =` into `required=`: a needle written
        // the way the C writes it has to go through the same reader as the haystack.
        string marker = CCall.Compact("size_t opus_frame_buf_size_required =");
        string code = CCall.Compact(CCall.Code(source));

        int at = code.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + marker.Length;
        int end = code.IndexOf(';', from);

        return end > from && int.TryParse(code[from..end].Trim(), out int size) ? size : null;
    }

    /// <summary>
    /// Whether the frame path still DROPS a result that is not exactly the buffer's size.
    ///
    /// The outcome a port leaves out, and the one that costs a session its microphone in silence:
    /// the C logs it at verbose, so a build encoding to any other length sends nothing and says
    /// nothing at ordinary log levels.
    /// </summary>
    public static bool AnUnexpectedSizeIsDropped(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        return CCall.Mark(code, "else if((size_t)r != encoder->opus_frame_buf_size)") >= 0
            && CCall.InOrder(
                code,
                "else if((size_t)r != encoder->opus_frame_buf_size)",
                "else",
                "chiaki_audio_sender_opus_data(");
    }

    /// <summary>Whether a return below one is still the error, which is opus_encode's convention.</summary>
    public static bool BelowOneIsTheError(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.Mark(CCall.Code(source), "if(r < 1)") >= 0;
    }

    /// <summary>
    /// Whether the header path still destroys the old encoder before it builds the new one.
    ///
    /// An order rather than a presence. Both calls are there in either arrangement, and which comes
    /// first decides what a session has after a header it cannot honour: nothing, which is the C's
    /// answer, or an encoder still running at the previous rate.
    /// </summary>
    public static bool TheOldEncoderGoesFirst(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(source)),
            "opus_encoder_destroy(encoder->opus_encoder);",
            "encoder->opus_encoder = NULL;",
            "encoder->opus_encoder = opus_encoder_create(");
    }

    /// <summary>
    /// Whether the encoder is still created with the restricted low-delay application.
    ///
    /// By the constant's NAME. Its value is libopus's and crosses through the shim, so a check
    /// against the number here would be asserting a copy of a copy.
    /// </summary>
    public static bool TheApplicationIsRestrictedLowDelay(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(source)),
            "int application = OPUS_APPLICATION_RESTRICTED_LOWDELAY;",
            "encoder->opus_encoder = opus_encoder_create(header->rate, header->channels, application, &error);");
    }
}
