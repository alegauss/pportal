using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP498, under PP27: the drain takion runs on a socket the PSN path hands it.
///
/// The shape worth pinning is that this function cannot succeed. Its normal ending is a TIMEOUT,
/// and the caller carries on from exactly that one - so "which failure" is the whole interface.
/// </summary>
public class TakionSocketDrainTests
{
    /// <summary>A quiet socket ends the drain on its FIRST wait, not on the second.</summary>
    [Fact]
    public void AQuietSocketEndsOnTheFirstWait()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain([DrainWait.Timeout], []);

        Assert.Equal(DrainEnd.WaitTimedOut, outcome.End);
        Assert.Equal(ChiakiError.Timeout, outcome.Error);
        Assert.Equal(0, outcome.Discarded);
        Assert.True(outcome.Forgiven);
    }

    /// <summary>Queued datagrams are read and thrown away until the socket goes quiet.</summary>
    [Fact]
    public void QueuedDatagramsAreDiscardedUntilItGoesQuiet()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain(
            [DrainWait.Readable, DrainWait.Readable, DrainWait.Readable, DrainWait.Timeout],
            [1200, 40, 900]);

        Assert.Equal(DrainEnd.WaitTimedOut, outcome.End);
        Assert.Equal(3, outcome.Discarded);
        Assert.Equal(0, outcome.EmptyDiscarded);
        Assert.True(outcome.Forgiven);
    }

    /// <summary>
    /// A zero-length datagram is another discard here, where PP488 made it the receive thread's
    /// end.
    ///
    /// Same socket, same event, twenty lines apart, two meanings. This asserts the local one and
    /// the join is asserted below.
    /// </summary>
    [Fact]
    public void AnEmptyDatagramIsDiscardedRatherThanEndingIt()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain(
            [DrainWait.Readable, DrainWait.Readable, DrainWait.Timeout], [0, 0]);

        Assert.Equal(DrainEnd.WaitTimedOut, outcome.End);
        Assert.Equal(2, outcome.Discarded);
        Assert.Equal(2, outcome.EmptyDiscarded);
    }

    /// <summary>And a negative length is what ends it, with the one unforgiven network error.</summary>
    [Fact]
    public void ANegativeLengthEndsItAndIsNotForgiven()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain(
            [DrainWait.Readable, DrainWait.Readable], [64, -1]);

        Assert.Equal(DrainEnd.NetworkError, outcome.End);
        Assert.Equal(ChiakiError.Network, outcome.Error);
        Assert.Equal(1, outcome.Discarded);
        Assert.False(outcome.Forgiven);
    }

    /// <summary>The stop pipe firing ends it as CANCELED, which the caller does not forgive.</summary>
    [Fact]
    public void TheStopPipeEndsItUnforgiven()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain([DrainWait.Canceled], []);

        Assert.Equal(DrainEnd.Canceled, outcome.End);
        Assert.Equal(ChiakiError.Canceled, outcome.Error);
        Assert.False(outcome.Forgiven);
    }

    /// <summary>
    /// The outer deadline only ever stops a socket that keeps delivering, and it is forgiven too.
    ///
    /// A socket that never goes quiet is drained for a second and then abandoned - which is a
    /// different ending from the quiet case and the same error code, so the two are distinguished
    /// by the trace rather than by the caller.
    /// </summary>
    [Fact]
    public void AnEndlessSocketIsAbandonedAtTheDeadline()
    {
        DrainOutcome outcome = TakionSocketDrain.Drain(
            Enumerable.Repeat(DrainWait.Readable, 100),
            Enumerable.Repeat(1000, 100),
            elapsedPerTurn: TakionSocketDrain.WaitMs);

        Assert.Equal(DrainEnd.DeadlinePassed, outcome.End);
        Assert.Equal(ChiakiError.Timeout, outcome.Error);
        Assert.True(outcome.Forgiven);

        // Six turns: the sixth begins at 1000, which is not yet past the deadline.
        Assert.Equal(6, outcome.Discarded);
    }

    /// <summary>
    /// Two endings share one error code, and only that code reaches the caller.
    ///
    /// So a port that reported "drained cleanly" as Success would change which of the four aborts
    /// a PSN connection, without any of the four changing.
    /// </summary>
    [Fact]
    public void OnlyTheTimeoutIsForgiven()
    {
        Assert.True(TakionSocketDrain.IsForgiven(ChiakiError.Timeout));

        foreach (ChiakiError error in new[]
                 { ChiakiError.Success, ChiakiError.Canceled, ChiakiError.Network, ChiakiError.Unknown })
        {
            Assert.False(TakionSocketDrain.IsForgiven(error));
        }
    }

    /// <summary>The read buffer is the same 1500 PP485 rented for the receive thread.</summary>
    [Fact]
    public void TheBufferIsTheSame1500()
        => Assert.Equal(TakionSocketDrain.BufferSize, TakionReceiveBuffer.DatagramCapacity);

    /// <summary>
    /// THE DRIFT CHECK: the drain still cannot succeed, the caller still forgives only the timeout,
    /// and only a negative length still ends the loop.
    /// </summary>
    [Fact]
    public void TheCStillSpellsTheDrainThisWay()
    {
        if (TakionSocketDrainSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string drain = Assert.IsType<string>(TakionSocketDrainSource.DrainBody(source));
        string connect = Assert.IsType<string>(TakionSocketDrainSource.ConnectBody(source));

        Assert.True(TakionSocketDrainSource.TheDrainNeverReturnsSuccess(drain));
        Assert.True(TakionSocketDrainSource.TheCallerForgivesOnlyTheTimeout(connect));
        Assert.True(TakionSocketDrainSource.OnlyANegativeLengthEndsTheDrain(drain));
        Assert.True(TakionSocketDrainSource.TheTwoDeadlinesAreStillThese(drain));
    }

    /// <summary>
    /// And nothing puts the socket back into blocking mode after the wait takes it out.
    ///
    /// Three files for one fact: the wait uses WSAEventSelect, the only switch is
    /// chiaki_socket_set_nonblock, and takion never calls it. If a call appears in takion.c the
    /// note above stops being true and this is what says so.
    /// </summary>
    [Fact]
    public void NothingPutsTheSocketBackIntoBlockingMode()
    {
        if (TakionSocketDrainSource.Locate() is not { } takion
            || TakionSocketDrainSource.LocateSock() is not { } sock
            || TakionSocketDrainSource.LocateStopPipe() is not { } stopPipe)
        {
            return;
        }

        Assert.True(TakionSocketDrainSource.NothingRestoresBlockingMode(
            File.ReadAllText(stopPipe), File.ReadAllText(sock), File.ReadAllText(takion)));
    }
}
