using System.Globalization;
using System.Windows.Media;

namespace ChiakiNg.Session;

/// <summary>
/// PP10: what the General tab's Audio/Video setting turns off, as the bitmask it really is.
///
/// The QML tests it three different ways in the same file - <c>== 0x01</c>, <c>&amp; 0x01</c> and
/// <c>&amp; 0x02</c> - and the three do not mean the same thing. A port reading it as an enum with
/// three values gets two of the three screens wrong.
/// </summary>
[Flags]
public enum AudioVideoDisabled
{
    None = 0,

    /// <summary>Audio off. On its own it is a LINE ON THE LOADING SCREEN, not a screen of its own.</summary>
    Audio = 0x01,

    /// <summary>Video off, which is what puts the black panel up and keeps it up.</summary>
    Video = 0x02,
}

/// <summary>
/// PP10: the numbers in the corner of a stream, and the four conversions between them and the
/// values they are read from.
///
/// Every one of these is a formatting rule with a unit inside it, and a unit is what a port gets
/// wrong in a way nobody can see: a latency read in seconds and printed as milliseconds is not
/// obviously wrong on screen, it is just a number that never looks alarming.
/// </summary>
public static class StreamStats
{
    /// <summary>Mbps, one decimal. The measure is already in Mbps; only the rounding is here.</summary>
    public static string Bitrate(double mbps) => Fixed(mbps, 1);

    /// <summary>Queue depth, one decimal. A count rather than a rate, and averaged upstream.</summary>
    public static string QueueDepth(double frames) => Fixed(frames, 1);

    /// <summary>
    /// The pending frame's age. Read in SECONDS and printed in MILLISECONDS - the QML multiplies
    /// by 1000 at the call site, so a port that bound the property straight to a label would show
    /// "0 ms" through the whole of a bad session.
    /// </summary>
    public static string PendingFrameAge(double seconds)
        => Fixed(seconds * 1000.0, 0) + " ms";

    /// <summary>
    /// Packet loss, as a percentage of a FRACTION. The session reports 0..1 and the label prints
    /// 0..100, and the same value is compared against a setting that is already a percentage -
    /// see <see cref="StreamOverlayViewModel.NetworkIndicatorVisible"/>, which is the other half
    /// of the same confusion.
    ///
    /// Non-finite reads as zero rather than as "NaN%": the QML guards with isFinite, because the
    /// average is a division that has no samples to divide at the start of every session.
    /// </summary>
    public static string PacketLoss(double fraction)
        => Fixed(Finite(fraction) * 100.0, 1) + "%";

    /// <summary>Dropped frames, a plain count with no guard - the window counts, it does not average.</summary>
    public static string DroppedFrames(long frames)
        => frames.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Lost frames, which IS guarded. Two counters that look identical on screen and are not:
    /// this one comes off the session and can be non-finite, the one above comes off the window.
    /// </summary>
    public static string LostFrames(double frames)
        => Fixed(Finite(frames), 0);

    private static double Finite(double value)
        => double.IsFinite(value) ? value : 0.0;

    private static string Fixed(double value, int decimals)
        => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}

/// <summary>
/// PP10: the stream overlay, which PP9 made an ordinary screen.
///
/// The task was filed as undecidable on purpose: if the renderer had landed on a child HWND, none
/// of this could be XAML over the video and all of it would have to be drawn into the frame. PP9
/// landed on D3DImage instead, so the video is a WPF brush like any other and the overlay is
/// elements above it. That is the whole answer to "what shape is the overlay", and it is why this
/// file is view-model rules rather than a compositor.
///
/// What is NOT ordinary is the visibility logic, and it is worth naming three times:
///
/// 1. <c>visible: opacity</c>. The QML binds visibility to a REAL, and 0.0 is false. So a panel
///    fading out is invisible only when the animation finishes, and a port that wrote
///    <c>visible: opacity &gt; 0</c> would be right about the same thing by accident. Reproduced as
///    booleans, because the animation is presentation and the rule is not.
///
/// 2. <c>visible: text</c>. Two error labels are shown IF AND ONLY IF they carry text - nothing
///    anywhere sets their visibility. A port that showed an empty error label would put a black
///    screen with nothing on it in front of a session that was merely loading.
///
/// 3. The loading panel is up when the session is loading, when it has errored, OR when video is
///    disabled in settings. The third is not a failure and stays up for the whole session, which
///    is the case a port reading "loading" as "not yet started" loses.
/// </summary>
public sealed class StreamOverlayViewModel : DialogViewModel
{
    private bool loading = true;
    private bool error;
    private AudioVideoDisabled disabled;
    private bool hasVideo;
    private bool cantDisplay;
    private bool sessionActive;
    private string errorTitle = "";
    private string errorText = "";
    private double packetLoss;
    private int droppedNotifyPercent = 3;
    private ImageSource? video;
    private double measuredBitrate;
    private double queueDepthAverage;
    private double pendingFrameAgeSeconds;
    private long droppedFrameCount;
    private double framesLost;
    private string menuShortcut = "";
    private bool hasController;
    private string dpadShortcut = "";

