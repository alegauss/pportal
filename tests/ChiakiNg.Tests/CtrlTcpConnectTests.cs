using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP415, under PP294: the socket under the ctrl request.
///
/// PP356 has the request and its crypt counters. This is the connect below it, and the reason a
/// machine out of memory used to be told the network had failed.
/// </summary>
public class CtrlTcpConnectTests
{
    /// <summary>A port can be stamped into the two families the C names, and nothing else.</summary>
    [Theory]
    [InlineData(AddressFamily.InterNetwork, true)]
    [InlineData(AddressFamily.InterNetworkV6, true)]
    [InlineData(AddressFamily.Unix, false)]
    [InlineData(AddressFamily.Unspecified, false)]
    [InlineData(AddressFamily.Ipx, false)]
    public void OnlyTwoFamiliesTakeAPort(AddressFamily family, bool stamped)
    {
        Assert.Equal(stamped, CtrlTcpConnect.CanStampAPortInto(family));
    }

    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. A memory failure is reported as one.
    ///
    /// It used to return without recording anything, and ctrl_thread_func answers any error from
    /// ctrl_connect with CTRL_CONNECT_FAILED - so somebody out of memory was told their network had
    /// failed. PP345 added the reason one function over for exactly this.
    /// </summary>
    [Fact]
    public void AMemoryFailureIsReportedAsMemory()
    {
        CtrlSocketAttempt attempt = CtrlTcpConnect.Attempt(CtrlSocketOutcome.NoMemory);

        Assert.Equal(ChiakiQuitReason.CtrlMemory, attempt.Reports);
        Assert.Equal(
            ChiakiQuitReason.CtrlMemory,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.NoMemory));

