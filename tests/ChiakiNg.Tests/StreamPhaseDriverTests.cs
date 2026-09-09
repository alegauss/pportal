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

        // PP767: a path that OPENS a session and installs, which is what the port's half now means.
        // An install anywhere says a composition root exists; this says one is reached.
        (string, string) installs = (
            "Composition.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); new ManagedStreamPhase(s, peer, v, b);");

        (string, string) doesNot = (
            "Composition.cs", "var s = ChiakiSession.TryCreate(info, log, out _); s.Start();");

        Assert.Equal(StreamDriver.TheC, StreamPhaseDriver.DriverOf(CRuns, [doesNot]));
        Assert.Equal(StreamDriver.ThePort, StreamPhaseDriver.DriverOf(CSilent, [installs]));
        Assert.Equal(StreamDriver.Both, StreamPhaseDriver.DriverOf(CRuns, [installs]));

        // THE ONE THAT SHIPPED. session.c hands over and nobody takes it.
        Assert.Equal(StreamDriver.Nobody, StreamPhaseDriver.DriverOf(CSilent, [doesNot]));
    }

    /// <summary>
    /// PP767: AN INSTALL THAT NO SESSION PATH MAKES IS NOT A DRIVER.
    ///
    /// The first version of this check asked any file in app, and PP762 answered it from inside
    /// ManagedStreamPhase - the class that would do the driving, which nothing constructs. So it
    /// passed on a tree where the C hands over, a composition root exists, and no session ever
    /// builds one: a client that cannot stream and a gate saying it can.
    ///
    /// The state that was live when this was written, asserted as the case it must refuse.
    /// </summary>
    [Fact]
    public void AComposisitionRootNoSessionPathBuildsIsNotADriver()
    {
        // The phase itself: it installs, and it opens no session.
        (string, string) phase = (
            @"app\Session\ManagedStreamPhase.cs", "handover.InstallOn(session);");

        // A path that opens one and does not reach for the phase.
        (string, string) path = (
            @"app\Session\StreamRun.cs", "var s = ChiakiSession.TryCreate(info, log, out _); s.Start();");

        // The old question says yes, and it is the one that let PP762's state look driven.
        Assert.NotEmpty(StreamPhaseDriver.InstallersIn([phase, path]));

        // The one this task adds says no, because no path that opens a session installs on it.
        Assert.Empty(StreamPhaseDriver.DrivingSessionPathsIn([phase, path]));

        const string CSilent = "\terr = session->stream_run_cb(data_sock, &reason, session->stream_run_cb_user);";
        Assert.Equal(StreamDriver.Nobody, StreamPhaseDriver.DriverOf(CSilent, [phase, path]));

        // And the path reaching for the phase is what makes it a driver.
        (string, string) driving = (
            @"app\Session\StreamRun.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); new ManagedStreamPhase(s, p, v, b);");

        Assert.Single(StreamPhaseDriver.DrivingSessionPathsIn([phase, driving]));
        Assert.Equal(StreamDriver.ThePort, StreamPhaseDriver.DriverOf(CSilent, [phase, driving]));
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

    /// <summary>
    /// PP801: EVERY PATH THAT OPENS A SESSION AND MEANS TO STREAM INSTALLS ONE.
    ///
    /// PP783 flipped the run and wired one of three. The captures kept writing their file and
    /// stopped holding afterwards; --measure-decoder decoded frames the C no longer produces; the
    /// front door reported Ended where it had reported Connected. Each loss is different and none
    /// of them said so, which is why this asks all of them at once rather than naming the one.
    /// </summary>
    [Fact]
    public void EverySessionPathThatMeansToStreamDrivesOne()
    {
        IReadOnlyList<(string Name, string Text)> files = StreamPhaseDriver.AppFiles();

        if (files.Count == 0)
            return;

        IReadOnlyList<string> missing = StreamPhaseDriver.PathsThatMustDrive(files);

        output.WriteLine(
            $"{StreamPhaseDriver.DrivingSessionPathsIn(files).Count} driving, {missing.Count} not");

        Assert.True(
            missing.Count == 0,
            "these open a session, are not named as paths that do not stream, and install no "
                + $"phase - so each quits with UNINITIALIZED: {string.Join(", ", missing)}");

        // PP271: a sweep that found no session paths at all would satisfy that.
        Assert.NotEmpty(StreamPhaseDriver.DrivingSessionPathsIn(files));
    }

    /// <summary>
    /// And the exemption is a list rather than a silence, so it can be argued with.
    ///
    /// The selftest opens sessions to assert what a refusal looks like, and a phase on those would
    /// be a stream nobody asked for on a run with no console. A name that stopped being a file
    /// would leave that reasoning attached to nothing, which is what this second half catches.
    /// </summary>
    [Fact]
    public void EveryExemptedPathIsStillAFileThatOpensASession()
    {
        IReadOnlyList<(string Name, string Text)> files = StreamPhaseDriver.AppFiles();

        if (files.Count == 0)
            return;

        Assert.All(
            StreamPhaseDriver.SessionPathsThatDoNotStream,
            leaf => Assert.Contains(
                files,
                one => Path.GetFileName(one.Name).Equals(leaf, StringComparison.OrdinalIgnoreCase)
                    && DeadAssertions.CodeOnly(one.Text)
                        .Contains(StreamPhaseDriver.BuildsASession, StringComparison.Ordinal)));
    }

    /// <summary>
    /// PP806: A SECOND SEAM'S INSTALL DOES NOT ANSWER THE STREAM'S QUESTION.
    ///
    /// PP792 gave senkusha a handover of its own, declaring an InstallOn with the same spelling. A
    /// check reading the call alone would then be satisfied by a session path that installs a
    /// senkusha phase and no stream one - which is exactly the state PP762 shipped and PP767 wrote
    /// this question to refuse.
    ///
    /// The old reading is asserted beside it, because it is the one that says yes: the two answers
    /// differing is the whole of what this task changed.
    /// </summary>
    [Fact]
    public void ASessionPathInstallingSomeOtherSeamIsNotAStreamDriver()
    {
        (string, string) senkusha = (
            @"app\Session\StreamRun.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); "
                + "var h = new SenkushaHandover(); h.InstallOn(s);");

        // The call is there, so the superseded question finds one.
        Assert.Single(StreamPhaseDriver.InstallersIn([senkusha]));

        // And the one that decides does not: nothing here names the stream's own seam.
        Assert.Empty(StreamPhaseDriver.DrivingSessionPathsIn([senkusha]));

        const string CSilent = "\terr = session->stream_run_cb(data_sock, &reason, session->stream_run_cb_user);";
        Assert.Equal(StreamDriver.Nobody, StreamPhaseDriver.DriverOf(CSilent, [senkusha]));
    }

    /// <summary>
    /// PP806: and a path that composes the STREAM's own seam still counts, which is the half a
    /// stricter check could have taken away.
    ///
    /// Two spellings have always been accepted - reaching for the composition root, or doing the
    /// composition - and the second is the one that had to be narrowed rather than dropped. The
    /// evidence is the type it constructs, which no other seam can spell.
    /// </summary>
    [Fact]
    public void APathComposingTheStreamsOwnSeamIsStillADriver()
    {
        (string, string) composing = (
            @"app\Session\StreamRun.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); "
                + "var h = new StreamHandover(); h.InstallOn(s);");

        Assert.Single(StreamPhaseDriver.DrivingSessionPathsIn([composing]));

        // And constructing one without ever installing it is not driving either.
        (string, string) built = (
            @"app\Session\StreamRun.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); var h = new StreamHandover();");

        Assert.Empty(StreamPhaseDriver.DrivingSessionPathsIn([built]));
    }

    /// <summary>
    /// PP830: a phase pointed somewhere the session is not, which is what killed a relayed stream.
    ///
    /// The session's connect info goes to a relay and the phase's takion opens its OWN socket, so a
    /// phase handed the console's address dials past the forwarder. Ctrl and senkusha reach the
    /// console from the relay and the stream from this machine, which is a peer changing half way
    /// through a session - and 85ms later it is `[E] StreamConnection run failed`. ExchangeCapture
    /// carried a comment arguing the console's address was RIGHT for exactly that reason.
    ///
    /// COMPARED AS IDENTIFIERS. Neither address exists before a console answers, so what a gate can
    /// read is the name: whatever a path assigns to Host is what its phases have to be handed. That
    /// is the whole mistake - <c>Host = target</c> beside <c>IPAddress.Parse(address)</c> - and no
    /// check that counts calls or types can see it, because both spellings are present either way.
    /// </summary>
    [Fact]
    public void APhasePointedAwayFromTheSessionIsNamed()
    {
        const string Astray =
            "var info = new ChiakiConnectInfo { Host = target }; "
                + "var s = ChiakiSession.TryCreate(info, log, out _); "
                + "var p = new ManagedStreamPhase(s, IPAddress.Parse(address), v, b);";

        IReadOnlyList<(string Name, string Session, string Phase)> astray =
            StreamPhaseDriver.PhasesPointedElsewhere([(@"app\Session\ExchangeCapture.cs", Astray)]);

        (string name, string session, string phase) = Assert.Single(astray);
        Assert.EndsWith("ExchangeCapture.cs", name, StringComparison.Ordinal);
        Assert.Equal("target", session);
        Assert.Equal("address", phase);

        // The same path pointed at what it pointed the session at, which is silent.
        Assert.Empty(
            StreamPhaseDriver.PhasesPointedElsewhere(
                [(@"app\Session\ExchangeCapture.cs", Astray.Replace("Parse(address)", "Parse(target)"))]));

        // AND THE TREE ITSELF, which is what the fix is for. Both roots point their phases at the
        // target they pointed the session at; a checkout is needed to ask, so an empty read is a
        // machine without one rather than a pass.
        IReadOnlyList<(string Name, string Text)> app = StreamPhaseDriver.AppFiles();

        if (app.Count == 0)
            return;

        Assert.NotEmpty(StreamPhaseDriver.DrivingSessionPathsIn(app));
        Assert.Empty(StreamPhaseDriver.PhasesPointedElsewhere(app));
    }
}
