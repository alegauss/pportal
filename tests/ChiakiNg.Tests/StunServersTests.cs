using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the STUN server list, and the shuffle that only looks like Fisher-Yates.
/// </summary>
public class StunServersTests
{
    /// <summary>A random source that hands out a fixed sequence, then repeats the last value.</summary>
    private static Func<uint> Draws(params uint[] values)
    {
        int at = 0;
        return () => values[Math.Min(at++, values.Length - 1)];
    }

    private static List<StunServer> Servers() => [.. StunServers.Default];

    /// <summary>Eleven servers, Moonlight's first.</summary>
    [Fact]
    public void TheListStartsWithMoonlight()
    {
        Assert.Equal(11, StunServers.Default.Count);
        Assert.Equal("stun.moonlight-stream.org", StunServers.Preferred.Host);
        Assert.Equal(3478, StunServers.Preferred.Port);
    }

    /// <summary>
    /// THE PREFERENCE IS A LOOP BOUND. The first server stays first because the shuffle starts at
    /// index one - there is no comparison anywhere that says it is preferred, so a port that
    /// shuffled everything and then hoisted the favourite would agree with the comment and not with
    /// the code.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(uint.MaxValue)]
    public void TheFirstServerIsNeverMoved(uint draw)
    {
        List<StunServer> servers = Servers();

        StunServers.Shuffle(servers, Draws(draw));

        Assert.Equal(StunServers.Preferred, servers[0]);
    }

    /// <summary>
    /// THE BIAS, PINNED. The draw is 1 + random % (i - 1), which gives j in [1, i-1] and can never
    /// be i - so the element sitting at i when its turn comes ALWAYS moves, and the last server can
    /// never stay last.
    ///
    /// A correct Fisher-Yates over [1, i] can leave it there, so this assertion is exactly the one
    /// a well-meaning correction would break. That is the point: the bias is reproduced on purpose,
    /// and the day someone fixes it they will find out here rather than in a connection log.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(uint.MaxValue)]
    public void TheLastServerCanNeverStayLast(uint draw)
    {
        List<StunServer> servers = Servers();
        StunServer wasLast = servers[^1];

        StunServers.Shuffle(servers, Draws(draw));

        Assert.NotEqual(wasLast, servers[^1]);
    }

    /// <summary>
    /// And the same holds at every step, not only the last one - which is what makes the
    /// distribution non-uniform rather than merely odd at one end.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void TheElementAtEachIndexAlwaysMoves(uint draw)
    {
        List<StunServer> servers = Servers();
        var whenItsTurnCame = new Dictionary<int, StunServer>();

        // The draw happens before its swap, so this records what sat at i at the top of step i.
        int i = servers.Count - 1;
        StunServers.Shuffle(servers, () =>
        {
            whenItsTurnCame[i] = servers[i];
            i--;
            return draw;
        });

        Assert.Equal(StunServers.SwapCount(StunServers.Default.Count), whenItsTurnCame.Count);
        Assert.Equal(9, whenItsTurnCame.Count);

        // Nothing above i is touched again after step i, so what is there now is final - and it is
        // never what was there, because j cannot be i.
        foreach ((int index, StunServer was) in whenItsTurnCame)
            Assert.NotEqual(was, servers[index]);
    }

    /// <summary>
    /// THE LIST IS A GLOBAL AND THE SHUFFLE MUTATES IT. Each attempt reorders the same array, so
    /// the order one connection sees is the order the previous one left behind - Default is the
    /// STARTING order, not somewhere the code returns to.
    /// </summary>
    [Fact]
    public void ShufflingTwiceKeepsReorderingTheSameList()
    {
        List<StunServer> servers = Servers();

        StunServers.Shuffle(servers, Draws(2));
        List<StunServer> afterOne = [.. servers];

        StunServers.Shuffle(servers, Draws(5));

        Assert.NotEqual(afterOne, servers);
        Assert.Equal(StunServers.Preferred, servers[0]);

        // Every server is still there - it is a reorder, not a loss.
        Assert.Equal(
            [.. StunServers.Default.OrderBy(s => s.Host, StringComparer.Ordinal).ThenBy(s => s.Port)],
            servers.OrderBy(s => s.Host, StringComparer.Ordinal).ThenBy(s => s.Port));
    }

    /// <summary>A list too short to shuffle is left alone rather than reaching past its end.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AListWithNothingToShuffleIsUntouched(int count)
    {
        List<StunServer> servers = [.. StunServers.Default.Take(count)];
        List<StunServer> before = [.. servers];

        StunServers.Shuffle(servers, Draws(1));

        Assert.Equal(before, servers);
        Assert.Equal(0, StunServers.SwapCount(count));
    }

    /// <summary>Over IPv6 exactly one server is tried, where IPv4 walks the whole list.</summary>
    [Fact]
    public void SixTriesOneServerAndGivesUp()
        => Assert.Equal(1, StunServers.Ipv6ServersTried);

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheListsRulesAreStillTheQtCores()
    {
        string? path = StunServersSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(StunServersSource.TheListIsStillDefinedInTheHeader(core), "defined in a header");
        Assert.True(StunServersSource.TheSameServersAreStillListed(core), "eleven, in this order");
        Assert.True(
            StunServersSource.TheDrawStillExcludesTheCurrentIndex(core), "the draw still excludes i");
        Assert.True(StunServersSource.TheFirstServerIsStillLeftAlone(core), "moonlight untouched");
        Assert.True(StunServersSource.SixStillStopsAfterOne(core), "one IPv6 server");
    }

    /// <summary>
    /// And the draw check earns its green: a core where the shuffle had been corrected must turn it
    /// red, because that one character is the difference this file is about.
    /// </summary>
    [Fact]
    public void TheDrawCheckFailsOnACorrectedShuffle()
    {
        string? path = StunServersSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        string corrected = core.Replace(
            "int j = 1 + chiaki_random_32() % (i - 1);",
            "int j = 1 + chiaki_random_32() % i;",
            StringComparison.Ordinal);

        Assert.NotEqual(core, corrected);
        Assert.False(StunServersSource.TheDrawStillExcludesTheCurrentIndex(corrected));
    }
}
