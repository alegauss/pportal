using System.Collections.ObjectModel;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the audio buffer, which is measured in three different units.
///
/// The store holds FRAMES. The slider is in units of 1920 frames, 1 to 10. And the label is in
/// MILLISECONDS, computed as the slider position times ten. So one number on screen, one on the
/// slider and a third in the file, with two conversions between them and neither written anywhere
/// but the binding.
///
/// A stored zero falls back to slider position 5, which the tab's hint prints as "(50 ms)". Same
/// truthiness-on-the-division as the Stream tab's bitrate: only an exact zero falls back, so a
/// stored 960 frames gives 0.5 - below the slider's own floor of 1.
///
/// The millisecond label is derived from the SLIDER, not from the frame count. This port reproduces
/// it rather than recomputing it: whether 1920 frames really is ten milliseconds depends on a sample
/// rate nothing here states, and a port that "corrected" the label would show a different number
/// from the client it shares a settings file with.
/// </summary>
public static class AudioBuffer
{
    /// <summary>The preference, in frames.</summary>
    public const string Key = "settings/audio_buffer_size";

    /// <summary>How many stored frames one slider step is worth.</summary>
    public const int FramesPerStep = 1920;

    /// <summary>The slider's bounds, in steps.</summary>
    public const int MinimumSteps = 1;

    public const int MaximumSteps = 10;

    /// <summary>The step a stored zero falls back to.</summary>
    public const int DefaultSteps = 5;

    /// <summary>What the label multiplies the slider position by.</summary>
    public const int MillisecondsPerStep = 10;

    /// <summary>Where the slider sits for a stored frame count.</summary>
    public static double StepsFor(uint storedFrames)
    {
        double steps = (double)storedFrames / FramesPerStep;
        return steps != 0 ? steps : DefaultSteps;
    }

    /// <summary>What the store receives when the slider moves.</summary>
    public static uint FramesFor(int steps) => (uint)(steps * FramesPerStep);

    /// <summary>The label, which is the slider position and not the frame count.</summary>
    public static string Caption(double steps)
        => $"{Math.Round(steps * MillisecondsPerStep)} ms";
}

/// <summary>
/// PP16: the output volume, stored 0-128 and shown as a percentage.
///
/// One conversion, in the label only - `(value / 128) * 100`. The store and the slider agree; it is
/// the number the user reads that differs, so a port storing a percentage would be out by a factor
/// of about 1.28 with nothing on screen looking wrong.
/// </summary>
public static class AudioVolumeSetting
{
    public const string Key = "settings/audio_volume";

    public const int Minimum = 0;

    /// <summary>Full volume, which the tab's hint calls 100%.</summary>
    public const int Maximum = 128;

    /// <summary>The label: the position as a percentage of <see cref="Maximum"/>.</summary>
    public static string Caption(double value)
        => $"{Math.Round(value / Maximum * 100)}% volume";
}

/// <summary>
/// PP16: a percentage on screen that is a FRACTION in the store.
///
/// packetLossReportedMax is the sharp one. qmlsettings multiplies by 100 on the way out and divides
/// by 100 on the way in, so the store holds 0.05 where the screen holds 5 and prints "5% packet
/// loss". Three layers again, and this time the middle one is a different TYPE as well as a
/// different number - a double in the file, an int in the property.
///
/// wifiDroppedNotif is beside it and is NOT converted: it is a whole percent everywhere. Two sliders
/// on the same tab, both labelled with a percent sign, and only one of them scales.
/// </summary>
public static class LossThresholds
{
    /// <summary>The one that scales. Stored as a fraction.</summary>
    public const string PacketLossKey = "settings/packet_loss_reported_max";

    /// <summary>
    /// The one that does not. Stored as a whole percent - and the KEY says so where the other's does
    /// not: `wifi_dropped_notif_percent` against `packet_loss_reported_max`. The name is the only
    /// place the difference is written down.
    /// </summary>
    public const string WifiDroppedKey = "settings/wifi_dropped_notif_percent";

    /// <summary>Both sliders run 0 to 100.</summary>
    public const int Minimum = 0;

    public const int Maximum = 100;

