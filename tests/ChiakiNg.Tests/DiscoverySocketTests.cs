using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP462, PP29: the discovery socket and the thread that reads it.
///
/// PP29's remainder names exactly this. PP6 gave the port a managed discovery service, but that is a
/// wrapper over libchiaki's own - it joins a thread libchiaki owns - so nothing managed decided where
/// the socket binds or what the loop does with a datagram.
///
/// The assertion worth the task is the bind ladder's log: both branches move the port on BEFORE
/// naming it, so every rung reports the port it is about to try as the one that just failed.
/// </summary>
public class DiscoverySocketTests
{
    private static string? Source()
    {
        string? path = DiscoverySocket.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    private static string? Init()
        => Source() is { } source ? CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_discovery_init") : null;

    /// <summary>Seventeen numbered ports and then any, in order.</summary>
    [Fact]
    public void TheLadderIsSeventeenPortsAndThenAny()
    {
        Assert.Equal(18, DiscoverySocket.Ladder.Count);

        Assert.Equal(DiscoverySocket.LocalPortMin, DiscoverySocket.Ladder[0].Port);
        Assert.Equal(DiscoverySocket.LocalPortMax, DiscoverySocket.Ladder[^2].Port);
        Assert.Equal(DiscoverySocket.AnyPort, DiscoverySocket.Ladder[^1].Port);

        // Nothing follows a failure on the random rung.
        Assert.Null(DiscoverySocket.Ladder[^1].Next);
        Assert.Equal(DiscoverySocket.AnyPort, DiscoverySocket.Ladder[^2].Next);
    }

    /// <summary>
    /// THE DEFECT: every rung's log names the NEXT port, not the one that failed.
    ///
    /// A failure on 9303 reports 9304, and a failure on 9319 reports 0 - "failed to bind port 0, trying
    /// random", which names the rung it is about to try as the one that just failed.
    /// </summary>
    [Theory]
    [InlineData((ushort)9303, (ushort)9304)]
    [InlineData((ushort)9318, (ushort)9319)]
    [InlineData((ushort)9319, (ushort)0)]
    public void EveryRungsLogNamesTheNextPort(ushort failedOn, ushort logged)
    {
        Assert.Equal(logged, DiscoverySocket.LoggedPortFor(failedOn));
        Assert.False(DiscoverySocket.TheLogNamesThePortThatFailed(failedOn));
    }

    /// <summary>And not one numbered rung logs its own port.</summary>
    [Fact]
    public void NoNumberedRungNamesItsOwnPort()
    {
        Assert.DoesNotContain(
            DiscoverySocket.Ladder.Where(r => r.Port != DiscoverySocket.AnyPort),
            r => r.Port == r.LoggedPort);
    }

    /// <summary>The order of the two statements in the C, which is the whole defect.</summary>
    [Fact]
    public void TheCStillMovesThePortOnBeforeLoggingIt()
    {
        if (Init() is not { } body)
            return;

        Assert.True(
            DiscoverySocket.BothLogsStillNameTheNextPort(body),
            "one of the two branches now logs before moving the port on, so this model is behind the C");
    }

    /// <summary>The ports are the header's, read rather than trusted here.</summary>
    [Fact]
    public void ThePortsAreStillTheHeaders()
    {
        if (DiscoverySocket.LocateHeader() is not { } path)
            return;

        string header = File.ReadAllText(path);

        Assert.Equal(
            (long?)DiscoverySocket.LocalPortMin,
            DiscoverySocket.PortDefineIn(header, "CHIAKI_DISCOVERY_PORT_LOCAL_MIN"));
        Assert.Equal(
            (long?)DiscoverySocket.LocalPortMax,
            DiscoverySocket.PortDefineIn(header, "CHIAKI_DISCOVERY_PORT_LOCAL_MAX"));
    }

    /// <summary>A broadcast option that fails is logged and stepped over.</summary>
    [Fact]
    public void ABroadcastFailureDoesNotStopTheInit()
    {
        Assert.False(DiscoverySocket.ABroadcastFailureStops);

        if (Init() is not { } body)
            return;

        Assert.True(DiscoverySocket.ABroadcastFailureStillOnlyLogs(body));
    }

    /// <summary>
    /// THE LOOP LEAVES ON A FAILED RECEIVE, unlike every other loop this port has met.
    ///
    /// PP238 and PP256's punch loop continues there and PP457 had to bound it. Asserted rather than
    /// assumed, because the reader arriving from those tasks would expect a spin.
    /// </summary>
    [Fact]
    public void AFailedReceiveLeavesRatherThanSpinning()
    {
        Assert.Equal(DiscoveryTurn.ReceiveFailed, DiscoverySocket.Next(false, false, -1, false));
        Assert.True(DiscoverySocket.Leaves(DiscoveryTurn.ReceiveFailed));

        if (Source() is not { } source || DiscoverySocket.ThreadBody(source) is not { } body)
            return;

        Assert.True(DiscoverySocket.TheLoopStillLeavesOnAFailedReceive(body));
    }

    /// <summary>An empty or unparseable datagram goes round again; a host is handed over.</summary>
    [Theory]
    [InlineData(0, false, DiscoveryTurn.Empty)]
    [InlineData(64, false, DiscoveryTurn.Unparseable)]
    [InlineData(64, true, DiscoveryTurn.Host)]
    public void WhatOneDatagramDoes(int received, bool parsed, DiscoveryTurn expected)
    {
        Assert.Equal(expected, DiscoverySocket.Next(false, false, received, parsed));
        Assert.False(DiscoverySocket.Leaves(expected));
    }

    /// <summary>Cancelling and a failed wait both leave, and are told apart.</summary>
    [Fact]
    public void CancellingAndAFailedWaitAreDifferentEndings()
    {
        Assert.Equal(DiscoveryTurn.Cancelled, DiscoverySocket.Next(true, false, 0, false));
        Assert.Equal(DiscoveryTurn.SelectFailed, DiscoverySocket.Next(false, true, 0, false));

        // Cancelling wins where both are set: the stop pipe is tested first.
        Assert.Equal(DiscoveryTurn.Cancelled, DiscoverySocket.Next(true, true, 0, false));
    }

    /// <summary>
    /// The clamp cannot fire, because the receive already asks for one less than the buffer - and it is
    /// reproduced anyway, as the arithmetic rather than as a removal.
    /// </summary>
    [Fact]
    public void TheTerminatorAlwaysHasRoom()
    {
        Assert.Equal(DiscoverySocket.ReceiveBufferSize - 1, DiscoverySocket.UsableBytes(9999));
        Assert.Equal(64, DiscoverySocket.UsableBytes(64));
        Assert.Equal(0, DiscoverySocket.UsableBytes(0));
    }

    /// <summary>
    /// The one-shot stops only on a parsed host WITH a callback - so "one-shot" is a property of the
    /// callback and the datagram, not of the thread.
    ///
    /// Stated rather than filed: nothing calls it. It is one of the unreferenced exports.
    /// </summary>
    [Fact]
    public void TheOneShotStopsOnlyWithACallbackAndAHost()
    {
        Assert.True(DiscoverySocket.TheOneShotStops(DiscoveryTurn.Host, hasCallback: true));
        Assert.False(DiscoverySocket.TheOneShotStops(DiscoveryTurn.Host, hasCallback: false));
        Assert.False(DiscoverySocket.TheOneShotStops(DiscoveryTurn.Unparseable, hasCallback: true));
    }

    /// <summary>
    /// And the two threads are fifty-four lines that differ by one break.
    ///
    /// The comparison is what makes that checkable: if one is edited and the other is not, this goes
    /// red rather than the duplication quietly diverging.
    /// </summary>
    [Fact]
    public void TheTwoThreadsDifferOnlyByTheBreak()
    {
        if (Source() is not { } source)
            return;

        if (DiscoverySocket.ThreadBody(source) is not { } continuous
            || DiscoverySocket.OneShotBody(source) is not { } oneShot)
        {
            return;
        }

        Assert.True(
            DiscoverySocket.TheTwoThreadsStillDifferOnlyByTheBreak(continuous, oneShot),
            "the two discovery threads have diverged by more than the one-shot's break");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(DiscoverySocket.ThreadBody(""));
        Assert.Null(DiscoverySocket.OneShotBody(""));
        Assert.Null(DiscoverySocket.PortDefineIn("", "CHIAKI_DISCOVERY_PORT_LOCAL_MIN"));
        Assert.False(DiscoverySocket.BothLogsStillNameTheNextPort(""));
        Assert.False(DiscoverySocket.ABroadcastFailureStillOnlyLogs(""));
        Assert.False(DiscoverySocket.TheLoopStillLeavesOnAFailedReceive(""));
        Assert.False(DiscoverySocket.TheTwoThreadsStillDifferOnlyByTheBreak("", ""));
    }
}
