using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP629: who owns the settings store when two applications are editing it.
///
/// PP2 read the Qt client's settings; PP626 wrote them. That is a different relationship with the
/// same registry tree, and the client does not know this port is there.
///
/// THE CLIENT'S WRITES ARE NOT INCREMENTAL. `Settings::SaveHiddenHosts` does
/// `settings->remove("hidden_hosts")` and then `beginWriteArray` over the list it holds in memory -
/// the same shape as `SaveRegisteredHosts` and `SaveManualHosts`. So a client that was already
/// running when this port hid a console writes its startup list back over the change on its next
/// save. Nothing errors; the console is simply visible again.
///
/// THE LOSS IS ONE-WAY. This port re-reads the store on every rebuild, so a change the client makes
/// arrives here on its own. A change made here survives only until the client next saves that
/// array - which is the direction that looks like the port not working.
///
/// SO THE DECISION IS: THE CLIENT OWNS IT, AND THE PERSON IS TOLD. Locking is not available -
/// QSettings has no lock this port could take and the client would not take one either - and
/// re-reading before the write narrows nothing, because the client's save is not a
/// read-modify-write at all. What IS available is the one fact that decides the outcome: whether a
/// client is running at the moment of the write. That is checkable, and it is the difference
/// between a change that will stick and one that will not.
/// </summary>
public static class StoreOwnership
{
    /// <summary>
    /// The client's process name, derived from the binary this port already names.
    ///
    /// <see cref="GuiFreshness.ClientRelativePath"/> is what `compile.cmd gui` builds and what the
    /// freshness check reads, so the name comes from there rather than being typed again.
    /// </summary>
    public static string BuiltClientProcess { get; } =
        Path.GetFileNameWithoutExtension(GuiFreshness.ClientRelativePath);

    /// <summary>
    /// The released client's name, which is not the one this tree builds.
    ///
    /// Somebody running this port is very likely running upstream's published build beside it -
    /// that is where their registrations came from - and it is installed under its own name rather
    /// than the one a local build produces.
    /// </summary>
    public const string ReleasedClientProcess = "chiaki-ng";

    /// <summary>Both names, which is what a check has to look for.</summary>
    public static IReadOnlyList<string> ClientProcesses { get; } =
        [BuiltClientProcess, ReleasedClientProcess];

    /// <summary>
    /// Whether one of them is among the processes named.
    ///
    /// Takes the names rather than reading them, so the rule can be asserted without a client on
    /// the machine - and compared without case, because a process name is not one.
    /// </summary>
    public static bool ClientIsRunning(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);

        return processNames.Any(one =>
            ClientProcesses.Contains(one, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The processes this machine is running, by name.</summary>
    public static IReadOnlyList<string> RunningProcesses()
    {
        try
        {
            return [.. System.Diagnostics.Process.GetProcesses().Select(one => one.ProcessName)];
        }
        catch (InvalidOperationException)
        {
            // A process that ended between the enumeration and the read. Nothing here is worth
            // failing a removal over, and an empty answer is the quiet side: no warning rather than
            // a warning about a client that may not be there.
            return [];
        }
    }

    /// <summary>
    /// What to add to a status line when a change was written while a client is running.
    ///
    /// A sentence and not a refusal. The write already happened and is correct; what the person
    /// needs to know is that somebody else is holding an older copy of the same list, and what to
    /// do about it - which is to close the other one.
    /// </summary>
    public const string Warning =
        " The Chiaki client is open and will write its own list back - close it first.";
}
