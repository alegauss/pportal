using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where the socket takion streams over came from.</summary>
public enum SocketOrigin
{
    /// <summary>Handed in by the caller - the PSN path, where the hole punch opened it.</summary>
    HandedIn,

    /// <summary>Made here, which is the local path.</summary>
    MadeHere,
}

/// <summary>
/// PP477, under PP27: the socket itself - the last of the four things PP27's own sentence names.
///
/// "the socket, the receive thread, the handshake and the resend loop". PP449 did the thread's timer,
/// PP450 the handshake, PP473 the postpone, PP475 the resend loop. This is the socket, and it is the
/// smallest of the five and the one with the most repetition.
///
/// THERE ARE TWO NEARLY IDENTICAL BRANCHES, and which one runs says where the socket came from. A
/// caller that hands one in gets it configured; a caller that does not gets one made and then
/// configured the same way. The configuring is written twice - the receive buffer, the fragment bit and
/// its four log lines - which is why the option names were wrong in four places rather than two.
///
/// THE RECEIVE BUFFER IS THE ADVERTISED WINDOW. SO_RCVBUF is set to a_rwnd, the same value the INIT
/// tells the console it can send into, so the socket's buffer and the protocol's promise are one
/// number. A port choosing a buffer size independently would advertise one thing and hold another.
///
/// AND FOUR LOG LINES NAMED THE WRONG OPTION, which PP477 fixed. The calls set IP_DONTFRAGMENT; the
/// failure logs said IP_MTU_DISCOVER, which is a different option controlling path MTU discovery
/// rather than the fragment bit. Reachable whenever the option fails, which is when somebody reads the
/// log to find out why - the same shape as PP463's bind ladder naming the port it was about to try.
///
/// `mac_dontfrag` IS A CONSTANT, AND THAT IS NOT A DEFECT. It is `bool mac_dontfrag = true;`, assigned
/// once and read in the four guards, so `if(r < 0 && mac_dontfrag)` is `if(r < 0)`. The name is a
/// leftover of the macOS build this tree's non-goals delete; the behaviour is what no guard would do.
/// Stated so nobody reads it as a platform switch that stopped switching.
/// </summary>
public static class TakionSocket
{
    /// <summary>What SO_RCVBUF is set to: the window the INIT advertises.</summary>
    public static uint ReceiveBufferIs => TakionHandshake.ARwnd;

    /// <summary>
    /// Whether this origin needs the socket created before it is configured.
    ///
    /// The configuring is the same either way, which is the duplication.
    /// </summary>
    public static bool Creates(SocketOrigin origin) => origin == SocketOrigin.MadeHere;

    /// <summary>
    /// The option the fragment-bit calls actually set, and what the failure log must name.
    ///
    /// One constant for both, because PP477's defect was exactly the two disagreeing.
    /// </summary>
    public const string FragmentOption = "IP_DONTFRAGMENT";

    /// <summary>The option the logs used to name, which controls something else entirely.</summary>
    public const string TheOptionTheLogsUsedToName = "IP_MTU_DISCOVER";

    /// <summary>How many fragment-bit log lines there are: two per branch, two branches.</summary>
    public const int FragmentLogLines = 4;

    /// <summary>
    /// Whether a failed fragment-bit setsockopt is fatal, given the guard's value.
    ///
    /// True, and the guard is a constant true - see the type's note on mac_dontfrag.
    /// </summary>
    public static bool AFragmentFailureIsFatal(bool macDontfrag) => macDontfrag;

    /// <summary>takion.c.</summary>
    public const string RelativePath = @"lib\src\takion.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>chiaki_takion_connect's body.</summary>
    public static string? ConnectBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_connect");

    /// <summary>
    /// PP477: whether every fragment-bit log now names the option the call sets.
    ///
    /// Both halves: the right name appears four times and the wrong one not at all. Counting only the
    /// right name would pass with two of four corrected, which is what a partial fix looks like.
    /// </summary>
    public static bool EveryFragmentLogNamesTheRightOption(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return CountOf(connectBody, $"setsockopt {FragmentOption}: ") == FragmentLogLines
            && !connectBody.Contains(TheOptionTheLogsUsedToName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the calls those logs belong to still set the fragment option - four of them, matching
    /// the four logs.
    /// </summary>
    public static bool TheCallsStillSetTheFragmentOption(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return CountOf(connectBody, $"IPPROTO_IP, {FragmentOption},") == FragmentLogLines;
    }

    /// <summary>Whether SO_RCVBUF is still set from a_rwnd, in both branches.</summary>
    public static bool TheReceiveBufferIsStillTheWindow(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return CountOf(connectBody, "const int rcvbuf_val = takion->a_rwnd;") == 2
            && CountOf(connectBody, "SOL_SOCKET, SO_RCVBUF,") == 2;
    }

    /// <summary>
    /// Whether the guard is still a constant - assigned true once and never again, so the four tests
    /// that read it are all the same test.
    /// </summary>
    public static bool TheGuardIsStillAConstant(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return CountOf(connectBody, "bool mac_dontfrag = true;") == 1
            && CountOf(connectBody, "mac_dontfrag =") == 1
            && CountOf(connectBody, "r < 0 && mac_dontfrag") == FragmentLogLines;
    }

    /// <summary>
    /// Whether the socket is still made in only one of the two branches, which is what the origin
    /// distinguishes.
    /// </summary>
    public static bool OnlyOneBranchMakesTheSocket(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return CountOf(connectBody, "takion->sock = socket(info->sa->sa_family") == 1;
    }

    private static int CountOf(string haystack, string needle)
    {
        var found = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
