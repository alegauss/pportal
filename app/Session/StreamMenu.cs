using System.Globalization;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP10: how the video fills the window.
///
/// The ORDER is the finding. qmlmainwindow.h declares Normal, Stretch, Zoom - and the menu draws
/// the buttons Zoom then Stretch, because that is the order the layout wanted. The value is what
/// reaches a setting, so a port numbering these from the row it can see stores 1 where it means 2.
/// </summary>
public enum StreamVideoMode
{
    Normal = 0,
    Stretch = 1,
    Zoom = 2,
}

/// <summary>
/// PP10: the renderer preset the menu switches between.
///
/// <see cref="Fast"/> is the whole reason this enum is transcribed rather than derived. It is
/// value 0 in qmlmainwindow.h and the stream menu HAS NO BUTTON FOR IT: the row starts at Default,
/// which is 1. A port numbering the five buttons from zero writes Fast where the user picked
/// Default and Custom where they picked HQ + Adv Spatial - every preset off by one, and the only
/// symptom is that the picture does not look like the name that is lit up.
/// </summary>
public enum StreamVideoPreset
{
    /// <summary>Declared, stored, and not offered anywhere on this menu.</summary>
    Fast = 0,
    Default = 1,
    HighQuality = 2,
    HighQualitySpatial = 3,
    HighQualityAdvancedSpatial = 4,
    Custom = 5,
}

/// <summary>
/// PP10: the in-stream menu - the half of the task the HUD's answer did not cover.
///
/// The HUD was a set of readouts. This is controls, and controls write, so every rule here is
/// about what a press changes and where it lands. Four are worth naming before the code:
///
/// 1. THE MIC BUTTON IS INVERTED. `checked: !session.muted` - the button is lit when the
///    microphone is LIVE. A port binding it to `muted` shows a muted mic as unmuted and vice
///    versa, and both states look deliberate.
///
/// 2. THE MODE IS NOT PERSISTED AND THE ZOOM FACTOR IS. Zoom and Stretch write
///    `window.videoMode` only; the zoom slider writes `window.ZoomFactor` AND
///    `settings.sZoomFactor`, and every preset button writes the window and the settings both. So
///    a session remembers how far it was zoomed and forgets that it was zoomed at all - which is
///    upstream's behaviour and is reproduced rather than corrected (no redesign while porting).
///
/// 3. THE TOGGLES RETURN TO NORMAL. Pressing Zoom while zoomed does not turn zoom off into
///    nothing; it selects Normal. Two buttons over one three-valued property, which is a radio
///    group wearing two checkboxes.
///
/// 4. THE ZOOM LABEL HAS THREE BRANCHES OVER TWO NUMBERS. -1 exactly is "No Black Bars";
///    zero and above is the value PLUS ONE; and between -1 and 0 the value is printed AS IS. The
///    offset appears in one of the two numeric branches and not the other.
/// </summary>
public sealed class StreamMenuViewModel : DialogViewModel
{
    private bool open;
    private bool sessionActive;
    private bool connected;
    private bool muted;
    private bool streamerMode;
    private string host = "";
    private int volume = AudioVolumeSetting.Maximum;
    private double zoomFactor;
    private long droppedFrames;
    private double measuredBitrate;
    private double packetLoss;
    private StreamVideoMode videoMode = StreamVideoMode.Normal;

    // HighQuality and NOT Default, though one of the six is literally named Default. PP17 read the
    // store: `placebo_preset_default` is PlaceboPreset::HighQuality, so a fresh install starts on
    // the third entry. Starting on the one called Default would have this menu light a different
    // button from the one the Qt client lights, on the same settings file.
    private StreamVideoPreset videoPreset = StreamVideoPreset.HighQuality;

    protected override string ButtonProperty => nameof(CloseGoesToTheMainView);

    /// <summary>The slider's floor, from the QML.</summary>
    public const double ZoomFactorMinimum = -1.0;

    /// <summary>And its ceiling.</summary>
    public const double ZoomFactorMaximum = 4.0;

    /// <summary>Whether the menu is up.</summary>
    public bool Open
    {
        get => open;
        set => Set(ref open, value);
    }

    /// <summary>Whether a session exists, which decides what the close button means.</summary>
    public bool SessionActive
    {
        get => sessionActive;
        set { Set(ref sessionActive, value); RaiseAll(); }
    }

    /// <summary>Whether that session is connected, which is what enables the mic button.</summary>
    public bool Connected
    {
        get => connected;
        set { Set(ref connected, value); RaiseAll(); }
    }

