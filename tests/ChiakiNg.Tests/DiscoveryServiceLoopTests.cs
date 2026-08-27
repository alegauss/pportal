using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP29: the discovery service's own loop, which is what PP29 had left after PP462.
///
/// PP462 ported the socket and the thread reading it. This is the layer above - a ping timer, a table
/// of hosts keyed by id, and a callback that fires only when the table changes. The assertions worth
/// having are the ones a rewrite would get wrong quietly: the first wait is a different length, and an
/// unchanged host reports nothing.
///
/// PP464 then fixed what PP29 had recorded here - the drop pass used to step over whatever moved into
/// slot 0 - so the traversal tests hold the repair rather than the quirk.
/// </summary>
public class DiscoveryServiceLoopTests
{
    private static string? Source()
    {
        string? path = DiscoveryServiceLoop.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>The first ping is one INITIAL interval in, not at start-up.</summary>
    [Fact]
    public void TheFirstWaitIsTheInitialIntervalAndTheRestAreNot()
    {
        Assert.Equal(500ul, DiscoveryServiceLoop.IntervalFor(0, 500, 2000));
        Assert.Equal(2000ul, DiscoveryServiceLoop.IntervalFor(1, 500, 2000));
        Assert.Equal(2000ul, DiscoveryServiceLoop.IntervalFor(9, 500, 2000));

        if (Source() is not { } source || DiscoveryServiceLoop.ThreadBody(source) is not { } body)
            return;

        Assert.True(DiscoveryServiceLoop.TheFirstWaitStillDiffers(body));
    }

    /// <summary>Only a timeout goes round again; everything else leaves, with the same silence.</summary>
    [Fact]
    public void OnlyATimeoutContinuesTheLoop()
    {
        Assert.True(DiscoveryServiceLoop.Continues(ChiakiError.Timeout));

        foreach (ChiakiError other in new[]
        {
            ChiakiError.Success, ChiakiError.Canceled, ChiakiError.Unknown, ChiakiError.MutexLocked,
        })
        {
            Assert.False(DiscoveryServiceLoop.Continues(other));
        }

        if (Source() is not { } source || DiscoveryServiceLoop.ThreadBody(source) is not { } body)
            return;

        Assert.True(DiscoveryServiceLoop.TheLoopStillRunsOnlyOnTimeout(body));
    }

    /// <summary>
    /// A discovery thread that will not start ends the service and logs nothing, so the only evidence
    /// is that no ping ever goes out.
    /// </summary>
    [Fact]
    public void AFailedThreadStartEndsTheServiceInSilence()
    {
        if (Source() is not { } source || DiscoveryServiceLoop.ThreadBody(source) is not { } body)
            return;

        Assert.True(DiscoveryServiceLoop.AFailedThreadStartStillSaysNothing(body));
    }

    /// <summary>The drop rule, at its boundary: kept while last + drop reaches the current ping.</summary>
    [Theory]
    [InlineData(10ul, 10ul, 0ul, true)]   // answered this ping
    [InlineData(9ul, 10ul, 0ul, false)]   // missed one, and no grace
    [InlineData(9ul, 10ul, 1ul, true)]    // one ping of grace covers it
    [InlineData(8ul, 10ul, 1ul, false)]   // two missed, one of grace
    public void AHostIsHeldWhileItsGraceReachesTheCurrentPing(
        ulong last, ulong current, ulong drop, bool held)
    {
        Assert.Equal(held, DiscoveryServiceLoop.IsHeld(last, current, drop));
    }

    /// <summary>
    /// A CHANGE IS THREE THINGS. Appearing, moving state or port, and being dropped - and a host
    /// answering again with everything the same tells nobody, which is what stops a console list
    /// redrawing every ping.
    /// </summary>
    [Fact]
    public void AnUnchangedHostReportsNothing()
    {
        var table = new DiscoveryHostTable(4);

        Assert.Equal(HostArrival.Added, table.Receive("abc", 200, 9295, 1));
        Assert.Equal(1, table.Reports);

        Assert.Equal(HostArrival.Refreshed, table.Receive("abc", 200, 9295, 2));
        Assert.Equal(1, table.Reports);

        Assert.Equal(HostArrival.Changed, table.Receive("abc", 620, 9295, 3));
        Assert.Equal(2, table.Reports);

        // The port alone is enough too.
        Assert.Equal(HostArrival.Changed, table.Receive("abc", 620, 9296, 4));
        Assert.Equal(3, table.Reports);
    }

    /// <summary>A host with no id is refused, and a full table refuses a new one.</summary>
    [Fact]
    public void AHostWithNoIdAndAFullTableAreBothRefused()
    {
        var table = new DiscoveryHostTable(1);

        Assert.Equal(HostArrival.NoId, table.Receive(null, 200, 1, 1));
        Assert.Equal(HostArrival.NoId, table.Receive("", 200, 1, 1));
        Assert.Empty(table.Hosts);

        Assert.Equal(HostArrival.Added, table.Receive("first", 200, 1, 1));
        Assert.Equal(HostArrival.NoSpace, table.Receive("second", 200, 1, 1));

        Assert.Single(table.Hosts);
        Assert.Equal(1, table.Reports);
    }

    /// <summary>
    /// PP464: two stale hosts at the front now go in ONE pass, like two anywhere else.
    ///
    /// This test carried the defect when PP29 wrote it - the first pass dropped one, left the other in
    /// slot 0, and a second pass was needed. The step-back is unconditional now, so the cursor lands on
    /// the slot the shift just filled at every index including zero.
    /// </summary>
    [Fact]
    public void TwoStaleHostsAtTheFrontGoInOnePass()
    {
        var table = new DiscoveryHostTable(8);
        table.Receive("a", 200, 1, 1);
        table.Receive("b", 200, 1, 1);
        table.Receive("c", 200, 1, 9);

        // a and b are both stale at ping 9 with no grace; c answered it.
        Assert.Equal(2, table.DropOldHosts(pingIndex: 9, dropPings: 0));
        Assert.Equal(new[] { "c" }, table.Hosts.Select(h => h.HostId).ToArray());
    }

    /// <summary>
    /// And the case away from index 0, which always worked - kept beside the one above so the fix is
    /// shown not to have cost it.
    /// </summary>
    [Fact]
    public void TwoStaleHostsAfterTheFirstGoInOnePass()
    {
        var table = new DiscoveryHostTable(8);
        table.Receive("keep", 200, 1, 9);
        table.Receive("a", 200, 1, 1);
        table.Receive("b", 200, 1, 1);

        Assert.Equal(2, table.DropOldHosts(pingIndex: 9, dropPings: 0));
        Assert.Equal(new[] { "keep" }, table.Hosts.Select(h => h.HostId).ToArray());
    }

    /// <summary>A whole table going stale at once empties it in one pass.</summary>
    [Fact]
    public void EveryHostGoingStaleEmptiesTheTableInOnePass()
    {
        var table = new DiscoveryHostTable(8);
        table.Receive("a", 200, 1, 1);
        table.Receive("b", 200, 1, 1);
        table.Receive("c", 200, 1, 1);

        Assert.Equal(3, table.DropOldHosts(pingIndex: 9, dropPings: 0));
        Assert.Empty(table.Hosts);
    }

    /// <summary>A drop reports once for the pass, however many went.</summary>
    [Fact]
    public void ADropReportsOncePerPass()
    {
        var table = new DiscoveryHostTable(8);
        table.Receive("keep", 200, 1, 9);
        table.Receive("a", 200, 1, 1);
        table.Receive("b", 200, 1, 1);

        int before = table.Reports;
        table.DropOldHosts(pingIndex: 9, dropPings: 0);

        Assert.Equal(before + 1, table.Reports);

        // And a pass that drops nothing reports nothing.
        table.DropOldHosts(pingIndex: 9, dropPings: 0);
        Assert.Equal(before + 1, table.Reports);
    }

    /// <summary>PP464: the guard that caused the skip is gone from the C.</summary>
    [Fact]
    public void TheStepBackIsNoLongerGuarded()
    {
        if (Source() is not { } source || DiscoveryServiceLoop.DropBody(source) is not { } body)
            return;

        Assert.True(
            DiscoveryServiceLoop.TheDropStepsBackUnconditionally(body),
            "the drop's step-back is guarded again, so index 0 is skipped and PP464's defect is back");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(DiscoveryServiceLoop.ThreadBody(""));
        Assert.Null(DiscoveryServiceLoop.DropBody(""));
        Assert.False(DiscoveryServiceLoop.TheFirstWaitStillDiffers(""));
        Assert.False(DiscoveryServiceLoop.TheLoopStillRunsOnlyOnTimeout(""));
        Assert.False(DiscoveryServiceLoop.AFailedThreadStartStillSaysNothing(""));
        Assert.False(DiscoveryServiceLoop.TheDropStepsBackUnconditionally(""));
    }
}
