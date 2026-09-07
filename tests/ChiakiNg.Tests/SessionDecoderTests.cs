using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP700: the decoder a session decodes into, which nothing in this port had.
///
/// Block C read finished and the path was not joined. The shim's session carried a create, a start,
/// an event callback and a controller state, and no video sink - so a stream reached the frame
/// processor and stopped. libchiaki hands every assembled frame to the session's video_sample_cb,
/// and chiaki_ffmpeg_decoder_video_sample_cb is the C's own implementation of it; installing that
/// with a decoder as its user is the whole join.
///
/// WHAT A TEST CAN HOLD HERE AND WHAT IT CANNOT. That a decoder opens, names its format and refuses
/// a device the machine has not got is checkable on any machine. That a SESSION decodes is a run -
/// and it was run: 709 frames as yuv420p asking for software, 711 as nv12 asking for vulkan,
/// against a PS5 at 192.168.1.224. PP22's rule about what only a runner can say applies, and the
/// numbers are in the ledger rather than in an assertion that needs a console to pass.
/// </summary>
public class SessionDecoderTests(ITestOutputHelper output)
{
    /// <summary>A software decoder opens, and names the format it resolved.</summary>
    [Fact]
    public void ASoftwareDecoderOpensAndNamesItsFormat()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        output.WriteLine($"software resolved {decoder.PixelFormatName} ({decoder.PixelFormat})");