    /// <summary>Whether the microphone is muted. The button shows the OPPOSITE.</summary>
    public bool Muted
    {
        get => muted;
        set { Set(ref muted, value); Raise(nameof(MicOn)); }
    }

    /// <summary>Whether the console's name is to be hidden from a stream's viewers.</summary>
    public bool StreamerMode
    {
        get => streamerMode;
        set { Set(ref streamerMode, value); Raise(nameof(ConsoleCaption)); }
    }

    /// <summary>The console being streamed from.</summary>
    public string Host
    {
        get => host;
        set { Set(ref host, value ?? ""); Raise(nameof(ConsoleCaption)); }
    }

    /// <summary>The volume as the store holds it: 0 to 128, not a percentage.</summary>
    public int Volume
    {
        get => volume;
        set { Set(ref volume, value); Raise(nameof(VolumeCaption)); }
    }

    /// <summary>The zoom slider's position, which is an OFFSET and not a factor - see the label.</summary>
    public double ZoomFactor
    {
        get => zoomFactor;
        set { Set(ref zoomFactor, value); Raise(nameof(ZoomCaption)); }
    }

    /// <summary>The window's dropped-frame count, which gates its own label here.</summary>
    public long DroppedFrames
    {
        get => droppedFrames;
        set
        {
            Set(ref droppedFrames, value);
            // Both. The count decides whether the line is there AND what it says, and raising only
            // the visibility puts an empty label on screen the moment there is something to report
            // - which is the failure this screen exists to show.
            Raise(nameof(DroppedFramesVisible));
            Raise(nameof(DroppedFramesText));
        }
    }

    /// <summary>How the video fills the window.</summary>
    public StreamVideoMode VideoMode
    {
        get => videoMode;
        set { Set(ref videoMode, value); RaiseAll(); }
    }

    /// <summary>Which renderer preset is lit.</summary>
    public StreamVideoPreset VideoPreset
    {
        get => videoPreset;
        set { Set(ref videoPreset, value); RaiseAll(); }
    }

    /// <summary>
    /// Whether closing the menu leaves the stream or goes back to the console list. With a
    /// session the button closes the WINDOW; without one it asks for the main view. One button,
    /// two meanings, and nothing on it says which.
    /// </summary>
    public bool CloseGoesToTheMainView => !SessionActive;

    /// <summary>The mic button's lit state, which is the inverse of muted.</summary>
    public bool MicOn => SessionActive && !Muted;

    /// <summary>And it is pressable only while connected.</summary>
    public bool MicEnabled => SessionActive && Connected;

    /// <summary>The volume label. Percent on screen, 0..128 in the store.</summary>
    public string VolumeCaption
        => $"{(Volume / (double)AudioVolumeSetting.Maximum * 100).ToString("F0", CultureInfo.InvariantCulture)}% Volume";

    /// <summary>Whether the zoom slider is on show at all.</summary>
    public bool ZoomFactorVisible => VideoMode == StreamVideoMode.Zoom;

    /// <summary>Whether the Placebo button is, which is the Custom preset and only that.</summary>
    public bool PlaceboVisible => VideoPreset == StreamVideoPreset.Custom;

    /// <summary>
    /// The dropped-frames readout, shown only when the COUNT IS NON-ZERO - the QML tests the
    /// number itself for truth. The HUD's copy of the same readout shows always, so the two
    /// screens disagree about a value they read from one place.
    /// </summary>
    public bool DroppedFramesVisible => SessionActive && DroppedFrames != 0;

    /// <summary>The zoom label's three branches. -1 is a word; the rest is a number with a unit.</summary>
    public string ZoomCaption => ZoomCaptionFor(ZoomFactor);

    /// <summary>Mbps, as the session measures it.</summary>
    public double MeasuredBitrate
    {
        get => measuredBitrate;
        set { Set(ref measuredBitrate, value); Raise(nameof(Bitrate)); }
    }

    /// <summary>The measured loss, a fraction between 0 and 1.</summary>
    public double PacketLoss
    {
        get => packetLoss;
        set { Set(ref packetLoss, value); Raise(nameof(PacketLossText)); }
    }

    /// <summary>
    /// The same three readouts the HUD carries, through the same formatting.
    ///
    /// Shared deliberately: the menu and the HUD print the same values from the same source, and
    /// two copies of a unit conversion is two chances to convert one of them differently.
    /// </summary>
    public string Bitrate => StreamStats.Bitrate(MeasuredBitrate);

    public string PacketLossText => StreamStats.PacketLoss(PacketLoss);

