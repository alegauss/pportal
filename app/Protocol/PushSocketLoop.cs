namespace ChiakiNg.Protocol;

/// <summary>
/// PP214: what a frame off the push socket is, as the FLAGS the loop tests.
///
/// Flags and not an enumeration, because the core tests them one at a time with four independent
/// ifs rather than switching on a kind. A frame can carry more than one - a close that is also a
/// ping is answered and then closes - and a port that picked a single kind per frame would drop
/// one of the two silently.
/// </summary>
[Flags]
public enum WebSocketFrameKind
{
    /// <summary>Nothing the loop acts on.</summary>
    None = 0,

    Text = 1 << 0,
    Binary = 1 << 1,
    Ping = 1 << 2,
    Pong = 1 << 3,
    Close = 1 << 4,
}

/// <summary>What one frame makes the loop do. Also flags, for the same reason.</summary>
[Flags]
public enum FrameAction
{
    /// <summary>The frame is not one this loop acts on.</summary>
    None = 0,

    /// <summary>A pong arrived, so the socket is answering and the deadline is off.</summary>
    StopExpectingPong = 1 << 0,

    /// <summary>Answer with a pong carrying the SAME bytes back.</summary>
    SendPong = 1 << 1,

    /// <summary>End the loop.</summary>
    Close = 1 << 2,

    /// <summary>The frame is a notification: parse it and put it on the queue.</summary>
    Deliver = 1 << 3,
}

/// <summary>What the loop should do before it tries to read another frame.</summary>
public enum KeepaliveStep
{
    /// <summary>A pong was expected and the interval has passed: the socket is gone.</summary>
    PongOverdue,

    /// <summary>Send a ping and start expecting a pong.</summary>
    SendPing,

    /// <summary>Neither: read.</summary>
    Read,
}

/// <summary>
/// PP212 gave the queue a notification lands on and PP190 what one is. This is the loop between
/// the socket and them: when it pings, when it gives up, and what it does with each frame.
///
/// TWO THINGS THAT ARE ONE THING. The interval between pings and the deadline for a pong are the
/// same five seconds, measured from the same instant, by the same subtraction - and the pong test
/// is asked FIRST. So a pong that is late by any amount at all ends the socket at exactly the
/// moment the next ping would have gone out. There is no grace: the deadline cannot drift away
/// from the cadence, because it is not a second number.
///
/// AND A NUMBER WRITTEN THREE TIMES. <see cref="PingIntervalSeconds"/> is the core's constant, and
/// the core uses it for the select timeout only - both ping tests are spelled 5LL * SECOND_US.
/// Today they agree. Change the constant and only the wait gets longer, while the socket goes on
/// pinging every five seconds. Stated here rather than tidied, because tidying it would be a
/// behaviour change hiding inside a refactor.
///
/// Split from any socket for the reason <see cref="ChiakiNg.Session.FocusChainBehavior.Decide"/>
/// gives: reading a frame needs a network, and deciding what a frame means needs a clock at most.
/// </summary>
public static class PushSocketLoop
{
    /// <summary>The core's constant - which governs the wait, not the ping. See the class note.</summary>
    public const long PingIntervalSeconds = 5;

    /// <summary>The microsecond clock the two tests are made against.</summary>
    public const long MicrosecondsPerSecond = 1_000_000;

    /// <summary>The interval both as the cadence and as the deadline, because it is both.</summary>
    public const long PingIntervalUs = PingIntervalSeconds * MicrosecondsPerSecond;

    /// <summary>How long the loop blocks waiting to be woken, in milliseconds.</summary>
    public const long SelectTimeoutMs = PingIntervalSeconds * 1000;

    /// <summary>The read buffer, which is also the largest frame this loop can see: 64 KiB.</summary>
    public const int MaxFrameSize = 64 * 1024;

    /// <summary>
    /// What to do before reading, given the clock and whether a pong is outstanding.
    ///
    /// The order is the core's and is the whole rule: the overdue test comes first, so at the
    /// instant the interval elapses a socket owing a pong is dropped rather than pinged again.
    /// </summary>
    /// <param name="nowUs">The monotonic clock.</param>
    /// <param name="lastPingSentUs">When the last ping went out. Zero before the first.</param>
    /// <param name="expectingPong">Whether a ping is outstanding.</param>
    public static KeepaliveStep Next(long nowUs, long lastPingSentUs, bool expectingPong)
    {
        bool elapsed = nowUs - lastPingSentUs > PingIntervalUs;

        if (expectingPong && elapsed)
            return KeepaliveStep.PongOverdue;

        return elapsed ? KeepaliveStep.SendPing : KeepaliveStep.Read;
    }

