using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP809, under PP784: who measures the link, asked of the two files that between them decide.
///
/// PP764 wrote this question for the stream because PP696 was green while the client could not
/// stream: session.c had stopped calling its own run and no file in app installed a callback, and
/// each of those is a legitimate state alone. Senkusha reached the same shape - the callback landed
/// with PP792 and nothing constructed a phase - and this is the check that would have said so.
/// </summary>
public class SenkushaPhaseDriverTests(ITestOutputHelper output)
{
    private const string CRuns = "\terr = chiaki_senkusha_run(&senkusha, &session->mtu_in, &s, &r, d);";
    private const string CSilent = "\terr = session->senkusha_run_cb(d, &in, &out, &rtt, u);";

    private static (string Name, string Text) APathThatInstalls => (
        @"app\Session\StreamRun.cs",
        "var s = ChiakiSession.TryCreate(info, log, out _); "
            + "using var k = new ManagedSenkushaPhase(s, address); k.InstallOn();");

    private static (string Name, string Text) APathThatDoesNot => (
        @"app\Session\StreamRun.cs",
        "var s = ChiakiSession.TryCreate(info, log, out _); s.Start();");

    /// <summary>
    /// THE STATE THIS LINE SHIPPED IN, and the one before it, told apart.
    ///
    /// Both is not a transition here, which is where the two drivers differ: session.c asks for an
    /// installed run BEFORE it starts its own, so the callback wins and the C is the other arm.
    /// A tree with no phase installed is not broken - it measures with the C, exactly as every
    /// build before PP792 did.
    /// </summary>
    [Theory]
    [InlineData(true, true, SenkushaDriver.Fallback)]
    [InlineData(true, false, SenkushaDriver.TheC)]
    [InlineData(false, true, SenkushaDriver.ThePort)]
    [InlineData(false, false, SenkushaDriver.Nobody)]
    public void EachPairOfFactsHasItsOwnName(bool cRuns, bool installed, SenkushaDriver driver)
    {
        (string Name, string Text) path = installed ? APathThatInstalls : APathThatDoesNot;

        Assert.Equal(driver, SenkushaPhaseDriver.DriverOf(cRuns ? CRuns : CSilent, [path]));
    }

    /// <summary>
    /// PP767's rule: an install no session path makes is not a driver.
    ///
    /// The phase itself constructs nothing and opens no session, so a check asking any file in app
    /// would answer yes on a tree where nothing ever builds one - which is the state PP762 shipped
    /// and the reason this question is about session PATHS.
    /// </summary>
    [Fact]
    public void AConstructionOutsideASessionPathIsNotADriver()
    {
        (string, string) elsewhere = (
            @"app\Session\SomeHelper.cs", "var k = new ManagedSenkushaPhase(s, address);");

        Assert.Empty(SenkushaPhaseDriver.DrivingSessionPathsIn([elsewhere]));
        Assert.Equal(SenkushaDriver.TheC, SenkushaPhaseDriver.DriverOf(CRuns, [elsewhere]));
    }

    /// <summary>
    /// A NAME IN A STRING OR A COMMENT IS NOT A CONSTRUCTION, which is PP735's trap.
    ///
    /// This file spells the phase's own name in a literal on the line above, and so does the driver
    /// class it tests. A reader keyed on flat text would find those and call the tree driven.
    /// </summary>
    [Fact]
    public void ASymbolInQuotesOrACommentIsNotAnInstall()
    {
        (string, string) quoted = (
            @"app\Session\StreamRun.cs",
            "ChiakiSession.TryCreate(i, l, out _); var s = \"new ManagedSenkushaPhase(\";");

        (string, string) commented = (
            @"app\Session\StreamRun.cs",
            "ChiakiSession.TryCreate(i, l, out _); // one day: new ManagedSenkushaPhase(s, a);");

        Assert.Empty(SenkushaPhaseDriver.DrivingSessionPathsIn([quoted, commented]));
    }

    /// <summary>
    /// AND THE TREE ITSELF, which is the assertion that fails without this task.
    ///
    /// Before PP809 the answer here was TheC: session.c had a callback nothing filled. PP809 made it
    /// Fallback, and PP784 is what makes it ThePort - so this one line has tracked the whole port
    /// through its three states, and this is the last of them.
    ///
    /// THE C'S ARM WENT BECAUSE A LIVE LINK SAID THE TWO AGREE, which is the condition PP792 wrote
    /// into session.c when it kept the fallback: on 2026-09-08 against PS5-385 the C read 1454 both
    /// ways at an average 3.849ms, and the port's phase read 1454 both ways with its round trip in
    /// the same range.
    ///
    /// AND EVERY SESSION PATH HAS TO MEASURE NOW. While the C's arm was there a path that forgot a
    /// phase still got real numbers; now it gets 1454/1454/1000 and streams anyway, which is a
    /// launch spec built from numbers no console answered and a run that looks entirely healthy.
    /// </summary>
    [Fact]
    public void ThisTreeMeasuresWithThePortAndTheCsArmHasGone()
    {
        if (SenkushaPhaseDriver.LocateSession() is not { } path)
            return;

        string source = File.ReadAllText(path);
        IReadOnlyList<(string Name, string Text)> files = StreamPhaseDriver.AppFiles();

        if (files.Count == 0)
            return;

        IReadOnlyList<string> driving = SenkushaPhaseDriver.DrivingSessionPathsIn(files);

        output.WriteLine($"driving session paths: {string.Join(", ", driving)}");

        Assert.NotEmpty(driving);
        Assert.Equal(SenkushaDriver.ThePort, SenkushaPhaseDriver.DriverOf(source, files));

        // And nothing that opens a session is left measuring with a run that is no longer there.
        IReadOnlyList<string> owing = SenkushaPhaseDriver.PathsThatMustMeasure(files);

        output.WriteLine($"session paths owing a phase: {string.Join(", ", owing)}");

        Assert.Empty(owing);
    }

    /// <summary>
    /// PP784: a session path with no phase is named, and the one that does not stream is not.
    ///
    /// Driven over text this test writes, because a tree that has the phase everywhere cannot show
    /// what happens to one that does not - the same reason PathsThatMustDrive is asked this way.
    /// </summary>
    [Fact]
    public void ASessionPathWithNoPhaseIsNamedUnlessItNeverStreams()
    {
        (string, string) forgot = (
            @"app\Session\ExchangeCapture.cs", "var s = ChiakiSession.TryCreate(info, log, out _);");

        (string, string) measures = (
            @"app\Session\StreamRun.cs",
            "var s = ChiakiSession.TryCreate(info, log, out _); new ManagedSenkushaPhase(s, p);");

        // The one that does not stream opens a session to assert what a REFUSAL looks like, and a
        // measurement on it would be a link nobody asked to measure.
        (string, string) refusing = (
            @"app\Session\SelfTest.cs", "var s = ChiakiSession.TryCreate(bad, log, out _);");

        Assert.Equal(
            [@"app\Session\ExchangeCapture.cs"],
            SenkushaPhaseDriver.PathsThatMustMeasure([forgot, measures, refusing]));

        Assert.Empty(SenkushaPhaseDriver.PathsThatMustMeasure([measures, refusing]));
    }
}
