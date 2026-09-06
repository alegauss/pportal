using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP751: opusdecoder.c managed, and the concealment PP740 had nowhere to send.
///
/// audioreceiver.c gives up on a missing index by emitting a frame with no buffer, and the decoder
/// turns that into a NULL packet for opus_decode - which is Opus's loss concealment rather than
/// silence. PP740 built one half and this is the other, so these hold the join as well as the
/// decode.
/// </summary>
public class ManagedOpusDecoderTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const byte Channels = 2;
    private const uint FrameSize = 480;

    private sealed class Heard : IPcmSink
    {
        public List<(byte Channels, uint Rate)> Settings { get; } = [];

        public List<int> SamplesPerChannel { get; } = [];

        public int TotalShorts { get; private set; }

        void IPcmSink.Settings(byte channels, uint rate) => Settings.Add((channels, rate));

        public void Pcm(ReadOnlySpan<short> pcm, int samplesPerChannel)
        {
            SamplesPerChannel.Add(samplesPerChannel);
            TotalShorts += pcm.Length;
        }
    }

    private static ManagedAudioHeader Header()
        => ManagedAudioHeader.Set(Channels, 16, Rate, FrameSize);

    /// <summary>
    /// chiaki_audio_header_frame_buf_size, in shorts - the multiplier that is easy to double.
    ///
    /// The C sizes a BYTE buffer as frame_size * channels * sizeof(int16_t) and decodes into it as
    /// int16_t. Managed the buffer is already shorts, so the two is the element size rather than a
    /// term, and a port carrying it across writes into twice the memory it needs.
    /// </summary>
    [Fact]
    public void TheBufferIsFrameSizeTimesChannels()
    {
        Assert.Equal(960, ManagedOpusDecoder.FrameBufferShorts(Header()));
        Assert.Equal(480, ManagedOpusDecoder.FrameBufferShorts(ManagedAudioHeader.Set(1, 16, Rate, FrameSize)));
    }

    /// <summary>A frame arriving before any STREAMINFO is dropped, as the C logs and drops it.</summary>
    [Fact]
    public void AFrameBeforeAnyHeaderIsDropped()
    {
        var heard = new Heard();
        using var decoder = new ManagedOpusDecoder(heard);

        Assert.False(decoder.Ready);

        decoder.Frame([1, 2, 3]);

        Assert.Equal(1, decoder.BeforeAnyHeader);
        Assert.Equal(0, decoder.Decoded);
        Assert.Empty(heard.SamplesPerChannel);
    }

    /// <summary>A header builds the decoder, sizes the buffer and announces the stream.</summary>
    [Fact]
    public void AHeaderBuildsTheDecoderAndAnnouncesTheStream()
    {
        if (!ManagedOpusDecoder.IsAvailable())
            return;

        var heard = new Heard();
        using var decoder = new ManagedOpusDecoder(heard);

        decoder.Header(Header());

        Assert.True(decoder.Ready);
        Assert.Equal(960, decoder.PcmBufferLength);
        Assert.Equal((Channels, (uint)Rate), Assert.Single(heard.Settings));
        Assert.Equal(FrameSize, decoder.Announced.FrameSize);
    }

    /// <summary>
    /// A ROUND TRIP: what the port's encoder produces, the port's decoder takes back.
    ///
    /// Both ends are the shim's own libopus, so this proves the decode is wired rather than that
    /// Opus works - which is the only thing a port can prove about somebody else's codec.
    /// </summary>
    [Fact]
    public void WhatTheEncoderMakesTheDecoderTakes()
    {
        if (!ManagedOpusDecoder.IsAvailable())
            return;

        var heard = new Heard();
        using var decoder = new ManagedOpusDecoder(heard);
        decoder.Header(Header());

        using var encoder = new NativeOpusEncoder(Rate, Channels);

        // A quiet ramp rather than silence: an all-zero frame encodes to almost nothing and would
        // not show a buffer being sized wrong.
        var pcm = new short[FrameSize * Channels];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (short)(i * 8);

        var packet = new byte[1275];
        int bytes = encoder.Encode(pcm, (int)FrameSize, packet);

        Assert.True(bytes > 0, $"the encoder refused the frame: {bytes}");

        decoder.Frame(packet.AsSpan(0, bytes));

        output.WriteLine($"{bytes} bytes in, {heard.SamplesPerChannel.FirstOrDefault()} samples per channel out");

        Assert.Equal(1, decoder.Decoded);
        Assert.Equal(0, decoder.Refused);
        Assert.Equal((int)FrameSize, Assert.Single(heard.SamplesPerChannel));
        Assert.Equal((int)(FrameSize * Channels), heard.TotalShorts);
    }

    /// <summary>
    /// AND AN EMPTY FRAME IS CONCEALED RATHER THAN DROPPED, which is the whole join with PP740.
    ///
    /// audioreceiver.c emits one when it gives up on a missing index. The shim passes NULL for a
    /// zero size, so opus_decode fills the gap from what it heard last - a different call to
    /// handing it an empty buffer, and the reason the size is checked in C rather than here.
    /// </summary>
    [Fact]
    public void AnEmptyFrameIsConcealedByOpusRatherThanDropped()
    {
        if (!ManagedOpusDecoder.IsAvailable())
            return;

        var heard = new Heard();
        using var decoder = new ManagedOpusDecoder(heard);
        decoder.Header(Header());

        decoder.Frame([]);

        output.WriteLine($"concealed {decoder.Concealed}, decoded {decoder.Decoded}, refused {decoder.Refused}");

        Assert.Equal(1, decoder.Concealed);
        Assert.Equal(1, decoder.Decoded);
        Assert.Equal(0, decoder.Refused);

        // It produced a frame's worth of samples, which is what concealment means.
        Assert.Equal((int)FrameSize, Assert.Single(heard.SamplesPerChannel));
    }

    /// <summary>
    /// The receiver's concealed frame IS that empty frame, driven end to end.
    ///
    /// PP740's jitter buffer gives up on a hole once something newer arrives, and what it emits is
    /// what this decodes. Neither side is told about the other here - the receiver is driven the
    /// way a stream drives it and the decoder is its sink.
    /// </summary>
    [Fact]
    public void TheReceiversOwnConcealedFrameReachesTheDecoder()
    {
        if (!ManagedOpusDecoder.IsAvailable())
            return;

        var heard = new Heard();
        using var decoder = new ManagedOpusDecoder(heard);
        var receiver = new ManagedAudioReceiver(decoder);

        receiver.StreamInfo(Header());

        using var encoder = new NativeOpusEncoder(Rate, Channels);
        var pcm = new short[FrameSize * Channels];
        var packet = new byte[1275];
        int bytes = encoder.Encode(pcm, (int)FrameSize, packet);
        byte[] unit = packet[..bytes];

        // Three to start playback, then a hole at 103 that a newer arrival gives up on.
        receiver.Frame(100, haptics: false, unit);
        receiver.Frame(101, haptics: false, unit);
        receiver.Frame(102, haptics: false, unit);
        receiver.Frame(104, haptics: false, unit);

        output.WriteLine($"decoded {decoder.Decoded}, of which concealed {decoder.Concealed}");

        Assert.Equal(1, decoder.Concealed);
        Assert.Equal(5, decoder.Decoded);
    }

    /// <summary>PP741: and the last seam but one is off the unreached list.</summary>
    [Fact]
    public void TheAudioFrameSeamIsNoLongerUnreached()
    {
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(typeof(ManagedOpusDecoder).Assembly);

        output.WriteLine(string.Join(", ", unreached));

        Assert.DoesNotContain(nameof(IAudioFrameSink), unreached);
        Assert.Equal([.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)], unreached);
    }
}
