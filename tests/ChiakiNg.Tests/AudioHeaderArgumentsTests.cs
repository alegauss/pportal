using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP422: sixteen channels at one bit.
///
/// chiaki_audio_header_set takes (channels, bits, rate, frame_size).
/// stream_connection_enable_microphone passed 16 and 1, which reads as the right format in the
/// wrong order. PP396's capture is what showed it.
/// </summary>
public class AudioHeaderArgumentsTests
{
    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. Both callers put channels where channels go.
    ///
    /// The two disagreeing is what proved it: one function, two call sites, opposite orders, and
    /// only one of them can be right about what a microphone is. So both are asked, and a check on
    /// one alone would pass the day somebody made the other match.
    /// </summary>
    [Fact]
    public void BothCallersPutChannelsFirst()
    {
        if (AudioHeaderArguments.LocateLib() is not { } lib)
            return;
        if (AudioHeaderArguments.LocateGui() is not { } gui)
            return;

        Assert.True(
            AudioHeaderArguments.BothCallersPutChannelsFirst(
                File.ReadAllText(lib), File.ReadAllText(gui)),
            "an audio header is built with a bit depth in the channel slot, so the console is told "
                + "a channel count no microphone has");
    }

    /// <summary>
    /// The reader refuses the call as it was, and accepts it as it is.
    ///
    /// Against synthetic calls rather than by putting the swap back, and both directions - because
    /// "channels come first" must not be satisfiable by a file with no call in it at all.
    /// </summary>
    [Fact]
    public void TheReaderRefusesTheSwapAndAnEmptyFile()
    {
        Assert.True(AudioHeaderArguments.ChannelsComeFirstIn(
            "\tchiaki_audio_header_set(&audio_header_input, 1, 16, 48000, 480);"));

        // The shape PP422 removed.
        Assert.False(AudioHeaderArguments.ChannelsComeFirstIn(
            "\tchiaki_audio_header_set(&audio_header_input, 16, 1, 48000, 480);"));

        // The Qt client's, which was right all along.
        Assert.True(AudioHeaderArguments.ChannelsComeFirstIn(
            "\tchiaki_audio_header_set(&audio_header, 2, 16, MICROPHONE_SAMPLES * 100, MICROPHONE_SAMPLES);"));

        // A file with no call has nothing to say, so it does not pass.
        Assert.False(AudioHeaderArguments.ChannelsComeFirstIn(""));

        // And a comment naming the old call does not satisfy it - PP400's rule.
        Assert.False(AudioHeaderArguments.ChannelsComeFirstIn(
            "// chiaki_audio_header_set(&audio_header_input, 1, 16, 48000, 480);"));
    }

    /// <summary>
    /// A named constant in the channel slot is counted and not judged.
    ///
    /// The Qt client's rate and frame size are already constants; a channel count could become one
    /// too, and refusing that would make this check about spelling rather than about the argument
    /// order.
    /// </summary>
    [Fact]
    public void ANamedConstantIsNotJudged()
    {
        Assert.True(AudioHeaderArguments.ChannelsComeFirstIn(
            "\tchiaki_audio_header_set(&h, MICROPHONE_CHANNELS, 16, 48000, 480);"));
    }

    /// <summary>
    /// THE BYTES CROSS OVER ONCE, and that is why the wire could show the swap.
    ///
    /// The arguments are (channels, bits) and chiaki_audio_header_save writes (bits, channels). So
    /// the corrected call produces 10-01 where the old one produced 01-10.
    /// </summary>
    [Fact]
    public void TheHeaderWritesBitsBeforeChannels()
    {
        byte[] header = AudioHeaderArguments.Microphone();

        Assert.Equal(AudioHeaderArguments.HeaderSize, header.Length);

        // bits 16, then channels 1 - the reverse of the argument order.
        Assert.Equal(16, header[0]);
        Assert.Equal(1, header[1]);

        // 48000 and 480, big endian, then the trailing 1.
        Assert.Equal<byte[]>([0x00, 0x00, 0xbb, 0x80], header[2..6]);
        Assert.Equal<byte[]>([0x00, 0x00, 0x01, 0xe0], header[6..10]);
        Assert.Equal<byte[]>([0x00, 0x00, 0x00, 0x01], header[10..]);
    }

    /// <summary>
    /// AND THE OLD BYTES ARE WHAT PP396'S CAPTURE HOLDS, which is the evidence rather than an
    /// argument.
    ///
    /// The swapped call produced 01-10: bits 1, channels 16. Stated here so the fix's before and
    /// after are both written down, and so a reader can find the recording that showed it.
    /// </summary>
    [Fact]
    public void TheSwappedCallProducedWhatTheCaptureShows()
    {
        byte[] swapped = AudioHeaderArguments.Save(
            channels: 16, bits: 1, rate: 48000, frameSize: 480);

        Assert.Equal(1, swapped[0]);
        Assert.Equal(16, swapped[1]);

        // The rest of the header is identical, which is why nothing else in the capture moved.
        Assert.Equal<byte[]>(AudioHeaderArguments.Microphone()[2..], swapped[2..]);
    }
}