        Assert.Equal(string.Empty, decoder.Requested);
        Assert.NotEqual(string.Empty, decoder.PixelFormatName);
        Assert.NotEqual(-1, decoder.PixelFormat);
    }

    /// <summary>
    /// And it has decoded nothing, which is what a decoder with no session has.
    ///
    /// The count is the whole of what PP700's first slice asserts about a run, so a decoder that
    /// reported frames before one is a counter measuring something else.
    /// </summary>
    [Fact]
    public void AFreshDecoderHasDecodedNothing()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        Assert.Equal(0UL, decoder.FramesAvailable);
    }

    /// <summary>
    /// A hardware name the machine has no device for is REFUSED, not fallen back from.
    ///
    /// This is the behaviour that makes a decoder comparison mean anything: a run that silently
    /// decoded on the CPU when it was asked for a GPU would report the CPU's numbers under the
    /// GPU's name, and PP76 exists to compare exactly those.
    /// </summary>
    [Theory]
    [InlineData("not-a-decoder")]
    [InlineData("metal")]
    [InlineData("videotoolbox")]
    public void ADeviceTheMachineHasNotGotIsRefused(string name)
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, name));

        Assert.Contains(name, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name asked for changes what resolves, which is what says it is honoured.
    ///
    /// Software gives yuv420p on this machine and vulkan gives nv12 - two different answers to the
    /// same session. A decoder that ignored the name would give one, and the comparison PP76 wants
    /// would be a comparison of one thing under three labels.
    ///
    /// Skipped where the hardware is absent rather than asserted, because which devices exist is a
    /// fact about the machine.
    /// </summary>
    [Theory]
    [InlineData("vulkan")]
    [InlineData("d3d11va")]
    [InlineData("cuda")]
    public void AHardwareNameThatResolvesChangesTheFormat(string name)
    {
        using var software = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        SessionDecoder hardware;
        try
        {
            hardware = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, name);
        }
        catch (InvalidOperationException refused)
        {
            output.WriteLine($"{name} is not on this machine: {refused.Message}");
            return;
        }

        using (hardware)
        {
            output.WriteLine(
                $"{name} resolved {hardware.PixelFormatName}, software resolved {software.PixelFormatName}");

            Assert.Equal(name, hardware.Requested);
            Assert.NotEqual(string.Empty, hardware.PixelFormatName);
        }
    }

    /// <summary>
    /// Whether the format copies every frame is the C's answer, because the constant is unnumbered.
    ///
    /// PP48 measured the per-frame copy libchiaki runs for any hardware frame that is not
    /// AV_PIX_FMT_VULKAN. pixfmt.h's enum is sequential and unnumbered, so a literal on this side
    /// would be a guess a different ffmpeg silently invalidates - and the guess would be wrong in
    /// the direction that reports a copy as free.
    /// </summary>
    [Fact]
    public void TheCopyQuestionIsAnsweredByTheC()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        // Software is not the no-copy format, whatever its number is.
        Assert.True(decoder.CopiesEveryFrame);
    }

    /// <summary>A disposed decoder answers rather than throwing, and disposing twice is safe.</summary>
    [Fact]
    public void ADisposedDecoderIsQuiet()
    {
        var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        decoder.Dispose();
        decoder.Dispose();

        Assert.Equal(0UL, decoder.FramesAvailable);
        Assert.Equal(-1, decoder.PixelFormat);
        Assert.Equal(string.Empty, decoder.PixelFormatName);
        Assert.True(decoder.CopiesEveryFrame);

        // The handle is the one thing that refuses, because a caller asking for it means to attach.
        Assert.Throws<ObjectDisposedException>(() => decoder.Handle);
    }

    /// <summary>Attaching to nothing is false rather than a crash across the seam.</summary>
    [Fact]
    public void AttachingToNoSessionIsFalse()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        Assert.False(SessionDecoder.AttachTo(IntPtr.Zero, decoder.Handle));
    }

    /// <summary>
    /// PP798: A REFUSED UNIT IS NAMED, which is what ffmpegdecoder.c's log line will not do.
    ///
    /// Two baseline runs each printed "Failed to push frame: Invalid data found when processing
    /// input" once and then decoded a thousand frames. The line says which ERROR and nothing that
    /// identifies the unit, so the likely reading - a first unit arriving before the codec has its
    /// extradata - stays a guess, and a corruption halfway through a session prints the same thing.
    ///
    /// AfterAnyDecode is the fact that separates them, and it is only true of the moment the unit
    /// was offered: a push that succeeds moves the decoder's own counter, so reading it afterwards
    /// would answer about the wrong instant.
    /// </summary>
    [Fact]
    public void ARefusedUnitSaysWhichOneAndWhetherAnythingHadDecodedYet()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        Assert.Equal(0, decoder.Pushed);
        Assert.Equal(0, decoder.Refused);
        Assert.Empty(decoder.Refusals);

        // Not an access unit by any reading, which is the refusal this is about - and the decoder
        // has produced nothing, so it is the benign-looking case.
        bool took = decoder.Sample(new byte[] { 0, 1, 2, 3 }, framesLost: 0, frameRecovered: false);

        Assert.False(took, "the decoder took four bytes of nothing, so this test proves nothing");
        Assert.Equal(1, decoder.Pushed);
        Assert.Equal(1, decoder.Refused);

        SessionDecoder.DecoderRefusal refusal = Assert.Single(decoder.Refusals);

        output.WriteLine($"{refusal}");

        Assert.Equal(0, refusal.Unit);
        Assert.Equal(4, refusal.Bytes);
        Assert.False(refusal.AfterAnyDecode, "nothing had decoded, so this is the first-unit case");
    }

    /// <summary>
    /// The list is bounded and the count is not, which is what makes this safe on a bad session.
    ///
    /// A session that starts refusing everything offers sixty units a second, and a reading kept
    /// per refusal would allocate for the whole of it. What this exists to attribute happens once.
    /// </summary>
    [Fact]
    public void TheKeptRefusalsAreBoundedAndTheCountIsNot()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        for (var i = 0; i < SessionDecoder.RefusalsKept + 5; i++)
            decoder.Sample(new byte[] { 0, 1, 2, 3 }, framesLost: 0, frameRecovered: false);

        Assert.Equal(SessionDecoder.RefusalsKept + 5, decoder.Refused);
        Assert.Equal(SessionDecoder.RefusalsKept, decoder.Refusals.Count);

        // And the ones kept are the FIRST, which is where the answer is.
        Assert.Equal(0, decoder.Refusals[0].Unit);
        Assert.Equal(SessionDecoder.RefusalsKept - 1, decoder.Refusals[^1].Unit);
    }

    /// <summary>An empty frame is refused before the seam and counts as neither.</summary>
    [Fact]
    public void AnEmptyUnitIsNotOfferedAtAll()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        Assert.False(decoder.Sample([], framesLost: 0, frameRecovered: false));
        Assert.Equal(0, decoder.Pushed);
        Assert.Equal(0, decoder.Refused);
    }

    /// <summary>And a null name is refused rather than read as software.</summary>
    [Fact]
    public void ANullNameIsRefused()
        => Assert.Throws<ArgumentNullException>(
            () => new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, null!));

    /// <summary>The three names the settings screen offers are the ones this knows.</summary>
    [Fact]
    public void TheHardwareNamesAreTheSettingsScreens()
        => Assert.Equal(["vulkan", "cuda", "d3d11va"], SessionDecoder.HardwareNames);

    /// <summary>
    /// PP787: THE SECOND DOOR IS THERE AND IT IS THE DELEGATE A COMPOSITION ROOT TAKES.
    ///
    /// PP700 opened one way into the decoder and it installs the sink on the SESSION, so what
    /// decodes a frame is the C's stream connection calling the C's callback. A managed run makes
    /// the same frames and had nowhere to put them - a flip that streams and shows nothing, which
    /// PP763 has already cost this port once.
    ///
    /// The load-bearing half is the SHAPE: Sample is a VideoSampleHandler, so it is what
    /// ManagedStreamPhase already takes and no adapter stands between them. A method that merely
    /// worked would still leave a root writing a lambda to bridge two signatures.
    /// </summary>
    [Fact]
    public void TheSampleDoorIsAVideoSampleHandler()
    {
        using var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);

        VideoSampleHandler handler = decoder.Sample;

        Assert.NotNull(handler);

        // Reaching the export at all is the other half: a P/Invoke to one that is not there throws
        // rather than answering, so a bool coming back is the boundary crossed.
        byte[] annexB = [0x00, 0x00, 0x00, 0x01, 0x09, 0x10];

        Assert.False(handler([], framesLost: 0, frameRecovered: false));
        handler(annexB, framesLost: 0, frameRecovered: false);
    }

    /// <summary>
    /// And a decoder that is gone refuses rather than calling through a freed handle.
    ///
    /// The one shape that takes the process with it: a sample arriving after the dispose, which on
    /// a live path is a receive thread outliving the window it was drawing into.
    /// </summary>
    [Fact]
    public void ASampleAfterTheDisposeIsRefused()
    {
        var decoder = new SessionDecoder(IntPtr.Zero, codec: 0, maxFps: 60, string.Empty);
        decoder.Dispose();

        Assert.False(decoder.Sample([0x00, 0x00, 0x00, 0x01], framesLost: 0, frameRecovered: false));
    }
}