    /// <summary>What qmlsettings multiplies and divides by.</summary>
    public const int PercentPerUnit = 100;

    /// <summary>The percentage a stored fraction means. Truncated, as the static_cast is.</summary>
    public static int PercentFromFraction(double stored) => (int)(stored * PercentPerUnit);

    /// <summary>The fraction a percentage is stored as.</summary>
    public static double FractionFromPercent(int percent) => (double)percent / PercentPerUnit;
}

/// <summary>
/// PP16: the settings screen's Audio and Wifi tab.
///
/// Nine controls, and the interest is that four of them carry a unit conversion the store does not
/// show: the buffer has two, the volume has one in its label, and the packet-loss threshold has one
/// in the property bridge while the wifi threshold beside it has none.
///
/// Two more things are behaviour rather than layout:
///
///   the device lists are enumerated when the tab BECOMES VISIBLE, not at startup -
///   `onVisibleChanged: if (visible) refreshAudioDevices()`. So a device plugged in while the
///   application is running appears on the next visit to this tab and not before;
///
///   and the three speech-processing controls are hidden by `typeof ... !== "undefined"`. That is a
///   BUILD-time feature gate written as a runtime type test: the property exists only where the
///   speech library was compiled in. A port whose property always exists would show three controls
///   that do nothing, which is worse than not showing them.
/// </summary>
public sealed class AudioSettingsViewModel : DialogViewModel
{
    /// <summary>The label the two device lists lead with.</summary>
    public const string AutoLabel = "Auto";

    /// <summary>
    /// Brings a bound collection to match a new list, in place and WITHOUT emptying it.
    ///
    /// Clear-then-add would be simpler and loses the selection: an empty collection has no valid
    /// index, so the combo drops to -1 and re-adding the items does not bring it back. Replacing
    /// entry by entry and only removing a genuine surplus means a device that is still present keeps
    /// its position - which is the case that matters, because a re-enumeration usually returns the
    /// same devices.
    /// </summary>
    private static void Sync(ObservableCollection<string> target, IReadOnlyList<string> items)
    {
        for (int i = target.Count - 1; i >= items.Count; i--)
            target.RemoveAt(i);

        for (int i = 0; i < items.Count; i++)
        {
            if (i >= target.Count)
                target.Add(items[i]);
            else if (!string.Equals(target[i], items[i], StringComparison.Ordinal))
                target[i] = items[i];
        }
    }

        // Mutated, never replaced. Assigning a combo's ItemsSource resets SelectedIndex to -1 and the
    // two-way binding writes that back - the Stream tab measured it twice. An observable collection
    // whose contents change in place is bound once in the markup and never re-assigned, so the reset
    // cannot happen at all. That matters here and not on the other tabs because these two lists are
    // genuinely dynamic: they are re-enumerated every time the tab becomes visible.
    private readonly ObservableCollection<string> outputs = new(EmptyFirstChoice.Build(AutoLabel, []));
    private readonly ObservableCollection<string> inputs = new(EmptyFirstChoice.Build(AutoLabel, []));

    private int outputIndex;
    private int inputIndex;
    private double bufferSteps = AudioBuffer.DefaultSteps;
    private int volume = AudioVolumeSetting.Maximum;
    private bool startMicUnmuted;
    private bool speechProcessing;
    private int noiseSuppress = 6;
    private int echoSuppress = 30;
    private int wifiDroppedPercent = 3;
    private int packetLossPercent = 5;
    private bool idrOnFecFailure;
    private bool showStreamStats;
    private int refreshCount;

    /// <summary>A tab with the Qt defaults.</summary>
    public AudioSettingsViewModel()
    {
    }

