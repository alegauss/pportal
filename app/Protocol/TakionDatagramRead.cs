namespace ChiakiNg.Protocol;

/// <summary>What the select on the stop pipe answered.</summary>
public enum TakionSelectOutcome
{
    /// <summary>The socket has something. CHIAKI_ERR_SUCCESS.</summary>
    Ready,

    /// <summary>Nothing arrived in time. CHIAKI_ERR_TIMEOUT.</summary>
    TimedOut,

    /// <summary>The stop pipe fired - an orderly shutdown. CHIAKI_ERR_CANCELED.</summary>
    Canceled,

    /// <summary>The select itself failed.</summary>
    Failed,
}

/// <summary>What one takion_recv produced, at the C's own granularity.</summary>
public enum TakionReadResult
{
    /// <summary>Bytes arrived.</summary>
    Datagram,

    /// <summary>CHIAKI_ERR_TIMEOUT, returned unlogged.</summary>
    TimedOut,

    /// <summary>CHIAKI_ERR_CANCELED, returned unlogged.</summary>
    Canceled,

    /// <summary>Whatever the select failed with, logged first.</summary>
    SelectFailed,

    /// <summary>CHIAKI_ERR_NETWORK - a recv that returned zero or less.</summary>
    NetworkError,
}

/// <summary>One read, and how many bytes it got.</summary>
/// <param name="Result">Which of the five.</param>
/// <param name="Length">The length, which is meaningful only for a datagram.</param>
public readonly record struct TakionReadOutcome(TakionReadResult Result, int Length);

/// <summary>
/// PP488, under PP27: the socket read the loop injects - a select on the stop pipe, one recv, and
/// the triage of both.
///
/// FIVE RESULTS BECOME TWO BEHAVIOURS. takion_recv distinguishes a timeout, a cancel, a failed
/// select and a bad recv, and returns a different error code for each. <see cref="TakionReceiveLoop"/>
/// tests exactly one of them: a timeout flushes the AV queues and goes round, and everything else
/// leaves the loop. So the stop pipe firing - which is an orderly shutdown, the way a session is
/// meant to end - takes the same exit as a broken socket, and the code that tells them apart is read
/// by nobody. <see cref="ForTheLoop"/> is that collapse, stated on purpose rather than by omission.
///
/// AND A RECV OF ZERO IS NOT END-OF-FILE. The socket is connected - takion.c calls connect and then
/// recv, never recvfrom - but it is still a DATAGRAM socket, so a return of zero is a legal
/// zero-length packet from the peer and not a closed connection. The C reads it as CHIAKI_ERR_NETWORK
/// under its own log line, "Takion recv returned 0", which ends the receive thread and with it the
/// session.
///
/// THIS DOES NOT DECIDE WHETHER THAT IS RIGHT. A stream that tolerates loss through a reorder queue
/// dying on an empty packet is an odd asymmetry; against that, the socket is connected, so such a
/// packet comes from the console's address or from something able to use it, and nobody here has
/// traffic to say how a real console behaves. What this does is stop it being accidental - the
/// behaviour is pinned, so changing it has to change a test.
/// </summary>
public static class TakionDatagramRead
{
    /// <summary>
    /// One read: the select's answer, then the recv's return value.
    /// </summary>
    /// <param name="select">What the stop-pipe select said.</param>
    /// <param name="recvReturn">
    /// What recv returned - negative for a socket error, zero for an empty datagram, positive for a
    /// length. Only read where the select said Ready, because the C returns before calling recv.
    /// </param>
    public static TakionReadOutcome Read(TakionSelectOutcome select, int recvReturn)
    {
        // The two the C returns straight out, before recv and before any log.
        switch (select)
        {
            case TakionSelectOutcome.TimedOut:
                return new TakionReadOutcome(TakionReadResult.TimedOut, 0);
            case TakionSelectOutcome.Canceled:
                return new TakionReadOutcome(TakionReadResult.Canceled, 0);
            case TakionSelectOutcome.Failed:
                return new TakionReadOutcome(TakionReadResult.SelectFailed, 0);
            default:
                break;
        }

        // `if(received_sz <= 0)`. Both halves are CHIAKI_ERR_NETWORK; only the log differs, because
        // zero and negative are a different thing happening and the author wrote both down.
        return recvReturn <= 0
            ? new TakionReadOutcome(TakionReadResult.NetworkError, 0)
            : new TakionReadOutcome(TakionReadResult.Datagram, recvReturn);
    }

    /// <summary>
    /// What the loop does with a result: continue on a timeout, leave on anything else.
    ///
    /// The collapse, in one place. A cancel is an orderly shutdown and a network error is a fault,
    /// and the loop cannot tell them apart because it never looks at the code - it tests TIMEOUT and
    /// breaks on the rest.
    /// </summary>
    public static TakionReceiveOutcome ForTheLoop(TakionReadResult result) => result switch
    {
        TakionReadResult.Datagram => TakionReceiveOutcome.Datagram,
        TakionReadResult.TimedOut => TakionReceiveOutcome.Timeout,
        _ => TakionReceiveOutcome.Failed,
    };

    /// <summary>
    /// Whether the C logs this result.
    ///
    /// A timeout and a cancel are silent, which is what makes them ordinary rather than exceptional -
    /// one is every idle moment of a session and the other is how a session ends.
    /// </summary>
    public static bool IsLogged(TakionReadResult result)
        => result is not (TakionReadResult.TimedOut or TakionReadResult.Canceled);
}

/// <summary>
/// PP488: the C's own spelling of the triage above.
/// </summary>
public static class TakionDatagramReadSource
{
    /// <summary>takion.c, through the path constant this port already has.</summary>
    public static string? Locate() => TakionReceiveBuffer.LocateTakion();

    /// <summary>Whether a timeout and a cancel are still returned together, before anything is logged.</summary>
    public static bool TimeoutAndCancelReturnTogether(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains(
            "err == CHIAKI_ERR_TIMEOUT || err == CHIAKI_ERR_CANCELED", StringComparison.Ordinal);
    }

    /// <summary>Whether a recv of zero or less is still one branch ending in CHIAKI_ERR_NETWORK.</summary>
    public static bool ZeroOrLessIsANetworkError(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("if(received_sz <= 0)", StringComparison.Ordinal)
            && takionText.Contains("return CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);
    }

    /// <summary>Whether the zero case still has a log line of its own, saying it was noticed.</summary>
    public static bool ZeroHasItsOwnLog(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("Takion recv returned 0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the socket is still connected and read with recv.
    ///
    /// This is what bounds who can send the zero-length packet above: a connected datagram socket
    /// takes traffic from one address. If this ever became recvfrom, the reasoning in the section
    /// changes and so does the answer to the question it leaves open.
    /// </summary>
    public static bool TheSocketIsConnectedAndReadWithRecv(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("connect(takion->sock", StringComparison.Ordinal)
            && takionText.Contains("recv(takion->sock", StringComparison.Ordinal)
            && !takionText.Contains("recvfrom(takion->sock", StringComparison.Ordinal);
    }
}
