using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP694, under PP32: the microphone's encoder in managed code, held against opusencoder.c.
///
/// PP32 measured that libopus has two consumers and that porting the decoder alone removes nothing.
/// Its own sentence named the blocker - the encoder is on the path that had no input - and PP652
/// answered it: WasapiCapture delivers whole 960-byte units in exactly the format
/// streamconnection.c announces, so there is something to encode.
///
/// THE ORACLE IS WHAT THE MODULE DOES, NOT WHAT IT RETURNS. chiaki_opus_encoder_frame needs an
/// audio sender, which needs a session, which needs a console. What it does to a frame is
/// opus_encode with two parameters of its own - the application mode and the forty-byte buffer -
/// and those run through the shim with nothing behind them.
///
/// AND THE COMPARISON IS NOT BYTE FOR BYTE, which is a finding rather than a compromise. Concentus
/// is a line-by-line port of libopus and is still not bit-exact: PP651 measured 386,460 of 480,000
/// samples differing on the decode side, and the encoder differs on EVERY frame. What agrees, on
/// every frame, is the length and the TOC byte - the mode, the bandwidth and the frame count - so
/// the differential is written to what the protocol carries, and says so rather than quietly
/// asserting something weaker than it looks.
/// </summary>
public class ManagedOpusEncoderTests(ITestOutputHelper output)
{
    /// <summary>The format streamconnection.c announces for the microphone.</summary>
    private const int Rate = 48000;

    private const int Channels = 1;

    /// <summary>480 samples a channel: 10ms, and the 960 bytes a unit WasapiCapture delivers.</summary>
    private const int FrameSize = 480;

    private const int Frames = 200;

    /// <summary>
    /// A deterministic signal, chosen to be work rather than silence.
    ///
    /// Silence encodes to almost nothing and would measure the call rather than the encoder, which
    /// is the same reason spike/opus-decode builds its corpus this way. Two tones and a little
    /// noise, from a fixed seed, so the comparison below is the same comparison on every machine -
    /// which is what "recorded input" has to mean for a path with no microphone in CI.
    /// </summary>
    private static short[] Pcm(int frames)
    {
        var pcm = new short[frames * FrameSize * Channels];
        var rng = new Random(20260904);

        for (var i = 0; i < pcm.Length; i++)
        {
            double t = i / (double)Rate;
            double v = (0.42 * Math.Sin(2 * Math.PI * 440 * t))
                + (0.21 * Math.Sin(2 * Math.PI * 1320 * t))
                + (0.03 * (rng.NextDouble() - 0.5));

            pcm[i] = (short)Math.Clamp(v * 32000, short.MinValue, short.MaxValue);
        }

        return pcm;
    }

    /// <summary>The shim carries libopus, so the differentials below are comparisons.</summary>
    [Fact]
    public void TheShimCarriesLibopus() => Assert.True(NativeOpusEncoder.IsAvailable());

