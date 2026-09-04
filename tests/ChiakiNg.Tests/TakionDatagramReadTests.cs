using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP488, under PP27: the socket read, and the two facts about it that were nowhere stated.
///
/// Five results collapse to two loop behaviours, so an orderly shutdown and a broken socket leave
/// the thread by the same door. And a recv of zero on a datagram socket is an empty packet rather
/// than an end of file, which the C ends the session on.
/// </summary>
public class TakionDatagramReadTests
{
    /// <summary>The select's two quiet answers come back untouched, before recv is reached.</summary>
    [Theory]
    [InlineData(TakionSelectOutcome.TimedOut, TakionReadResult.TimedOut)]
    [InlineData(TakionSelectOutcome.Canceled, TakionReadResult.Canceled)]
    [InlineData(TakionSelectOutcome.Failed, TakionReadResult.SelectFailed)]
    public void ASelectThatIsNotReadyNeverReachesTheRecv(
        TakionSelectOutcome select, TakionReadResult expected)
    {
        // A recv return that would be a perfectly good datagram, which must not be read.
        TakionReadOutcome outcome = TakionDatagramRead.Read(select, 512);

        Assert.Equal(expected, outcome.Result);
        Assert.Equal(0, outcome.Length);
    }

    /// <summary>A ready select and bytes is a datagram at the length recv reported.</summary>
    [Fact]
    public void BytesAreADatagramAtTheLengthRecvReported()
    {
        TakionReadOutcome outcome = TakionDatagramRead.Read(TakionSelectOutcome.Ready, 1187);

        Assert.Equal(TakionReadResult.Datagram, outcome.Result);
        Assert.Equal(1187, outcome.Length);
    }

    /// <summary>
    /// A RECV OF ZERO ENDS THE THREAD, and on a datagram socket zero is a legal empty packet.
    ///
    /// Not end-of-file: that is what zero means on a stream socket. Here it means the peer sent a
    /// packet with no payload, and the C turns it into CHIAKI_ERR_NETWORK under its own log line, so
    /// the loop leaves and the session goes with it. Pinned rather than endorsed - §PP488 leaves the
    /// question open - but pinned, so changing it has to change this.
    /// </summary>
    [Fact]
    public void AnEmptyDatagramIsANetworkErrorAndEndsTheThread()
    {
        TakionReadOutcome outcome = TakionDatagramRead.Read(TakionSelectOutcome.Ready, 0);

        Assert.Equal(TakionReadResult.NetworkError, outcome.Result);
        Assert.Equal(TakionReceiveOutcome.Failed, TakionDatagramRead.ForTheLoop(outcome.Result));
    }

    /// <summary>And a negative recv is the same result by the other half of the same branch.</summary>
    [Fact]
    public void ASocketErrorIsTheSameResultAsAnEmptyDatagram()
    {
        Assert.Equal(
            TakionReadResult.NetworkError,
            TakionDatagramRead.Read(TakionSelectOutcome.Ready, -1).Result);
    }

    /// <summary>
    /// THE COLLAPSE: only a timeout continues the loop, and the other three are one thing to it.
    ///
    /// A cancel is how a session is meant to end and a network error is a fault, and the loop treats
    /// them identically because it tests TIMEOUT and breaks on the rest. Stated here so it is a
    /// decision rather than an omission.
    /// </summary>
    [Theory]
    [InlineData(TakionReadResult.Datagram, TakionReceiveOutcome.Datagram)]
    [InlineData(TakionReadResult.TimedOut, TakionReceiveOutcome.Timeout)]
    [InlineData(TakionReadResult.Canceled, TakionReceiveOutcome.Failed)]
    [InlineData(TakionReadResult.SelectFailed, TakionReceiveOutcome.Failed)]
    [InlineData(TakionReadResult.NetworkError, TakionReceiveOutcome.Failed)]
    public void OnlyATimeoutContinuesTheLoop(
        TakionReadResult result, TakionReceiveOutcome expected)
        => Assert.Equal(expected, TakionDatagramRead.ForTheLoop(result));

