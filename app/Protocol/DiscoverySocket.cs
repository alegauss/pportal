using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One attempt in the bind ladder.</summary>
/// <param name="Port">The port tried. 0 is the last rung and means "any".</param>
/// <param name="LoggedPort">
/// The port the C's log line names for this attempt's FAILURE - which is not <paramref name="Port"/>.
/// See <see cref="DiscoverySocket"/>.
/// </param>
/// <param name="Next">The port the following attempt would use, or null where this was the last.</param>
public readonly record struct BindAttempt(ushort Port, ushort LoggedPort, ushort? Next);

/// <summary>What the receive loop does with one turn.</summary>
public enum DiscoveryTurn
{
    /// <summary>The stop pipe fired: leave.</summary>
    Cancelled,

    /// <summary>The wait itself failed: leave.</summary>
    SelectFailed,

    /// <summary>The receive failed: leave. Unlike every other loop in this tree.</summary>
    ReceiveFailed,

    /// <summary>Nothing in the datagram: go round again.</summary>
    Empty,

    /// <summary>Not a discovery reply: go round again.</summary>
    Unparseable,

    /// <summary>A host, handed to the callback.</summary>
    Host,
}

/// <summary>
/// PP462, under PP29: the discovery socket and the thread that reads it.
///
/// PP29's remainder is exactly this - "the discovery socket and its two threads are still C; the reply
/// parser, the broadcast packet and the PIN exchange have landed". PP6 gave the port a managed
/// discovery SERVICE, but that is a wrapper over libchiaki's own: <see cref="DiscoveryService"/> says
/// so, and joins a thread libchiaki owns. Nothing managed decides where the socket binds or what the
/// loop does with a datagram.
///
/// THE BIND LADDER IS SEVENTEEN PORTS AND THEN ANY. 9303 to 9319 in order, and if every one is taken,
/// port 0 - which the loop treats as the last rung rather than as a retry, so a failure there ends it.
///
/// AND EVERY RUNG'S LOG NAMES THE WRONG PORT. The increment happens BEFORE the log, in both branches:
/// a failure on 9303 reports "failed to bind port 9304, trying one higher", and a failure on 9319
/// reports "failed to bind port 0, trying random" - a sentence naming the port it is about to try as
/// the one that just failed. Reachable whenever anything else holds 9303, which two instances of this
/// client on one machine is enough for. Reproduced here as <see cref="BindAttempt.LoggedPort"/> beside
/// the real one, and filed as a defect rather than corrected in passing.
///
/// A BROADCAST OPTION THAT FAILS IS LOGGED AND IGNORED. setsockopt's result is tested only to log it,
/// and the function returns success either way - so discovery runs with broadcast disabled, finds
/// nothing, and the only evidence is one line. The same shape PP259 recorded for the STUN server list.
///
/// THE LOOP LEAVES ON A FAILED RECEIVE, which is worth saying because nothing else in this tree does.
/// PP256 and PP238's punch loop continues; PP457 had to bound it. Here `n < 0` breaks, so the reader
/// who has met those loops should not assume this one spins.
/// </summary>
public static class DiscoverySocket
{
    /// <summary>CHIAKI_DISCOVERY_PORT_LOCAL_MIN.</summary>
    public const ushort LocalPortMin = 9303;

    /// <summary>CHIAKI_DISCOVERY_PORT_LOCAL_MAX.</summary>
    public const ushort LocalPortMax = 9319;

    /// <summary>The port that means "any", and the ladder's last rung.</summary>
    public const ushort AnyPort = 0;

    /// <summary>How long the receive buffer is, and one less is what a datagram may fill.</summary>
    public const int ReceiveBufferSize = 512;

    /// <summary>Whether a setsockopt failure for SO_BROADCAST stops the init. It does not.</summary>
    public const bool ABroadcastFailureStops = false;

    /// <summary>
    /// Every rung of the ladder in order, with the port each attempt's log names.
    ///
    /// Seventeen numbered ports and then any: eighteen attempts, the last of which cannot be followed.
    /// </summary>
    public static IReadOnlyList<BindAttempt> Ladder { get; } = BuildLadder();

    private static IReadOnlyList<BindAttempt> BuildLadder()
    {
        var rungs = new List<BindAttempt>();

        for (var port = LocalPortMin; port <= LocalPortMax; port++)
        {
            ushort? next = port == LocalPortMax ? AnyPort : (ushort)(port + 1);
            rungs.Add(new BindAttempt(port, LoggedPortFor(port), next));
        }

        // The last rung: any port, and nothing follows a failure there.
        rungs.Add(new BindAttempt(AnyPort, LoggedPortFor(AnyPort), Next: null));

        return rungs;
    }

    /// <summary>
    /// The port the C's log names when the attempt on <paramref name="port"/> fails.
    ///
    /// The defect, as arithmetic: the increment runs first, so this is the NEXT port and not the one
    /// that failed. At the top of the numbered range it is 0, which is the random rung.
    /// </summary>
    public static ushort LoggedPortFor(ushort port)
    {
        if (port == AnyPort)
            return AnyPort;

        return port == LocalPortMax ? AnyPort : (ushort)(port + 1);
    }

    /// <summary>Whether the log for this attempt names the port that actually failed.</summary>
    public static bool TheLogNamesThePortThatFailed(ushort port) => LoggedPortFor(port) == port;