    /// <summary>
    /// THE DIFFERENTIAL: every frame comes out the same length, with the same TOC byte.
    ///
    /// Both halves are the protocol. The LENGTH is what opusencoder.c tests before it sends - a
    /// result that is not exactly forty is dropped as a violation - so a managed encoder producing
    /// thirty-nine would send nothing at all and log it at verbose. The TOC is the first byte of an
    /// Opus packet and carries the mode, the bandwidth and the frame count, so two encoders
    /// agreeing on it agree about what kind of packet this is.
    ///
    /// The payload is asserted to DIFFER, on purpose. That is what Concentus is, measured rather
    /// than assumed, and a test that quietly dropped the comparison would leave a reader thinking
    /// the two were the same encoder.
    /// </summary>
    [Fact]
    public void TheManagedEncoderAgreesWithLibopusOnLengthAndTocAndNotOnBytes()
    {
        if (!NativeOpusEncoder.IsAvailable())
            return;

        short[] pcm = Pcm(Frames);

        using var native = new NativeOpusEncoder(Rate, Channels);
        using var managed = new ManagedOpusEncoder();

        Assert.True(managed.Header(Rate, Channels));

        var theirs = new byte[ManagedOpusEncoder.FrameBytes];
        int sameLength = 0, sameToc = 0, identical = 0;

        for (var frame = 0; frame < Frames; frame++)
        {
            ReadOnlySpan<short> unit = pcm.AsSpan(frame * FrameSize * Channels, FrameSize * Channels);

            int nativeLength = native.Encode(unit, FrameSize, theirs);
            OpusFrameOutcome outcome = managed.Frame(unit, out ReadOnlySpan<byte> ours);

            Assert.Equal(OpusFrameOutcome.Sent, outcome);
            Assert.Equal(ManagedOpusEncoder.FrameBytes, nativeLength);

            if (nativeLength == ours.Length)
                sameLength++;

            if (theirs[0] == ours[0])
                sameToc++;

            if (ours.SequenceEqual(theirs.AsSpan(0, nativeLength)))
                identical++;
        }

        output.WriteLine(
            $"{Frames} frames: {sameLength} same length, {sameToc} same TOC, {identical} identical");

        Assert.Equal(Frames, sameLength);
        Assert.Equal(Frames, sameToc);

        // The measured fact, stated rather than skipped over. If this ever becomes non-zero the
        // library changed under the port, which is news either way.
        Assert.Equal(0, identical);
    }

    /// <summary>
    /// Both encoders fill the buffer exactly, which is what makes forty a BITRATE and not a bound.
    ///
    /// libopus reads a small maximum as a hard constraint and pads to it, so forty bytes a frame at
    /// a hundred frames a second is 32 kbps. A port that read the number as "up to forty" would be
    /// right about the buffer and wrong about the protocol - and would send nothing, because the C
    /// drops everything that is not exactly that.
    /// </summary>
    [Fact]
    public void BothEncodersFillTheBufferExactly()
    {
        if (!NativeOpusEncoder.IsAvailable())
            return;

        short[] pcm = Pcm(8);

        using var native = new NativeOpusEncoder(Rate, Channels);
        using var managed = new ManagedOpusEncoder();
        managed.Header(Rate, Channels);

        var theirs = new byte[ManagedOpusEncoder.FrameBytes];

        for (var frame = 0; frame < 8; frame++)
        {
            ReadOnlySpan<short> unit = pcm.AsSpan(frame * FrameSize * Channels, FrameSize * Channels);

            Assert.Equal(ManagedOpusEncoder.FrameBytes, native.Encode(unit, FrameSize, theirs));
            Assert.Equal(OpusFrameOutcome.Sent, managed.Frame(unit, out ReadOnlySpan<byte> ours));
            Assert.Equal(ManagedOpusEncoder.FrameBytes, ours.Length);
        }
    }

    /// <summary>
    /// SILENCE DOES NOT FILL IT, and opusencoder.c therefore drops every frame of it.
    ///
    /// Measured rather than expected: libopus pads a small maximum out to a constraint for content
    /// with something in it, and answers three bytes for a silent frame. opusencoder.c's test is
    /// equality with forty, so a silent microphone produces frames the module discards as a
    /// protocol violation, logged at verbose - which is invisible at ordinary log levels.
    ///
    /// This is the C's behaviour and the port reproduces it. It is also the answer to a question a
    /// reader of that function would ask the other way round: not "why forty" but "what happens
    /// when it is not forty", and the answer is that it happens all the time, on purpose or not.
    ///
    /// Both encoders answer the same length, which is the differential this case is actually for.
    /// </summary>
    [Fact]
    public void SilenceDoesNotFillTheFrameAndIsDropped()
    {
        if (!NativeOpusEncoder.IsAvailable())
            return;

        var silence = new short[FrameSize * Channels];

        using var native = new NativeOpusEncoder(Rate, Channels);
        using var managed = new ManagedOpusEncoder();
        managed.Header(Rate, Channels);

        var theirs = new byte[ManagedOpusEncoder.FrameBytes];

        // Twice: the first frame of any Opus stream carries more, so the steady state is the second.
        for (var frame = 0; frame < 2; frame++)
        {
            int nativeLength = native.Encode(silence, FrameSize, theirs);
            OpusFrameOutcome outcome = managed.Frame(silence, out ReadOnlySpan<byte> ours);

            output.WriteLine($"silent frame {frame}: libopus {nativeLength} bytes, managed {outcome}");

            Assert.InRange(nativeLength, 1, ManagedOpusEncoder.FrameBytes - 1);
            Assert.Equal(OpusFrameOutcome.UnexpectedSize, outcome);
            Assert.True(ours.IsEmpty, "a dropped frame hands back nothing, as the C sends nothing");
        }
    }