    /// <summary>The tab as the store holds it, for whichever devices this run can offer.</summary>
    public AudioSettingsViewModel(
        IPreferences preferences,
        IEnumerable<string>? outputDevices = null,
        IEnumerable<string>? inputDevices = null,
        bool speechAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        SpeechProcessingAvailable = speechAvailable;

        Sync(outputs, EmptyFirstChoice.Build(AutoLabel, outputDevices ?? []));
        Sync(inputs, EmptyFirstChoice.Build(AutoLabel, inputDevices ?? []));

        outputIndex = EmptyFirstChoice.IndexOf(outputs, preferences.GetString("settings/audio_out_device"));
        inputIndex = EmptyFirstChoice.IndexOf(inputs, preferences.GetString("settings/audio_in_device"));

        bufferSteps = AudioBuffer.StepsFor(preferences.GetUInt(AudioBuffer.Key));
        volume = preferences.GetInt(AudioVolumeSetting.Key);
        startMicUnmuted = preferences.GetBool("settings/start_mic_unmuted");
        speechProcessing = preferences.GetBool("settings/enable_speech_processing");
        noiseSuppress = preferences.GetInt("settings/noise_suppress_level");
        echoSuppress = preferences.GetInt("settings/echo_suppress_level");

        wifiDroppedPercent = (int)preferences.GetUInt(LossThresholds.WifiDroppedKey);
        packetLossPercent = LossThresholds.PercentFromFraction(
            preferences.GetDouble(LossThresholds.PacketLossKey));

        idrOnFecFailure = preferences.GetBool("settings/idr_on_fec_failure");
        showStreamStats = preferences.GetBool("settings/show_stream_stats");
    }

    protected override string ButtonProperty => nameof(SpeechControlsVisible);

    /// <summary>
    /// Whether the speech-processing property exists in this build. The QML asks `typeof`, which is
    /// a build-time gate wearing a runtime test.
    /// </summary>
    public bool SpeechProcessingAvailable { get; init; } = true;

    /// <summary>What the output list offers, first entry included.</summary>
    public ObservableCollection<string> OutputDevices => outputs;

    /// <summary>What the input list offers.</summary>
    public ObservableCollection<string> InputDevices => inputs;

    /// <summary>How many times the device lists have been re-enumerated.</summary>
    public int RefreshCount => refreshCount;

    /// <summary>
    /// The tab becoming visible, which is the only thing that re-enumerates the devices. A device
    /// plugged in while the application runs appears on the next visit here and not before.
    /// </summary>
    public void BecameVisible(IEnumerable<string> outputDevices, IEnumerable<string> inputDevices)
    {
        ArgumentNullException.ThrowIfNull(outputDevices);
        ArgumentNullException.ThrowIfNull(inputDevices);

        string chosenOut = OutputStored;
        string chosenIn = InputStored;

        Sync(outputs, EmptyFirstChoice.Build(AutoLabel, outputDevices));
        Sync(inputs, EmptyFirstChoice.Build(AutoLabel, inputDevices));
        refreshCount++;

        // The chosen device is looked up again in the new list, so one that went away reads as the
        // first entry - which stores the empty string.
        outputIndex = EmptyFirstChoice.IndexOf(outputs, chosenOut);
        inputIndex = EmptyFirstChoice.IndexOf(inputs, chosenIn);

        Raise(nameof(OutputDevices));
        Raise(nameof(InputDevices));
        Raise(nameof(OutputIndex));
        Raise(nameof(InputIndex));
        Raise(nameof(OutputStored));
        Raise(nameof(InputStored));
    }

    public int OutputIndex
    {
        get => outputIndex;
        set
        {
            // A negative index is the ItemsSource reset, not a choice - refused for the same reason the
            // Stream tab refuses one.
            if (value < 0)
            {
                Raise(nameof(OutputIndex));
                return;
            }

            Set(ref outputIndex, value);
            Raise(nameof(OutputStored));
        }
    }

    public int InputIndex
    {
        get => inputIndex;
        set
        {
            if (value < 0)
            {
                Raise(nameof(InputIndex));
                return;
            }

            Set(ref inputIndex, value);
            Raise(nameof(InputStored));
        }
    }

    /// <summary>What the store receives for the output device - empty for the first entry.</summary>
    public string OutputStored => EmptyFirstChoice.StoredFor(outputs, OutputIndex);

    /// <summary>And for the input device.</summary>
    public string InputStored => EmptyFirstChoice.StoredFor(inputs, InputIndex);

    /// <summary>Where the buffer slider sits, in steps of 1920 frames.</summary>
    public double BufferSteps
    {
        get => bufferSteps;
        set
        {
            Set(ref bufferSteps, value);
            Raise(nameof(BufferFrames));
            Raise(nameof(BufferCaption));
        }
    }

