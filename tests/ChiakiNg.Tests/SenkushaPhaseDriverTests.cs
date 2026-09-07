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
    /// Before PP809 the answer here was TheC: session.c had a callback nothing filled. It is
    /// Fallback now, and PP784 is what makes it ThePort - so this one line tracks the whole port
    /// through its two remaining states.
    /// </summary>
    [Fact]
    public void ThisTreeMeasuresWithThePortAndKeepsTheCBehindIt()
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
        Assert.Equal(SenkushaDriver.Fallback, SenkushaPhaseDriver.DriverOf(source, files));
    }
}
