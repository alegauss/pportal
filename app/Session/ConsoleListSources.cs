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
/// PP600 JOINED BY NICKNAME AND PP624 DOES NOT. Build asks whether a discovered host's
/// <see cref="DiscoveredConsole.Id"/> is in the registered set, and PP600 could not make the two
/// spellings meet, so it handed Build the ids of the discovered consoles whose NAME the store had a
/// registration for - and passed an empty hidden set, because hiding has no such fallback.
///
/// The Qt client's own answer settles it: `DiscoveryHost::GetHostMAC` parses the host-id from hex
/// and refuses anything that is not six bytes, and `HostMAC::ToString` is `toHex()`. So the key is
/// twelve lower-case hexadecimal characters, <see cref="HostId"/> is where that lives, and both
/// sets are built from the bytes the store actually holds.
/// </summary>
public static class ConsoleListSources
{
    /// <summary>The consoles the store has a registration for, as the keys Build tests against.</summary>
    public static IReadOnlySet<string> RegisteredMacs(IReadOnlyList<RegisteredHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        return Keys(hosts.Select(one => one.ServerMac));
    }

    /// <summary>
    /// The consoles the user hid, which PP600 had no way to key and passed as nothing.
    ///
    /// <see cref="ConsoleActions.RemovalFor"/> models three outcomes and Hide is one of them, so a
    /// port that could not read this set had a third of that rule unreachable from any screen.
    /// </summary>
    public static IReadOnlySet<string> HiddenMacs(IReadOnlyList<HiddenHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        return Keys(hosts.Select(one => one.ServerMac));
    }

    /// <summary>
    /// The readable identities among some stored MACs.
    ///
    /// An entry whose bytes are not six is dropped rather than keyed on what it has: the Qt client
    /// refuses the same shape on the way in, and a half-read identity that matched another
    /// half-read one would hide or register the wrong console.
    /// </summary>
    private static IReadOnlySet<string> Keys(IEnumerable<byte[]> macs)
        => new HashSet<string>(
            macs.Select(HostId.Key).OfType<string>(), StringComparer.Ordinal);

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
                    one.Host!, HostId.Key(one.RegisteredMac) ?? "", one.Registered))
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