    /// <summary>What the store receives, in frames.</summary>
    public uint BufferFrames => AudioBuffer.FramesFor((int)BufferSteps);

    /// <summary>What the label prints, in milliseconds derived from the slider.</summary>
    public string BufferCaption => AudioBuffer.Caption(BufferSteps);

    /// <summary>The volume, 0 to 128 in both the slider and the store.</summary>
    public int Volume
    {
        get => volume;
        set
        {
            Set(ref volume, value);
            Raise(nameof(VolumeCaption));
        }
    }

    /// <summary>The volume as a percentage, which is the only place the conversion happens.</summary>
    public string VolumeCaption => AudioVolumeSetting.Caption(Volume);

    public bool StartMicUnmuted
    {
        get => startMicUnmuted;
        set => Set(ref startMicUnmuted, value);
    }

    /// <summary>Noise suppression and echo cancellation, together, on one checkbox.</summary>
    public bool SpeechProcessing
    {
        get => speechProcessing;
        set
        {
            Set(ref speechProcessing, value);
            Raise(nameof(SuppressionSlidersVisible));
        }
    }

    /// <summary>Whether the checkbox and its hint are on screen at all.</summary>
    public bool SpeechControlsVisible => SpeechProcessingAvailable;

    /// <summary>
    /// Whether the two suppression sliders are on screen: the build has the feature AND the user
    /// turned it on. Two conditions, and the QML spells both in one expression per control.
    /// </summary>
    public bool SuppressionSlidersVisible => SpeechProcessingAvailable && SpeechProcessing;

    public int NoiseSuppressDb
    {
        get => noiseSuppress;
        set
        {
            Set(ref noiseSuppress, value);
            Raise(nameof(NoiseSuppressCaption));
        }
    }

    public int EchoSuppressDb
    {
        get => echoSuppress;
        set
        {
            Set(ref echoSuppress, value);
            Raise(nameof(EchoSuppressCaption));
        }
    }

    /// <summary>The weak-wifi threshold, a whole percent everywhere.</summary>
    public int WifiDroppedPercent
    {
        get => wifiDroppedPercent;
        set
        {
            Set(ref wifiDroppedPercent, value);
            Raise(nameof(WifiDroppedCaption));
        }
    }

    /// <summary>The reported-loss cap, a percent here and a fraction in the store.</summary>
    public int PacketLossPercent
    {
        get => packetLossPercent;
        set
        {
            Set(ref packetLossPercent, value);
            Raise(nameof(PacketLossStored));
            Raise(nameof(PacketLossCaption));
        }
    }

    /// <summary>What the store receives for it: the percentage over a hundred.</summary>
    public double PacketLossStored => LossThresholds.FractionFromPercent(PacketLossPercent);

    // The four remaining captions, on the model with the other two rather than as StringFormat in
    // the markup. Not only for consistency: ">= %1% dropped packets" cannot be written as a
    // StringFormat inside a markup extension at all, because the parser reads the `=` as a
    // name-value separator. A caption that has to live in one place is better than three that live
    // in the markup and one that cannot.

    /// <summary>`>= %1% dropped packets`, as the QML spells it.</summary>
    public string WifiDroppedCaption => $">= {WifiDroppedPercent}% dropped packets";

    /// <summary>`%1% packet loss`.</summary>
    public string PacketLossCaption => $"{PacketLossPercent}% packet loss";

    /// <summary>`%1 dB`, for the noise slider.</summary>
    public string NoiseSuppressCaption => $"{NoiseSuppressDb} dB";

    /// <summary>And for the echo slider.</summary>
    public string EchoSuppressCaption => $"{EchoSuppressDb} dB";

    public bool IdrOnFecFailure
    {
        get => idrOnFecFailure;
        set => Set(ref idrOnFecFailure, value);
    }

    public bool ShowStreamStats
    {
        get => showStreamStats;
        set => Set(ref showStreamStats, value);
    }
}

