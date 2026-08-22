namespace ChiakiNg.Session;

/// <summary>
/// PP11: the window type a session is started with, as settings.h numbers it.
///
/// Six values, and the third is the one to know about: AdjustableResolution sits between the two
/// resolution choices and Fullscreen, so the fullscreen values are 3, 4 and 5. It is also the one
/// value <c>updateWindowType</c> does NOTHING for - see <see cref="StreamWindowState.Apply"/>.
/// </summary>
public enum StreamWindowType
{
    SelectedResolution = 0,
    CustomResolution = 1,

    /// <summary>Sized by hand. The switch that acts on every other type falls through for this one.</summary>
    AdjustableResolution = 2,

    Fullscreen = 3,
    Zoom = 4,
    Stretch = 5,
}

/// <summary>
/// PP11: what the window does about fullscreen, which is a state machine and not a flag.
///
/// The Qt client's <c>fullscreenTime</c> and <c>normalTime</c> are eleven lines that a port would
/// write as two, and the difference is in what they refuse to do:
///
/// 1. BOTH START WITH A GUARD. fullscreenTime returns immediately when already fullscreen, and
///    normalTime's restore runs only when it is. That guard is load-bearing rather than tidy: the
///    remembered "was maximized" is written on the way in, so a second entry that did not return
///    early would overwrite it with false - and leaving fullscreen would then restore a maximized
///    window to a normal one. Nothing on screen explains it, and it needs two entries to happen.
///
/// 2. THE MINIMUM SIZE IS PINNED. setMinimumSize(size()) on the way in, and back to zero on the
///    way out. A window whose minimum was left pinned cannot be made small again after the stream
///    ends, which reads as a window manager bug.
///
/// 3. THE ADJUSTABLE FLAG COMES BACK LATE AND UNCONDITIONALLY. normalTime schedules it a full
///    SECOND later, OUTSIDE the fullscreen guard - so it runs even when the window was not
///    fullscreen at all. The delay is the window settling; the unconditionality is what a port
///    reading the function as "leave fullscreen" would drop.
/// </summary>
public sealed class StreamWindowState
{
    /// <summary>How long after leaving fullscreen the window becomes adjustable again.</summary>
    public static readonly TimeSpan AdjustableDelay = TimeSpan.FromSeconds(1);

    /// <summary>Whether the window is fullscreen now.</summary>
    public bool Fullscreen { get; private set; }

    /// <summary>Whether it was maximized before it went fullscreen, which is what it returns to.</summary>
    public bool WasMaximized { get; private set; }

    /// <summary>Whether it is maximized now.</summary>
    public bool Maximized { get; private set; }

    /// <summary>The pinned minimum, or zero. Set on the way in and released on the way out.</summary>
    public (int Width, int Height) MinimumSize { get; private set; }

    /// <summary>The window's size, which is what the minimum is pinned to.</summary>
    public (int Width, int Height) Size { get; private set; } = (1280, 720);

    /// <summary>
    /// Whether the window may be resized. False for the whole of fullscreen, and true again only
    /// after <see cref="AdjustableDelay"/> - see <see cref="AdjustableAfter"/>.
    /// </summary>
    public bool Adjustable { get; private set; } = true;

    /// <summary>
    /// When the adjustable flag is due back, counted from the last <see cref="LeaveFullscreen"/>.
    /// Null when nothing is pending.
    /// </summary>
    public TimeSpan? AdjustableAfter { get; private set; }

    /// <summary>How the video fills the window, which the type decides along with the fullscreen.</summary>
    public StreamVideoMode VideoMode { get; private set; } = StreamVideoMode.Normal;

    /// <summary>Sets the current size, which only matters at the moment fullscreen is entered.</summary>
    public void Resize(int width, int height) => Size = (width, height);

    /// <summary>Maximizes or unmaximizes, which is the state the way out consults.</summary>
    public void SetMaximized(bool maximized) => Maximized = maximized;

    /// <summary>
    /// fullscreenTime. Refuses when already fullscreen - and that refusal is the whole of point 1
    /// above, because the refusal is what protects <see cref="WasMaximized"/>.
    /// </summary>
    public void EnterFullscreen()
    {
        if (Fullscreen)
            return;

        Adjustable = false;
        MinimumSize = Size;
        WasMaximized = Maximized;
        Fullscreen = true;
        Maximized = false;
    }

    /// <summary>
    /// normalTime. Restores the window only when it is fullscreen, and schedules the adjustable
    /// flag either way - the timer sits outside the guard in the QML's own code.
    /// </summary>
    public void LeaveFullscreen()
    {
        if (Fullscreen)
        {
            Maximized = WasMaximized;
            WasMaximized = false;
            Fullscreen = false;
            MinimumSize = (0, 0);
        }

        AdjustableAfter = AdjustableDelay;
    }

    /// <summary>
    /// The delay elapsing. Separate from <see cref="LeaveFullscreen"/> because the second is what
    /// a port would inline, and inlining it makes the window adjustable while it is still settling.
    /// </summary>
    public void AdjustableDelayElapsed()
    {
        if (AdjustableAfter is null)
            return;

        AdjustableAfter = null;
        Adjustable = true;
    }

