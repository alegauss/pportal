using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>A console the user typed in by address.</summary>
public readonly record struct ManualConsole(string Address, string Mac, bool Registered);

/// <summary>A console PSN knows about, reachable only through the relay.</summary>
public readonly record struct PsnConsole(string Nickname, string Duid, bool IsPs5);

/// <summary>One row of the list, in the order the front door shows them.</summary>
/// <param name="Display">
/// Whether the row is SHOWN. Not whether it exists - the list keeps rows it does not display,
/// which is why MainView.qml's navigation skips invisible items rather than trusting the index.
/// </param>
public readonly record struct ConsoleRow(
    string Name, string Address, bool Discovered, bool Manual, bool Registered, bool Display);

/// <summary>
/// PP13: the console list, which is the front door and the first screen with real logic in it.
///
/// Three sources merged in one order - discovered, then manual, then PSN - and the merge is where
/// the mistakes are, because two of the three are suppressed by DIFFERENT mechanisms:
///
///   a manual host that has already been discovered is still in the list, with Display false;
///
///   a PSN host that has already been discovered is NOT in the list at all.
///
/// Flattening those into one rule changes behaviour either way. Make the manual one absent and it
/// vanishes the moment discovery stops answering, which is what a network hiccup looks like. Make
/// the PSN one present-and-hidden and the list grows entries the Qt client never had, which
/// nobody sees until something counts them.
///
/// The dedup keys differ too. A manual host is matched by MAC and address together; a PSN host is
/// matched by NICKNAME. That asymmetry is not tidiness waiting to happen - a PSN host has no MAC
/// to match on, and a console whose nickname changed will appear twice until discovery agrees.
///
/// And hidden discovered hosts stay in the list with Display false, for the same reason as the
/// manual ones: the list is a model, and hiding is a property of a row rather than of the set.
/// </summary>
public static class ConsoleList
{
    /// <summary>
    /// The nickname the Qt client adds to the discovered set once every registered PS4 has been
    /// seen, so a PSN entry for a generic PS4 stops being offered.
    ///
    /// A literal, and one the port cannot invent: PSN reports an unregistered PS4 under this
    /// name, so suppressing it depends on matching the string exactly.
    /// </summary>
    public const string MainPs4Nickname = "Main PS4 Console";

    /// <summary>
    /// Whether a reply describes a PS5, from the host-type string the console actually sent.
    ///
    /// Compared case-insensitively and against a prefix, because the field is free text on the
    /// wire and a port that matched "PS5" exactly would file a future spelling as a PS4.
    /// </summary>
    public static bool IsPs5(DiscoveredConsole host)
        => host.HostType?.StartsWith("PS5", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Builds the list. <paramref name="hiddenMacs"/> are the consoles the user hid, and
    /// <paramref name="registeredMacs"/> the ones already paired.
    /// </summary>
    public static IReadOnlyList<ConsoleRow> Build(
        IEnumerable<DiscoveredConsole> discovered,
        IEnumerable<ManualConsole> manual,
        IEnumerable<PsnConsole> psn,
        IReadOnlySet<string> hiddenMacs,
        IReadOnlySet<string> registeredMacs,
        int registeredPs4Count = 0)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(psn);
        ArgumentNullException.ThrowIfNull(hiddenMacs);
        ArgumentNullException.ThrowIfNull(registeredMacs);

        var rows = new List<ConsoleRow>();
        var discoveredNicknames = new HashSet<string>(StringComparer.Ordinal);
        var discoveredManual = new HashSet<ManualConsole>();
        var manualList = manual.ToList();
        int registeredDiscoveredPs4s = 0;

        foreach (DiscoveredConsole host in discovered)
        {
            // The reply's own fields, not a parallel type: Id is the host id the settings are
            // keyed by, and HostType is the string the console sent rather than a bool derived
            // once and carried around.
            string mac = host.Id ?? "";
            string nickname = host.Name ?? "";
            bool registered = registeredMacs.Contains(mac);

            // A registered host that was also hidden stops being hidden. Pairing with a console
            // is a statement that you want to see it, and the Qt client removes the hidden entry
            // rather than leaving the two settings to disagree.
            bool hidden = !registered && hiddenMacs.Contains(mac);

            bool isManual = manualList.Any(m =>
                m.Registered && m.Mac == mac && m.Address == host.Address);
            if (isManual)
                discoveredManual.Add(manualList.First(m =>
                    m.Registered && m.Mac == mac && m.Address == host.Address));

            rows.Add(new ConsoleRow(nickname, host.Address ?? "", Discovered: true,
                Manual: isManual, Registered: registered, Display: !hidden));

            discoveredNicknames.Add(nickname);
            if (!IsPs5(host) && registered)
                registeredDiscoveredPs4s++;
        }

        // Manual hosts are ALWAYS appended. Already discovered only makes them invisible.
        foreach (ManualConsole host in manualList)
        {
            bool registered = host.Registered && registeredMacs.Contains(host.Mac);
            rows.Add(new ConsoleRow(host.Address, host.Address, Discovered: false,
                Manual: true, Registered: registered, Display: !discoveredManual.Contains(host)));
        }

        // Once every registered PS4 has been discovered, the generic PSN name is treated as
        // already seen - otherwise a PS4 with no nickname of its own is offered twice.
        if (registeredPs4Count > 0 && registeredDiscoveredPs4s >= registeredPs4Count)
            discoveredNicknames.Add(MainPs4Nickname);

        // PSN hosts are SKIPPED, not hidden - the other suppression, matched by nickname.
        foreach (PsnConsole host in psn)
        {
            if (discoveredNicknames.Contains(host.Nickname))
                continue;

            rows.Add(new ConsoleRow(host.Nickname, "", Discovered: false,
                Manual: false, Registered: true, Display: true));
        }

        return rows;
    }
}