/// <summary>
/// PP16: the Audio tab's rules where the Qt client states them.
/// </summary>
public static class AudioSettingsSource
{
    /// <summary>The settings screen, or null outside a checkout.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Whether the devices are still re-enumerated on the tab becoming visible.</summary>
    public static bool DevicesRefreshOnVisible(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "onVisibleChanged: if (visible) Chiaki.settings.refreshAudioDevices()",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a device list is still Auto plus what the machine reported.</summary>
    public static bool TheDeviceListLeadsWithAuto(string qml, string property)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(property);

        return qml.Contains(
                $"model: [qsTr(\"Auto\")].concat(Chiaki.settings.available{property})",
                StringComparison.Ordinal);
    }

    /// <summary>Whether picking the first device still stores the empty string.</summary>
    public static bool TheFirstDeviceStoresEmpty(string qml, string property)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(property);

        return qml.Contains(
                $"currentIndex: Math.max(0, model.indexOf(Chiaki.settings.{property}))",
                StringComparison.Ordinal)
            && qml.Contains(
                $"Chiaki.settings.{property} = index ? model[index] : \"\"", StringComparison.Ordinal);
    }

    /// <summary>Whether the buffer is still frames in the store and steps of 1920 on the slider.</summary>
    public static bool TheBufferIsInFrames(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        return qml.Contains(
                $"Chiaki.settings.audioBufferSize / {AudioBuffer.FramesPerStep} ? "
                    + $"(Chiaki.settings.audioBufferSize / {AudioBuffer.FramesPerStep}) : "
                    + $"{AudioBuffer.DefaultSteps}",
                StringComparison.Ordinal)
            && qml.Contains(
                $"Chiaki.settings.audioBufferSize = value * {AudioBuffer.FramesPerStep};",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the buffer's label is still the slider position times ten.</summary>
    public static bool TheBufferLabelComesFromTheSlider(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            $"(parent.value * {AudioBuffer.MillisecondsPerStep}).toFixed(0) + qsTr(\" ms\")",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the volume is still 0-128 with the percentage only in the label.</summary>
    public static bool TheVolumeIsPercentInTheLabelOnly(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains($"to: {AudioVolumeSetting.Maximum}", StringComparison.Ordinal)
            && qml.Contains("value: Chiaki.settings.audioVolume", StringComparison.Ordinal)
            && qml.Contains(
                $"((parent.value / {AudioVolumeSetting.Maximum}.0) * 100).toFixed(0) + qsTr(\"% volume\")",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the three speech controls are still gated on the property existing.</summary>
    public static int SpeechGateCount(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int count = 0;
        const string needle = "typeof Chiaki.settings.speechProcessing !== \"undefined\"";
        for (int at = 0; (at = qml.IndexOf(needle, at, StringComparison.Ordinal)) >= 0; at += needle.Length)
            count++;

        return count;
    }

    /// <summary>Whether the reported-loss cap is still scaled in the property bridge.</summary>
    public static bool ThePacketLossCapIsScaled(string qmlSettingsCpp)
    {
        ArgumentNullException.ThrowIfNull(qmlSettingsCpp);

        return qmlSettingsCpp.Contains(
                $"return static_cast<int>(settings->GetPacketLossReportedMax() * {LossThresholds.PercentPerUnit});",
                StringComparison.Ordinal)
            && qmlSettingsCpp.Contains(
                $"static_cast<float>(packet_loss_reported_max) / {LossThresholds.PercentPerUnit}.0f;",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the wifi threshold beside it is still NOT scaled.</summary>
    public static bool TheWifiThresholdIsNotScaled(string qmlSettingsCpp)
    {
        ArgumentNullException.ThrowIfNull(qmlSettingsCpp);

        // PP272: the getter has to BE there before the absence of scaling around it says anything.
        // An empty file has no scaling in it either, and would otherwise answer yes.
        if (!qmlSettingsCpp.Contains("GetWifiDroppedNotif", StringComparison.Ordinal))
            return false;

        return qmlSettingsCpp.Contains(
                "return settings->GetWifiDroppedNotif();", StringComparison.Ordinal)
            || !qmlSettingsCpp.Contains("GetWifiDroppedNotif() * 100", StringComparison.Ordinal);
    }
}





