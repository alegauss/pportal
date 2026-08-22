using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP259: the first call, and the function behind slashes.
///
/// <see cref="AskingForIpv6GetsIpv4OnTheFirstCall"/> carries the task, and
/// <see cref="TheEmptyMacHasAReasonNow"/> closes the loop PP33 opened.
/// </summary>
public class StunLookupTests
{
    /// <summary>
    /// THE FINDING. The one call that does not read the family is the first one of a session.
    /// </summary>
    [Fact]
    public void AskingForIpv6GetsIpv4OnTheFirstCall()
    {
        // Not measured yet: the test runs, and it never looks at the family.
        StunCall first = StunLookup.CallFor(StunLookup.NotMeasured, ipv4: false);

        Assert.Equal(StunCall.AllocationTest, first);
        Assert.False(StunLookup.ReadsTheFamily(first));
        Assert.Equal("stun_server_list", StunLookup.ListFor(first));

        Assert.False(StunLookup.AnIpv6RequestGetsIpv6(StunLookup.NotMeasured));
    }

    /// <summary>And every later call does read it.</summary>
    [Fact]
    public void EveryLaterCallReadsTheFamily()
    {
        Assert.Equal(StunCall.Ipv6Lookup, StunLookup.CallFor(0, ipv4: false));
        Assert.Equal(StunCall.Ipv4Lookup, StunLookup.CallFor(0, ipv4: true));

        Assert.True(StunLookup.AnIpv6RequestGetsIpv6(0));
        Assert.True(StunLookup.AnIpv6RequestGetsIpv6(1));

        Assert.Equal("stun_server_list_ipv6", StunLookup.ListFor(StunCall.Ipv6Lookup));
        Assert.Equal("stun_server_list", StunLookup.ListFor(StunCall.Ipv4Lookup));
    }

    /// <summary>
    /// The field carrying the sentinel is the one PP253 writes into, so two tasks share a variable
    /// that means two things.
    /// </summary>
    [Fact]
    public void TheSentinelSharesItsFieldWithAMeasurement()
    {
        Assert.Equal(-1, StunLookup.NotMeasured);

        // PP253 asserts one into it, which is not the sentinel - so the test does not re-run.
        int asserted = NatDiagnosis.AssertedIncrement;

        Assert.NotEqual(StunLookup.NotMeasured, asserted);
        Assert.Equal(StunCall.Ipv4Lookup, StunLookup.CallFor(asserted, ipv4: true));

        // And that value really is what the forcing path writes.
        NatWriteBack written = NatDiagnosis.WriteBackFor(NatVerdict.Rewriting);
        Assert.True(written.Writes);
        Assert.Equal(asserted, written.Increment);
    }

    /// <summary>A server list that would not load does not stop the lookup.</summary>
    [Fact]
    public void AListFailureDoesNotStopIt()
        => Assert.False(StunLookup.AListFailureStops);

    /// <summary>Three calls, one sentence - so the log cannot say which failed.</summary>
    [Fact]
    public void ThreeCallsReportTheSameWords()
    {
        Assert.Equal(3, Enum.GetValues<StunCall>().Length);
        Assert.Contains("Failed to get external address", StunLookup.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP33 recorded that this client sends an empty MAC. This is why: the function that would
    /// fetch it is commented out, and both request builders zero the field instead.
    /// </summary>
    [Fact]
    public void TheEmptyMacHasAReasonNow()
    {
        Assert.Equal("", StunLookup.MacSent);
        Assert.Equal(SessionMessageWriter.RouteMacSent, StunLookup.MacSent);

        Assert.Equal(39, StunLookup.CommentedOutLines);
        Assert.Equal(2, StunLookup.PlacesThatZeroIt);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheLookupIsStillTheCores()
    {
        string? file = StunLookupSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            StunLookupSource.TheSentinelIsStillWhatBranches(core),
            "the sentinel is still what branches");
        Assert.True(
            StunLookupSource.TheTestStillIgnoresTheFamily(core),
            "the test is still handed the IPv4 list with no mention of the family");
        Assert.True(
            StunLookupSource.TheLaterCallsStillReadTheFamily(core),
            "while the later calls still read it");

        Assert.True(
            StunLookupSource.TheListFailureStillDoesNotStop(core),
            "the server list's failure still does not stop the lookup");

        Assert.Equal(3, StunLookupSource.HowManySayTheSameThing(core));

        Assert.True(
            StunLookupSource.TheMacFetcherIsStillCommentedOut(core),
            "the MAC fetcher is still commented out in both places");
        Assert.Equal(
            StunLookup.CommentedOutLines, StunLookupSource.HowManyLinesAreCommentedOut(core));
        Assert.Equal(
            StunLookup.PlacesThatZeroIt, StunLookupSource.HowManyPlacesZeroTheMac(core));
        Assert.True(
            StunLookupSource.ThePrinterStillSkipsAZeroMac(core),
            "and the printer still skips a MAC of all zeros");
    }
}
