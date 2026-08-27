using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What arriving host did to the table.</summary>
public enum HostArrival
{
    /// <summary>No host id: refused, and nothing is touched.</summary>
    NoId,

    /// <summary>Not seen before, and there was room. Reported.</summary>
    Added,

    /// <summary>Not seen before, and the table is full. Dropped with a log.</summary>
    NoSpace,

    /// <summary>Seen before, and its state or request port moved. Reported.</summary>
    Changed,

    /// <summary>Seen before and unchanged: the ping index is refreshed and nobody is told.</summary>
    Refreshed,
}

/// <summary>One host the service is holding, and when it was last heard from.</summary>
/// <param name="HostId">What the table matches on.</param>
/// <param name="State">Ready, standby or unknown.</param>
/// <param name="RequestPort">The port the reply advertised.</param>
/// <param name="LastPingIndex">The ping this host last answered.</param>
public readonly record struct ServiceHost(
    string HostId, int State, ushort RequestPort, ulong LastPingIndex);

/// <summary>
/// PP29: the discovery SERVICE's own loop - the ping cadence, the host table and what
/// makes it tell anybody.
///
/// PP462 ported the socket and the thread that reads it; PP29's remainder after that was this file's
/// own loop, which nothing managed had a model of. It is the layer above: it owns a ping timer, a
/// table of hosts keyed by id, and a callback that fires only when the table CHANGES.
///
/// THE FIRST WAIT IS A DIFFERENT LENGTH FROM THE REST. The thread waits `ping_initial_ms`, then pings
/// and waits `ping_ms` from then on - so the first ping is not sent at start-up, it is sent one
/// initial interval in. A port that pinged immediately would find consoles sooner and would not be
/// this service.
///
/// AND THE LOOP LEAVES ON ANYTHING THAT IS NOT A TIMEOUT. `while(err == CHIAKI_ERR_TIMEOUT)` - so the
/// stop condition firing ends it, and so does any error from the wait, with the same silence. The
/// discovery thread failing to start ends it too, before the loop, and logs nothing at all.
///
/// A CHANGE IS THREE THINGS AND NOT FOUR. A host appearing, a host's state or request port moving, and
/// a host being dropped each report; a host answering again with everything the same refreshes its
/// ping index and tells nobody. That is what keeps a console list from redrawing every ping.
///
/// THE DROP TRAVERSAL USED TO SKIP THE SLOT IT JUST FILLED, AT INDEX 0 ONLY, AND PP464 FIXED IT.
/// Removing element i shifts the rest down and steps the index back so the `i++` lands on the same
/// slot; the step-back was written `if(i > 0) i--`, so at zero it did not happen and the host that
/// moved into slot 0 waited a whole ping cycle. The guard looked like it was avoiding an underflow
/// and there was none: `i` is a `size_t`, the decrement wraps to SIZE_MAX and the increment brings it
/// back, which every other index already relied on one step removed.
/// </summary>
public static class DiscoveryServiceLoop
{
    /// <summary>
    /// How long the thread waits before the ping numbered <paramref name="pingsSent"/>.
    ///
    /// The zeroth wait is the initial one; every wait after it is the regular interval.
    /// </summary>
    public static ulong IntervalFor(int pingsSent, ulong initialMs, ulong pingMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pingsSent);