    protected override string ButtonProperty => nameof(LoadingVisible);

    /// <summary>Whether the session is still coming up.</summary>
    public bool Loading
    {
        get => loading;
        set { Set(ref loading, value); RaiseVisibilities(); }
    }

    /// <summary>Whether the session failed. Independent of <see cref="Loading"/>, and both can be false.</summary>
    public bool Error
    {
        get => error;
        set { Set(ref error, value); RaiseVisibilities(); }
    }

    /// <summary>What the settings turned off, as a mask rather than as a choice.</summary>
    public AudioVideoDisabled Disabled
    {
        get => disabled;
        set { Set(ref disabled, value); RaiseVisibilities(); }
    }

    /// <summary>Whether the window has ever had a frame, which gates the can't-display notice.</summary>
    public bool HasVideo
    {
        get => hasVideo;
        set { Set(ref hasVideo, value); RaiseVisibilities(); }
    }

    /// <summary>Whether the console says the screen may not be shown - a copyright block, not a fault.</summary>
    public bool CantDisplay
    {
        get => cantDisplay;
        set { Set(ref cantDisplay, value); RaiseVisibilities(); }
    }

    /// <summary>Whether there is a session at all, which is what the readouts are gated on.</summary>
    public bool SessionActive
    {
        get => sessionActive;
        set { Set(ref sessionActive, value); RaiseVisibilities(); }
    }

    /// <summary>The failure's heading, or the empty string. Its own visibility, by rule 2.</summary>
    public string ErrorTitle
    {
        get => errorTitle;
        set { Set(ref errorTitle, value ?? ""); RaiseVisibilities(); }
    }

    /// <summary>The failure's detail, likewise.</summary>
    public string ErrorText
    {
        get => errorText;
        set { Set(ref errorText, value ?? ""); RaiseVisibilities(); }
    }

    /// <summary>The measured loss, as the session reports it: a FRACTION between 0 and 1.</summary>
    public double PacketLoss
    {
        get => packetLoss;
        set { Set(ref packetLoss, value); Raise(nameof(NetworkIndicatorVisible)); }
    }

    /// <summary>The wifiDroppedNotif setting, which is a WHOLE PERCENT and not a fraction.</summary>
    public int DroppedNotifyPercent
    {
        get => droppedNotifyPercent;
        set { Set(ref droppedNotifyPercent, value); Raise(nameof(NetworkIndicatorVisible)); }
    }

    /// <summary>
    /// The black panel over the video: loading, failed, or video switched off in settings. The
    /// third is the one that is not a transient.
    /// </summary>
    public bool LoadingVisible => Loading || Error || Disabled.HasFlag(AudioVideoDisabled.Video);

    /// <summary>The spinner, which is loading ALONE - a failed session stops spinning.</summary>
    public bool SpinnerVisible => Loading;

    /// <summary>
    /// The "video disabled" notice, which is deliberately not shown while loading or failed even
    /// though the panel behind it is up for all three. Three reasons for one panel, one notice
    /// each, and they do not stack.
    /// </summary>
    public bool DisabledNoticeVisible
        => !Loading && !Error && Disabled.HasFlag(AudioVideoDisabled.Video);

    /// <summary>
    /// Its heading. Audio-and-video when the audio bit is set too - so the mask is read twice, once
    /// to decide whether to show it and once to decide what it says.
    /// </summary>
    public string DisabledTitle => Disabled.HasFlag(AudioVideoDisabled.Audio)
        ? "Audio and Video Disabled"
        : "Video Disabled";