    /// <summary>
    /// What one frame makes the loop do.
    ///
    /// Four independent tests, so the answers accumulate. Text and binary are one answer between
    /// them - the core asks for either and does the same thing - and neither is exclusive with a
    /// ping or a close arriving on the same frame.
    /// </summary>
    public static FrameAction ActionsFor(WebSocketFrameKind flags)
    {
        FrameAction actions = FrameAction.None;

        if ((flags & WebSocketFrameKind.Pong) != 0)
            actions |= FrameAction.StopExpectingPong;

        if ((flags & WebSocketFrameKind.Ping) != 0)
            actions |= FrameAction.SendPong;

        if ((flags & WebSocketFrameKind.Close) != 0)
            actions |= FrameAction.Close;

        if ((flags & (WebSocketFrameKind.Text | WebSocketFrameKind.Binary)) != 0)
            actions |= FrameAction.Deliver;

        return actions;
    }

    /// <summary>
    /// The bytes a pong answers with: the ping's own payload, not an empty frame. The core sends
    /// back exactly what arrived, and the length it received with it.
    /// </summary>
    public static ReadOnlyMemory<byte> PongPayloadFor(ReadOnlyMemory<byte> ping) => ping;
}

/// <summary>
/// PP214: the frame loop where the core writes it.
/// </summary>
public static class PushSocketLoopSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the pong deadline is still the ping interval, and still asked first.</summary>
    public static bool ThePongDeadlineIsStillTheCadence(string core)
    {
        string body = Body(core);

        int overdue = body.IndexOf(
            "if (expecting_pong && now - last_ping_sent > 5LL * SECOND_US)", StringComparison.Ordinal);
        int ping = body.IndexOf(
            "if (now - last_ping_sent > 5LL * SECOND_US)", StringComparison.Ordinal);

        return overdue >= 0 && ping > overdue;
    }

    /// <summary>
    /// Whether the constant still governs the wait alone. True means the divergence described in
    /// <see cref="PushSocketLoop"/> is still latent - the constant is spent on the select timeout
    /// and the two ping tests are still literals.
    /// </summary>
    public static bool TheConstantStillGovernsTheWaitAlone(string core)
    {
        string body = Body(core);
        return body.Contains(
                "uint64_t timeout = WEBSOCKET_PING_INTERVAL_SEC * 1000;", StringComparison.Ordinal)
            && body.Contains("5LL * SECOND_US", StringComparison.Ordinal)
            && !body.Contains("WEBSOCKET_PING_INTERVAL_SEC * SECOND_US", StringComparison.Ordinal);
    }

    /// <summary>And whether that constant is still the five this port carries.</summary>
    public static bool TheIntervalIsStillFiveSeconds(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"#define WEBSOCKET_PING_INTERVAL_SEC {PushSocketLoop.PingIntervalSeconds}",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the four frame tests are still independent ifs rather than a switch.</summary>
    public static bool TheFrameTestsAreStillIndependent(string core)
    {
        string body = Body(core);

        return body.Contains("if (meta->flags & CURLWS_PONG)", StringComparison.Ordinal)
            && body.Contains("if (meta->flags & CURLWS_PING)", StringComparison.Ordinal)
            && body.Contains("if (meta->flags & CURLWS_CLOSE)", StringComparison.Ordinal)
            && body.Contains(
                "if (meta->flags & CURLWS_TEXT || meta->flags & CURLWS_BINARY)", StringComparison.Ordinal)
            && !body.Contains("else if (meta->flags", StringComparison.Ordinal);
    }

    /// <summary>Whether a ping is still answered with the bytes it arrived with.</summary>
    public static bool ThePongStillCarriesThePingsBytes(string core)
    {
        string body = Body(core);

        // rlen, the received length - not 0, which is what the loop's own outgoing PING sends.
        return body.Contains(
                "curl_ws_send(curl, buf, rlen, &wlen, 0, CURLWS_PONG)", StringComparison.Ordinal)
            && body.Contains(
                "curl_ws_send(curl, buf, 0, &wlen, 0, CURLWS_PING)", StringComparison.Ordinal);
    }

    /// <summary>And whether the read buffer is still what bounds a frame.</summary>
    public static bool TheFrameSizeIsStillThis(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("#define WEBSOCKET_MAX_FRAME_SIZE 64 * 1024", StringComparison.Ordinal)
            && PushSocketLoop.MaxFrameSize == 64 * 1024;
    }

    /// <summary>
    /// websocket_thread_func's body, cut at the two lines that bound it.
    /// </summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(
            "uint64_t timeout = WEBSOCKET_PING_INTERVAL_SEC * 1000;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf("cleanup_json:", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
