using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP11: the window's fullscreen rules, which are guards rather than a flag.
/// </summary>
public class StreamWindowTests
{
    /// <summary>
    /// The order of WindowType, which decides what number a setting holds. AdjustableResolution
    /// sits third, so the three fullscreen types are 3, 4 and 5 rather than 2, 3 and 4.
    /// </summary>
    [Fact]
    public void TheWindowTypesAreNumberedAroundAdjustableResolution()
    {
        Assert.Equal(2, (int)StreamWindowType.AdjustableResolution);
        Assert.Equal(3, (int)StreamWindowType.Fullscreen);
        Assert.Equal(5, (int)StreamWindowType.Stretch);
    }

    /// <summary>
    /// The guard that matters. Entering fullscreen twice must not overwrite what the window was
    /// before, or leaving it restores a maximized window to a normal one - a defect that needs two
    /// entries to appear and explains nothing on screen when it does.
    /// </summary>
    [Fact]
    public void ASecondEntryDoesNotForgetThatTheWindowWasMaximized()
    {
        var window = new StreamWindowState();
        window.SetMaximized(true);

        window.EnterFullscreen();
        Assert.True(window.Fullscreen);
        Assert.True(window.WasMaximized);

        // The second entry, which a port without the guard would let through.
        window.EnterFullscreen();
        Assert.True(window.WasMaximized);

        window.LeaveFullscreen();
        Assert.False(window.Fullscreen);
        Assert.True(window.Maximized);
    }

    /// <summary>And a window that was not maximized comes back to normal, not to maximized.</summary>
    [Fact]
    public void ANormalWindowComesBackNormal()
    {
        var window = new StreamWindowState();

        window.EnterFullscreen();
        window.LeaveFullscreen();

        Assert.False(window.Maximized);
    }

    /// <summary>
    /// The minimum size is pinned to whatever the window was, and released on the way out. A
    /// minimum left pinned is a window that cannot be made small again once the stream ends.
    /// </summary>
    [Fact]
    public void TheMinimumSizeIsPinnedGoingInAndReleasedComingOut()
    {
        var window = new StreamWindowState();
        window.Resize(1600, 900);

        window.EnterFullscreen();
        Assert.Equal((1600, 900), window.MinimumSize);

        window.LeaveFullscreen();
        Assert.Equal((0, 0), window.MinimumSize);
    }

    /// <summary>
    /// The adjustable flag goes at once and comes back a second later. Two separate steps, because
    /// a port that restored it inside the same call makes the window resizable while it is still
    /// settling into its old geometry.
    /// </summary>
    [Fact]
    public void TheAdjustableFlagGoesAtOnceAndComesBackLate()
    {
        var window = new StreamWindowState();

        window.EnterFullscreen();
        Assert.False(window.Adjustable);
        Assert.Null(window.AdjustableAfter);

        window.LeaveFullscreen();
        Assert.False(window.Adjustable);
        Assert.Equal(TimeSpan.FromSeconds(1), window.AdjustableAfter);

        window.AdjustableDelayElapsed();
        Assert.True(window.Adjustable);
        Assert.Null(window.AdjustableAfter);
    }

    /// <summary>
    /// And leaving fullscreen when there is no fullscreen to leave still schedules it. The timer
    /// sits outside the guard, so the function is not only about fullscreen - which is what a port
    /// reading its name rather than its body would lose.
    /// </summary>
    [Fact]
    public void LeavingAWindowThatIsNotFullscreenStillSchedulesTheFlag()
    {
        var window = new StreamWindowState();

        window.LeaveFullscreen();

        Assert.False(window.Fullscreen);
        Assert.Equal(TimeSpan.FromSeconds(1), window.AdjustableAfter);
    }

