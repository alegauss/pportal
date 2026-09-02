using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP309, holding PP225: the startup ordering, which fails silently in both directions.
///
/// PP225 shipped with nothing asserting it and was the only code change in the port of which that
/// was true. Its two halves are exactly the kind a person cannot notice: one kills the process
/// before anything is drawn, the other draws nothing and reports success.
/// </summary>
public class StartupSequenceTests
{
    /// <summary>The assignment that throws, in the spellings it would be written in.</summary>
    [Theory]
    [InlineData("StartupUri = null;")]
    [InlineData("this.StartupUri = null;")]
    [InlineData("Application.Current.StartupUri = null;")]
    [InlineData("StartupUri=null;")]
    public void ClearingTheStartupUriIsSeenHoweverItIsWritten(string line)
        => Assert.True(StartupSequence.ClearsStartupUri(line));

    /// <summary>
    /// And the prose about it is not the assignment.
    ///
    /// The comment in App.xaml.cs says clearing StartupUri is not an option, so a check that
    /// matched the words would fail on the file that documents why it must not happen - which is
    /// the reading that gets a check deleted rather than believed.
    /// </summary>
    [Fact]
    public void SayingItIsNotDoingIt()
    {
        Assert.False(StartupSequence.ClearsStartupUri(
            "// Clearing StartupUri instead is not an option: the property refuses null"));
        Assert.False(StartupSequence.ClearsStartupUri("if (StartupUri is null) { }"));
    }

    /// <summary>Queued at idle is the fix; called inline is the defect wearing the same name.</summary>
    [Fact]
    public void CallingIsNotQueueing()
    {
        Assert.True(StartupSequence.QueuesTheMappingScreen(
            "Dispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.ApplicationIdle);"));

        Assert.False(StartupSequence.QueuesTheMappingScreen("StartMappingScreen();"));

        // Queued, and at a priority that runs before the window exists - which draws nothing, the
        // same as calling it, and looks like the fix.
        Assert.False(StartupSequence.QueuesTheMappingScreen(
            "Dispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.Send);"));
    }

    /// <summary>And after base.OnStartup, because that is what creates the window.</summary>
    [Fact]
    public void TheOrderAroundTheWindowIsRead()
    {
        Assert.True(StartupSequence.QueuesAfterTheWindowIsCreated(
            "base.OnStartup(e);\nDispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.ApplicationIdle);"));

        Assert.False(StartupSequence.QueuesAfterTheWindowIsCreated(
            "Dispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.ApplicationIdle);\nbase.OnStartup(e);"));
    }

    /// <summary>
    /// PP628: the ordinary path is PP13's console list, which is what MainWindow was filed to open
    /// with.
    ///
    /// PP223 put the mapping screen behind a flag and said why: putting it in the ordinary path
    /// would be a navigation decision taken by a diagnostic. PP600 put the list behind a flag for
    /// the same reason, because the list could not yet connect. It can now, so the reason expired.
    /// </summary>
    [Fact]
    public void NoFlagOpensTheConsoleList()
    {
        Assert.Equal(StartupScreen.ConsoleList, StartupSequence.ScreenFor([]));
        Assert.Equal(StartupScreen.ConsoleList, StartupSequence.ScreenFor(["--consoles"]));

        // A flag that says nothing about screens leaves the front door where it is.
        Assert.Equal(StartupScreen.ConsoleList, StartupSequence.ScreenFor(["--record"]));
    }

    /// <summary>
    /// PP628: and a diagnostic screen REPLACES it rather than racing it.
    ///
    /// One choice and not two tests. Two things queued against the same window show whichever
    /// ShowScreen runs last, which is a screen that appears or does not on ordering nothing states.
    /// </summary>
    [Fact]
    public void AskingForTheMappingScreenReplacesTheFrontDoor()
    {
        Assert.Equal(StartupScreen.Mapping, StartupSequence.ScreenFor(["--map-controller"]));
        Assert.Equal(StartupScreen.Mapping, StartupSequence.ScreenFor(["--MAP-CONTROLLER"]));
        Assert.Equal(
            StartupScreen.Mapping,
            StartupSequence.ScreenFor(["--record", "--map-controller"]));
    }

    /// <summary>
    /// PP628: two screen flags together are answered by order, first one wins.
    ///
    /// The rule a person already has for a command line, and the only one that leaves both
    /// spellings meaning something. A fixed winner would make one of the two a literal that changes
    /// nothing - which is the shape of an option that quietly does not work.
    /// </summary>
    [Fact]
    public void TwoScreenFlagsAreAnsweredByOrder()
    {
        Assert.Equal(
            StartupScreen.Mapping,
            StartupSequence.ScreenFor(["--map-controller", "--consoles"]));

        Assert.Equal(
            StartupScreen.ConsoleList,
            StartupSequence.ScreenFor(["--consoles", "--map-controller"]));
    }

    /// <summary>
    /// THE ASSERTION PP225 SHIPPED WITHOUT. The real file still starts up the way it was fixed to.
    /// </summary>
    [Fact]
    public void TheHostStillStartsUpTheWayPP225LeftIt()
    {
        string? sourcePath = StartupSequence.Locate();
        Assert.True(sourcePath is not null, "not running out of a checkout");

        string source = File.ReadAllText(sourcePath);

        Assert.False(StartupSequence.ClearsStartupUri(source),
            "something assigns null to StartupUri, which throws inside OnStartup - the run dies "
                + "before anything is drawn and no window appears at all");

        Assert.True(StartupSequence.QueuesTheMappingScreen(source),
            "the mapping screen is no longer queued at ApplicationIdle, so it runs before "
                + "StartupUri has created MainWindow and does nothing, silently");

        Assert.True(StartupSequence.QueuesAfterTheWindowIsCreated(source),
            "the mapping screen is queued before base.OnStartup, which is what creates the window "
                + "it is queued behind");

        // PP628: and the front door is queued the same way, behind the same window. A list called
        // inline would find no window and do nothing, silently - the failure PP225 was filed for,
        // arriving through the screen that replaced its subject in the ordinary path.
        Assert.Contains(
            "Dispatcher.BeginInvoke(StartConsoleList, DispatcherPriority.ApplicationIdle)",
            source, StringComparison.Ordinal);
    }
}