    public string DroppedFramesText => StreamStats.DroppedFrames(DroppedFrames);

    /// <summary>
    /// The console line, which says Connected or Connecting and hides the name on request.
    ///
    /// The word "hidden" is the host's replacement and not an extra label: streamer mode swaps
    /// the value, so the sentence still reads as a sentence.
    /// </summary>
    public string ConsoleCaption
    {
        get
        {
            if (!SessionActive)
                return "";

            string name = StreamerMode ? "hidden" : Host;
            return (Connected ? "Connected to " : "Connecting to ") + name;
        }
    }

    /// <summary>
    /// Pressing Zoom or Stretch: the same mode turns back to Normal, a different one takes over.
    /// One three-valued property behind two checkboxes.
    /// </summary>
    public void Toggle(StreamVideoMode mode)
        => VideoMode = VideoMode == mode ? StreamVideoMode.Normal : mode;

    /// <summary>
    /// Pressing the mic button, which is `onToggled: session.muted = !session.muted`.
    ///
    /// The press flips MUTED and the lit state follows. Written this way round because the other
    /// way round is the inversion bug: a button that set its own checked state and then told the
    /// session about it would be lit correctly and mute the wrong way.
    /// </summary>
    public void ToggleMic() => Muted = !Muted;

    /// <summary>
    /// The label under the zoom slider, and the one rule here a reader would not guess.
    ///
    /// Exactly -1 is "No Black Bars" - the slider's floor is a named position rather than a
    /// number. From 0 up, the value is shown PLUS ONE, so 0 reads as 1.00x and the slider's top
    /// reads 5.00x. And in between - below zero but above the floor - the value is printed
    /// unchanged, negative sign and all. Reproduced, not tidied: the third branch is what the QML
    /// does, and a port that applied the offset everywhere would disagree with the Qt client on
    /// exactly the range where the picture is being cropped rather than magnified.
    /// </summary>
    public static string ZoomCaptionFor(double value)
    {
        // An EXACT comparison, and it is the QML's own `=== -1`. A tolerance here would claim "No
        // Black Bars" for a slider one step off the floor, which is a different picture with a
        // different name - and the floor is the only value a step of 0.01 can land on exactly.
#pragma warning disable S1244
        if (value == ZoomFactorMinimum)
#pragma warning restore S1244
            return "No Black Bars";

        double shown = value >= 0 ? value + 1 : value;
        return shown.ToString("F2", CultureInfo.InvariantCulture) + " x";
    }

    /// <summary>
    /// Whether picking this preset writes the setting as well as the window.
    ///
    /// The five with buttons do: each `onClicked` writes `Chiaki.window.videoPreset` AND
    /// `Chiaki.settings.videoPreset`, which is what makes the video MODE's silence about the store
    /// worth noticing. <see cref="StreamVideoPreset.Fast"/> is the exception, and it is one for the
    /// reason the enum already names - the menu has no button for it, so no press on this screen
    /// can store it.
    ///
    /// PP575: the body was `Enum.IsDefined`, which answers whether a value is a member of its enum.
    /// That is a different question, and the input it gets wrong is exactly Fast - the one preset
    /// this method exists to separate from the rest. It had no caller, so being wrong cost nothing;
    /// <see cref="StreamMenuSource.EveryPresetAgreesWithTheMenu"/> is the caller and the oracle.
    /// </summary>
    public static bool PresetIsPersisted(StreamVideoPreset preset)
        => Enum.IsDefined(preset) && preset != StreamVideoPreset.Fast;

    private void RaiseAll()
    {
        Raise(nameof(MicOn));
        Raise(nameof(MicEnabled));
        Raise(nameof(ZoomFactorVisible));
        Raise(nameof(PlaceboVisible));
        Raise(nameof(DroppedFramesVisible));
        Raise(nameof(ConsoleCaption));
    }
}

/// <summary>
/// PP10: the menu's rules where the Qt client states them.
/// </summary>
public static class StreamMenuSource
{
    /// <summary>The menu window.</summary>
    public const string MenuQml = @"gui\src\qml\StreamMenuWindow.qml";

    /// <summary>Where the two enums are declared, which is the half the QML does not show.</summary>
    public const string WindowHeader = @"gui\include\qmlmainwindow.h";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether the mic button is still lit for an UNMUTED microphone.</summary>
    public static bool TheMicButtonIsStillInverted(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("checked: Chiaki.session && !Chiaki.session.muted", StringComparison.Ordinal)
            && qml.Contains("enabled: Chiaki.session && Chiaki.session.connected", StringComparison.Ordinal);
    }

