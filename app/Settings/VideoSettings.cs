using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the Video tab's window-type combo, where the label and the stored string are DIFFERENT
/// words on half the rows.
///
/// The General tab found that a combo index can be stored as a string. This is the same mechanism
/// with the assumption removed: there, "ask" and "Ask" at least matched case-insensitively, so a
/// port that stored a lowercased label would have worked by luck. Here three of the six do not
/// match at all - the combo offers "Stream Resolution" and the store holds "Selected Resolution",
/// "Adjust Resolution Manually" against "Adjust Manually", and "Zoom [adjust zoom using slider in
/// stream menu]" against "Zoom".
///
/// So a port deriving the stored value from the label gets three rows right and three wrong, and
/// the three wrong ones fall back to Fullscreen - which is also the default, and therefore looks
/// like the setting simply not sticking.
/// </summary>
public static class WindowTypeChoice
{
    /// <summary>
    /// Window Type. Default is Fullscreen, which is index THREE - so neither the first choice nor
    /// the last, and an index-based port with a zero default silently changes what a fresh install
    /// does with the stream window.
    /// </summary>
    public static StoredChoice Window { get; } = new(
        "settings/window_type",
        new[]
        {
            "Stream Resolution",
            "Custom Resolution",
            "Adjust Resolution Manually",
            "Fullscreen",
            "Zoom [adjust zoom using slider in stream menu]",
            "Stretch",
        },
        // WindowType: SelectedResolution, CustomResolution, AdjustableResolution, Fullscreen, Zoom,
        // Stretch - settings.h's order, which is the combo's order. The strings are settings.cpp's
        // and are not the labels above.
        new[]
        {
            "Selected Resolution",
            "Custom Resolution",
            "Adjust Manually",
            "Fullscreen",
            "Zoom",
            "Stretch",
        },
        3);

    /// <summary>
    /// The index the custom-resolution fields are shown for. The QML spells it `windowType == 1`,
    /// a bare number, so the port names it once here rather than repeating the literal.
    /// </summary>
    public const int CustomResolution = 1;
}

/// <summary>
/// PP16: the hardware-decoder combo, whose first entry does not store its own name.
///
/// The list is built at runtime - availableDecoders() is "none", then whichever of vulkan, d3d11va
/// and cuda ffmpeg reports AND this build can actually open, then "auto" - so the port cannot carry
/// the entries. What it can carry, and has to, is the two rules around them.
///
/// The first is the finding. `onActivated: Chiaki.settings.decoder = index ? model[index] : ""` -
/// index 0 stores the EMPTY STRING, not the word "none" that the list shows. And the empty string
/// is not a placeholder: streamsession.cpp passes `hw_decoder.isEmpty() ? NULL : ...` to the
/// decoder, so empty is precisely how "no hardware decoder" reaches ffmpeg. A port that stored
/// model[0] uniformly writes "none", which is not empty, and is therefore handed to ffmpeg as a
/// device type that does not exist.
///
/// The second is that reading is lenient in the other direction:
/// `Math.max(0, model.indexOf(decoder))`. An unrecognised value - including the empty string, and
/// including a decoder that was available last run and is not now - shows as index 0. So the
/// display round-trips while the stored value changes from "auto" to "" if nobody notices, and
/// nothing on screen says the selection moved.
/// </summary>
public static class DecoderChoice
{
    /// <summary>settings/hw_decoder.</summary>
    public const string Key = "settings/hw_decoder";

    /// <summary>The first entry's label, which is NOT what selecting it stores.</summary>
    public const string NoneLabel = "none";

    /// <summary>What selecting the first entry actually stores, and what means "no hardware decoder".</summary>
    public const string NoneStored = "";

    /// <summary>The last entry, and the default.</summary>
    public const string Auto = "auto";

    /// <summary>
    /// The three device types the Qt client will offer if ffmpeg has them. A hard allow-list there,
    /// so a build that grew a fourth would not show it - which is why the port holds the same three
    /// rather than trusting whatever av_hwdevice_iterate_types returns.
    /// </summary>
    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "vulkan", "d3d11va", "cuda" };

    /// <summary>
    /// The list as availableDecoders() builds it, for whichever types are present. Unlike the audio
    /// device lists it also has a trailing entry, "auto", which is its default.
    /// </summary>
    public static IReadOnlyList<string> Available(IEnumerable<string> ffmpegTypes)
    {
        ArgumentNullException.ThrowIfNull(ffmpegTypes);

        var list = EmptyFirstChoice.Build(NoneLabel, ffmpegTypes.Where(Allowed.Contains)).ToList();
        list.Add(Auto);
        return list;
    }

    /// <summary>
    /// What the store receives for a chosen index. Index 0 is the empty string - the rule is
    /// <see cref="EmptyFirstChoice"/>'s, shared with the two audio device lists.
    /// </summary>
    public static string StoredFor(IReadOnlyList<string> available, int index)
        => EmptyFirstChoice.StoredFor(available, index);

    /// <summary>
    /// The index a stored value shows at: its position, or 0 for anything the list does not hold.
    /// Zero and not the default's index - which is what makes an unavailable decoder read as "none"
    /// rather than as "auto".
    /// </summary>
    public static int IndexOf(IReadOnlyList<string> available, string? stored)
        => EmptyFirstChoice.IndexOf(available, stored);

    /// <summary>
    /// Whether a stored value means "no hardware decoder" downstream, which is `isEmpty()` and
    /// nothing else - so the word "none" does NOT mean it.
    /// </summary>
    public static bool MeansNoHardwareDecoder(string? stored)
        => EmptyFirstChoice.MeansAutomatic(stored);
}

