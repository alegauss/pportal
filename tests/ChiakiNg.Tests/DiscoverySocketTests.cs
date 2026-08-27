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
/// The assertion worth the task was the bind ladder's log: both branches moved the port on before
/// naming it, so every rung reported the port it was about to try as the one that just failed. PP463
/// fixed that, and <see cref="EachLogNamesThePortThatFailed"/> is what holds the repair.
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
    /// PP463: each rung's log names the port that FAILED, which is the order of two statements and
    /// nothing else.
    ///
    /// It used to name the next one: a failure on 9303 reported 9304, and a failure on 9319 reported
    /// "failed to bind port 0, trying random" - the rung it was about to try. The log lines themselves
    /// are unchanged, so this assertion is the only thing that can tell the fix from the bug.
    /// </summary>
    [Fact]
    public void EachLogNamesThePortThatFailed()
    {
        if (Init() is not { } body)
            return;

        Assert.True(
            DiscoverySocket.BothLogsNameThePortThatFailed(body),
            "one of the two branches moves the port on before logging it again, which is PP463's "
                + "defect returning");
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
        Assert.False(DiscoverySocket.BothLogsNameThePortThatFailed(""));
        Assert.False(DiscoverySocket.ABroadcastFailureStillOnlyLogs(""));
        Assert.False(DiscoverySocket.TheLoopStillLeavesOnAFailedReceive(""));
        Assert.False(DiscoverySocket.TheTwoThreadsStillDifferOnlyByTheBreak("", ""));
    }
}