        // And NOT the generic one the caller would otherwise have supplied.
        Assert.NotEqual(
            ChiakiQuitReason.CtrlConnectFailed,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.NoMemory));
    }

    /// <summary>
    /// AND THE BAD-FAMILY PATH IS LEFT ALONE, which is the other half of the decision.
    ///
    /// An address the client cannot use IS a connect failure, so it still records nothing of its own
    /// and the caller's generic reason is the right one. Stated so the fix above does not read as a
    /// rule that every path must report.
    /// </summary>
    [Fact]
    public void AnUnusableAddressIsStillAConnectFailure()
    {
        CtrlSocketAttempt attempt = CtrlTcpConnect.Attempt(CtrlSocketOutcome.UnsupportedFamily);

        Assert.Null(attempt.Reports);
        Assert.Equal(
            ChiakiQuitReason.CtrlConnectFailed,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.UnsupportedFamily));
    }

    /// <summary>
    /// A CANCEL IS NOT A FAULT. The stop pipe firing closes the socket and records nothing.
    /// </summary>
    [Fact]
    public void ACancelIsAStopRatherThanAFailure()
    {
        CtrlSocketAttempt attempt = CtrlTcpConnect.Attempt(CtrlSocketOutcome.Cancelled);

        Assert.Null(attempt.Reports);
        Assert.True(attempt.ClosesTheSocket);
        Assert.Equal(
            ChiakiQuitReason.Stopped,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.Cancelled));
    }

    /// <summary>A refusal is told apart from every other connect failure.</summary>
    [Fact]
    public void ARefusalIsToldApart()
    {
        Assert.Equal(
            ChiakiQuitReason.CtrlConnectionRefused,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.ConnectionRefused));

        Assert.Equal(
            ChiakiQuitReason.CtrlUnknown,
            CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.ConnectFailed));
    }

    /// <summary>Which outcomes close the socket they opened, and which never opened one.</summary>
    [Theory]
    [InlineData(CtrlSocketOutcome.Connected, false)]
    [InlineData(CtrlSocketOutcome.NoMemory, false)]
    [InlineData(CtrlSocketOutcome.UnsupportedFamily, false)]
    [InlineData(CtrlSocketOutcome.SocketCreationFailed, false)]
    [InlineData(CtrlSocketOutcome.NonBlockingFailed, true)]
    [InlineData(CtrlSocketOutcome.Cancelled, true)]
    [InlineData(CtrlSocketOutcome.ConnectionRefused, true)]
    [InlineData(CtrlSocketOutcome.ConnectFailed, true)]
    public void OnlyAPathThatOpenedASocketClosesOne(CtrlSocketOutcome outcome, bool closes)
    {
        Assert.Equal(closes, CtrlTcpConnect.Attempt(outcome).ClosesTheSocket);
    }

    /// <summary>A connected attempt reports nothing and keeps its socket.</summary>
    [Fact]
    public void AConnectedAttemptReportsNothing()
    {
        CtrlSocketAttempt attempt = CtrlTcpConnect.Attempt(CtrlSocketOutcome.Connected);

        Assert.Null(attempt.Reports);
        Assert.False(attempt.ClosesTheSocket);
        Assert.Equal(
            ChiakiQuitReason.None, CtrlTcpConnect.ReasonTheUserSees(CtrlSocketOutcome.Connected));
    }

    /// <summary>
    /// THE NOTIFY MUTEX IS NOT HELD ACROSS THE WAIT, and the timeout is why that matters.
    ///
    /// Five seconds of connect. A port holding the mutex across it makes that the floor on how long
    /// a stop takes to be noticed, on the one operation most likely to need one.
    /// </summary>
    [Fact]
    public void TheConnectDoesNotHoldTheNotifyMutex()
    {
        Assert.False(CtrlTcpConnect.HoldsTheNotifyMutexWhileConnecting);
        Assert.Equal(5000, CtrlTcpConnect.ConnectTimeoutMs);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheConnectsRulesAreStillTheQtCores()
    {
        string? path = CtrlTcpConnectSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.NotNull(CtrlTcpConnectSource.Body(core));

        Assert.True(
            CtrlTcpConnectSource.ThePortIsStillStampedPerFamily(core),
            "the port is no longer stamped into both families");
        Assert.True(
            CtrlTcpConnectSource.ThePortMacroIsStillThis(core),
            "SESSION_CTRL_PORT is not the number this port stamps");
        Assert.True(
            CtrlTcpConnectSource.TheNotifyMutexIsStillDroppedAroundTheConnect(core),
            "the notify mutex is held across the connect, so the timeout is now the stop latency");
        Assert.True(
            CtrlTcpConnectSource.TheTimeoutIsStillThis(core),
            "the connect timeout moved");
        Assert.True(
            CtrlTcpConnectSource.TheAllocationFailureStillReportsMemory(core),
            "the sockaddr allocation failure stopped reporting memory, so it reads as a network fault");
        Assert.True(
            CtrlTcpConnectSource.ACancelStillReportsNothing(core),
            "a cancelled connect records a quit reason, so a stop is reported as a fault");
        Assert.True(
            CtrlTcpConnectSource.ARefusalIsStillToldApart(core),
            "a refusal is no longer told apart from any other connect failure");
    }

    /// <summary>
    /// And the reason this fix uses still has a string a user can read.
    ///
    /// PP345 added both. A reason with no case in the string table reaches a screen as whatever the
    /// default is, which would make this fix look like it changed nothing.
    /// </summary>
    [Fact]
    public void TheMemoryReasonStillHasAString()
    {
        string? path = ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\src\session.c");
        if (path is null)
            return;

        Assert.Contains(
            "case CHIAKI_QUIT_REASON_CTRL_MEMORY:",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>PP272: and every reader answers no to an empty file.</summary>
    [Fact]
    public void EveryReaderAnswersNoToAnEmptyFile()
    {
        Assert.Null(CtrlTcpConnectSource.Body(""));
        Assert.False(CtrlTcpConnectSource.ThePortIsStillStampedPerFamily(""));
        Assert.False(CtrlTcpConnectSource.ThePortMacroIsStillThis(""));
        Assert.False(CtrlTcpConnectSource.TheNotifyMutexIsStillDroppedAroundTheConnect(""));
        Assert.False(CtrlTcpConnectSource.TheTimeoutIsStillThis(""));
        Assert.False(CtrlTcpConnectSource.TheAllocationFailureStillReportsMemory(""));
        Assert.False(CtrlTcpConnectSource.ACancelStillReportsNothing(""));
        Assert.False(CtrlTcpConnectSource.ARefusalIsStillToldApart(""));
    }
}
