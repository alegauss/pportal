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

    /// <summary>
    /// PP625: a starter that hands back a handle, and lets a test say what the console did next.
    /// </summary>
    internal sealed class FakeStarter : IConsoleSessionStarter
    {
        public ConnectRequest? Started { get; private set; }

        public ChiakiError Answer { get; init; } = ChiakiError.Success;

        /// <summary>The report the last start was given, so a test can be the console.</summary>
        public Action<ConsoleSessionEvent>? Report { get; private set; }

        /// <summary>Whether the handle it handed back has been disposed.</summary>
        public bool Released { get; private set; }

        /// <summary>How many sessions it has been asked for.</summary>
        public int Starts { get; private set; }

        /// <summary>PP627: the PIN it was handed, as text.</summary>
        public string? Pin { get; private set; }

        /// <summary>PP627: what to say about that PIN.</summary>
        public ChiakiError PinAnswer { get; init; } = ChiakiError.Success;

        public ConsoleSessionStart Start(ConnectRequest request, Action<ConsoleSessionEvent> report)
        {
            Started = request;
            Report = report;
            Starts++;
            Released = false;

            return Answer == ChiakiError.Success
                ? new(ChiakiError.Success, new Handle(this))
                : new(Answer, null);
        }

        private sealed class Handle(FakeStarter owner) : IHeldSession
        {
            public ChiakiError AnswerPin(ReadOnlySpan<byte> pin)
            {
                owner.Pin = System.Text.Encoding.ASCII.GetString(pin);
                return owner.PinAnswer;
            }

            public void Dispose() => owner.Released = true;
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
    /// PP624: the registered set is the store's own bytes, and the reply's id is read as the same
    /// identity.
    ///
    /// PP600 could not make the two spellings meet and joined by nickname instead, which cost it
    /// two consoles sharing a name. The Qt client's own source settles it - `GetHostMAC` parses the
    /// host-id from hex and refuses anything that is not six bytes - so the key is twelve lower-case
    /// hexadecimal characters and both sides can be written in it.
    ///
    /// The console sends UPPER case here on purpose: that is the half a set built out of the store
    /// would miss, and missing it disables the button on a console that really is paired.
    /// </summary>
    [Fact]
    public void TheRegisteredSetIsTheStoresOwnIdentities()
    {
        DiscoveredConsole living = Reply("Living room", "90474882FC29");
        DiscoveredConsole other = Reply("Somebody else's", "aabbccddeeff");

        IReadOnlySet<string> keys = ConsoleListSources.RegisteredMacs([Registration("Living room")]);

        Assert.Contains("90474882fc29", keys);

        IReadOnlyList<ConsoleRow> rows =
            ConsoleList.Build([living, other], [], [], new HashSet<string>(), keys);

        Assert.True(rows.Single(r => r.Name == "Living room").Connectable);
        Assert.False(rows.Single(r => r.Name == "Somebody else's").Connectable);
    }

    /// <summary>
    /// PP624: and the hidden set works, which PP600 had no way to key and passed as nothing.
    ///
    /// <see cref="ConsoleActions.RemovalFor"/> has three outcomes and Hide is one of them. A port
    /// that could not read this set had that third unreachable from any screen - the row was drawn
    /// whatever the user had said about it.
    /// </summary>
    [Fact]
    public void AHiddenConsoleIsNotDrawn()
    {
        IReadOnlySet<string> hidden = ConsoleListSources.HiddenMacs(
            [new HiddenHost("Living room", [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29])]);

        ConsoleRow row = Assert.Single(ConsoleList.Build(
            [Reply("Living room", "90474882FC29")], [], [], hidden, new HashSet<string>()));

        Assert.False(row.Display);
    }

    /// <summary>
    /// PP624: two consoles under one nickname are two consoles, which is what the identity buys.
    ///
    /// The exact case PP600's nickname join could not tell apart. Only one of them is registered,
    /// and a row that reached for the other's key would offer a Connect that opens a session with
    /// the wrong console's credentials.
    /// </summary>
    [Fact]
    public void TwoConsolesUnderOneNicknameAreToldApart()
    {
        RegisteredHost stored = Registration("PS5-385");

        ConsoleRow[] rows =
        [
            .. ConsoleList.Build(
                [Reply("PS5-385", "90474882fc29"), Reply("PS5-385", "001122334455")],
                [], [], new HashSet<string>(),
                ConsoleListSources.RegisteredMacs([stored]))
        ];

        Assert.Same(stored, ConsoleConnect.RegistrationFor(rows[0], [stored]));
        Assert.Null(ConsoleConnect.RegistrationFor(rows[1], [stored]));
    }

    /// <summary>
    /// PP624: an id that is not six bytes is not an identity, which is the Qt client's own refusal.
    ///
    /// `DiscoveryHost::GetHostMAC` prints "Invalid mac received" and hands back a zeroed one. A port
    /// that keyed on whatever arrived would let two unreadable replies match each other, and the
    /// two consoles that agreed would be the ones nothing could read.
    /// </summary>
    [Fact]
    public void AnIdThatIsNotSixBytesIsNoIdentity()
    {
        Assert.Equal("90474882fc29", HostId.Key("90:47:48:82:FC:29"));
        Assert.Equal("90474882fc29", HostId.Key("90474882fc29"));
        Assert.Null(HostId.Key("0011223344556677"));
        Assert.Null(HostId.Key("90474882fc2"));
        Assert.Null(HostId.Key("90474882fc2z"));
        Assert.Null(HostId.Key((string?)null));
        Assert.Null(HostId.Key(new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    /// PP624: and PP13's own keys still match, so this widened the comparison and took nothing away.
    ///
    /// The merge's assertions hand Build short strings - "AA" for a console called Bedroom - because
    /// they are about which rows appear and not about identity. A normalisation that refused them
    /// would have been a rewrite of PP13 wearing this task's name.
    /// </summary>
    [Fact]
    public void TheKeysPP13AssertsWithStillMatch()
    {
        Assert.True(HostId.Knows(new HashSet<string> { "AA" }, "AA"));
        Assert.False(HostId.Knows(new HashSet<string> { "AA" }, "BB"));
    }

    /// <summary>
    /// PP624: and the spelling is the client's, read out of the client rather than remembered.
    ///
    /// The rule this whole task rests on is somebody else's code: six bytes of hex, written back as
    /// `toHex()`. A port that decided that once and wrote it down would be right until the day the
    /// source moved, and the symptom would be a Connect button disabled on a console that is paired
    /// - a wrong answer that looks exactly like a console being off.
    /// </summary>
    [Fact]
    public void TheIdentitysSpellingIsTheQtClientsOwn()
    {
        if (SanitizerSource.LocateRelative(HostId.DiscoveryManagerRelativePath) is { } manager)
        {
            Assert.True(
                HostId.TheClientParsesHexAndRefusesAnyOtherLength(File.ReadAllText(manager)),
                "the client no longer reads a host-id as six bytes of hex");
        }

        if (SanitizerSource.LocateRelative(HostId.HostHeaderRelativePath) is { } header)
        {
            Assert.True(
                HostId.TheClientSpellsItBareHex(File.ReadAllText(header)),
                "the client no longer writes an identity as bare hexadecimal");
        }
    }

    private static DiscoveredConsole Reply(string name, string id)
        => new("10.0.0.5", "1.0", "00030010", name, "PS5", id, null, null,
            DiscoveryHostState.Ready, 9295);

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