    /// <summary>
    /// What one turn of the receive loop does.
    /// </summary>
    /// <param name="cancelled">Whether the stop pipe fired.</param>
    /// <param name="selectFailed">Whether the wait failed for any other reason.</param>
    /// <param name="received">Bytes the receive returned, negative where it failed.</param>
    /// <param name="parsed">Whether the datagram parsed as a discovery reply.</param>
    public static DiscoveryTurn Next(bool cancelled, bool selectFailed, int received, bool parsed)
    {
        if (cancelled)
            return DiscoveryTurn.Cancelled;

        if (selectFailed)
            return DiscoveryTurn.SelectFailed;

        // Leaves, where the punch loop would have continued. See the type's note.
        if (received < 0)
            return DiscoveryTurn.ReceiveFailed;

        if (received == 0)
            return DiscoveryTurn.Empty;

        return parsed ? DiscoveryTurn.Host : DiscoveryTurn.Unparseable;
    }

    /// <summary>Whether a turn leaves the loop.</summary>
    public static bool Leaves(DiscoveryTurn turn)
        => turn is DiscoveryTurn.Cancelled or DiscoveryTurn.SelectFailed or DiscoveryTurn.ReceiveFailed;

    /// <summary>
    /// How many bytes of a datagram the loop will look at, and where it writes the terminator.
    ///
    /// The receive asks for <see cref="ReceiveBufferSize"/> - 1 so the last byte is always free for the
    /// NUL. The clamp after it - `if(n > sizeof(buf) - 1) n = sizeof(buf) - 1` - therefore cannot fire,
    /// and is reproduced as arithmetic rather than removed: a recvfrom that returned more than it was
    /// asked for is the thing it guards against.
    /// </summary>
    public static int UsableBytes(int received)
        => Math.Min(Math.Max(received, 0), ReceiveBufferSize - 1);

    /// <summary>
    /// Whether the one-shot thread would stop on this turn.
    ///
    /// It stops only where a host was parsed AND a callback is present - the `break` sits inside
    /// `if(thread->cb)`. So "one-shot" is a property of having a callback, and of the datagram parsing,
    /// rather than of the thread. Nothing calls it: it is one of the unreferenced exports, which is why
    /// this is stated rather than filed.
    /// </summary>
    public static bool TheOneShotStops(DiscoveryTurn turn, bool hasCallback)
        => turn == DiscoveryTurn.Host && hasCallback;

    /// <summary>discovery.c, where all of this lives.</summary>
    public const string RelativePath = @"lib\src\discovery.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The continuous thread's body.</summary>
    public static string? ThreadBody(string source)
        => CFunction.Body(source, "static void *discovery_thread_func");

    /// <summary>And the one-shot's.</summary>
    public static string? OneShotBody(string source)
        => CFunction.Body(source, "static void *discovery_thread_func_oneshot");

    /// <summary>A `#define` from discovery.h, which is where the ports are.</summary>
    public static long? PortDefineIn(string header, string name) => CDefine.Value(header, name);

    /// <summary>discovery.h, where the ports are defined.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\discovery.h";

    /// <summary>discovery.h, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>
    /// Whether both log lines still print the port AFTER moving it on - the defect, read as the order
    /// of two statements rather than inferred.
    /// </summary>
    public static bool BothLogsStillNameTheNextPort(string initBody)
    {
        ArgumentNullException.ThrowIfNull(initBody);

        string text = initBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int random = text.IndexOf("port = 0;", StringComparison.Ordinal);
        int randomLog = text.IndexOf("trying random", StringComparison.Ordinal);
        int higher = text.IndexOf("port++;", StringComparison.Ordinal);
        int higherLog = text.IndexOf("trying one higher", StringComparison.Ordinal);

        return random >= 0 && randomLog > random
            && higher >= 0 && higherLog > higher;
    }

    /// <summary>Whether the broadcast option's failure still only logs.</summary>
    public static bool ABroadcastFailureStillOnlyLogs(string initBody)
    {
        ArgumentNullException.ThrowIfNull(initBody);

        string text = initBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int option = text.IndexOf("SO_BROADCAST", StringComparison.Ordinal);
        if (option < 0)
            return false;

        string tail = text[option..];

        // Logged, and the only return after it is the success one - so the failure is stepped over.
        return tail.Contains("Discovery failed to setsockopt SO_BROADCAST", StringComparison.Ordinal)
            && tail.Contains("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)
            && !tail.Contains("return CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);
    }

    /// <summary>Whether the loop still leaves on a failed receive rather than continuing.</summary>
    public static bool TheLoopStillLeavesOnAFailedReceive(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string text = threadBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int failed = text.IndexOf("Discovery thread failed to read from socket", StringComparison.Ordinal);
        if (failed < 0)
            return false;

        int closes = text.IndexOf("\n\t\t}", failed, StringComparison.Ordinal);
        return closes > failed
            && text[failed..closes].Contains("break;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two threads are still the same function but for the one-shot's break.
    ///
    /// Compared after stripping whitespace and the `break;` the one-shot adds: if what is left differs,
    /// somebody has changed one and not the other, which is the risk fifty-four duplicated lines carry.
    /// </summary>
    public static bool TheTwoThreadsStillDifferOnlyByTheBreak(string threadBody, string oneShotBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);
        ArgumentNullException.ThrowIfNull(oneShotBody);

        static string Bare(string body) => new(
            [.. body.Where(c => !char.IsWhiteSpace(c))]);

        string continuous = Bare(threadBody);
        string oneShot = Bare(oneShotBody);

        if (continuous.Length == 0 || oneShot.Length == 0)
            return false;

        // The one-shot wraps the callback in a block and adds the break; undo exactly that.
        string reduced = oneShot
            .Replace(
                "if(thread->cb){thread->cb(&response,thread->cb_user);break;}",
                "if(thread->cb)thread->cb(&response,thread->cb_user);",
                StringComparison.Ordinal);

        return string.Equals(continuous, reduced, StringComparison.Ordinal);
    }
}
