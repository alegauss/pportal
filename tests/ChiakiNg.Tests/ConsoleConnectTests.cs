using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP600: the front door's connect - the caller PP13's decisions never had.
///
/// The rules here are all about the room: a console that is not paired, one that is only on PSN,
/// one whose registration this process cannot read. Every one of them is a sentence somebody has to
/// be told, so every one of them is named rather than folded into a null.
///
/// None of this reaches a console, and that is the design. What crosses the seam is a
/// <see cref="ConnectRequest"/>, so what a real session would be handed is assertable on a machine
/// with nothing switched on.
/// </summary>
public class ConsoleConnectTests
{
    private static RegisteredHost Registration(string nickname, int target = 1_000_100)
        => new()
        {
            ServerNickname = nickname,
            ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29],
            Target = target,
            RpRegistKey = "12ab34cd"u8.ToArray(),
            RpKey = [.. Enumerable.Repeat((byte)7, 16)],
        };

    private static ConsoleRow Discovered(string name, bool registered = true)
        => new(name, "10.0.0.5", Discovered: true, Manual: false, Registered: registered, Display: true);

    private sealed class FakeStarter : IConsoleSessionStarter
    {
        public ConnectRequest? Started { get; private set; }

        public ChiakiError Answer { get; init; } = ChiakiError.Success;

        public ChiakiError Start(ConnectRequest request)
        {
            Started = request;
            return Answer;
        }
    }

    /// <summary>
    /// PP600: the button's enabled state, which is a rule the row can answer on its own.
    ///
    /// Deliberately not the store's question. A list can say a console is paired while this process
    /// cannot read a key for it, and those two want different words - so the one that can be
    /// answered while DRAWING is the one the binding uses.
    /// </summary>
    [Fact]
    public void OnlyAPairedConsoleWithAnAddressOffersTheAction()
    {
        Assert.True(ConsoleConnect.CanConnect(Discovered("Living room")));
        Assert.False(ConsoleConnect.CanConnect(Discovered("Living room", registered: false)));

        // A PSN row: registered, and no address at all.
        Assert.False(ConsoleConnect.CanConnect(
            new ConsoleRow("Bedroom", "", Discovered: false, Manual: false, Registered: true, Display: true)));
    }

    /// <summary>PP600: and the row exposes it, because a binding cannot call a static method.</summary>
    [Fact]
    public void TheRowCarriesTheAnswerForTheBinding()
    {
        Assert.True(Discovered("Living room").Connectable);
        Assert.False(Discovered("Living room", registered: false).Connectable);
    }

    /// <summary>
    /// PP600: each refusal is a different sentence, because each is a different thing to do about it.
    ///
    /// A single "cannot connect" is the message a port collects complaints about: re-pair the
    /// console, wait for PSN to be wired, or register it again are three answers, and the person in
    /// front of the screen is the only one who can act on any of them.
    /// </summary>
    [Fact]
    public void EveryRefusalIsNamedAndSaysSomethingDifferent()
    {
        Assert.Equal(
            ConnectRefusal.NotRegistered,
            ConsoleConnect.Prepare(Discovered("Living room", registered: false), []).Refusal);

        Assert.Equal(
            ConnectRefusal.NoAddress,
            ConsoleConnect.Prepare(
                new ConsoleRow("Bedroom", "", false, false, true, true), []).Refusal);

        // Paired according to the list, and the store has nothing under that name.
        Assert.Equal(
            ConnectRefusal.NoRegistration,
            ConsoleConnect.Prepare(Discovered("Living room"), [Registration("Bedroom")]).Refusal);

        string[] said =
        [
            ConsoleConnect.Explain(ConnectRefusal.NotRegistered),
            ConsoleConnect.Explain(ConnectRefusal.NoAddress),
            ConsoleConnect.Explain(ConnectRefusal.NoRegistration),
        ];

        Assert.Equal(said.Length, said.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("", said);
    }

    /// <summary>
    /// PP600: a registration missing its key is refused rather than half-used.
    ///
    /// The store carries the entry either way. An RP key of the wrong length is what a partly
    /// written registration looks like, and passing it on produces a session that fails inside
    /// libchiaki with a message about the protocol.
    /// </summary>
    [Fact]
    public void AStoredRegistrationWithoutItsKeysIsNotOne()
    {
        RegisteredHost half = Registration("Living room") with { RpKey = [1, 2, 3] };

        Assert.Equal(
            ConnectRefusal.NoRegistration,
            ConsoleConnect.Prepare(Discovered("Living room"), [half]).Refusal);
    }

    /// <summary>
    /// PP600: PP13's nickname rule, kept where the request is built.
    ///
    /// <see cref="ConsoleActions.ConnectSendsTheNickname"/> says the nickname goes only with a
    /// DISCOVERED console, because it is what the wake-then-connect path waits to see come back on
    /// the network. A port that always sent it waits on a name that never arrives.
    /// </summary>
    [Fact]
    public void TheNicknameGoesOnlyWithADiscoveredConsole()
    {
        ConnectRequest? found = ConsoleConnect
            .Prepare(Discovered("Living room"), [Registration("Living room")]).Request;

        Assert.NotNull(found);
        Assert.Equal("Living room", found.Nickname);

        // A manual row's name IS its address, and it was never discovered.
        var manual = new ConsoleRow("10.0.0.5", "10.0.0.5", false, true, true, true);
        ConnectRequest? typed = ConsoleConnect
            .Prepare(manual, [Registration("10.0.0.5")]).Request;

        Assert.NotNull(typed);
        Assert.Null(typed.Nickname);
    }

    /// <summary>
    /// PP600: which protocol the session speaks comes from the REGISTRATION.
    ///
    /// Discovery's host-type string is a different fact and can disagree with it - the reply is
    /// free text and the registration is what the console was actually paired as.
    /// </summary>
    [Fact]
    public void ThePs5FlagComesFromTheStoredTarget()
    {
        Assert.True(ConsoleConnect
            .Prepare(Discovered("Living room"), [Registration("Living room")]).Request!.Ps5);

        Assert.False(ConsoleConnect
            .Prepare(Discovered("Living room"), [Registration("Living room", target: 1000)])
            .Request!.Ps5);
    }

    /// <summary>
    /// PP600: THE THING NO SCREEN COULD DO. The row reaches a session, through the seam.
    /// </summary>
    [Fact]
    public void TheListStartsASessionForARow()
    {
        var starter = new FakeStarter();
        var model = new ConsoleListViewModel(starter, () => [Registration("Living room")]);

        Assert.Equal(ConnectRefusal.None, model.Connect(Discovered("Living room")));

        Assert.NotNull(starter.Started);
        Assert.Equal("10.0.0.5", starter.Started.Host);
        Assert.Equal("Living room", starter.Started.Nickname);
        Assert.Contains("Living room", model.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP600: and libchiaki's own refusal reaches the screen rather than the log.
    ///
    /// PP224's rule: a WinExe started from a shortcut has no console, and this port's one place a
    /// failure was written was bound to standard output. The window is where somebody is.
    /// </summary>
    [Fact]
    public void ARefusedSessionSaysSoOnTheScreen()
    {
        var starter = new FakeStarter { Answer = ChiakiError.Unknown };
        var model = new ConsoleListViewModel(starter, () => [Registration("Living room")]);

        model.Connect(Discovered("Living room"));

        Assert.Contains("refused", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP600: a row that cannot connect never reaches the seam, and still says why.
    ///
    /// Both halves. A starter called for an unpaired console would build a request out of a
    /// registration that is not there; a screen that said nothing reads as a broken button, which
    /// is the failure that gets reported as "it does not work".
    /// </summary>
    [Fact]
    public void ARefusedRowNeverReachesTheSeamAndStillExplainsItself()
    {
        var starter = new FakeStarter();
        var model = new ConsoleListViewModel(starter, () => [Registration("Living room")]);

        Assert.Equal(
            ConnectRefusal.NotRegistered,
            model.Connect(Discovered("Living room", registered: false)));

        Assert.Null(starter.Started);
        Assert.Equal(ConsoleConnect.Explain(ConnectRefusal.NotRegistered), model.Status);
    }

    /// <summary>
    /// PP600: a list built with no starter says that, and does not blame the console.
    ///
    /// The state every existing caller of this view model is in - a test about the merge - and a
    /// real one: it is what the list is before somebody hands it a way to open a session.
    /// </summary>
    [Fact]
    public void AListWithNoWayToOpenASessionSaysSo()
    {
        var model = new ConsoleListViewModel();

        model.Connect(Discovered("Living room"));

        Assert.Contains("no way to open a session", model.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP600: the registered set is built from NICKNAMES, which is the join that exists.
    ///
    /// <see cref="ConsoleList.Build"/> asks whether a discovered host's id is in the set, and that
    /// id is the reply's `host-id` - bare hexadecimal, and eight bytes on the console this port was
    /// read against. <see cref="RegisteredHost.MacText"/> is six bytes with colons. Handing Build
    /// the MACs would disable the button on every console, silently and forever.
    /// </summary>
    [Fact]
    public void TheRegisteredSetIsTheIdsOfConsolesTheStoreKnowsByName()
    {
        DiscoveredConsole living = new(
            "10.0.0.5", "1.0", "00030010", "Living room", "PS5", "0011223344556677", null, null,
            DiscoveryHostState.Ready, 9295);
        DiscoveredConsole other = living with { Name = "Somebody else's", Id = "8899aabbccddeeff" };

        IReadOnlySet<string> ids =
            ConsoleListSources.RegisteredIds([living, other], [Registration("Living room")]);

        Assert.Contains("0011223344556677", ids);
        Assert.DoesNotContain("8899aabbccddeeff", ids);

        // And the whole point: the row Build produces is registered, so the button is enabled.
        IReadOnlyList<ConsoleRow> rows = ConsoleList.Build(
            [living, other], [], [], new HashSet<string>(), ids);

        Assert.True(rows.Single(r => r.Name == "Living room").Connectable);
        Assert.False(rows.Single(r => r.Name == "Somebody else's").Connectable);
    }

    /// <summary>
    /// PP600: a manual entry with no address is dropped rather than drawn blank.
    ///
    /// The address IS the name for these, so an entry without one has nothing to show and nothing
    /// to reach - a row that is only an empty line with a dead button beside it.
    /// </summary>
    [Fact]
    public void AManualEntryWithNoAddressIsNotARow()
    {
        IReadOnlyList<ManualConsole> manual = ConsoleListSources.Manual(
        [
            new ManualHost(1, "10.0.0.9", true, [0x90, 0x47]),
            new ManualHost(2, null, false, null),
            new ManualHost(3, "   ", false, null),
        ]);

        Assert.Equal("10.0.0.9", Assert.Single(manual).Address);
    }
}