    /// <summary>
    /// A frame before a header is the C's first arm: nothing encoded, nothing sent.
    ///
    /// It is not an error either. The C logs and returns, so the frame is lost with one line in the
    /// log - which is the behaviour a session has if a header never arrived.
    /// </summary>
    [Fact]
    public void AFrameBeforeAHeaderIsNotEncoded()
    {
        using var managed = new ManagedOpusEncoder();

        Assert.False(managed.Initialised);
        Assert.Equal(
            OpusFrameOutcome.NotInitialised,
            managed.Frame(new short[FrameSize], out ReadOnlySpan<byte> frame));

        Assert.True(frame.IsEmpty);
    }

    /// <summary>
    /// A header the encoder cannot honour leaves NOTHING behind, which is the C's order.
    ///
    /// The old encoder is destroyed before the new one is attempted, so a session told to switch to
    /// a format libopus refuses ends up with no encoder rather than with the previous one still
    /// running. That is the safer of the two: the wrong rate is audible garbage, and silence is at
    /// least a symptom somebody reports.
    /// </summary>
    [Fact]
    public void AHeaderThatCannotBeHonouredLeavesNoEncoder()
    {
        using var managed = new ManagedOpusEncoder();

        Assert.True(managed.Header(Rate, Channels));
        Assert.True(managed.Initialised);

        Assert.False(managed.Header(rate: 12345, channels: Channels));
        Assert.False(managed.Initialised);
        Assert.Null(managed.Format);

        Assert.Equal(
            OpusFrameOutcome.NotInitialised, managed.Frame(new short[FrameSize], out _));
    }

    /// <summary>
    /// A frame size Opus has no packet for is the C's `r < 1` arm rather than a throw.
    ///
    /// 100 samples is not one of Opus's frame durations, and libopus answers OPUS_BAD_ARG. Concentus
    /// throws instead, which is the same refusal by a different road - so the port catches it and
    /// reports the outcome the C reports.
    /// </summary>
    [Fact]
    public void AFrameSizeOpusHasNoPacketForIsAnEncodeFailure()
    {
        using var managed = new ManagedOpusEncoder();
        managed.Header(Rate, Channels);

        Assert.Equal(OpusFrameOutcome.EncodeFailed, managed.Frame(new short[100], out _));
    }

    /// <summary>
    /// The application mode is libopus's number, asked rather than written down here.
    ///
    /// Concentus names it with its own enum and the C names it with libopus's macro. A managed
    /// constant would have been right until either renumbered, so the shim is asked and the two are
    /// held together here.
    /// </summary>
    [Fact]
    public void TheApplicationModeIsTheOneTheCChooses()
    {
        if (!NativeOpusEncoder.IsAvailable())
            return;

        // OPUS_APPLICATION_RESTRICTED_LOWDELAY, which is what opusencoder.c names.
        Assert.Equal(2051, NativeOpusEncoder.Application);

        Assert.Equal(
            (int)Concentus.Enums.OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY,
            NativeOpusEncoder.Application);
    }