    /// <summary>
    /// updateWindowType: which types go fullscreen, and which video mode each one selects.
    ///
    /// Three of the six go fullscreen and each of those also sets a mode; the two resolution types
    /// set Normal without touching fullscreen; and AdjustableResolution does NOTHING AT ALL - it
    /// reaches the switch's default and falls out, so a window already fullscreen stays fullscreen
    /// and whatever mode was selected stays selected. A port completing the switch "for tidiness"
    /// would change what that setting does.
    /// </summary>
    public void Apply(StreamWindowType type)
    {
        switch (type)
        {
            case StreamWindowType.SelectedResolution:
            case StreamWindowType.CustomResolution:
                VideoMode = StreamVideoMode.Normal;
                break;

            case StreamWindowType.Fullscreen:
                EnterFullscreen();
                VideoMode = StreamVideoMode.Normal;
                break;

            case StreamWindowType.Zoom:
                EnterFullscreen();
                VideoMode = StreamVideoMode.Zoom;
                break;

            case StreamWindowType.Stretch:
                EnterFullscreen();
                VideoMode = StreamVideoMode.Stretch;
                break;

            default:
                // AdjustableResolution, and anything a later settings.h adds. Untouched on purpose.
                break;
        }
    }

    /// <summary>
    /// The other entry: a session started straight into a window, where the three flags are
    /// separate booleans rather than one type.
    ///
    /// The order is the constructor's and it is not the same shape as <see cref="Apply"/>: any of
    /// the three goes fullscreen FIRST, and only then is a mode selected - with zoom winning over
    /// stretch when both are set, which is a combination the settings screen cannot produce and
    /// the command line can.
    /// </summary>
    public void StartSession(bool fullscreen, bool zoom, bool stretch)
    {
        if (fullscreen || zoom || stretch)
            EnterFullscreen();

        if (zoom)
            VideoMode = StreamVideoMode.Zoom;
        else if (stretch)
            VideoMode = StreamVideoMode.Stretch;
    }
}

/// <summary>
/// PP11: the window's rules where the Qt client states them.
/// </summary>
public static class StreamWindowSource
{
    /// <summary>The window.</summary>
    public const string WindowCpp = @"gui\src\qmlmainwindow.cpp";

    /// <summary>Where WindowType is declared and numbered.</summary>
    public const string SettingsHeader = @"gui\include\settings.h";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Whether entering fullscreen still refuses to run twice.
    ///
    /// Matched on the function and the guard's own text rather than on the two together as one
    /// string: the file's line endings are not this check's business, and a drift check that fails
    /// on a checkout setting is a check that gets deleted rather than read.
    /// </summary>
    public static bool EnteringFullscreenStillRefusesToRunTwice(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        int start = cpp.IndexOf("void QmlMainWindow::fullscreenTime()", StringComparison.Ordinal);
        if (start < 0)
            return false;

        // The guard is the first statement of the function; anything past the setMinimumSize is a
        // different part of it and would make this pass for the wrong reason.
        int pin = cpp.IndexOf("setMinimumSize(size());", start, StringComparison.Ordinal);
        if (pin < 0)
            return false;

        string head = cpp[start..pin];
        return head.Contains("if(windowState() == Qt::WindowFullScreen)", StringComparison.Ordinal)
            && head.Contains("return;", StringComparison.Ordinal);
    }

    /// <summary>Whether the minimum size is still pinned on the way in and released on the way out.</summary>
    public static bool TheMinimumSizeIsPinnedAndReleased(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("setMinimumSize(size());", StringComparison.Ordinal)
            && cpp.Contains("setMinimumSize(QSize(0, 0));", StringComparison.Ordinal);
    }

    /// <summary>Whether the adjustable flag still comes back a second later.</summary>
    public static bool TheAdjustableFlagComesBackLate(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("QTimer::singleShot(1000, this, [this]{", StringComparison.Ordinal);
    }

    /// <summary>Whether the three fullscreen types still each select a video mode as well.</summary>
    public static bool TheThreeFullscreenTypesEachSetAMode(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("case WindowType::Fullscreen:", StringComparison.Ordinal)
            && cpp.Contains("case WindowType::Zoom:", StringComparison.Ordinal)
            && cpp.Contains("case WindowType::Stretch:", StringComparison.Ordinal)
            && cpp.Contains("setVideoMode(VideoMode::Stretch);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether AdjustableResolution is still absent from that switch, which is what makes it the
    /// one type that changes nothing.
    /// </summary>
    public static bool AdjustableResolutionIsStillUnhandled(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        // PP272: the switch has to be there before a missing arm of it means anything.
        return cpp.Contains("case WindowType::", StringComparison.Ordinal)
            && !cpp.Contains("case WindowType::AdjustableResolution:", StringComparison.Ordinal);
    }

    /// <summary>Whether the session constructor still goes fullscreen for any of its three flags.</summary>
    public static bool AStartedSessionGoesFullscreenForAnyOfThree(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            "if (connect_info.fullscreen || connect_info.zoom || connect_info.stretch)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether WindowType is still in the order this port numbered it from.</summary>
    public static bool TheWindowTypesAreStillInThisOrder(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        int selected = header.IndexOf("SelectedResolution,", StringComparison.Ordinal);
        int custom = header.IndexOf("CustomResolution,", StringComparison.Ordinal);
        int adjustable = header.IndexOf("AdjustableResolution,", StringComparison.Ordinal);
        int full = header.IndexOf("Fullscreen,", StringComparison.Ordinal);

        return selected >= 0 && custom > selected && adjustable > custom && full > adjustable;
    }
}
