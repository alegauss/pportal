using System.Net;
using System.Net.Sockets;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>Why a row cannot be woken, or that it can.</summary>
public enum WakeRefusal
{
    /// <summary>It can.</summary>
    None,

    /// <summary>The row does not offer the action - it is discovered, or it is a PSN entry.</summary>
    NotOffered,

    /// <summary>Offered, and the console is not paired, so there is no credential to send.</summary>
    NotRegistered,

    /// <summary>Paired, and the store holds no usable registration under this row.</summary>
    NoRegistration,

    /// <summary>The registration key is not a wake credential - the console needs re-pairing.</summary>
    NoCredential,
}

/// <summary>A magic packet, addressed.</summary>
/// <param name="Address">Where to send it.</param>
/// <param name="Ps5">Which port and which protocol version.</param>
/// <param name="Credential">The registration key read as a hexadecimal number.</param>
public readonly record struct WakeRequest(string Address, bool Ps5, ulong Credential);

/// <summary>What waking a row would take, or why it would not.</summary>
public readonly record struct WakePlan(WakeRefusal Refusal, WakeRequest? Request);

/// <summary>
/// PP626: the row's other three actions, which PP13 modelled and no screen performed.
///
/// <see cref="ConsoleActions"/> has answered which of connect, wake and remove a row offers since
/// PP13, and it performs none of them. PP600 wired the first; these are the rest.
///
/// WAKING KEEPS THE CLIENT'S OWN TWO RULES, which disagree on purpose.
/// <see cref="ConsoleActions.CanWake"/> is what the SCREEN offers - not discovered, no DUID -
/// because a discovered console is awake and a PSN one is reached through the relay.
/// <see cref="ConsoleActions.WakeWouldBeSent"/> is what the BACKEND would do, and it refuses a
/// console with no registration, because a magic packet carries the registration key and there is
/// nothing to put in it. Offered-and-refused is a real state and it is the one worth a sentence.
///
/// The DUID is derived rather than carried. A <see cref="ConsoleRow"/> has no field for it, and it
/// does not need one: PSN entries are the rows that are neither discovered nor manual, which is how
/// <see cref="ConsoleList.Build"/> makes them.
/// </summary>
public static class ConsoleRowActions
{
    /// <summary>The row as the action rules see it, with the DUID derived from its shape.</summary>
    public static ConsoleActionState StateOf(ConsoleRow row)
        => new(row.Discovered, row.Manual, row.Registered,
            // Neither discovered nor manual is a PSN entry, and a PSN entry is exactly the case the
            // DUID stands for in the client's rule.
            !row.Discovered && !row.Manual ? "psn" : null);

    /// <summary>Whether the screen offers the wake at all, which is what its button binds to.</summary>
    public static bool CanWake(ConsoleRow row) => ConsoleActions.CanWake(StateOf(row));

    /// <summary>Which removal the row offers, which is what its second button is labelled.</summary>
    public static RemoveAction RemovalFor(ConsoleRow row)
        => ConsoleActions.RemovalFor(StateOf(row));

    /// <summary>
    /// What the removal's button says. `None` is a word and not an empty one: PP13 records that the
    /// entry is THERE and does nothing, and a button that vanished would be the port filling in the
    /// branch the client deliberately leaves empty.
    /// </summary>
    public static string RemovalLabel(RemoveAction action) => action switch
    {
        RemoveAction.Delete => "Delete",
        RemoveAction.Hide => "Hide",
        _ => "Remove",
    };

    /// <summary>What waking this row would take, or why it would not.</summary>
    public static WakePlan PrepareWake(ConsoleRow row, IReadOnlyList<RegisteredHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        if (!CanWake(row))
            return new(WakeRefusal.NotOffered, null);

        // The screen's rule and the backend's, kept apart: this one is why an offered wake can
        // still not be sent, and it is a different sentence.
        if (!ConsoleActions.WakeWouldBeSent(StateOf(row)))
            return new(WakeRefusal.NotRegistered, null);

        if (ConsoleConnect.RegistrationFor(row, hosts) is not { } host
            || host.RpRegistKey is not { Length: > 0 } key)
        {
            return new(WakeRefusal.NoRegistration, null);
        }

        if (!ExchangeCapture.TryWakeCredential(key, out ulong credential))
            return new(WakeRefusal.NoCredential, null);

        return new(
            WakeRefusal.None,
            new WakeRequest(row.Address, host.Target >= ConsoleConnect.PsnTarget, credential));
    }

