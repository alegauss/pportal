using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: guessing which external port the NAT will hand out next, two different ways.
///
/// Once the allocation test (PP199) has said how far a NAT moves its ports, the offer carries
/// guesses at where the next one will land. There are two generators, chosen by whether the
/// increment looked steady or random, and they disagree about almost everything:
///
///   THE STEADY ONE STARTS ONE INCREMENT AHEAD. Its first candidate is the STUN port PLUS the
///   increment - the port that answered is not among the eight it offers, because it has already
///   been used. The spread generator's first candidate is delta zero, which IS that port. So the
///   same observation is either included or excluded depending on which branch was taken.
///
///   THEY WRAP THE SAME OVERFLOW TO DIFFERENT PLACES. Past 65535 the steady one lands at
///   <c>port - 65535 + 1024</c> - just above the well-known range - and the spread one lands at
///   <c>49152 + (port - 65536)</c>, the base of the ephemeral range. One overflow, two answers,
///   about twenty-four thousand apart.
///
///   AND THE STEADY ONE HAS TWO UNDERFLOW RULES, PICKED BY WHERE IT CAME FROM. Dropping below 1024
///   from ABOVE 1024 wraps to the top of the space; dropping below 1 from within the well-known
///   range adds 65535 instead. A guess that walks down through the well-known ports is left there
///   rather than wrapped, which is deliberate: the comment says a router already allocating in that
///   range is a router that uses it.
///
/// The spread is centred rather than forward-only: 0, +1, -1, +2, -2, and so on. A random NAT is as
/// likely to have gone backwards as forwards, and the port that answered is the best guess there is.
/// </summary>
public static class PortGuessing
{
    /// <summary>How many guesses the steady generator makes.</summary>
    public const int SequentialGuesses = 8;

    /// <summary>Plus the remote and local candidates, which is what the offer carries.</summary>
    public const int SequentialCandidates = SequentialGuesses + 2;

    /// <summary>How many guesses the spread generator makes by default.</summary>
    public const int RandomAllocationGuesses = 75;

    /// <summary>And how many sockets are opened to catch them.</summary>
    public const int RandomAllocationSocks = 250;

    /// <summary>The first port that is not well-known.</summary>
    public const int WellKnownLimit = 1024;

    /// <summary>The base of the ephemeral range, which only the spread generator wraps to.</summary>
    public const int EphemeralBase = 49152;

    /// <summary>The largest port there is.</summary>
    public const int MaxPort = ushort.MaxValue;

    /// <summary>
    /// The spread's offset for the nth guess: 0, +1, -1, +2, -2, and so on outwards.
    /// </summary>
    public static int Delta(int index)
    {
        if (index == 0)
            return 0;

        return index % 2 == 1 ? (index + 1) / 2 : -(index / 2);
    }

    /// <summary>
    /// The steady generator: <paramref name="count"/> ports, each one increment past the last,
    /// starting one increment PAST the port that answered.
    /// </summary>
    public static IReadOnlyList<ushort> Sequential(
        ushort stunPort, int increment, int count = SequentialGuesses)
    {
        var ports = new List<ushort>(count);
        int port = stunPort;

        for (int i = 0; i < count; i++)
        {
            int before = port;
            port += increment;

            if (port < WellKnownLimit && before > WellKnownLimit)
            {
                // Came down from above the well-known range: wrap to the top.
                port = MaxPort - (WellKnownLimit - port);
            }
            else if (port < 1)
            {
                // Came down from inside it: a different wrap entirely.
                port += MaxPort;
            }
            else if (port > MaxPort)
            {
                port = port - MaxPort + WellKnownLimit;
            }

            ports.Add((ushort)port);
        }

        return ports;
    }

    /// <summary>
    /// The spread generator: <paramref name="count"/> ports either side of the one that answered,
    /// starting with that port itself.
    /// </summary>
    public static IReadOnlyList<ushort> Spread(ushort stunPort, int count = RandomAllocationGuesses)
    {
        var ports = new List<ushort>(count);

        for (int i = 0; i < count; i++)
        {
            int port = stunPort + Delta(i);

            if (port > MaxPort)
                port = EphemeralBase + (port - MaxPort - 1);
            else if (port < WellKnownLimit)
                port = MaxPort - (WellKnownLimit - port);

            ports.Add((ushort)port);
        }

        return ports;
    }
}

/// <summary>
/// PP33: the guessing's rules where the Qt core states them.
/// </summary>
public static class PortGuessingSource
{
    /// <summary>Where the offer is built.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The constants this port copied, and what the core spells them.</summary>
    public static IReadOnlyList<(string Name, string Value)> Constants { get; } =
    [
        ("RANDOM_ALLOCATION_GUESSES_NUMBER", "75"),
        ("RANDOM_ALLOCATION_SOCKS_NUMBER", "250"),
    ];

    /// <summary>Whether both still hold the value this port was built against.</summary>
    public static bool TheConstantsAreStillTheseValues(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string name, string value) in Constants)
        {
            if (!core.Contains($"#define {name} {value}", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether the steady generator still makes eight guesses and offers ten candidates.</summary>
    public static bool TheSteadyRunIsStillEight(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("for(int i=0; i<8; i++)", StringComparison.Ordinal)
            && core.Contains("msg.conn_request->num_candidates = 10;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether it still starts one increment past the port that answered - the add comes BEFORE the
    /// candidate's port is set, which is what leaves the STUN port itself out.
    /// </summary>
    public static bool TheSteadyRunStillStartsOneAhead(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int add = core.IndexOf(
            "port_check += session->stun_allocation_increment;", StringComparison.Ordinal);
        int assign = core.IndexOf("candidate_stun2->port = port_check;", StringComparison.Ordinal);

        return add > 0 && assign > add;
    }

    /// <summary>Whether the two generators still wrap an overflow to different places.</summary>
    public static bool TheTwoOverflowsStillDisagree(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("port_check = port_check - UINT16_MAX + 1024;", StringComparison.Ordinal)
            && core.Contains($"port = {PortGuessing.EphemeralBase} + (port - UINT16_MAX - 1);", StringComparison.Ordinal);
    }

    /// <summary>Whether the steady generator still has two underflow rules.</summary>
    public static bool TheSteadyRunStillHasTwoUnderflows(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(port_check < 1024 && tmp > 1024)", StringComparison.Ordinal)
            && core.Contains("else if(port_check < 1)", StringComparison.Ordinal)
            && core.Contains("port_check += UINT16_MAX;", StringComparison.Ordinal);
    }

    /// <summary>Whether the spread is still centred, and still written out twice.</summary>
    public static bool TheSpreadIsStillCentredAndDuplicated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        const string body = "delta = (i + 1) / 2;";
        int first = core.IndexOf(body, StringComparison.Ordinal);
        if (first < 0)
            return false;

        // The same generator is written out in two branches - the forced path and the measured one.
        return core.IndexOf(body, first + 1, StringComparison.Ordinal) > first
            && core.Contains("delta = -(i / 2);", StringComparison.Ordinal);
    }
}
