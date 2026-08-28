using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one wait on the socket produced.</summary>
public enum DrainWait
{
    /// <summary>A datagram is ready. Its length still decides what happens.</summary>
    Readable,

    /// <summary>Nothing arrived inside the per-wait window.</summary>
    Timeout,

    /// <summary>The stop pipe fired.</summary>
    Canceled,
}

/// <summary>How the drain ended. There is no success.</summary>
public enum DrainEnd
{
    /// <summary>The whole-drain deadline passed with the socket still delivering.</summary>
    DeadlinePassed,

    /// <summary>A wait found nothing. The normal outcome on a quiet socket.</summary>
    WaitTimedOut,

    /// <summary>The stop pipe fired.</summary>
    Canceled,

    /// <summary>recv returned a negative length.</summary>
    NetworkError,
}

/// <summary>What a whole drain did.</summary>
/// <param name="End">How it stopped.</param>
/// <param name="Error">The code the caller sees.</param>
/// <param name="Discarded">How many datagrams were read and thrown away.</param>
/// <param name="EmptyDiscarded">How many of those were zero-length.</param>
/// <param name="Forgiven">Whether chiaki_takion_connect treats this as "carry on".</param>
public readonly record struct DrainOutcome(
    DrainEnd End, ChiakiError Error, int Discarded, int EmptyDiscarded, bool Forgiven);

/// <summary>
/// PP498, under PP27: the socket drain takion runs before its thread starts, on the PSN path.
///
/// When the session hands takion a socket the hole punch already used, whatever is still queued on
/// it belongs to the punch and not to takion. Fourteen lines throw it away, and the function HAS NO
/// SUCCESS RETURN.
///
/// EVERY EXIT IS AN ERROR AND ONE OF THEM IS THE NORMAL ONE. The whole-drain deadline gives
/// TIMEOUT. A wait that finds nothing gives TIMEOUT too - and that is what happens on a quiet
/// socket, after one wait rather than after the second. The stop pipe gives CANCELED and a failed
/// recv gives NETWORK. chiaki_takion_connect forgives exactly TIMEOUT, so this function's success
/// is spelled as one particular failure and a port that returns Success for "drained cleanly" has
/// changed which of the four aborts the connection.
///
/// THE TWO DEADLINES ARE NOT THE SAME DEADLINE. The outer one bounds the whole drain and is tested
/// at the top of each turn, so it can only stop a socket that keeps delivering. The inner one
/// bounds one wait. Collapsed into one, a busy socket is abandoned early or a quiet one costs a
/// second before the stream can start.
///
/// A ZERO-LENGTH DATAGRAM IS JUST ANOTHER DISCARD HERE. Only a negative length ends the loop. PP488
/// pinned the opposite for the receive thread, where an empty datagram IS the network error that
/// ends it - the same event on the same socket, meaning two different things twenty lines apart.
///
/// AND THE DRAIN LEAVES THE SOCKET NON-BLOCKING. The wait is WSAEventSelect underneath, which sets
/// that mode as a side effect on Windows, and nothing in this tree ever puts it back -
/// chiaki_socket_set_nonblock is only ever called with true. That is why the recv below the wait
/// cannot block, and it is a property of the platform rather than of this code.
/// </summary>
public static class TakionSocketDrain
{
    /// <summary>How long the whole drain may take.</summary>
    public const int DeadlineMs = 1000;

    /// <summary>How long one wait may take.</summary>
    public const int WaitMs = 200;

    /// <summary>The buffer the C reads into, and PP485's number again.</summary>
    public const int BufferSize = 1500;

    /// <summary>The one ending chiaki_takion_connect carries on from.</summary>
    public static bool IsForgiven(ChiakiError error) => error == ChiakiError.Timeout;

    /// <summary>
    /// Runs the drain over a scripted socket.
    /// </summary>
    /// <param name="waits">What each wait returns, in order.</param>
    /// <param name="lengths">
    /// What recv returns for each readable wait. Negative ends the drain; zero is discarded like
    /// any other message.
    /// </param>
    /// <param name="elapsedPerTurn">
    /// Milliseconds the clock advances per turn, so the outer deadline can be reached without one.
    /// </param>
    public static DrainOutcome Drain(
        IEnumerable<DrainWait> waits, IEnumerable<int> lengths, int elapsedPerTurn = 0)
    {
        ArgumentNullException.ThrowIfNull(waits);
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedPerTurn);

        using IEnumerator<DrainWait> wait = waits.GetEnumerator();
        using IEnumerator<int> length = lengths.GetEnumerator();

        var discarded = 0;
        var empty = 0;
        var now = 0;

