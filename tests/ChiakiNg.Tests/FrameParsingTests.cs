using System.Text;
using System.Text.Json;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP215: what a reused json-c tokener does, measured, and the rule that keeps the port out of it.
///
/// The first half is json-c's own answer to each frame sequence. That is the evidence - the claim
/// in <see cref="FrameParsing"/> is about a library this project does not ship the source of, and a
/// claim like that is worth exactly what it was measured against. The second half runs the same
/// sequences through the port and asserts the opposite.
///
/// PP33: THE MEASUREMENT IS NOW READ RATHER THAN RE-TAKEN. It used to call the library on the spot
/// and decline where PP663's flag had left it out, so six findings about a state machine were
/// reported as passes on every ordinary build - and the file that carried the library is the one
/// PP33 deletes. <see cref="JsonOracleRecording"/> holds what json-c said, taken from the library
/// by <see cref="JsonOracleRecorder"/>, and JsonDifferentialTests re-derives it wherever a build
/// still can.
/// </summary>
public class FrameParsingTests
{
    private const string Good = JsonOracleCases.Good;
    private const string AlsoGood = JsonOracleCases.AlsoGood;

    /// <summary>An opening brace and a key with no value: not wrong, just not finished.</summary>
    private const string Truncated = JsonOracleCases.Truncated;

    /// <summary>Not JSON in any state of completion.</summary>
    private const string Garbage = JsonOracleCases.Garbage;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Whether one frame yields a document, through the port.</summary>
    private static bool Parses(string frame)
    {
        using JsonDocument? document = FrameParsing.Parse(Bytes(frame));
        return document is not null;
    }

    /// <summary>The recorded run of that name, which is json-c's own answer to the sequence.</summary>
    private static JsonTokenerRun Recorded(string name)
    {
        JsonOracleRecording? recording = JsonOracleRecording.Read();
        Assert.True(recording is not null, $"{JsonOracleRecording.RelativePath} is missing");

        JsonTokenerRun? run = recording.Run(name);
        Assert.True(run is not null, $"the recording holds no run called \"{name}\"");

        return run.Value;
    }

    /// <summary>Whether each frame of a recorded sequence yielded a document.</summary>
    private static bool[] ParsedIn(string name) => [.. Recorded(name).Parsed];

    /// <summary>
    /// Reuse is fine while every frame is a whole document, which is why this has never been
    /// noticed: the socket carries complete notifications and the tokener never sees a partial one.
    /// </summary>
    [Fact]
    public void ThreeCompleteFramesInARowAllParse()
    {
        Assert.Equal([true, true, true], ParsedIn("three complete frames all parse"));
        Assert.All(Recorded("three complete frames all parse").Errors, error => Assert.Equal(0, error));
    }

    /// <summary>
    /// A truncated frame does not fail - it leaves the tokener expecting the rest. The NEXT frame
    /// is then read as that rest, so a whole good notification is consumed producing nothing, and
    /// the one after it is a hard error the tokener never leaves.
    /// </summary>
    [Fact]
    public void ATruncatedFrameSwallowsTheOneAfterIt()
    {
        JsonTokenerRun run = Recorded("a truncated frame swallows the one after it");

        // The truncated frame itself, the good one it consumed, and the two after it that never
        // come back on their own.
        Assert.Equal([false, false, false, false], run.Parsed);
        Assert.NotEqual(0, run.Errors[0]);
    }

    /// <summary>Garbage is the same outcome one step sooner: nothing, from that frame onwards.</summary>
    [Fact]
    public void GarbageStopsTheTokenerAtOnceAndForGood()
        => Assert.Equal(
            [false, false, false], ParsedIn("garbage stops the tokener at once and for good"));

    /// <summary>
    /// And the frame it refused was never the problem. A tokener that has not seen the bad one
    /// parses the same text perfectly - which is what makes this a property of the PARSER and not
    /// of the traffic.
    /// </summary>
    [Fact]
    public void AFreshTokenerParsesWhatAPoisonedOneRefused()
    {
        Assert.Equal([false, false], ParsedIn("a poisoned tokener refuses what is good"));
        Assert.Equal([true], ParsedIn("a fresh tokener parses what the poisoned one refused"));
    }

    /// <summary>The one call that clears it - which holepunch.c never makes.</summary>
    [Fact]
    public void AResetIsWhatClearsIt()
    {
        JsonTokenerRun run = Recorded("a reset is what clears it, and holepunch.c never calls one");

        // Garbage, then a good frame it refuses, then the SAME good frame after a reset.
        Assert.Equal([false, false, true], run.Parsed);
        Assert.Equal([false, false, true], run.ResetBefore);
        Assert.Equal(0, run.Errors[2]);
    }

    /// <summary>
    /// Two things that look like they would break it and do not, which is worth knowing because
    /// they bound the defect: it is unfinished input that does this, not any awkward input.
    /// </summary>
    [Fact]
    public void TrailingBytesAndAnEmptyFrameAreHarmless()
    {
        // Trailing bytes parse, the frame after them parses, an empty frame yields nothing, and
        // the frame after THAT still parses - so neither poisons the tokener.
        Assert.Equal(
            [true, true, false, true], ParsedIn("trailing bytes and an empty frame are harmless"));
    }

    /// <summary>
    /// And the port, over the same sequences. Every good frame parses whatever preceded it,
    /// because nothing is held between calls at all.
    /// </summary>
    [Fact]
    public void ThePortKeepsNoStateBetweenFrames()
    {
        foreach (string bad in new[] { Truncated, Garbage, "{\"a\":}", "" })
        {
            Assert.False(Parses(bad), bad);

            Assert.True(Parses(Good), $"good frame after {bad}");
            Assert.True(Parses(AlsoGood), $"and the next one after {bad}");
        }
    }

    /// <summary>Trailing bytes are ignored on this side too, which is json-c's answer measured above.</summary>
    [Fact]
    public void TrailingBytesAfterTheDocumentAreIgnoredHereToo()
        => Assert.True(Parses(Good + "xx"));

    /// <summary>
    /// A frame is its length, not a terminated string. The core reads into a buffer it zeroed and
    /// hands the tokener the RECEIVED length, so a frame padded with NUL bytes is still one frame -
    /// and one longer than that buffer is not a frame either side can see.
    /// </summary>
    [Fact]
    public void AFrameIsItsLengthRatherThanATerminator()
    {
        byte[] atTheLimit = new byte[PushSocketLoop.MaxFrameSize];
        Bytes(Good).CopyTo(atTheLimit, 0);

        using JsonDocument? fits = FrameParsing.Parse(atTheLimit);
        Assert.NotNull(fits);

        byte[] tooBig = new byte[PushSocketLoop.MaxFrameSize + 1];
        Bytes(Good).CopyTo(tooBig, 0);

        using JsonDocument? oversized = FrameParsing.Parse(tooBig);
        Assert.Null(oversized);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheOneReusedTokenerIsStillThere()
    {
        string? file = FrameParsingSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(FrameParsingSource.TheLoopStillKeepsOneTokener(core), "made once, fed in the loop");
        Assert.True(FrameParsingSource.TheTokenerIsStillNeverReset(core), "and never reset");
        Assert.True(
            FrameParsingSource.EveryOtherTokenerIsStillOneDocumentLong(core),
            "one tokener per parse everywhere else");
        Assert.True(
            FrameParsingSource.ABadFrameStillOnlyReadsTheNextOne(core),
            "and a refused frame just reads on");
    }
}