/// <summary>
/// PP16: the settings screen's Video tab.
///
/// Nine controls, and three of them carry something a port would not invent:
///
///   the window type stores words that are not its labels (<see cref="WindowTypeChoice"/>), and the
///   custom-resolution fields appear only for one of its six choices;
///
///   the decoder's first entry stores the empty string (<see cref="DecoderChoice"/>);
///
///   and Vertical Sync can END THE PROCESS. Its handler is
///   `if (runtimeRendererBackend === 1 && settings.restartApplication()) Qt.quit()` - and
///   restartApplication is not a question, it launches a detached copy of the executable and
///   returns whether that worked. So ticking a checkbox on one renderer backend relaunches the
///   application, and it is the only control on the screen that does anything of the kind.
///
/// The two resolution fields are PP140's <see cref="NumericSettingField"/> - commit on editing
/// finished, an invalid entry commits zero and clears the box, and the parse is JavaScript's - so
/// nothing about them is restated here beyond which key each writes.
/// </summary>
public sealed class VideoSettingsViewModel : DialogViewModel
{
    private int windowIndex = WindowTypeChoice.Window.DefaultIndex;
    private int decoderIndex;
    private IReadOnlyList<string> available = DecoderChoice.Available([]);
    private bool zeroCopy = true;
    private bool fullscreenDoubleClick;
    private bool hideCursor = true;
    private bool vSync;
    private string renderBackend = "vulkan";

    /// <summary>A tab with the Qt defaults, for a screen shown before a store is available.</summary>
    public VideoSettingsViewModel()
    {
        Width = new NumericSettingField();
        Height = new NumericSettingField();
        decoderIndex = DecoderChoice.IndexOf(available, DecoderChoice.Auto);
    }

