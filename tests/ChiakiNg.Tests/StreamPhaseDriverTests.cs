using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP764: the stream phase has somebody driving it, which is the check PP696 got past.
///
/// That commit was green and the client could not stream. What was missing was a CALL - session.c
/// stopped making one and nothing in app started - and two files each looked fine on its own. This
/// is the pair, read together, offline.
///
/// IT DOES NOT PROVE A RUN WORKS. A gate with no console cannot, and pretending otherwise is how the
/// last one passed. What it proves is that somebody is expected to make one.
/// </summary>
public class StreamPhaseDriverTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE TREE HAS A DRIVER, and it is never Nobody.
    ///
    /// Both is allowed and reported, because a tree mid-handover is a real state somebody may be
    /// standing in - but Nobody is the one that shipped, and it is refused by name.
    /// </summary>
    [Fact]
    public void TheStreamPhaseIsDrivenBySomebody()
    {
        if (StreamPhaseDriver.LocateSession() is not { } path)
            return;

        IReadOnlyList<(string Name, string Text)> app = StreamPhaseDriver.AppFiles();

        // PP271: a sweep that read no files would report Nobody about a tree that is fine, or
        // ThePort about one that is not, depending on which half went missing.
        Assert.True(app.Count > 100, $"only {app.Count} app files were read, so this is not a sweep");

        StreamDriver driver = StreamPhaseDriver.DriverOf(File.ReadAllText(path), app);
        output.WriteLine($"{app.Count} app file(s), driver: {driver}");

        Assert.NotEqual(StreamDriver.Nobody, driver);
    }

    /// <summary>
    /// The four states, on text this file owns.
    ///
    /// One of them exists on any tree at a time, and the one that matters is the one that does not:
    /// a check whose failing branch is never exercised is a branch nobody has run. PP696 is what
    /// happens when the failing branch was never written at all.
    /// </summary>
    [Fact]
    public void TheReaderTellsTheFourStatesApart()
    {
        const string CRuns = "\terr = chiaki_stream_connection_run(&session->stream_connection, data_sock);";
        const string CSilent = "\terr = session->stream_run_cb(data_sock, &reason, session->stream_run_cb_user);";

        (string, string) installs = ("Composition.cs", "handover.InstallOn(session);");
        (string, string) doesNot = ("Composition.cs", "session.Start();");

        Assert.Equal(StreamDriver.TheC, StreamPhaseDriver.DriverOf(CRuns, [doesNot]));
        Assert.Equal(StreamDriver.ThePort, StreamPhaseDriver.DriverOf(CSilent, [installs]));
        Assert.Equal(StreamDriver.Both, StreamPhaseDriver.DriverOf(CRuns, [installs]));

        // THE ONE THAT SHIPPED. session.c hands over and nobody takes it.
        Assert.Equal(StreamDriver.Nobody, StreamPhaseDriver.DriverOf(CSilent, [doesNot]));
    }

    /// <summary>
    /// A NAME IN A STRING IS NOT A CALLER, which is PP735's trap and the reason this reads code.
    ///
    /// StreamRunHandoff spells the install's own name in a literal because describing the contract
    /// is its job. A reader keyed on flat text would find it and call the phase driven - on exactly
    /// the tree where nothing drives it, since that file is what a handover-shaped tree carries.
    /// </summary>
    [Fact]
    public void ASymbolInQuotesOrACommentIsNotAnInstall()
    {
        (string, string) quoted = ("Contract.cs", "public const string Call = \"handover.InstallOn(session)\";");
        (string, string) commented = ("Notes.cs", "// one day: handover.InstallOn(session);");
        (string, string) blockComment = ("Notes.cs", "/* handover.InstallOn(session); */");

        Assert.Empty(StreamPhaseDriver.InstallersIn([quoted, commented, blockComment]));

        // And the real call in the same file IS found, so this is not a reader that sees nothing.
        (string, string) both = ("Notes.cs", "// handover.InstallOn(a);\n\thandover.InstallOn(b);");
        Assert.Single(StreamPhaseDriver.InstallersIn([both]));
    }

    /// <summary>
    /// The declaring file is not a caller of itself, told two ways.
    ///
    /// StreamHandover declares InstallOn, and a declaration has no receiver before the name - so the
    /// dot alone already excludes it. Excluded by name as well, because a check resting only on that
    /// spelling is one a rename can quietly empty, and this one is load-bearing.
    /// </summary>
    [Fact]
    public void TheFileThatDeclaresItIsNotAnInstaller()
    {
        (string, string) declaration = (
            @"app\Protocol\StreamHandover.cs", "public void InstallOn(Native.ChiakiSession session)");

        Assert.Empty(StreamPhaseDriver.InstallersIn([declaration]));

        // Even carrying a call, because it is excluded by name too.
        (string, string) andACall = (@"app\Protocol\StreamHandover.cs", "other.InstallOn(session);");
        Assert.Empty(StreamPhaseDriver.InstallersIn([andACall]));

        // The same call anywhere else is an install.
        Assert.Single(StreamPhaseDriver.InstallersIn([(@"app\Session\StreamRun.cs", "other.InstallOn(session);")]));
    }
}