    /// <summary>
    /// opusencoder.c's own numbers, so the port cannot drift off the file it copies.
    ///
    /// The forty is a literal in that file and no header publishes it, so this is where the managed
    /// constant is checked. The three orderings beside it are the parts a port gets wrong without
    /// failing: which arm drops, which return code is the error, and what a failed header leaves.
    /// </summary>
    [Fact]
    public void TheCsOwnNumbersAndOrderingsStillHold()
    {
        if (OpusEncoderSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(ManagedOpusEncoder.FrameBytes, OpusEncoderSource.FrameBytesIn(source));
        Assert.True(OpusEncoderSource.AnUnexpectedSizeIsDropped(source));
        Assert.True(OpusEncoderSource.BelowOneIsTheError(source));
        Assert.True(OpusEncoderSource.TheOldEncoderGoesFirst(source));
        Assert.True(OpusEncoderSource.TheApplicationIsRestrictedLowDelay(source));
    }

    /// <summary>And each of those readers refuses a file that lost what it names.</summary>
    [Fact]
    public void EachSourceReaderRefusesAFileThatLostIt()
    {
        Assert.Null(OpusEncoderSource.FrameBytesIn("nothing here"));
        Assert.False(OpusEncoderSource.AnUnexpectedSizeIsDropped("if(r < 1) return;"));
        Assert.False(OpusEncoderSource.BelowOneIsTheError("if(r < 0) return;"));
        Assert.False(OpusEncoderSource.TheOldEncoderGoesFirst(
            "encoder->opus_encoder = opus_encoder_create(header->rate, header->channels, application, &error);"));
        Assert.False(OpusEncoderSource.TheApplicationIsRestrictedLowDelay(
            "int application = OPUS_APPLICATION_VOIP;"));
    }

    /// <summary>
    /// PP694's second criterion: what still holds libopus, with every caller counted.
    ///
    /// Across lib, shim and test rather than lib/src alone, which is the correction PP692 made one
    /// library over. The answer is not "nothing": the playback path is unported, and the shim's own
    /// wrappers hold it as an oracle - which is a different kind of holding, and the census would
    /// be misleading if it counted them the same way.
    /// </summary>
    [Fact]
    public void TheCensusNamesEveryCallerAndSaysWhatStillHoldsIt()
    {
        IReadOnlyList<string> swept = OpusDependency.CallingFilesEverywhere();
        if (swept.Count == 0)
            return;

        output.WriteLine($"calls libopus: {string.Join(", ", swept)}");
        output.WriteLine(
            "still holding it after the encoder is managed: "
                + string.Join(", ", OpusDependency.StillHoldingIt.Select(one => $"{one.File} ({one.Role})")));

        Assert.Equal(
            OpusDependency.Callers.Select(one => one.File).Order(StringComparer.Ordinal),
            swept);

        // The microphone's is the one PP694 replaces, and the only one.
        Assert.Equal(
            "opusencoder.c",
            Assert.Single(OpusDependency.Callers, one => one.Role == OpusCallerRole.Microphone).File);

        // So the dependency does NOT leave with this line, and the census is what says so.
        Assert.NotEmpty(OpusDependency.StillHoldingIt);
        Assert.Contains(OpusDependency.StillHoldingIt, one => one.Role == OpusCallerRole.Playback);
        Assert.Contains(OpusDependency.StillHoldingIt, one => one.Role == OpusCallerRole.Oracle);
    }

    /// <summary>
    /// And audiosender.c is still not a caller, which is the trap PP32 named.
    ///
    /// It names its parameter opus_sender and its buffers frame this and frame that, and it calls
    /// nothing in the library - it carries frames that are already encoded. A census taken by
    /// searching for the word gets three consumers and concludes the encoder is one of two.
    /// </summary>
    [Fact]
    public void TheFileThatNamesOpusEverywhereStillCallsItNowhere()
    {
        if (OpusDependency.Locate(@"lib\src\" + OpusDependency.CarriesEncodedFramesOnly) is not { } path)
            return;

        Assert.False(OpusDependency.CallsOpus(File.ReadAllText(path)));
        Assert.DoesNotContain(
            OpusDependency.CarriesEncodedFramesOnly,
            OpusDependency.CallingFilesEverywhere());
    }
}
