using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP625: the session outlives the click, and its ending reaches the person who asked for it.
///
/// PP600's starter created a session, started it and released it on the way out, so "Connecting..."
/// was a sentence about a call. What is asserted here is the lifetime - one session at a time, held
/// until somebody ends it or the console does - and the sentence a quit becomes, which is
/// qmlbackend.cpp's own composition and not an invention.
/// </summary>
public class ConsoleSessionTests
{
    private static RegisteredHost Registration(string nickname)
        => new()
        {
            ServerNickname = nickname,
            ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, 0x29],
            Target = 1_000_100,
            RpRegistKey = "12ab34cd"u8.ToArray(),
            RpKey = [.. Enumerable.Repeat((byte)7, 16)],
        };

    private static ConsoleRow Row(string name)
        => new(name, "10.0.0.5", Discovered: true, Manual: false, Registered: true, Display: true);

    private static ConsoleListViewModel Wired(ConsoleConnectTests.FakeStarter starter)
        => new(starter, () => [Registration("Living room")]);

    /// <summary>
    /// PP625: a started session is HELD, which is the whole difference from PP600.
    ///
    /// The handle is what the way out needs to exist for. Released on the way out, there is nothing
    /// to end and nothing to say has ended - and the console is occupied by a session the port has
    /// forgotten about.
    /// </summary>
    [Fact]
    public void AStartedSessionIsHeldUntilSomebodyEndsIt()
    {
        var starter = new ConsoleConnectTests.FakeStarter();
        ConsoleListViewModel model = Wired(starter);

        model.Connect(Row("Living room"));

        Assert.True(model.HasSession);
        Assert.False(starter.Released);

        model.Disconnect();

        Assert.False(model.HasSession);
        Assert.True(starter.Released);
    }

    /// <summary>
    /// PP625: a second Connect moves, and the old session goes FIRST.
    ///
    /// A console accepts one remote play session. Starting the new one first would have two asking
    /// the same console at once, and the one that is refused would be the one somebody just clicked.
    /// </summary>
    [Fact]
    public void ASecondConnectReleasesTheFirst()
    {
        var starter = new ConsoleConnectTests.FakeStarter();
        ConsoleListViewModel model = Wired(starter);

        model.Connect(Row("Living room"));
        Assert.False(starter.Released);

        model.Connect(Row("Living room"));

        Assert.Equal(2, starter.Starts);
        Assert.True(model.HasSession);
    }

    /// <summary>
    /// PP625: the console's own ending reaches the screen, and the handle goes with it.
    ///
    /// Both halves. A quit that only changed the message would leave the way out enabled for a
    /// session that is over, which is a button whose second click tries to stop nothing.
    /// </summary>
    [Fact]
    public void TheConsolesOwnEndingArrivesAndReleasesTheHandle()
    {
        var starter = new ConsoleConnectTests.FakeStarter();
        ConsoleListViewModel model = Wired(starter);

        model.Connect(Row("Living room"));
        Assert.NotNull(starter.Report);

        starter.Report(new ConsoleSessionEvent(ConsoleSessionState.Connected, null));
        Assert.Equal("Connected.", model.Status);

        starter.Report(new ConsoleSessionEvent(
            ConsoleSessionState.Ended, "The session has quit: Unknown"));

        Assert.Equal("The session has quit: Unknown", model.Status);
        Assert.False(model.HasSession);
        Assert.True(starter.Released);
    }

    /// <summary>
    /// PP625: a start that fails hands back no handle, so there is nothing to end.
    /// </summary>
    [Fact]
    public void ARefusedStartHoldsNothing()
    {
        var starter = new ConsoleConnectTests.FakeStarter { Answer = ChiakiError.Unknown };
        ConsoleListViewModel model = Wired(starter);

        model.Connect(Row("Living room"));

        Assert.False(model.HasSession);
        Assert.Contains("refused", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP625: the events arrive on libchiaki's thread, so the caller says how to get onto the
    /// binding one.
    ///
    /// PP217's parameter. Running inline is what a test wants and is wrong in the application, and a
    /// view model that reached for a dispatcher itself could only be asserted from wherever the
    /// runner happens to sit.
    /// </summary>
    [Fact]
    public void EveryReportGoesThroughTheMarshal()
    {
        var starter = new ConsoleConnectTests.FakeStarter();
        var queued = new List<Action>();

        var model = new ConsoleListViewModel(
            starter, () => [Registration("Living room")], queued.Add);

        model.Connect(Row("Living room"));
        starter.Report!(new ConsoleSessionEvent(ConsoleSessionState.Connected, null));

        // Nothing has moved: the report is waiting for the thread that owns the bindings.
        Assert.NotEqual("Connected.", model.Status);

        foreach (Action run in queued)
            run();

        Assert.Equal("Connected.", model.Status);
    }

    /// <summary>
    /// PP625: a quit that is not an error does not read as one.
    ///
    /// `chiaki_quit_reason_is_error` is false for STOPPED and for the console shutting down
    /// remotely. A port that showed an error for the first would be telling somebody their own
    /// Disconnect went wrong.
    /// </summary>
    [Fact]
    public void StoppingIsNotAFailure()
    {
        Assert.False(QuitSentence.IsError(ChiakiQuitReason.Stopped));
        Assert.False(QuitSentence.IsError(ChiakiQuitReason.StreamConnectionRemoteShutdown));
        Assert.True(QuitSentence.IsError(ChiakiQuitReason.CtrlConnectFailed));

        Assert.Equal(QuitSentence.Ended, QuitSentence.For(ChiakiQuitReason.Stopped, "whatever"));
    }

    /// <summary>
    /// PP625: and that rule is the C's, read out of the header rather than remembered.
    ///
    /// `chiaki_quit_reason_is_error` is a `static inline` in session.h, so it has no symbol the shim
    /// could wrap and the port has to carry a copy. This is what holds the copy to the original.
    /// </summary>
    [Fact]
    public void TheRuleIsTheHeadersOwn()
    {
        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\session.h") is not { } path)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains(
            "static inline bool chiaki_quit_reason_is_error", header, StringComparison.Ordinal);

        Assert.Contains(
            "reason != CHIAKI_QUIT_REASON_STOPPED && reason != CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_SHUTDOWN",
            header, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP625: an error carries libchiaki's reason, and the console's own words after it where there
    /// are any.
    ///
    /// The reason string is filled ONLY from a disconnect the console sent, so it is null on the
    /// commonest failure there is - a console that is switched off. A sentence built as if it were
    /// always there would end in an empty pair of quotation marks exactly when somebody is trying to
    /// work out why nothing happened.
    /// </summary>
    [Fact]
    public void AnErrorNamesTheReasonAndQuotesTheConsoleOnlyWhenItSpoke()
    {
        string silent = QuitSentence.For(ChiakiQuitReason.CtrlConnectFailed, null);
        string spoke = QuitSentence.For(ChiakiQuitReason.CtrlConnectFailed, "in use");

        Assert.DoesNotContain("\"", silent, StringComparison.Ordinal);
        Assert.Contains("\"in use\"", spoke, StringComparison.Ordinal);

        // The named reason is libchiaki's own, through the shim, and not the enum read aloud.
        Assert.Contains(
            ChiakiSession.QuitReasonString((int)ChiakiQuitReason.CtrlConnectFailed)!,
            silent, StringComparison.Ordinal);

        // And whitespace is not the console speaking.
        Assert.Equal(silent, QuitSentence.For(ChiakiQuitReason.CtrlConnectFailed, "   "));
    }
}
