using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP600: where the console list's three inputs come from, which nothing had ever assembled.
///
/// <see cref="ConsoleList.Build"/> has taken discovered hosts, manual hosts, PSN hosts and two sets
/// of keys since PP13, and every caller of it has been a test. The rules were asserted and the
/// wiring was not written, so this is the wiring - and it is a separate file because one of the
/// joins is a decision rather than a conversion.
///
/// THE JOIN IS BY NICKNAME AND NOT BY MAC, which is the decision. Build asks whether a discovered
/// host's <see cref="DiscoveredConsole.Id"/> is in the registered set, and that id is the reply's
/// `host-id` - bare hexadecimal, eight bytes on the console this port was read against.
/// <see cref="RegisteredHost.MacText"/> is six bytes with colons. They are not the same string and
/// no amount of care in the caller makes them one, so the set handed to Build is built the other
/// way round: the ids OF the discovered consoles whose nickname the store has a registration for.
///
/// That is the join <see cref="ExchangeCapture"/> already makes and the only one available here.
/// What it costs is two consoles with the same nickname, which the Qt client has the same problem
/// with; what it buys is a Connect button that is enabled on a console that really is paired,
/// instead of one disabled on every console because two spellings never met.
/// </summary>
public static class ConsoleListSources
{
    /// <summary>
    /// The discovered consoles whose nickname the store holds a registration for, as the ids Build
    /// tests against.
    /// </summary>
    public static IReadOnlySet<string> RegisteredIds(
        IEnumerable<DiscoveredConsole> discovered, IReadOnlyList<RegisteredHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(hosts);

        return new HashSet<string>(
            discovered
                .Where(one => one.Id is not null
                    && hosts.Any(host => string.Equals(
                        host.ServerNickname, one.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(one => one.Id!),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The manual hosts the store carries, in the shape the list merges.
    ///
    /// A manual entry with no address is dropped rather than drawn as a blank row: the address IS
    /// the name for these, so an entry without one has nothing to show and nothing to reach.
    /// </summary>
    public static IReadOnlyList<ManualConsole> Manual(IReadOnlyList<ManualHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        return
        [
            .. hosts.Where(one => !string.IsNullOrWhiteSpace(one.Host))
                .Select(one => new ManualConsole(
                    one.Host!,
                    one.RegisteredMac is { Length: > 0 } mac
                        ? string.Join(':', mac.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)))
                        : "",
                    one.Registered))
        ];
    }

    /// <summary>
    /// How long the front door looks before it has anything to show.
    ///
    /// Shorter than the capture's eight seconds on purpose: that one is a script that can afford to
    /// wait, and this is a person watching a list. Consoles keep arriving after it - the sweep does
    /// not stop - so this is only how long the empty message stays up in the ordinary case.
    /// </summary>
    public static TimeSpan FirstAnswer { get; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// PP600: discovery as the front door needs it - every console on every subnet, as they answer.
///
/// <see cref="ExchangeCapture"/> has had this since PP297 and has it privately and for one console:
/// it looks for a NAME and stops. A list wants the set, kept, changing while somebody looks at it.
///
/// One service per broadcast address, for the reason the capture gives: Windows sends a limited
/// broadcast out one interface, and a machine with a VPN or a Hyper-V switch picks the wrong one.
/// A service that cannot be created on an address is skipped rather than fatal - that is an
/// interface this machine cannot broadcast on, and the others still answer.
/// </summary>
public sealed class ConsoleDiscovery : IDisposable
{
    private readonly List<DiscoveryService> services = [];
    private readonly Dictionary<string, DiscoveredConsole> seen = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>Starts sweeping, and calls back with the whole set every time it changes.</summary>
    public ConsoleDiscovery(Action<IReadOnlyList<DiscoveredConsole>> changed, ChiakiLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(changed);

        foreach (IPAddress broadcast in ExchangeCapture.Broadcasts())
        {
            try
            {
                services.Add(new DiscoveryService(
                    broadcast.ToString(),
                    consoles => Offer(consoles, changed),
                    pingMs: 1000,
                    log: log));
            }
            catch (InvalidOperationException)
            {
                // An interface that will not carry a broadcast socket. The others still answer, and
                // a front door that refused to open because one adapter is odd is worse than a list.
            }
        }
    }

    /// <summary>Whether any interface answered at all, which is the difference between quiet and broken.</summary>
    public bool Sweeping => services.Count > 0;

    private void Offer(
        IReadOnlyList<DiscoveredConsole> consoles, Action<IReadOnlyList<DiscoveredConsole>> changed)
    {
        List<DiscoveredConsole>? snapshot = null;

        lock (gate)
        {
            var moved = false;
            foreach (DiscoveredConsole console in consoles)
            {
                // Keyed by ADDRESS and not by id: a console in standby answers with fewer fields,
                // and the reply that arrives when it wakes is the same console with more of them.
                if (console.Address is not { Length: > 0 } address)
                    continue;

                if (!seen.TryGetValue(address, out DiscoveredConsole already) || already != console)
                {
                    seen[address] = console;
                    moved = true;
                }
            }

            if (moved)
                snapshot = [.. seen.Values];
        }

        if (snapshot is not null)
            changed(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (DiscoveryService service in services)
            service.Dispose();

        services.Clear();
    }
}