    /// <summary>Three of the six window types go fullscreen, and each picks a video mode with it.</summary>
    [Theory]
    [InlineData(StreamWindowType.Fullscreen, true, StreamVideoMode.Normal)]
    [InlineData(StreamWindowType.Zoom, true, StreamVideoMode.Zoom)]
    [InlineData(StreamWindowType.Stretch, true, StreamVideoMode.Stretch)]
    [InlineData(StreamWindowType.SelectedResolution, false, StreamVideoMode.Normal)]
    [InlineData(StreamWindowType.CustomResolution, false, StreamVideoMode.Normal)]
    public void EachWindowTypeDecidesFullscreenAndMode(
        StreamWindowType type, bool fullscreen, StreamVideoMode mode)
    {
        var window = new StreamWindowState();

        window.Apply(type);

        Assert.Equal(fullscreen, window.Fullscreen);
        Assert.Equal(mode, window.VideoMode);
    }

    /// <summary>
    /// And AdjustableResolution changes NOTHING - it is the switch's default, so a window already
    /// zoomed and fullscreen stays exactly that. A port completing the switch for tidiness would
    /// make that setting drop the user out of fullscreen.
    /// </summary>
    [Fact]
    public void AdjustableResolutionChangesNothingAtAll()
    {
        var window = new StreamWindowState();
        window.Apply(StreamWindowType.Zoom);

        window.Apply(StreamWindowType.AdjustableResolution);

        Assert.True(window.Fullscreen);
        Assert.Equal(StreamVideoMode.Zoom, window.VideoMode);
    }

    /// <summary>
    /// The other entry: three flags rather than one type, fullscreen for any of them, and zoom
    /// winning over stretch when both are set - which the settings screen cannot produce and a
    /// command line can.
    /// </summary>
    [Fact]
    public void AStartedSessionGoesFullscreenForAnyFlagAndZoomWins()
    {
        var stretched = new StreamWindowState();
        stretched.StartSession(fullscreen: false, zoom: false, stretch: true);
        Assert.True(stretched.Fullscreen);
        Assert.Equal(StreamVideoMode.Stretch, stretched.VideoMode);

        var both = new StreamWindowState();
        both.StartSession(fullscreen: false, zoom: true, stretch: true);
        Assert.Equal(StreamVideoMode.Zoom, both.VideoMode);

        var plain = new StreamWindowState();
        plain.StartSession(fullscreen: true, zoom: false, stretch: false);
        Assert.True(plain.Fullscreen);
        Assert.Equal(StreamVideoMode.Normal, plain.VideoMode);
    }

    /// <summary>And no flag at all leaves the window alone.</summary>
    [Fact]
    public void ASessionWithNoFlagsLeavesTheWindowAlone()
    {
        var window = new StreamWindowState();

        window.StartSession(false, false, false);

        Assert.False(window.Fullscreen);
        Assert.Equal(StreamVideoMode.Normal, window.VideoMode);
    }

    /// <summary>Every rule above, still stated the same way in the window and the header.</summary>
    [Fact]
    public void TheWindowsRulesAreStillTheQtClients()
    {
        string? window = StreamWindowSource.Locate(StreamWindowSource.WindowCpp);
        string? header = StreamWindowSource.Locate(StreamWindowSource.SettingsHeader);
        if (window is null || header is null)
            return;

        string cpp = File.ReadAllText(window);

        Assert.True(StreamWindowSource.EnteringFullscreenStillRefusesToRunTwice(cpp), "the guard");
        Assert.True(StreamWindowSource.TheMinimumSizeIsPinnedAndReleased(cpp), "the pinned minimum");
        Assert.True(StreamWindowSource.TheAdjustableFlagComesBackLate(cpp), "the second's delay");
        Assert.True(StreamWindowSource.TheThreeFullscreenTypesEachSetAMode(cpp), "three types, three modes");
        Assert.True(StreamWindowSource.AdjustableResolutionIsStillUnhandled(cpp), "and one with no case");
        Assert.True(StreamWindowSource.AStartedSessionGoesFullscreenForAnyOfThree(cpp), "any of three flags");
        Assert.True(StreamWindowSource.TheWindowTypesAreStillInThisOrder(File.ReadAllText(header)),
            "AdjustableResolution still third");
    }
}