        while (true)
        {
            // Tested at the top, and strictly greater - so a turn beginning exactly on the deadline
            // still runs.
            if (now > DeadlineMs)
                return Ended(DrainEnd.DeadlinePassed, ChiakiError.Timeout, discarded, empty);

            DrainWait outcome = wait.MoveNext() ? wait.Current : DrainWait.Timeout;

            if (outcome == DrainWait.Timeout)
                return Ended(DrainEnd.WaitTimedOut, ChiakiError.Timeout, discarded, empty);

            if (outcome == DrainWait.Canceled)
                return Ended(DrainEnd.Canceled, ChiakiError.Canceled, discarded, empty);

            int received = length.MoveNext() ? length.Current : -1;
            if (received < 0)
                return Ended(DrainEnd.NetworkError, ChiakiError.Network, discarded, empty);

            discarded++;
            if (received == 0)
                empty++;

            now += elapsedPerTurn;
        }

        static DrainOutcome Ended(DrainEnd end, ChiakiError error, int discarded, int empty)
            => new(end, error, discarded, empty, IsForgiven(error));
    }
}

/// <summary>
/// PP498: the C's own spelling, because "no success return" is a claim about what is absent.
/// </summary>
public static class TakionSocketDrainSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>sock.c, where the non-blocking switch lives.</summary>
    public const string SockRelativePath = @"lib\src\sock.c";

    /// <summary>The stop pipe, whose wait sets that mode as a side effect.</summary>
    public const string StopPipeRelativePath = @"lib\src\stoppipe.c";

    /// <summary>sock.c, or null outside a checkout.</summary>
    public static string? LocateSock() => SanitizerSource.LocateRelative(SockRelativePath);

    /// <summary>stoppipe.c, or null outside a checkout.</summary>
    public static string? LocateStopPipe() => SanitizerSource.LocateRelative(StopPipeRelativePath);

    /// <summary>The drain.</summary>
    public static string? DrainBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode takion_read_extra_sock_messages");

    /// <summary>chiaki_takion_connect, which is what forgives the timeout.</summary>
    public static string? ConnectBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_connect");

    /// <summary>
    /// Whether the drain still has no CHIAKI_ERR_SUCCESS return of its own.
    ///
    /// A claim about absence, so it is read as absence: the body returns TIMEOUT, NETWORK, and
    /// whatever the wait handed back, and never names success.
    /// </summary>
    public static bool TheDrainNeverReturnsSuccess(string drainBody)
    {
        ArgumentNullException.ThrowIfNull(drainBody);

        return !drainBody.Contains("return CHIAKI_ERR_SUCCESS", StringComparison.Ordinal)
            && drainBody.Contains("return CHIAKI_ERR_TIMEOUT;", StringComparison.Ordinal)
            && drainBody.Contains("return CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the caller still forgives TIMEOUT and nothing else.
    ///
    /// The other half of the claim. If this test loses its TIMEOUT arm, a quiet socket aborts every
    /// PSN connection - and nothing in the drain itself would look wrong.
    /// </summary>
    public static bool TheCallerForgivesOnlyTheTimeout(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return connectBody.Contains(
            "if(err != CHIAKI_ERR_SUCCESS && err != CHIAKI_ERR_TIMEOUT)", StringComparison.Ordinal);
    }

    /// <summary>Whether only a negative length still ends the loop.</summary>
    public static bool OnlyANegativeLengthEndsTheDrain(string drainBody)
    {
        ArgumentNullException.ThrowIfNull(drainBody);

        return drainBody.Contains("if (len < 0)", StringComparison.Ordinal)
            && !drainBody.Contains("len <= 0", StringComparison.Ordinal)
            && !drainBody.Contains("len == 0", StringComparison.Ordinal);
    }

    /// <summary>Whether the two deadlines are still two, at the values this models.</summary>
    public static bool TheTwoDeadlinesAreStillThese(string drainBody)
    {
        ArgumentNullException.ThrowIfNull(drainBody);

        return drainBody.Contains(
                $"uint64_t expired = {TakionSocketDrain.DeadlineMs} + chiaki_time_now_monotonic_ms();",
                StringComparison.Ordinal)
            && drainBody.Contains($", false, {TakionSocketDrain.WaitMs});", StringComparison.Ordinal)
            && drainBody.Contains("if(now > expired)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the wait is still WSAEventSelect underneath, and nothing restores blocking mode.
    ///
    /// Two files, because the fact is the join between them: the wait sets the mode, and the only
    /// function that could unset it is never called with false.
    /// </summary>
    public static bool NothingRestoresBlockingMode(string stopPipeSource, string sockSource, string takionSource)
    {
        ArgumentNullException.ThrowIfNull(stopPipeSource);
        ArgumentNullException.ThrowIfNull(sockSource);
        ArgumentNullException.ThrowIfNull(takionSource);

        return stopPipeSource.Contains("WSAEventSelect(fd, events[1]", StringComparison.Ordinal)
            && sockSource.Contains("ioctlsocket(sock, FIONBIO, &nbio)", StringComparison.Ordinal)
            && !takionSource.Contains("chiaki_socket_set_nonblock(", StringComparison.Ordinal);
    }
}
