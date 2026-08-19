using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP13: the front door's list, and the merge that decides what is on it.
///
/// PP37 names "a console list that keeps a stale entry after a failed refresh" as one of the four
/// things a view model can assert and a window cannot. This is that list, and the interesting
/// part is that keeping an entry is sometimes CORRECT - a manual host stays when discovery stops
/// answering, and a PSN one does not.
/// </summary>
public class ConsoleListTests
{
    private static readonly HashSet<string> None = new(StringComparer.Ordinal);

    /// <summary>
    /// A reply as discovery would hand it over - the real type, with the fields the datagram
    /// carries, rather than a parallel one shaped to suit the list.
    /// </summary>
    private static DiscoveredConsole Found(string mac, string nick, string addr = "10.0.0.5", bool ps5 = true)
        => new(addr, "1.0", "00030010", nick, ps5 ? "PS5" : "PS4", mac, null, null,
            DiscoveryHostState.Ready, 9295);

    [Fact]
    public void AnEmptyNetworkIsAnEmptyList()
        => Assert.Empty(ConsoleList.Build([], [], [], None, None, 0));

    [Fact]
    public void DiscoveredConsolesComeFirstAndAreShown()
    {
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Living room")], [], [], None, None, 0);

        ConsoleRow row = Assert.Single(rows);
        Assert.Equal("Living room", row.Name);
        Assert.True(row.Discovered);
        Assert.True(row.Display);
    }

    /// <summary>
    /// A hidden console stays IN the list and is not shown. The list is a model and hiding is a
    /// property of a row - which is why MainView.qml's navigation skips invisible items rather
    /// than trusting the index, and why filtering the list instead would renumber everything.
    /// </summary>
    [Fact]
    public void AHiddenConsoleStaysInTheListUnshown()
    {
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Bedroom")], [], [], new HashSet<string> { "AA" }, None, 0);

        ConsoleRow row = Assert.Single(rows);
        Assert.False(row.Display);
    }

    /// <summary>
    /// Registering a console un-hides it. Pairing is a statement that you want to see it, so the
    /// two settings are not left to disagree.
    /// </summary>
    [Fact]
    public void RegisteringAConsoleUnhidesIt()
    {
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Bedroom")], [], [],
            new HashSet<string> { "AA" }, new HashSet<string> { "AA" }, 0);

        Assert.True(Assert.Single(rows).Display);
        Assert.True(rows[0].Registered);
    }

    /// <summary>
    /// The asymmetry, first half: a manual host already discovered is STILL in the list, merely
    /// unshown. Drop it instead and it vanishes the moment discovery stops answering - which is
    /// what a network hiccup looks like, and is exactly the stale-entry failure in reverse.
    /// </summary>
    [Fact]
    public void ADiscoveredManualHostIsHiddenRatherThanDropped()
    {
        var manual = new ManualConsole("10.0.0.5", "AA", Registered: true);

        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Living room")], [manual], [], None, new HashSet<string> { "AA" }, 0);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Display);      // the discovered row
        Assert.False(rows[1].Display);     // the manual one, present and unshown
        Assert.True(rows[1].Manual);
    }

    /// <summary>And when discovery stops answering, the manual row shows again on its own.</summary>
    [Fact]
    public void TheManualHostReappearsWhenDiscoveryGoesQuiet()
    {
        var manual = new ManualConsole("10.0.0.5", "AA", Registered: true);

        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [], [manual], [], None, new HashSet<string> { "AA" }, 0);

        Assert.True(Assert.Single(rows).Display);
    }

    /// <summary>
    /// The asymmetry, second half: a PSN host already discovered is ABSENT. Hiding it instead
    /// would grow the list entries the Qt client never had - which nobody sees until something
    /// counts them.
    /// </summary>
    [Fact]
    public void ADiscoveredPsnHostIsAbsentRatherThanHidden()
    {
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Living room")], [], [new PsnConsole("Living room", "duid", true)],
            None, None, 0);

        Assert.Single(rows);
        Assert.True(rows[0].Discovered);
    }

    /// <summary>
    /// PSN hosts are matched by NICKNAME, not by MAC - a PSN host has no MAC to match on. So a
    /// console whose nickname differs appears twice, and that is the client's behaviour rather
    /// than a defect the port should quietly repair.
    /// </summary>
    [Fact]
    public void APsnHostUnderAnotherNicknameAppearsTwice()
    {
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [Found("AA", "Living room")], [], [new PsnConsole("Lounge", "duid", true)],
            None, None, 0);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Lounge", rows[1].Name);
    }

    /// <summary>
    /// Once every registered PS4 has been discovered, PSN's generic name for one is treated as
    /// already seen - otherwise a PS4 with no nickname of its own is offered twice.
    /// </summary>
    [Fact]
    public void TheGenericPs4NameIsSuppressedOnceAllAreDiscovered()
    {
        var psn = new PsnConsole(ConsoleList.MainPs4Nickname, "duid", false);

        IReadOnlyList<ConsoleRow> withAll = ConsoleList.Build(
            [Found("AA", "Den", ps5: false)], [], [psn], None, new HashSet<string> { "AA" }, 1);
        Assert.Single(withAll);

        // And is NOT suppressed while one is still missing.
        IReadOnlyList<ConsoleRow> withOneMissing = ConsoleList.Build(
            [Found("AA", "Den", ps5: false)], [], [psn], None, new HashSet<string> { "AA" }, 2);
        Assert.Equal(2, withOneMissing.Count);
    }

    /// <summary>Both suppression rules are still the Qt client's, and they are still different.</summary>
    [Fact]
    public void TheTwoSuppressionsAreStillTheQtClients()
    {
        string? file = ConsoleListSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(ConsoleListSource.ManualIsHiddenNotDropped(text), "manual hidden, not dropped");
        Assert.True(ConsoleListSource.PsnIsSkippedNotHidden(text), "psn skipped, not hidden");
        Assert.Equal(ConsoleList.MainPs4Nickname, ConsoleListSource.MainPs4Nickname(text));
    }
}
