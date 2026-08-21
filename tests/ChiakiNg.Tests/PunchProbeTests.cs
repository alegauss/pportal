using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP243: the probe, and the arrays the other end measures.
///
/// <see cref="TheProbePutsItWhereTheReplyTakesItFrom"/> closes PP236: that task could port the echo
/// without knowing what was echoed, and this is what was echoed.
///
/// <see cref="TheStackTheConsoleGetsToChoose"/> is the one worth reading twice - the size is stated
/// as a number rather than described, because "unbounded" is an adjective and 4517 is not.
/// </summary>
public class PunchProbeTests
{
    private static byte[] Id(byte fill) => Enumerable.Repeat(fill, PunchResponse.IdLength).ToArray();

    /// <summary>The probe is the reply's packet with a different word at the front.</summary>
    [Fact]
    public void TheProbeIsTheReplysPacketWithADifferentFront()
    {
        byte[] probe = PunchProbe.Build(
            [1, 2, 3, 4, 5], Id(0xa1), Id(0xc0), sidLocal: 0x1111, sidConsole: 0x2222);

        Assert.Equal(PunchResponse.Length, probe.Length);

        // REQ, not RESP - and the two differ, which is the whole of the front.
        Assert.Equal(0x06, probe[0]);
        Assert.NotEqual(PunchResponse.ResponseType, PunchProbe.RequestType);

        // The identifiers in their slots, twelve zero bytes behind each.
        Assert.Equal(0xa1, probe[PunchResponse.LocalIdAt]);
        Assert.Equal(0, probe[PunchResponse.LocalIdAt + PunchResponse.IdLength]);
        Assert.Equal(0xc0, probe[PunchResponse.ConsoleIdAt]);

        // And the session ids as themselves.
        Assert.Equal(0x11, probe[PunchResponse.SessionIdsAt]);
        Assert.Equal(0x22, probe[PunchResponse.SessionIdsAt + 2]);
    }

    /// <summary>
    /// THE PAIRING. The random bytes go to the offset the reply echoes from, so the two halves are
    /// one constant rather than two numbers that agree by luck.
    /// </summary>
    [Fact]
    public void TheProbePutsItWhereTheReplyTakesItFrom()
    {
        byte[] probe = PunchProbe.Build(
            [0xde, 0xad, 0xbe, 0xef, 0x5a], Id(0), Id(0), 0, 0);

        Assert.Equal(PunchResponse.EchoAt, PunchProbe.RequestIdAt);
        Assert.Equal(
            new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x5a },
            probe.AsSpan(PunchProbe.RequestIdAt, PunchProbe.RequestIdLength).ToArray());

        // And a reply built from this probe hands exactly those five back.
        byte[]? reply = PunchResponse.Build(
            probe, Id(0), Id(0), 0, 0, "10.0.0.1", 9295);

        Assert.NotNull(reply);
        Assert.Equal(
            probe.AsSpan(PunchProbe.RequestIdAt, PunchProbe.RequestIdLength).ToArray(),
            reply.AsSpan(PunchResponse.EchoAt, PunchResponse.EchoLength).ToArray());
    }

    /// <summary>
    /// The key is the reply's alone - the probe leaves 0x50 and 0x54 empty, so the obfuscation runs
    /// one direction only.
    /// </summary>
    [Fact]
    public void TheProbeCarriesNoKey()
    {
        byte[] probe = PunchProbe.Build([1, 2, 3, 4, 5], Id(0xff), Id(0xff), 0xffff, 0xffff);

        Assert.Equal(0, probe[PunchResponse.AddressKeyAt]);
        Assert.Equal(0, probe[PunchResponse.AddressKeyAt + 2]);
        Assert.Equal(0, probe[PunchResponse.PortKeyAt]);

        // Which the reply does not.
        byte[]? reply = PunchResponse.Build(probe, Id(0), Id(0), 0xffff, 0xffff, "10.0.0.1", 9295);
        Assert.NotNull(reply);
        Assert.NotEqual(0, reply[PunchResponse.AddressKeyAt]);
    }

    /// <summary>
    /// THE MEASUREMENT. Four stack arrays, sized from a count the console sent, and the count at
    /// which they no longer fit an ordinary thread stack is a small one.
    /// </summary>
    [Fact]
    public void TheStackTheConsoleGetsToChoose()
    {
        Assert.Equal(4, PunchProbe.StackArrays);

        // Three slots of headroom whatever the console said.
        Assert.Equal(3, PunchProbe.SlotsFor(0));
        Assert.Equal(13, PunchProbe.SlotsFor(10));

        // A real exchange is small and costs nothing.
        Assert.True(PunchProbe.StackBytesFor(10) < 4096);

        // And the count that does not fit a megabyte is reachable in one JSON array.
        int overruns = PunchProbe.CountThatOverrunsAMegabyte();

        // Not "large" - this one. A candidate array of that length is a few hundred kilobytes of
        // JSON, which is nothing to send.
        Assert.Equal(4517, overruns);
        Assert.True(PunchProbe.StackBytesFor(overruns) > 1024 * 1024);
        Assert.True(PunchProbe.StackBytesFor(overruns - 1) <= 1024 * 1024);
    }

    /// <summary>
    /// The probe count is one, and the test for "answered" is written so that raising it moves.
    /// </summary>
    [Fact]
    public void EverythingIsDimensionedToAProbeCountOfOne()
    {
        Assert.Equal(1, PunchProbe.RequestCount);

        Assert.False(PunchProbe.Answered(0));
        Assert.True(PunchProbe.Answered(1));
        Assert.True(PunchProbe.Answered(2));
    }

    /// <summary>The builder refuses anything that would land at the wrong offset.</summary>
    [Fact]
    public void TheBuilderRefusesTheWrongLengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PunchProbe.Build([1, 2, 3, 4], Id(0), Id(0), 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PunchProbe.Build([1, 2, 3, 4, 5], new byte[19], Id(0), 0, 0));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheProbeIsStillTheCores()
    {
        string? file = PunchProbeSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(PunchProbeSource.TheProbeIsStillLaidOutThatWay(core), "the layout");
        Assert.True(
            PunchProbeSource.TheRandomBytesStillGoWhereTheReplyEchoesFrom(core),
            "and the random bytes still at the echoed offset");
        Assert.True(PunchProbeSource.TheProbeStillCarriesNoKey(core), "no key in the probe");
        Assert.True(
            PunchProbeSource.TheFourArraysAreStillStackSizedByTheCount(core),
            "four arrays still sized on the stack by the count");
        Assert.True(
            PunchProbeSource.TheCountStillArrivesUnbounded(core),
            "the count still arriving with nothing bounding it");
        Assert.True(PunchProbeSource.TheProbeCountIsStillOne(core), "and the probe count still one");
    }
}