    /// <summary>Whether the mode is still written to the window alone, with no setting behind it.</summary>
    public static bool TheVideoModeIsNotPersisted(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                "Chiaki.window.videoMode = Chiaki.window.videoMode == ChiakiWindow.VideoMode.Zoom "
                + "? ChiakiWindow.VideoMode.Normal : ChiakiWindow.VideoMode.Zoom",
                StringComparison.Ordinal)
            && !qml.Contains("Chiaki.settings.videoMode", StringComparison.Ordinal);
    }

    /// <summary>And whether the zoom FACTOR beside it still is persisted, which is the contrast.</summary>
    public static bool TheZoomFactorIsPersisted(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Chiaki.settings.sZoomFactor = value", StringComparison.Ordinal);
    }

    /// <summary>Whether the zoom label still has three branches with the offset in one of them.</summary>
    public static bool TheZoomLabelOffsetsOnlyTheUpperBranch(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if (parent.value === -1)", StringComparison.Ordinal)
            && qml.Contains("qsTr((parent.value + 1).toFixed(2))", StringComparison.Ordinal)
            && qml.Contains("return qsTr(parent.value.toFixed(2))", StringComparison.Ordinal);
    }

    /// <summary>Whether the Placebo button is still tied to the Custom preset alone.</summary>
    public static bool ThePlaceboButtonNeedsTheCustomPreset(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "visible: Chiaki.window.videoPreset == ChiakiWindow.VideoPreset.Custom",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the close button still means two different things.</summary>
    public static bool TheCloseButtonHasTwoMeanings(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Chiaki.window.close();", StringComparison.Ordinal)
            && qml.Contains("streamMenuWindow.mainViewRequested();", StringComparison.Ordinal);
    }

    /// <summary>Whether the menu's dropped-frames line is still gated on the count being non-zero.</summary>
    public static bool TheDroppedLineNeedsANonZeroCount(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "opacity: parent.visible && Chiaki.window.droppedFrames ? 1.0 : 0.0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the menu still stores exactly the presets <see cref="StreamMenuViewModel"/> says it
    /// does - both writes present for each one it calls persisted, and neither write anywhere for
    /// the one it does not.
    ///
    /// This is <see cref="StreamMenuViewModel.PresetIsPersisted"/>'s oracle rather than a second
    /// copy of its rule: the model answers per preset and this holds each answer against the QML,
    /// so the two disagree in the suite rather than in a screenshot. PP575 - the method had no
    /// caller at all, which is what let a body answering a different question stay green.
    /// </summary>
    public static bool EveryPresetAgreesWithTheMenu(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        foreach (StreamVideoPreset preset in Enum.GetValues<StreamVideoPreset>())
        {
            bool onTheMenu = Assigns(qml, "window", preset) && Assigns(qml, "settings", preset);
            if (onTheMenu != StreamMenuViewModel.PresetIsPersisted(preset))
                return false;
        }

        return true;
    }

    /// <summary>
    /// One of the two writes a preset button makes, matched with the NAME ENDING where it does.
    ///
    /// `VideoPreset.HighQuality` is a prefix of `HighQualitySpatial` and of
    /// `HighQualityAdvancedSpatial`, so a plain Contains would still report the HighQuality button
    /// as present after it was deleted - three buttons answering for one.
    /// </summary>
    private static bool Assigns(string qml, string target, StreamVideoPreset preset)
    {
        string needle = $"Chiaki.{target}.videoPreset = ChiakiWindow.VideoPreset.{preset}";

        for (int at = qml.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = qml.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            int after = at + needle.Length;
            if (after >= qml.Length || !char.IsLetterOrDigit(qml[after]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the two enums are still in the order this port transcribed - Stretch before Zoom,
    /// and a Fast preset ahead of Default that no button on the menu offers.
    /// </summary>
    public static bool TheEnumsAreStillInThisOrder(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        int normal = header.IndexOf("Normal,", StringComparison.Ordinal);
        int stretch = header.IndexOf("Stretch,", StringComparison.Ordinal);
        int zoom = header.IndexOf("Zoom\n", StringComparison.Ordinal);
        if (zoom < 0)
            zoom = header.IndexOf("Zoom\r\n", StringComparison.Ordinal);

        int fast = header.IndexOf("Fast,", StringComparison.Ordinal);
        int def = header.IndexOf("Default,", StringComparison.Ordinal);

        return normal >= 0 && stretch > normal && zoom > stretch
            && fast >= 0 && def > fast;
    }
}