        return pingsSent == 0 ? initialMs : pingMs;
    }

    /// <summary>Whether the loop goes round again after a wait that answered this.</summary>
    public static bool Continues(ChiakiError wait) => wait == ChiakiError.Timeout;

    /// <summary>
    /// Whether a host that last answered at <paramref name="lastPingIndex"/> is still held.
    ///
    /// Kept while `last + drop >= current`, which is the C's own comparison rather than its negation:
    /// at a drop count of zero a host is kept for the ping it answered and dropped on the next.
    /// </summary>
    public static bool IsHeld(ulong lastPingIndex, ulong pingIndex, ulong dropPings)
        => lastPingIndex + dropPings >= pingIndex;

    /// <summary>discoveryservice.c, where the loop lives.</summary>
    public const string RelativePath = @"lib\src\discoveryservice.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The service thread's body.</summary>
    public static string? ThreadBody(string source)
        => CFunction.Body(source, "static void *discovery_service_thread_func");

    /// <summary>The drop pass's body.</summary>
    public static string? DropBody(string source)
        => CFunction.Body(source, "static void discovery_service_drop_old_hosts");

    /// <summary>Whether the first wait still uses a different interval from the rest.</summary>
    public static bool TheFirstWaitStillDiffers(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int initial = threadBody.IndexOf(
            "chiaki_bool_pred_cond_timedwait(&service->stop_cond, service->options.ping_initial_ms)",
            StringComparison.Ordinal);
        int regular = threadBody.IndexOf(
            "chiaki_bool_pred_cond_timedwait(&service->stop_cond, service->options.ping_ms)",
            StringComparison.Ordinal);

        return initial >= 0 && regular > initial;
    }

    /// <summary>Whether the loop still runs only while the wait answers TIMEOUT.</summary>
    public static bool TheLoopStillRunsOnlyOnTimeout(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains("while(err == CHIAKI_ERR_TIMEOUT)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a discovery thread that fails to start still leaves without logging.
    ///
    /// The `goto beach` skips straight to the unlock, and nothing between the failed call and the label
    /// says anything - so the service simply stops, and the only evidence is that no ping ever goes.
    /// </summary>
    public static bool AFailedThreadStartStillSaysNothing(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int start = threadBody.IndexOf("chiaki_discovery_thread_start(", StringComparison.Ordinal);
        if (start < 0)
            return false;

        int label = threadBody.IndexOf("beach:", start, StringComparison.Ordinal);
        if (label < 0)
            return false;

        string between = threadBody[start..label];

        return between.Contains("goto beach;", StringComparison.Ordinal)
            && !between.Contains("CHIAKI_LOG", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP464: whether the drop traversal steps back unconditionally, so index 0 is treated like every
    /// other.
    ///
    /// Both halves: the decrement is there AND the guard is gone. Checking only for the decrement
    /// would stay green if the guard came back around it, which is the shape the defect had.
    /// </summary>
    public static bool TheDropStepsBackUnconditionally(string dropBody)
    {
        ArgumentNullException.ThrowIfNull(dropBody);

        string text = dropBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("i--;", StringComparison.Ordinal)
            && !text.Contains("if(i > 0)", StringComparison.Ordinal);
    }
}

/// <summary>
/// PP29: the service's table of hosts, keyed by id.
///
/// Mutable, because the C's is: a fixed array with a count, shifted down on a removal. The order that
/// produces is part of the behaviour - it is the order a console list is drawn in.
/// </summary>
public sealed class DiscoveryHostTable
{
    private readonly List<ServiceHost> hosts = [];

    /// <param name="capacity">options.hosts_max - the point at which a new host is refused.</param>
    public DiscoveryHostTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
    }

    /// <summary>How many hosts the table may hold.</summary>
    public int Capacity { get; }

    /// <summary>The hosts, in the order the C's array holds them.</summary>
    public IReadOnlyList<ServiceHost> Hosts => hosts;

    /// <summary>How many times a change has been reported.</summary>
    public int Reports { get; private set; }

    /// <summary>One host off the wire.</summary>
    public HostArrival Receive(string? hostId, int state, ushort requestPort, ulong pingIndex)
    {
        if (string.IsNullOrEmpty(hostId))
            return HostArrival.NoId;

        int index = hosts.FindIndex(h => string.Equals(h.HostId, hostId, StringComparison.Ordinal));

        if (index < 0)
        {
            if (hosts.Count == Capacity)
                return HostArrival.NoSpace;

            hosts.Add(new ServiceHost(hostId, state, requestPort, pingIndex));
            Reports++;
            return HostArrival.Added;
        }

        ServiceHost slot = hosts[index];
        bool changed = slot.State != state || slot.RequestPort != requestPort;

        hosts[index] = slot with
        {
            State = state,
            RequestPort = requestPort,
            LastPingIndex = pingIndex,
        };

        if (!changed)
            return HostArrival.Refreshed;

        Reports++;
        return HostArrival.Changed;
    }

    /// <summary>
    /// The drop pass, traversed as the C traverses it - including the step-back guard that skips
    /// whatever moves into slot 0.
    /// </summary>
    /// <returns>How many hosts were dropped.</returns>
    public int DropOldHosts(ulong pingIndex, ulong dropPings)
    {
        var dropped = 0;
        var change = false;

        for (var i = 0; i < hosts.Count; i++)
        {
            if (DiscoveryServiceLoop.IsHeld(hosts[i].LastPingIndex, pingIndex, dropPings))
                continue;

            hosts.RemoveAt(i);
            dropped++;
            change = true;

            // PP464: unconditional, so the increment lands back on the slot the shift just filled -
            // at every index including zero, which a guard here used to skip.
            i--;
        }

        if (change)
            Reports++;

        return dropped;
    }
}