    /// <summary>Shown iff it carries text. Rule 2, and the reason it is a property here.</summary>
    public bool ErrorTitleVisible => ErrorTitle.Length > 0;

    public bool ErrorTextVisible => ErrorText.Length > 0;

    /// <summary>
    /// The can't-display notice, which needs a picture to be covering. hasVideo is in the rule
    /// because the message is about what is on screen, and a session with no frame yet has
    /// nothing for the console to have objected to.
    /// </summary>
    public bool CantDisplayVisible => HasVideo && CantDisplay;

    /// <summary>The readouts, which exist only while a session does.</summary>
    public bool StatsVisible => SessionActive;

    /// <summary>
    /// The video, as an ordinary ImageSource.
    ///
    /// This property is PP10's answer in one line. The renderer's D3DImage is a brush like any
    /// other, so the overlay is elements above it rather than a compositor - which is exactly the
    /// question the task was filed to leave open until PP9 was decided.
    /// </summary>
    public ImageSource? Video
    {
        get => video;
        set => Set(ref video, value);
    }

    /// <summary>Mbps, as the session measures it.</summary>
    public double MeasuredBitrate
    {
        get => measuredBitrate;
        set { Set(ref measuredBitrate, value); Raise(nameof(Bitrate)); }
    }

    /// <summary>The window's averaged queue depth, in frames.</summary>
    public double QueueDepthAverage
    {
        get => queueDepthAverage;
        set { Set(ref queueDepthAverage, value); Raise(nameof(QueueDepth)); }
    }

    /// <summary>The pending frame's age in SECONDS, which is the unit it arrives in.</summary>
    public double PendingFrameAgeSeconds
    {
        get => pendingFrameAgeSeconds;
        set { Set(ref pendingFrameAgeSeconds, value); Raise(nameof(PendingFrameAge)); }
    }

    /// <summary>The window's dropped-frame count.</summary>
    public long DroppedFrameCount
    {
        get => droppedFrameCount;
        set { Set(ref droppedFrameCount, value); Raise(nameof(DroppedFrames)); }
    }

    /// <summary>The session's lost-frame count, which can be non-finite before the first sample.</summary>
    public double FramesLost
    {
        get => framesLost;
        set { Set(ref framesLost, value); Raise(nameof(LostFrames)); }
    }

    /// <summary>The shortcut a connected controller opens the menu with.</summary>
    public string MenuShortcut
    {
        get => menuShortcut;
        set { Set(ref menuShortcut, value ?? ""); Raise(nameof(LoadingHint)); }
    }

    /// <summary>Whether any controller is connected, which decides which shortcut is named.</summary>
    public bool HasController
    {
        get => hasController;
        set { Set(ref hasController, value); Raise(nameof(LoadingHint)); }
    }

    /// <summary>The dpad-touch shortcut, or empty when the setting is off and the line is absent.</summary>
    public string DpadShortcut
    {
        get => dpadShortcut;
        set { Set(ref dpadShortcut, value ?? ""); Raise(nameof(LoadingHint)); }
    }

    public string Bitrate => StreamStats.Bitrate(MeasuredBitrate);

    public string QueueDepth => StreamStats.QueueDepth(QueueDepthAverage);

    public string PendingFrameAge => StreamStats.PendingFrameAge(PendingFrameAgeSeconds);

    public string PacketLossText => StreamStats.PacketLoss(PacketLoss);

    public string DroppedFrames => StreamStats.DroppedFrames(DroppedFrameCount);

    public string LostFrames => StreamStats.LostFrames(FramesLost);

    /// <summary>The hint under the spinner, for this screen's own state.</summary>
    public string LoadingHint
        => LoadingHintFor(MenuShortcut, HasController, Disabled, DpadShortcut);

    /// <summary>
    /// The dropped-network indicator, and the unit mismatch it is built on: the measure is a
    /// FRACTION and the setting is a PERCENT, so the QML multiplies the setting by 0.01 rather
    /// than the measure by 100. Ported as the same comparison in the same direction - a port that
    /// scaled the other side would light the icon at a hundredth of the loss it was meant to.
    /// </summary>
    public bool NetworkIndicatorVisible
        => double.IsFinite(PacketLoss) && PacketLoss > DroppedNotifyPercent * 0.01;

