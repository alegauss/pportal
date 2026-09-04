using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP652: the format a capture path has to produce, held against the C that announces it.
///
/// MicrophoneSurface settled that four subsystems assume a microphone and nothing opens a device.
/// The first thing any capture path needs is the format, and it is not a choice: streamconnection.c
/// tells the console one channel, sixteen bits, 48000 Hz, 480 frames, and a capture producing
/// anything else produces something the console was never told about.
///
/// READ, NOT TRANSCRIBED. PP666's lesson is that a table copied from a source stops being checked
/// the moment the source moves, and this particular call has been wrong before - PP422 found the
/// port passing (16, 1) into a (channels, bits) parameter list, announcing sixteen channels at one
/// bit. So both halves are read: the four numbers from the call site, and the parameter ORDER from
/// the setter's own declaration, because the defect was never a wrong number.
/// </summary>
public class MicrophoneFormatTests(ITestOutputHelper output)
{
    private static string? Read(string relative)
        => MicrophoneFormat.Locate(relative) is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE CRITERION: what the model says is what streamconnection.c says.
    ///
    /// A console the C announces differently to fails here, which is the whole point of reading it.
    /// </summary>
    [Fact]
    public void TheModelIsWhatTheCAnnounces()
    {
        if (Read(MicrophoneFormat.AnnouncerRelativePath) is not { } announcer)
            return;

        MicrophoneAnnouncement? read = MicrophoneFormat.AnnouncementIn(announcer);

        Assert.NotNull(read);
        output.WriteLine($"streamconnection.c announces {read}");

        Assert.Equal(MicrophoneFormat.Announced, read.Value);
    }

    /// <summary>
    /// And the parameter order is what makes those four numbers mean what they mean.
    ///
    /// PP422's defect passed valid numbers into the wrong holes. A check on the numbers alone would
    /// have read (16, 1) as a perfectly good announcement of sixteen channels at one bit.
    /// </summary>
    [Fact]
    public void TheSetterTakesChannelsBeforeBits()
    {
        if (Read(MicrophoneFormat.SetterRelativePath) is not { } setter)
            return;

        IReadOnlyList<string> order = MicrophoneFormat.ParameterOrderIn(setter);
        output.WriteLine("audio.c declares: " + string.Join(", ", order));

        Assert.Equal(MicrophoneFormat.ExpectedParameterOrder, order);
    }

    /// <summary>
    /// The unit's arithmetic, which is what a capture buffer is sized by.
    ///
    /// 960 bytes and ten milliseconds. The ten is why 480 is the frame size at all: it is Opus's
    /// ten-millisecond frame at 48 kHz, not a buffer somebody picked.
    /// </summary>
    [Fact]
    public void AUnitIsTenMillisecondsAndNineHundredAndSixtyBytes()
    {
        MicrophoneAnnouncement announced = MicrophoneFormat.Announced;

        Assert.Equal(2, MicrophoneFormat.BytesPerSample(announced));
        Assert.Equal(960, MicrophoneFormat.BytesPerUnit(announced));
        Assert.Equal(10.0, MicrophoneFormat.UnitMilliseconds(announced), 6);
        Assert.Equal(100.0, MicrophoneFormat.UnitsPerSecond(announced), 6);
    }

    /// <summary>The arithmetic on a format that is not the announced one, so it is arithmetic.</summary>
    [Theory]
    [InlineData(2, 16, 48000, 480, 1920, 10.0)]
    [InlineData(1, 8, 8000, 160, 160, 20.0)]
    [InlineData(2, 32, 44100, 441, 3528, 10.0)]
    public void TheUnitIsDerivedAndNotStored(
        int channels, int bits, int rate, int frames, int expectedBytes, double expectedMs)
    {
        var announced = new MicrophoneAnnouncement(channels, bits, rate, frames);

        Assert.Equal(expectedBytes, MicrophoneFormat.BytesPerUnit(announced));
        Assert.Equal(expectedMs, MicrophoneFormat.UnitMilliseconds(announced), 6);
    }

    /// <summary>
    /// The reader finds the call rather than the comment beside it.
    ///
    /// The real site carries a long comment naming both the wrong order and the right one, with
    /// digits in it. A reader that matched loosely would find whichever numbers that prose held.
    /// </summary>
    [Fact]
    public void TheCommentAtTheSiteIsNotTheCall()
    {
        const string source = """
            // PP422: (channels, bits), not (bits, channels). This passed 16 and 1, which scans as
            // sixteen bits and one channel. chiaki_audio_header_set(&h, 99, 99, 99, 99);
            chiaki_audio_header_set(&audio_header_input, 1, 16, 48000, 480);
            """;

        Assert.Equal(new MicrophoneAnnouncement(1, 16, 48000, 480), MicrophoneFormat.AnnouncementIn(source));
    }

    /// <summary>PP422's own defect, as the reader would see it: the numbers swapped.</summary>
    [Fact]
    public void ThePP422ShapeReadsBackAsSixteenChannels()
    {
        MicrophoneAnnouncement? wrong = MicrophoneFormat.AnnouncementIn(
            "chiaki_audio_header_set(&h, 16, 1, 48000, 480);");

        Assert.NotNull(wrong);
        Assert.Equal(16, wrong.Value.Channels);
        Assert.Equal(1, wrong.Value.Bits);
        Assert.NotEqual(MicrophoneFormat.Announced, wrong.Value);
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(MicrophoneFormat.AnnouncementIn(""));
        Assert.Empty(MicrophoneFormat.ParameterOrderIn(""));
        Assert.Equal(0.0, MicrophoneFormat.UnitMilliseconds(new MicrophoneAnnouncement(1, 16, 0, 480)));
        Assert.Equal(0.0, MicrophoneFormat.UnitsPerSecond(new MicrophoneAnnouncement(1, 16, 0, 480)));
    }
}