    /// <summary>
    /// And the two quiet ones are the two the C does not log, which is the same distinction from the
    /// other side: one is every idle moment of a session, the other is how it ends.
    /// </summary>
    [Fact]
    public void TheTwoOrdinaryResultsAreTheTwoThatAreSilent()
    {
        Assert.False(TakionDatagramRead.IsLogged(TakionReadResult.TimedOut));
        Assert.False(TakionDatagramRead.IsLogged(TakionReadResult.Canceled));

        Assert.True(TakionDatagramRead.IsLogged(TakionReadResult.SelectFailed));
        Assert.True(TakionDatagramRead.IsLogged(TakionReadResult.NetworkError));
        Assert.True(TakionDatagramRead.IsLogged(TakionReadResult.Datagram));
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still triages both halves the way the model above says.
    ///
    /// The last of the four matters most to the open question: a connected socket bounds who can send
    /// the empty packet, and a change to recvfrom would change the reasoning as well as the code.
    /// </summary>
    [Fact]
    public void TheCStillTriagesBothHalvesThisWay()
    {
        if (TakionDatagramReadSource.Locate() is not { } path)
            return;

        string text = File.ReadAllText(path);

        Assert.True(TakionDatagramReadSource.TimeoutAndCancelReturnTogether(text));
        Assert.True(TakionDatagramReadSource.ZeroOrLessIsANetworkError(text));
        Assert.True(TakionDatagramReadSource.ZeroHasItsOwnLog(text));
        Assert.True(TakionDatagramReadSource.TheSocketIsConnectedAndReadWithRecv(text));
    }

    /// <summary>A loop host that reads through the triage above, so the two models are one path.</summary>
    private sealed class ReadingHost(params int[] recvReturns) : ITakionLoopHost
    {
        private readonly Queue<int> returns = new(recvReturns);

        public bool CryptAvailable => true;

        public bool HasPostponed => false;

        public ulong NextTimeoutMs => 100;

        public int Dispatches { get; private set; }

        // The three the loop can call and this host has nothing to say about: PP487 asserts when each
        // of them fires, and this host exists to exercise the READ triage underneath them. Empty on
        // purpose rather than unimplemented - CryptAvailable is true and HasPostponed is false above,
        // so the first two are unreachable here and only a timeout would reach the third.
        public void RecheckMacs()
        {
        }

        public void FlushPostponed()
        {
        }

        public void FlushWithTimeout()
        {
        }

        public TakionReceiveResult Receive(Span<byte> into, ulong timeoutMs)
        {
            int recvReturn = returns.Count > 0 ? returns.Dequeue() : -1;
            TakionReadOutcome read = TakionDatagramRead.Read(TakionSelectOutcome.Ready, recvReturn);

            return new TakionReceiveResult(TakionDatagramRead.ForTheLoop(read.Result), read.Length);
        }

        public void Dispatch(Span<byte> datagram) => Dispatches++;
    }

    /// <summary>
    /// END TO END: a host reading through this triage ends PP487's loop on one empty datagram.
    ///
    /// The seam between the two models, asserted rather than left to the reader. PP454 and PP458 each
    /// cost a task to undo a second model of one thing, and two models that only compose in a
    /// sentence are how that starts - the first datagram is handled, the empty one after it ends the
    /// thread, and nothing in between had to be believed.
    /// </summary>
    [Fact]
    public void AnEmptyDatagramEndsTheLoopEndToEnd()
    {
        var host = new ReadingHost(8, 0);

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(1, host.Dispatches);
        Assert.True(outcome.ExitedOnFailure);
        Assert.False(outcome.HitLimit);
        Assert.Equal(2, outcome.Iterations);
    }

    /// <summary>The connected-socket predicate says no to a file that reads with recvfrom.</summary>
    [Fact]
    public void ThePredicateSaysNoToAnUnconnectedRead()
    {
        Assert.False(TakionDatagramReadSource.TheSocketIsConnectedAndReadWithRecv(
            "connect(takion->sock, info->sa, info->sa_len);\n"
                + "recv(takion->sock, buf, n, 0);\n"
                + "recvfrom(takion->sock, buf, n, 0, &from, &len);\n"));

        Assert.False(TakionDatagramReadSource.TheSocketIsConnectedAndReadWithRecv(
            "recv(takion->sock, buf, n, 0);\n"));
    }
}