    /// <summary>
    /// The hint under the spinner. The shortcut it names depends on whether a controller is
    /// connected - with none it is the keyboard's Ctrl+O, spelled literally in the QML rather than
    /// looked up - and the audio line appears only for audio-alone, which is the <c>== 0x01</c>
    /// test that a mask read as flags would turn into "audio bit set" and show for both.
    /// </summary>
    public static string LoadingHintFor(
        string menuShortcut, bool hasController, AudioVideoDisabled disabled, string? dpadShortcut)
    {
        ArgumentNullException.ThrowIfNull(menuShortcut);

        string shortcut = hasController ? menuShortcut : KeyboardMenuShortcut;
        string hint = $"Press {shortcut} to open stream menu";

        if (disabled == AudioVideoDisabled.Audio)
            hint = "Audio Disabled in settings\n" + hint;

        if (!string.IsNullOrEmpty(dpadShortcut))
            hint += $"\nPress {dpadShortcut} to toggle between regular dpad and dpad touch";

        return hint;
    }

    /// <summary>The shortcut named when there is no controller, as the QML spells it.</summary>
    public const string KeyboardMenuShortcut = "Ctrl+O";

    private void RaiseVisibilities()
    {
        Raise(nameof(SpinnerVisible));
        Raise(nameof(DisabledNoticeVisible));
        Raise(nameof(DisabledTitle));
        Raise(nameof(ErrorTitleVisible));
        Raise(nameof(ErrorTextVisible));
        Raise(nameof(CantDisplayVisible));
        Raise(nameof(StatsVisible));
    }
}

/// <summary>
/// PP10: the overlay's rules where the Qt client states them.
/// </summary>
public static class StreamOverlaySource
{
    /// <summary>The stream screen.</summary>
    public const string StreamViewQml = @"gui\src\qml\StreamView.qml";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(StreamViewQml);

    /// <summary>Whether the black panel is still up for all three reasons.</summary>
    public static bool TheLoadingPanelHasThreeReasons(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "opacity: sessionError || sessionLoading || (Chiaki.settings.audioVideoDisabled & 0x02) ? 1.0 : 0.0",
            StringComparison.Ordinal);
    }

    /// <summary>Whether visibility is still bound to a real number rather than to a condition.</summary>
    public static bool VisibilityIsStillBoundToOpacity(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("visible: opacity\n", StringComparison.Ordinal)
            || qml.Contains("visible: opacity\r\n", StringComparison.Ordinal);
    }

    /// <summary>Whether the two error labels are still shown by their own text alone.</summary>
    public static bool TheErrorLabelsAreShownByTheirText(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("visible: text", StringComparison.Ordinal);
    }

    /// <summary>Whether the audio-alone hint is still an equality test rather than a bit test.</summary>
    public static bool TheAudioHintTestsTheWholeMask(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(Chiaki.settings.audioVideoDisabled == 0x01)", StringComparison.Ordinal);
    }

    /// <summary>Whether the missing-controller shortcut is still the literal Ctrl+O.</summary>
    public static bool TheKeyboardShortcutIsSpeltOut(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            $"Chiaki.settings.stringForStreamMenuShortcut() : \"{StreamOverlayViewModel.KeyboardMenuShortcut}\"",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the indicator still scales the SETTING rather than the measure.</summary>
    public static bool TheIndicatorScalesTheSetting(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "running: Chiaki.session?.averagePacketLoss > (Chiaki.settings.wifiDroppedNotif * 0.01)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the frame age is still read in seconds and printed in milliseconds.</summary>
    public static bool TheFrameAgeIsConvertedAtTheLabel(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "qsTr(\"%1 ms\").arg((Chiaki.window.pendingFrameAge * 1000.0).toFixed(0))",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the two averaged readouts are still guarded against a non-finite value.</summary>
    public static bool TheAveragesAreGuarded(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("isFinite(Chiaki.session.averagePacketLoss)", StringComparison.Ordinal)
            && qml.Contains("isFinite(Chiaki.session.framesLost)", StringComparison.Ordinal);
    }
}
