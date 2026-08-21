using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One STUN server to ask for this end's external address.</summary>
/// <param name="Host">Its name.</param>
/// <param name="Port">And its port, which is not always 3478.</param>
public readonly record struct StunServer(string Host, ushort Port);

/// <summary>
/// PP33: the STUN servers, and the shuffle that is not the shuffle it looks like.
///
/// THE PREFERENCE IS A LOOP BOUND, NOT A SORT. Moonlight's server is first in the list and the
/// shuffle starts at index one, so it stays first by never being touched - there is no comparison
/// anywhere that says it is preferred. A port that shuffled the whole list and then moved the
/// preferred one to the front would agree with the comment and disagree with the code the first
/// time the list gained a second pinned entry.
///
/// THE SHUFFLE IS A BROKEN FISHER-YATES. The core draws <c>j = 1 + random % (i - 1)</c>, which
/// gives j in [1, i-1] - a range that EXCLUDES i. A correct Fisher-Yates over [1, i] draws from
/// [1, i], and the difference is not academic: because j can never be i, the element sitting at i
/// when its turn comes is guaranteed to move. The last server can never stay last. The resulting
/// order is not uniform over the permutations, and no amount of good randomness makes it so.
///
/// That is reproduced rather than corrected, and the reasoning is not the same as PP194's. The
/// overflow there was a latent defect with no observable behaviour to port; this has behaviour -
/// an order - and the order is what the next connection attempt uses. Correcting it would be a
/// redesign wearing a bug fix's name, which the non-goals refuse. The bias is pinned by a test that
/// a correct Fisher-Yates would fail, so the day someone does fix it, they will know they did.
///
/// THE LIST IS A GLOBAL, AND THE SHUFFLE MUTATES IT. It is defined - not declared - in a header, so
/// there is exactly one array for the process, and each shuffle reorders the same one. The order a
/// connection attempt sees is the order the previous attempt left behind. <see cref="Default"/> is
/// therefore the STARTING order and not a constant the code returns to.
///
/// And over IPv6 exactly ONE server is tried before giving up, where IPv4 walks the whole list.
/// </summary>
public static class StunServers
{
    /// <summary>How long a reply is waited for, in seconds.</summary>
    public const int ReplyTimeoutSeconds = 5;

    /// <summary>The index the shuffle starts at, which is what keeps the first server first.</summary>
    public const int FirstShuffled = 1;

    /// <summary>How many servers are tried over IPv6 before giving up. One.</summary>
    public const int Ipv6ServersTried = 1;

    /// <summary>The list as the header defines it, before any shuffle has reordered it.</summary>
    public static IReadOnlyList<StunServer> Default { get; } =
    [
        new("stun.moonlight-stream.org", 3478),
        new("stun.l.google.com", 19302),
        new("stun.l.google.com", 19305),
        new("stun1.l.google.com", 19302),
        new("stun1.l.google.com", 19305),
        new("stun2.l.google.com", 19302),
        new("stun2.l.google.com", 19305),
        new("stun3.l.google.com", 19302),
        new("stun3.l.google.com", 19305),
        new("stun4.l.google.com", 19302),
        new("stun4.l.google.com", 19305),
    ];

    /// <summary>The one that is pinned to the front by not being shuffled.</summary>
    public static StunServer Preferred => Default[0];

    /// <summary>
    /// The core's shuffle, in place and with its bias intact.
    ///
    /// <paramref name="random"/> stands in for chiaki_random_32. The draw is
    /// <c>1 + random % (i - 1)</c>, which cannot produce i - see the class note for why that is
    /// kept rather than corrected.
    /// </summary>
    public static void Shuffle(IList<StunServer> servers, Func<uint> random)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(random);

        for (int i = servers.Count - 1; i > FirstShuffled; i--)
        {
            int j = FirstShuffled + (int)(random() % (uint)(i - FirstShuffled));
            (servers[i], servers[j]) = (servers[j], servers[i]);
        }
    }

    /// <summary>How many swaps a shuffle of this many servers performs.</summary>
    public static int SwapCount(int count) => Math.Max(0, count - 1 - FirstShuffled);
}

/// <summary>
/// PP33: the server list's rules where the Qt core states them.
/// </summary>
public static class StunServersSource
{
    /// <summary>Where the list and the shuffle live - a header, which is part of the finding.</summary>
    public const string RelativePath = @"lib\src\remote\stun.h";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the list is still defined - not declared - in that header.</summary>
    public static bool TheListIsStillDefinedInTheHeader(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("StunServer STUN_SERVERS[] = {", StringComparison.Ordinal)
            && !core.Contains("extern StunServer STUN_SERVERS", StringComparison.Ordinal);
    }

    /// <summary>Whether every server this port knows about is still in it, in this order.</summary>
    public static bool TheSameServersAreStillListed(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int cursor = 0;
        foreach (StunServer server in StunServers.Default)
        {
            int at = core.IndexOf($"{{\"{server.Host}\", {server.Port}}}", cursor, StringComparison.Ordinal);
            if (at < 0)
                return false;

            cursor = at + 1;
        }

        return true;
    }

    /// <summary>
    /// Whether the draw still excludes i - which is the whole of the bias, and the one line a
    /// well-meaning correction would touch.
    /// </summary>
    public static bool TheDrawStillExcludesTheCurrentIndex(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("int j = 1 + chiaki_random_32() % (i - 1);", StringComparison.Ordinal);
    }

    /// <summary>Whether the loop still leaves the first server alone.</summary>
    public static bool TheFirstServerIsStillLeftAlone(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("for (int i = num_servers - 1; i > 1; i--)", StringComparison.Ordinal)
            && core.Contains("Shuffle order of servers other than moonlight server", StringComparison.Ordinal);
    }

    /// <summary>Whether IPv6 still stops after one server.</summary>
    public static bool SixStillStopsAfterOne(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("// Only try 1 IPV6 server", StringComparison.Ordinal);
    }
}
