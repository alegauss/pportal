using ChiakiNg.Session;
using ChiakiNg.Settings;
using Microsoft.Win32;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP626: the row's other three actions, which PP13 modelled and no screen performed.
///
/// PP600 wired Connect and left the wake and the two removals as a model with no caller. What is
/// asserted here is that each of them now has one AND that the rules are still the client's - the
/// wake's two rules that disagree on purpose, and the removal whose third outcome is silence.
///
/// The store half is round-tripped through a registry key of this test's own, because a writer
/// nothing reads back is a writer that can be wrong in a spelling only the Qt client would notice.
/// </summary>
public class ConsoleRowActionsTests : IDisposable
{
    /// <summary>
    /// A store nobody else has. NOT the Qt client's key: these tests write, and what is under
    /// HKCU\SOFTWARE\Chiaki is somebody's real registrations.
    /// </summary>
    private readonly string keyPath =
        $@"SOFTWARE\ChiakiNgTests\{Guid.NewGuid():N}";

    private QSettingsStore Store() => new(keyPath);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        GC.SuppressFinalize(this);
    }

    private static RegisteredHost Registration(string nickname, string key = "3e91107c")
        => new()
        {
            ServerNickname = nickname,
            ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29],
            Target = 1_000_100,
            RpRegistKey = System.Text.Encoding.ASCII.GetBytes(key),
            RpKey = [.. Enumerable.Repeat((byte)7, 16)],
        };

    private static ConsoleRow Manual(string address, bool registered = true)
        => new(address, address, Discovered: false, Manual: true, Registered: registered, Display: true)
        {
            Mac = "90474882fc29",
        };

    private sealed class FakeWaker : IConsoleWaker
    {
        public WakeRequest? Sent { get; private set; }

        public void Wake(WakeRequest request) => Sent = request;
    }

    /// <summary>
    /// PP626: the screen offers a wake only where the client offers one.
    ///
    /// Not a discovered console - it is awake - and not a PSN one, which is reached through the
    /// relay. The DUID the client's rule turns on is derived from the row's shape rather than
    /// carried: a PSN entry is the row that is neither discovered nor manual.
    /// </summary>
    [Fact]
    public void OnlyAConsoleWithNoOtherWayInOffersAWake()
    {
        Assert.True(ConsoleRowActions.CanWake(Manual("10.0.0.9")));

        // Discovered: already awake.
        Assert.False(ConsoleRowActions.CanWake(
            new ConsoleRow("Living room", "10.0.0.5", true, false, true, true)));

        // Neither discovered nor manual is a PSN entry, which has a DUID and a relay.
        Assert.False(ConsoleRowActions.CanWake(
            new ConsoleRow("Bedroom", "", false, false, true, true)));
    }

    /// <summary>
    /// PP626: and the two rules that disagree on purpose stay apart.
    ///
    /// The screen offers the wake for an unregistered manual console; the backend would send
    /// nothing, because a magic packet carries the registration key and there is none. That is a
    /// real state and it is the one worth a sentence - a button that was simply disabled would tell
    /// somebody nothing about why.
    /// </summary>
    [Fact]
    public void AnOfferedWakeCanStillNotBeSent()
    {
        ConsoleRow unpaired = Manual("10.0.0.9", registered: false);

        Assert.True(ConsoleRowActions.CanWake(unpaired));
        Assert.Equal(
            WakeRefusal.NotRegistered,
            ConsoleRowActions.PrepareWake(unpaired, []).Refusal);

        Assert.NotEqual(
            ConsoleRowActions.Explain(WakeRefusal.NotRegistered),
            ConsoleRowActions.Explain(WakeRefusal.NotOffered));
    }

    /// <summary>
    /// PP626: the credential is the registration key read as a hexadecimal NUMBER.
    ///
    /// discoverymanager.cpp truncates at the first NUL, reads the rest as base 16 and refuses more
    /// than eight characters. A key that is not one of those is not a wake credential, and the
    /// packet it would build wakes nothing and reports nothing - fire-and-forget UDP.
    /// </summary>
    [Fact]
    public void TheCredentialIsTheKeyAsANumber()
    {
        WakePlan plan = ConsoleRowActions.PrepareWake(
            Manual("10.0.0.9"), [Registration("10.0.0.9")]);

        Assert.Equal(WakeRefusal.None, plan.Refusal);
        Assert.Equal(0x3e91107cUL, plan.Request!.Value.Credential);
        Assert.Equal("10.0.0.9", plan.Request.Value.Address);
        Assert.True(plan.Request.Value.Ps5);

        // A key that is not eight hexadecimal characters is not a credential.
        Assert.Equal(
            WakeRefusal.NoCredential,
            ConsoleRowActions.PrepareWake(
                Manual("10.0.0.9"), [Registration("10.0.0.9", "nothexadec")]).Refusal);
    }

    /// <summary>PP626: and the packet reaches the seam, which is all a wake can be told about.</summary>
    [Fact]
    public void TheWakeReachesTheSeam()
    {
        var waker = new FakeWaker();
        var model = new ConsoleListViewModel(
            null, () => [Registration("10.0.0.9")], null, waker, null);

        Assert.Equal(WakeRefusal.None, model.Wake(Manual("10.0.0.9")));

        Assert.Equal(0x3e91107cUL, waker.Sent!.Value.Credential);
        Assert.Contains("Woke", model.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP626: THE THIRD OUTCOME IS SILENCE, and it is kept.
    ///
    /// A discovered console that IS registered offers neither Delete nor Hide. The entry is there
    /// and does nothing, and filling that branch in loses the user their registration - so the
    /// label is still a word, the click still lands, and no write happens.
    /// </summary>
    [Fact]
    public void ARegisteredConsoleOnTheNetworkIsRemovedByNothing()
    {
        var row = new ConsoleRow("Living room", "10.0.0.5", true, false, true, true)
        {
            Mac = "90474882fc29",
        };

        Assert.Equal(RemoveAction.None, ConsoleRowActions.RemovalFor(row));
        Assert.Equal("Remove", row.RemoveLabel);

        var model = new ConsoleListViewModel(null, static () => [], null, null, new RefusingRemover());

        Assert.Equal(RemoveAction.None, model.Remove(row));
        Assert.Contains("cannot be removed", model.Status, StringComparison.Ordinal);
    }

    private sealed class RefusingRemover : IConsoleRemover
    {
        public bool Remove(ConsoleRow row, RemoveAction action)
            => throw new InvalidOperationException("the silent branch must not write");
    }

    /// <summary>
    /// PP626: a manual console is DELETED, and the store says so when it is read back.
    ///
    /// The round trip is the assertion. This port has read the Qt client's settings since PP2 and
    /// never written one, so the encoding going out is the half nothing has ever checked - and a
    /// value in a spelling Qt does not recognise is a console list that comes back wrong on
    /// somebody's next launch rather than an error anybody sees.
    /// </summary>
    [Fact]
    public void DeletingAManualConsoleLeavesTheOthers()
    {
        QSettingsStore store = Store();
        store.WriteManualHosts(
        [
            new ManualHost(1, "10.0.0.9", true, [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29]),
            new ManualHost(2, "10.0.0.10", false, null),
        ]);

        Assert.Equal(2, store.ManualHosts().Count);

        Assert.True(new QSettingsConsoleRemover(store).Remove(Manual("10.0.0.9"), RemoveAction.Delete));

        ManualHost left = Assert.Single(store.ManualHosts());
        Assert.Equal("10.0.0.10", left.Host);

        // The id is the one it was written with, not its new position: the Qt client picks the next
        // id from these, and renumbering would collide with a console remembered elsewhere.
        Assert.Equal(2, left.Id);
    }

    /// <summary>
    /// PP626: a discovered console is HIDDEN, because deleting one does not remove it.
    ///
    /// It answers the next discovery sweep whatever the store says, so hiding is the only removal
    /// that means anything - and it is keyed on the identity rather than the name, for PP624's
    /// reason: two consoles can share a nickname.
    /// </summary>
    [Fact]
    public void HidingADiscoveredConsoleIsKeyedOnItsIdentity()
    {
        QSettingsStore store = Store();
        var remover = new QSettingsConsoleRemover(store);

        var row = new ConsoleRow("Living room", "10.0.0.5", true, false, false, true)
        {
            Mac = "90474882fc29",
        };

        Assert.Equal(RemoveAction.Hide, ConsoleRowActions.RemovalFor(row));
        Assert.True(remover.Remove(row, RemoveAction.Hide));

        HiddenHost hidden = Assert.Single(store.HiddenHosts());
        Assert.Equal("Living room", hidden.ServerNickname);
        Assert.Equal("90474882fc29", HostId.Key(hidden.ServerMac));

        // And it is not hidden twice, which would grow the array on every click.
        Assert.False(remover.Remove(row, RemoveAction.Hide));
        Assert.Single(store.HiddenHosts());
    }

    /// <summary>
    /// PP626: the hidden set the list is built from is the one that was just written.
    ///
    /// The join the whole removal rests on: the row disappears because Build reads the store, which
    /// is what PP624 made possible by keying both sides the same way.
    /// </summary>
    [Fact]
    public void AHiddenConsoleStopsBeingDrawn()
    {
        QSettingsStore store = Store();
        var row = new ConsoleRow("Living room", "10.0.0.5", true, false, false, true)
        {
            Mac = "90474882fc29",
        };

        new QSettingsConsoleRemover(store).Remove(row, RemoveAction.Hide);

        DiscoveredConsole reply = new(
            "10.0.0.5", "1.0", "00030010", "Living room", "PS5", "90474882FC29", null, null,
            DiscoveryHostState.Ready, 9295);

        ConsoleRow drawn = Assert.Single(ConsoleList.Build(
            [reply], [], [], ConsoleListSources.HiddenMacs(store.HiddenHosts()), new HashSet<string>()));

        Assert.False(drawn.Display);
    }

    /// <summary>
    /// PP626: the writer's encodings are the ones the reader already documents.
    ///
    /// Round-tripped rather than compared against a literal, because what has to hold is that
    /// <see cref="QSettingsValue"/> reads back what was handed in - the rules being the same rules,
    /// run the other way. The three cases are the three that lose something silently: a payload
    /// whose last byte is `)`, one containing a NUL, and a string beginning with `@`.
    /// </summary>
    [Fact]
    public void EveryEncodingSurvivesTheRoundTrip()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{keyPath}\probe", writable: true);

        // Rule 2: a real MAC whose last byte IS `)`.
        byte[] mac = [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29];
        QSettingsWriter.WriteByteArray(key, "server_mac", mac);
        Assert.Equal(mac, QSettingsValue.AsByteArray(key.GetValue("server_mac")));

        // Rule 3: eight characters and eight NULs cannot be a REG_SZ.
        byte[] registKey = [.. "3e91107c"u8.ToArray(), .. new byte[8]];
        QSettingsWriter.WriteByteArray(key, "rp_regist_key", registKey);
        Assert.Equal(registKey, QSettingsValue.AsByteArray(key.GetValue("rp_regist_key")));
        Assert.Equal(RegistryValueKind.Binary, key.GetValueKind("rp_regist_key"));

        // Rule 4: a nickname beginning with `@` is stored doubled and read back as it was typed.
        QSettingsWriter.WriteString(key, "server_nickname", "@home");
        Assert.Equal("@home", QSettingsValue.AsString(key.GetValue("server_nickname")));

        // And an ordinary one is not touched.
        QSettingsWriter.WriteString(key, "plain", "Living room");
        Assert.Equal("Living room", QSettingsValue.AsString(key.GetValue("plain")));
    }

    /// <summary>
    /// PP626: an array is replaced whole, so a removal never leaves a hole behind it.
    ///
    /// QSettings reads entries 1..size. Deleting the middle one of three by removing its subkey
    /// leaves a size of three and a gap - and Qt's own reader does not survive that as gracefully as
    /// this port's does. What is written is 1..n with the leftovers gone.
    /// </summary>
    [Fact]
    public void RemovingFromTheMiddleRenumbersTheRest()
    {
        QSettingsStore store = Store();
        store.WriteManualHosts(
        [
            new ManualHost(1, "10.0.0.1", false, null),
            new ManualHost(2, "10.0.0.2", false, null),
            new ManualHost(3, "10.0.0.3", false, null),
        ]);

        new QSettingsConsoleRemover(store).Remove(Manual("10.0.0.2", registered: false), RemoveAction.Delete);

        using RegistryKey array = Registry.CurrentUser.OpenSubKey($@"{keyPath}\manual_hosts")!;

        Assert.Equal(2, QSettingsValue.AsInt(array.GetValue("size")));
        Assert.Null(array.OpenSubKey("3"));
        Assert.Equal(["10.0.0.1", "10.0.0.3"], store.ManualHosts().Select(one => one.Host));
    }
}