    /// <summary>What to put on the screen for a wake that will not happen.</summary>
    public static string Explain(WakeRefusal refusal) => refusal switch
    {
        WakeRefusal.None => "",
        WakeRefusal.NotOffered => "This console does not need waking.",
        WakeRefusal.NotRegistered => "Waking sends the registration key, and this console has none.",
        WakeRefusal.NoRegistration => "No usable registration for this console - register it again.",
        WakeRefusal.NoCredential => "The registration key is not a wake credential - register it again.",
        _ => "This console cannot be woken.",
    };
}

/// <summary>Where a removal becomes a write.</summary>
public interface IConsoleRemover
{
    /// <summary>Removes the row the way the action says, and whether anything changed.</summary>
    bool Remove(ConsoleRow row, RemoveAction action);
}

/// <summary>
/// The real one: the Qt client's own two lists, rewritten.
///
/// Both are read-modify-write of a whole array, which is what
/// <see cref="QSettingsWriter.ReplaceArray"/> exists for: QSettings reads entries 1..size, so
/// deleting the middle one of three leaves a hole its own reader walks into.
///
/// An entry that is not there is not an error. Two screens can be open on one store and discovery
/// can rebuild a row between the click and the write, so a removal that finds nothing has already
/// happened - and saying so is more useful than a failure about a console that is gone.
/// </summary>
public sealed class QSettingsConsoleRemover(QSettingsStore store) : IConsoleRemover
{
    /// <inheritdoc />
    public bool Remove(ConsoleRow row, RemoveAction action)
    {
        ArgumentNullException.ThrowIfNull(store);

        return action switch
        {
            RemoveAction.Delete => Delete(row),
            RemoveAction.Hide => Hide(row),
            _ => false,
        };
    }

    /// <summary>
    /// A manual console, which exists only because the user typed it in - so the entry IS it.
    ///
    /// Matched on the address, which is what a manual row's name is and the only thing the user
    /// gave. An entry with the same address twice would go together, which is what removing it
    /// means: they are one console typed in twice.
    /// </summary>
    private bool Delete(ConsoleRow row)
    {
        IReadOnlyList<ManualHost> manual = store.ManualHosts();

        ManualHost[] kept =
        [
            .. manual.Where(one =>
                !string.Equals(one.Host, row.Address, StringComparison.OrdinalIgnoreCase))
        ];

        if (kept.Length == manual.Count)
            return false;

        store.WriteManualHosts(kept);
        return true;
    }

    /// <summary>
    /// A discovered console, which deleting would not remove - it answers the next sweep.
    ///
    /// Keyed on the identity and not on the name, for PP624's reason: the name is a caption and two
    /// consoles can share it. A row with no readable identity cannot be hidden at all, which is the
    /// honest answer rather than an entry that hides nothing.
    /// </summary>
    private bool Hide(ConsoleRow row)
    {
        if (HostId.ToBytes(row.Mac) is not { } mac)
            return false;

        IReadOnlyList<HiddenHost> hidden = store.HiddenHosts();
        if (hidden.Any(one => string.Equals(
                HostId.Key(one.ServerMac), row.Mac, StringComparison.Ordinal)))
        {
            return false;
        }

        store.WriteHiddenHosts([.. hidden, new HiddenHost(row.Name, mac)]);
        return true;
    }
}

/// <summary>Where a wake request becomes a datagram.</summary>
public interface IConsoleWaker
{
    /// <summary>Sends it, and says nothing about the answer - there is not one.</summary>
    void Wake(WakeRequest request);
}

/// <summary>
/// The real one: one datagram, fire and forget.
///
/// A wake packet is UDP and unacknowledged, which is why <see cref="ExchangeCapture"/> follows one
/// with a second discovery sweep rather than a return value. Nothing here waits: the list is already
/// sweeping, so the console appearing as Ready IS the answer, and it arrives on its own.
/// </summary>
public sealed class NativeConsoleWaker : IConsoleWaker
{
    /// <inheritdoc />
    public void Wake(WakeRequest request)
    {
        if (!IPAddress.TryParse(request.Address, out IPAddress? to))
            return;

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            byte[] packet = Discovery.Packet(DiscoveryCommand.Wakeup, request.Ps5, request.Credential);
            udp.Send(packet, packet.Length, new IPEndPoint(to, Discovery.Port(request.Ps5)));
        }
        catch (SocketException)
        {
            // Nothing to report. The packet is unacknowledged either way, and a console that does
            // not wake says so by staying absent from the sweep that is already running.
        }
    }
}