/// <summary>
/// PP13: the merge rules as qmlbackend.cpp states them, so the port cannot flatten them by
/// accident.
/// </summary>
public static partial class ConsoleListSource
{
    /// <summary>Where the list is assembled.</summary>
    public const string RelativePath = @"gui\src\qmlbackend.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether a discovered manual host is still hidden rather than dropped.</summary>
    public static bool ManualIsHiddenNotDropped(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains(
            @"m[""display""] = discovered_manual_hosts.contains(host) ? false : true;",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a discovered PSN host is still skipped rather than hidden.</summary>
    public static bool PsnIsSkippedNotHidden(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SkipRegex().IsMatch(text);
    }

    /// <summary>The generic PS4 nickname, read rather than remembered.</summary>
    public static string? MainPs4Nickname(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Match m = MainPs4Regex().Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"if\(discovered\)\s*\r?\n\s*continue;")]
    private static partial Regex SkipRegex();

    [GeneratedRegex(@"discovered_nicknames\.append\(QString\(""([^""]+)""\)\)")]
    private static partial Regex MainPs4Regex();
}

/// <summary>
/// PP13: the front door's view model - the rows, and whether any of them show.
///
/// A thin wrapper over <see cref="ConsoleList.Build"/> rather than a second merge. The rules are
/// there and asserted there; what this adds is the two things a screen binds to and cannot
/// compute in markup.
/// </summary>
public sealed class ConsoleListViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<ConsoleRow> rows = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every row, shown or not - the model keeps what it does not display.</summary>
    public IReadOnlyList<ConsoleRow> Rows
    {
        get => rows;
        private set
        {
            rows = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rows)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasVisibleRows)));
        }
    }

    /// <summary>
    /// Whether anything is on screen, which is NOT whether the list is empty.
    ///
    /// A list of nothing but hidden consoles shows nothing, and a screen that decided by count
    /// would leave a blank panel where the empty message belongs - which reads as broken rather
    /// than as quiet.
    /// </summary>
    public bool HasVisibleRows => Rows.Any(r => r.Display);

    /// <summary>Rebuilds from what discovery, the manual list and PSN currently say.</summary>
    public void Refresh(
        IEnumerable<DiscoveredConsole> discovered,
        IEnumerable<ManualConsole> manual,
        IEnumerable<PsnConsole> psn,
        IReadOnlySet<string> hiddenMacs,
        IReadOnlySet<string> registeredMacs,
        int registeredPs4Count = 0)
        => Rows = ConsoleList.Build(
            discovered, manual, psn, hiddenMacs, registeredMacs, registeredPs4Count);
}