    /// <summary>The tab as the store holds it, for whichever decoders this run can offer.</summary>
    public VideoSettingsViewModel(IPreferences preferences, IEnumerable<string>? ffmpegTypes = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        available = DecoderChoice.Available(ffmpegTypes ?? []);

        windowIndex = WindowTypeChoice.Window.IndexOf(
            preferences.GetString(WindowTypeChoice.Window.Key));
        decoderIndex = DecoderChoice.IndexOf(available, preferences.GetString(DecoderChoice.Key));

        zeroCopy = preferences.GetBool("settings/use_zero_copy");
        fullscreenDoubleClick = preferences.GetBool("settings/fullscreen_doubleclick");
        hideCursor = preferences.GetBool("settings/hide_cursor");
        vSync = preferences.GetBool("settings/vsync");
        renderBackend = preferences.GetString("settings/render_backend") ?? "vulkan";

        Width = new NumericSettingField
        {
            Text = preferences.GetUInt("settings/custom_resolution_width")
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        Width.Commit();

        // custom_resolution_LENGTH in the store, customResolutionHeight on the screen - PP142's
        // third rename, and the only place the two names disagree about the dimension.
        Height = new NumericSettingField
        {
            Text = preferences.GetUInt("settings/custom_resolution_length")
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        Height.Commit();
    }

    protected override string ButtonProperty => nameof(CustomResolutionVisible);

    /// <summary>Which window type is chosen, as the combo's index.</summary>
    public int WindowIndex
    {
        get => windowIndex;
        set
        {
            Set(ref windowIndex, value);
            Raise(nameof(WindowStored));
            Raise(nameof(CustomResolutionVisible));
        }
    }

    /// <summary>What the store must receive for it - one of settings.cpp's six words.</summary>
    public string WindowStored => WindowTypeChoice.Window.StoredFor(WindowIndex);

    /// <summary>
    /// Whether the two resolution fields are on screen. `windowType == 1` in the QML, and part of
    /// the rule rather than a layout state: choosing any other window type has to hide them.
    /// </summary>
    public bool CustomResolutionVisible => WindowIndex == WindowTypeChoice.CustomResolution;

    /// <summary>What the decoder combo offers this run.</summary>
    public IReadOnlyList<string> AvailableDecoders => available;

    /// <summary>Which decoder is chosen, as the combo's index.</summary>
    public int DecoderIndex
    {
        get => decoderIndex;
        set
        {
            Set(ref decoderIndex, value);
            Raise(nameof(DecoderStored));
        }
    }

    /// <summary>What the store receives - the empty string for the first entry, not "none".</summary>
    public string DecoderStored => DecoderChoice.StoredFor(available, DecoderIndex);

    public bool ZeroCopy
    {
        get => zeroCopy;
        set => Set(ref zeroCopy, value);
    }

    public bool FullscreenDoubleClick
    {
        get => fullscreenDoubleClick;
        set => Set(ref fullscreenDoubleClick, value);
    }

    public bool HideCursor
    {
        get => hideCursor;
        set => Set(ref hideCursor, value);
    }

    /// <summary>
    /// Vertical Sync. Setting it does NOT restart anything by itself - see
    /// <see cref="VSyncNeedsRestart"/>, which is the question the handler asks first.
    /// </summary>
    public bool VSync
    {
        get => vSync;
        set
        {
            Set(ref vSync, value);
            Raise(nameof(VSyncNeedsRestart));
        }
    }

    /// <summary>Which renderer is actually running, which decides whether vSync needs a restart.</summary>
    public string RenderBackend
    {
        get => renderBackend;
        set
        {
            Set(ref renderBackend, value ?? "");
            Raise(nameof(VSyncNeedsRestart));
        }
    }

    /// <summary>
    /// Whether changing vSync requires relaunching the application.
    ///
    /// The QML asks `runtimeRendererBackend === 1`, a bare number against a runtime value rather
    /// than the stored preference - so it is the renderer in use that decides, not the one
    /// configured. Named here so the screen never spells the 1.
    /// </summary>
    public bool VSyncNeedsRestart => RuntimeBackendIndex(RenderBackend) == OpenGlBackend;

    /// <summary>The backend index vSync is conditioned on.</summary>
    public const int OpenGlBackend = 1;

    /// <summary>
    /// The runtime backend as an index, which the QML compares numerically. Vulkan is 0 and OpenGL
    /// is 1, matching the order render_backend's own values are declared in.
    /// </summary>
    public static int RuntimeBackendIndex(string? backend) => backend switch
    {
        "vulkan" => 0,
        "opengl" => 1,
        _ => 0,
    };

    /// <summary>Custom resolution width, on PP140's commit-on-finish rules.</summary>
    public NumericSettingField Width { get; }

    /// <summary>Custom resolution height, stored under custom_resolution_LENGTH.</summary>
    public NumericSettingField Height { get; }

    /// <summary>The key the height field writes, which is not named after the height.</summary>
    public const string HeightKey = "settings/custom_resolution_length";
}

/// <summary>
/// PP16: the Video tab's rules where the Qt client states them.
/// </summary>
public static class VideoSettingsSource
{
    /// <summary>Where availableDecoders and the property bridge live.</summary>
    public const string QmlSettingsCpp = @"gui\src\qmlsettings.cpp";

    /// <summary>Where the empty decoder string becomes a NULL.</summary>
    public const string StreamSessionCpp = @"gui\src\streamsession.cpp";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether selecting the first decoder still stores the empty string.</summary>
    public static bool FirstDecoderStoresEmpty(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            @"onActivated: (index) => Chiaki.settings.decoder = index ? model[index] : """"",
            StringComparison.Ordinal);
    }

    /// <summary>Whether an unrecognised decoder still displays as the first entry.</summary>
    public static bool UnknownDecoderShowsAsFirst(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "currentIndex: Math.max(0, model.indexOf(Chiaki.settings.decoder))",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the empty decoder string is still what reaches ffmpeg as no decoder.</summary>
    public static bool EmptyDecoderMeansNull(string streamSession)
    {
        ArgumentNullException.ThrowIfNull(streamSession);
        return streamSession.Contains(
            "connect_info.hw_decoder.isEmpty() ? NULL :",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the decoder allow-list is still these three device types.</summary>
    public static bool DecoderAllowListIs(string qmlSettings, IReadOnlySet<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(qmlSettings);
        ArgumentNullException.ThrowIfNull(allowed);

        return allowed.All(name => qmlSettings.Contains($"\"{name}\",", StringComparison.Ordinal))
            && qmlSettings.Contains("QStringList out = {\"none\"};", StringComparison.Ordinal)
            && qmlSettings.Contains("out.append(\"auto\");", StringComparison.Ordinal);
    }

    /// <summary>Whether the custom-resolution fields are still shown for window type 1 only.</summary>
    public static int CustomResolutionVisibilityCount(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int count = 0;
        const string needle = "visible: Chiaki.settings.windowType == 1";
        for (int at = 0; (at = qml.IndexOf(needle, at, StringComparison.Ordinal)) >= 0; at += needle.Length)
            count++;

        return count;
    }

    /// <summary>
    /// Whether vSync still relaunches the application on one renderer backend. The condition and
    /// the quit are checked together, because either alone is harmless.
    /// </summary>
    public static bool VSyncStillRestartsTheApplication(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "if (Chiaki.window.runtimeRendererBackend === 1 && Chiaki.settings.restartApplication())",
            StringComparison.Ordinal)
            && qml.Contains("Qt.quit()", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether restartApplication still starts a detached copy rather than merely asking. It is
    /// named like a question and is not one, which is the reason to pin it.
    /// </summary>
    public static bool RestartLaunchesADetachedCopy(string qmlSettings)
    {
        ArgumentNullException.ThrowIfNull(qmlSettings);
        return qmlSettings.Contains(
            "QProcess::startDetached(application, arguments)", StringComparison.Ordinal);
    }
}
