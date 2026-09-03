using System.Text;
using System.Text.Json;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP215: what a reused json-c tokener does, measured, and the rule that keeps the port out of it.
///
/// The first half runs json-c itself through <see cref="NativeJsonTokener"/>. That is the evidence
/// - the claim in <see cref="FrameParsing"/> is about a library this project does not ship the
/// source of, and a claim like that is worth exactly what it was measured against. The second half
/// runs the same sequences through the port and asserts the opposite.
/// </summary>
public class FrameParsingTests
{
    private const string Good = "{\"a\":1}";
    private const string AlsoGood = "{\"b\":2}";
    private const string Third = "{\"c\":3}";

    /// <summary>An opening brace and a key with no value: not wrong, just not finished.</summary>
    private const string Truncated = "{\"a\":";

    /// <summary>Not JSON in any state of completion.</summary>
    private const string Garbage = "not json at all";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Whether one frame yields a document, through the port.</summary>
    private static bool Parses(string frame)
    {
        using JsonDocument? document = FrameParsing.Parse(Bytes(frame));
        return document is not null;
    }

    /// <summary>
    /// Reuse is fine while every frame is a whole document, which is why this has never been
    /// noticed: the socket carries complete notifications and the tokener never sees a partial one.
    /// </summary>
    [Fact]
    public void ThreeCompleteFramesInARowAllParse()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? tokener = NativeJsonTokener.Create();
        Assert.NotNull(tokener);

        foreach (string frame in new[] { Good, AlsoGood, Third })
        {
            using NativeJson? document = tokener.Parse(frame);

            Assert.NotNull(document);
            Assert.Equal(0, tokener.Error);
        }
    }

    /// <summary>
    /// A truncated frame does not fail - it leaves the tokener expecting the rest. The NEXT frame
    /// is then read as that rest, so a whole good notification is consumed producing nothing, and
    /// the one after it is a hard error the tokener never leaves.
    /// </summary>
    [Fact]
    public void ATruncatedFrameSwallowsTheOneAfterIt()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? tokener = NativeJsonTokener.Create();
        Assert.NotNull(tokener);

        Assert.Null(tokener.Parse(Truncated));
        Assert.NotEqual(0, tokener.Error);

        // The good frame that follows it: gone, and not because there was anything wrong with it.
        Assert.Null(tokener.Parse(AlsoGood));

        // And it never comes back on its own.
        Assert.Null(tokener.Parse(Third));
        Assert.Null(tokener.Parse(Good));
    }

    /// <summary>Garbage is the same outcome one step sooner: nothing, from that frame onwards.</summary>
    [Fact]
    public void GarbageStopsTheTokenerAtOnceAndForGood()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? tokener = NativeJsonTokener.Create();
        Assert.NotNull(tokener);

        Assert.Null(tokener.Parse(Garbage));

        Assert.Null(tokener.Parse(Good));
        Assert.Null(tokener.Parse(AlsoGood));
    }

    /// <summary>
    /// And the frame it refused was never the problem. A tokener that has not seen the bad one
    /// parses the same text perfectly - which is what makes this a property of the PARSER and not
    /// of the traffic.
    /// </summary>
    [Fact]
    public void AFreshTokenerParsesWhatAPoisonedOneRefused()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? poisoned = NativeJsonTokener.Create();
        Assert.NotNull(poisoned);

        Assert.Null(poisoned.Parse(Garbage));
        Assert.Null(poisoned.Parse(AlsoGood));

        using NativeJsonTokener? fresh = NativeJsonTokener.Create();
        Assert.NotNull(fresh);

        using NativeJson? document = fresh.Parse(AlsoGood);
        Assert.NotNull(document);
    }

    /// <summary>The one call that clears it - which holepunch.c never makes.</summary>
    [Fact]
    public void AResetIsWhatClearsIt()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? tokener = NativeJsonTokener.Create();
        Assert.NotNull(tokener);

        Assert.Null(tokener.Parse(Garbage));
        Assert.Null(tokener.Parse(Good));

        tokener.Reset();

        using NativeJson? document = tokener.Parse(Good);
        Assert.NotNull(document);
        Assert.Equal(0, tokener.Error);
    }

    /// <summary>
    /// Two things that look like they would break it and do not, which is worth knowing because
    /// they bound the defect: it is unfinished input that does this, not any awkward input.
    /// </summary>
    [Fact]
    public void TrailingBytesAndAnEmptyFrameAreHarmless()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJsonTokener? trailing = NativeJsonTokener.Create();
        Assert.NotNull(trailing);

        using (NativeJson? first = trailing.Parse(Good + "xx"))
            Assert.NotNull(first);

        using (NativeJson? second = trailing.Parse(AlsoGood))
            Assert.NotNull(second);

        using NativeJsonTokener? empty = NativeJsonTokener.Create();
        Assert.NotNull(empty);

        Assert.Null(empty.Parse(""));

        using NativeJson? after = empty.Parse(AlsoGood);
        Assert.NotNull(after);
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
