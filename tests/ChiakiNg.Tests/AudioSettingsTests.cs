using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Audio and Wifi tab, where four controls carry a unit the store does not show.
/// </summary>
public class AudioSettingsTests
{
    /// <summary>
    /// The buffer is in three units at once: frames in the store, steps of 1920 on the slider, and
    /// milliseconds in the label.
    /// </summary>
    [Theory]
    [InlineData(1920u, 1)]
    [InlineData(9600u, 5)]
    [InlineData(19200u, 10)]
    public void TheBufferIsInThreeUnits(uint frames, int steps)
    {
        Assert.Equal(steps, AudioBuffer.StepsFor(frames));
        Assert.Equal(frames, AudioBuffer.FramesFor(steps));
        Assert.Equal($"{steps * 10} ms", AudioBuffer.Caption(steps));
    }

    /// <summary>A stored zero is the absence of a choice, and lands on the hint's 50 ms.</summary>
    [Fact]
    public void AZeroBufferFallsBackToFiftyMilliseconds()
    {
        Assert.Equal(AudioBuffer.DefaultSteps, AudioBuffer.StepsFor(0));
        Assert.Equal("50 ms", AudioBuffer.Caption(AudioBuffer.StepsFor(0)));
        Assert.Equal(0u, Preferences.Find(AudioBuffer.Key)!.Default);
    }

    /// <summary>
    /// And only an exact zero: the truthiness test is on the DIVISION, so 960 frames gives 0.5 -
    /// below the slider's own floor of 1 - rather than falling back.
    /// </summary>
    [Fact]
    public void OnlyAnExactZeroFallsBack()
    {
        Assert.Equal(0.5, AudioBuffer.StepsFor(960));
        Assert.True(AudioBuffer.StepsFor(960) < AudioBuffer.MinimumSteps);
        Assert.NotEqual(AudioBuffer.DefaultSteps, AudioBuffer.StepsFor(960));
    }

    /// <summary>
    /// The volume's conversion lives ONLY in the label: store and slider both run 0 to 128, and a
    /// port storing a percentage would be out by about 1.28 with nothing looking wrong.
    /// </summary>
    [Theory]
    [InlineData(128, "100% volume")]
    [InlineData(64, "50% volume")]
    [InlineData(0, "0% volume")]
    public void TheVolumesPercentageIsInTheLabelOnly(int stored, string caption)
    {
        Assert.Equal(caption, AudioVolumeSetting.Caption(stored));
        Assert.Equal(128, AudioVolumeSetting.Maximum);
        Assert.Equal(128, Preferences.Find(AudioVolumeSetting.Key)!.Default);
    }

    /// <summary>
    /// The sharp one: the reported-loss cap is a FRACTION in the store and a percent on screen, and
    /// the two are different types as well as different numbers - a double in the file, an int in
    /// the property.
    /// </summary>
    [Theory]
    [InlineData(0.05, 5)]
    [InlineData(0.1, 10)]
    [InlineData(1.0, 100)]
    [InlineData(0.0, 0)]
    public void ThePacketLossCapIsAFractionInTheStore(double stored, int percent)
    {
        Assert.Equal(percent, LossThresholds.PercentFromFraction(stored));
        Assert.Equal(stored, LossThresholds.FractionFromPercent(percent), 10);

        Assert.Equal(QSettingsKind.Double, Preferences.Find(LossThresholds.PacketLossKey)!.Kind);
        Assert.Equal(0.05, Preferences.Find(LossThresholds.PacketLossKey)!.Default);
    }

    /// <summary>
    /// And the wifi threshold beside it is NOT scaled - a whole percent everywhere. Two sliders on
    /// one tab, both labelled with a percent sign, and only one of them converts. The KEY is where
    /// the difference is written down.
    /// </summary>
    [Fact]
    public void TheWifiThresholdBesideItIsNotScaled()
    {
        Assert.Equal(QSettingsKind.UInt, Preferences.Find(LossThresholds.WifiDroppedKey)!.Kind);
        Assert.Equal(3u, Preferences.Find(LossThresholds.WifiDroppedKey)!.Default);
        Assert.EndsWith("_percent", LossThresholds.WifiDroppedKey);
        Assert.DoesNotContain("percent", LossThresholds.PacketLossKey);
    }

    /// <summary>
    /// The device lists lead with Auto and their first entry stores the EMPTY STRING - the Video
    /// tab's decoder rule, which is why it lives in one place now.
    /// </summary>
    [Fact]
    public void TheFirstDeviceStoresEmpty()
    {
        IReadOnlyList<string> list = EmptyFirstChoice.Build(
            AudioSettingsViewModel.AutoLabel, ["Speakers", "Headset"]);

        Assert.Equal(["Auto", "Speakers", "Headset"], list);
        Assert.Equal("", EmptyFirstChoice.StoredFor(list, 0));
        Assert.NotEqual(list[0], EmptyFirstChoice.StoredFor(list, 0));
        Assert.Equal("Headset", EmptyFirstChoice.StoredFor(list, 2));

        Assert.True(EmptyFirstChoice.MeansAutomatic(""));
        Assert.False(EmptyFirstChoice.MeansAutomatic("Auto"));
    }

    /// <summary>And it is the same rule the Video tab's decoder uses, not a copy of it.</summary>
    [Fact]
    public void TheDecoderAndTheDevicesShareOneRule()
    {
        IReadOnlyList<string> decoders = DecoderChoice.Available(["vulkan"]);

        Assert.Equal("", DecoderChoice.StoredFor(decoders, 0));
        Assert.Equal(EmptyFirstChoice.StoredFor(decoders, 0), DecoderChoice.StoredFor(decoders, 0));
        Assert.Equal(EmptyFirstChoice.IndexOf(decoders, "vulkan"), DecoderChoice.IndexOf(decoders, "vulkan"));
        Assert.Equal(
            EmptyFirstChoice.MeansAutomatic("none"), DecoderChoice.MeansNoHardwareDecoder("none"));
    }

    /// <summary>
    /// The device lists are enumerated when the tab BECOMES VISIBLE. A device plugged in while the
    /// application runs appears on the next visit and not before.
    /// </summary>
    [Fact]
    public void TheDevicesAreEnumeratedOnBecomingVisible()
    {
        var model = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/audio_out_device", "Speakers"),
            ["Speakers"],
            []);

        Assert.Equal(0, model.RefreshCount);
        Assert.Equal(1, model.OutputIndex);
        Assert.Equal("Speakers", model.OutputStored);

        model.BecameVisible(["Speakers", "Headset"], ["Microphone"]);

        Assert.Equal(1, model.RefreshCount);
        Assert.Equal(["Auto", "Speakers", "Headset"], model.OutputDevices);
        Assert.Equal("Speakers", model.OutputStored);   // the choice survived the re-enumeration
        Assert.Equal(["Auto", "Microphone"], model.InputDevices);
    }

    /// <summary>
    /// A device that went away reads as the first entry - which stores the empty string. So a
    /// re-enumeration can rewrite the setting without the user touching anything.
    /// </summary>
    [Fact]
    public void ADeviceThatWentAwayFallsBackToAuto()
    {
        var model = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/audio_out_device", "Headset"),
            ["Speakers", "Headset"],
            []);

        Assert.Equal("Headset", model.OutputStored);

        model.BecameVisible(["Speakers"], []);

        Assert.Equal(0, model.OutputIndex);
        Assert.Equal("", model.OutputStored);
        Assert.True(EmptyFirstChoice.MeansAutomatic(model.OutputStored));
    }

    /// <summary>
    /// The three speech controls are gated on the property EXISTING, which is a build-time feature
    /// gate written as a runtime type test. A port whose property always exists shows three controls
    /// that do nothing.
    /// </summary>
    [Fact]
    public void TheSpeechControlsNeedTheFeatureToBeCompiledIn()
    {
        var without = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/enable_speech_processing", true),
            speechAvailable: false);

        Assert.False(without.SpeechControlsVisible);
        Assert.False(without.SuppressionSlidersVisible);   // even though the setting is on

        var with = new AudioSettingsViewModel(
            new FakePreferences().Set("settings/enable_speech_processing", true),
            speechAvailable: true);

        Assert.True(with.SpeechControlsVisible);
        Assert.True(with.SuppressionSlidersVisible);
    }

    /// <summary>And the two sliders need the feature AND the checkbox - two conditions, not one.</summary>
    [Fact]
    public void TheSuppressionSlidersNeedBoth()
    {
        var model = new AudioSettingsViewModel(new FakePreferences());

        Assert.True(model.SpeechControlsVisible);
        Assert.False(model.SuppressionSlidersVisible);   // off by default

        model.SpeechProcessing = true;
        Assert.True(model.SuppressionSlidersVisible);

        model.SpeechProcessing = false;
        Assert.False(model.SuppressionSlidersVisible);
    }

    /// <summary>An empty store gives the Qt defaults, and the two suppression hints match them.</summary>
    [Fact]
    public void AnEmptyStoreGivesTheQtDefaults()
    {
        var model = new AudioSettingsViewModel(new FakePreferences());

        Assert.Equal(0, model.OutputIndex);
        Assert.Equal(0, model.InputIndex);
        Assert.Equal("50 ms", model.BufferCaption);
        Assert.Equal(128, model.Volume);
        Assert.Equal("100% volume", model.VolumeCaption);
        Assert.False(model.StartMicUnmuted);
        Assert.False(model.SpeechProcessing);
        Assert.Equal(6, model.NoiseSuppressDb);    // the tab prints "(6 dB)"
        Assert.Equal(30, model.EchoSuppressDb);    // and "(30 dB)"
        Assert.Equal(3, model.WifiDroppedPercent); // "(3%)"
        Assert.Equal(5, model.PacketLossPercent);  // "(5%)"
        Assert.Equal(0.05, model.PacketLossStored, 10);
        Assert.False(model.IdrOnFecFailure);
        Assert.False(model.ShowStreamStats);
    }

    /// <summary>Every rule above is still the Qt client's.</summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? qmlPath = AudioSettingsSource.LocateQml();
        string? bridgePath = VideoSettingsSource.Locate(VideoSettingsSource.QmlSettingsCpp);
        if (qmlPath is null || bridgePath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string bridge = File.ReadAllText(bridgePath);

        Assert.True(AudioSettingsSource.DevicesRefreshOnVisible(qml), "refresh on visible");

        Assert.True(AudioSettingsSource.TheDeviceListLeadsWithAuto(qml, "AudioOutDevices"), "out list");
        Assert.True(AudioSettingsSource.TheDeviceListLeadsWithAuto(qml, "AudioInDevices"), "in list");
        Assert.True(AudioSettingsSource.TheFirstDeviceStoresEmpty(qml, "audioOutDevice"), "out empty");
        Assert.True(AudioSettingsSource.TheFirstDeviceStoresEmpty(qml, "audioInDevice"), "in empty");

        Assert.True(AudioSettingsSource.TheBufferIsInFrames(qml), "buffer frames");
        Assert.True(AudioSettingsSource.TheBufferLabelComesFromTheSlider(qml), "buffer label");
        Assert.True(AudioSettingsSource.TheVolumeIsPercentInTheLabelOnly(qml), "volume label");

        // Three controls, and each one states the gate for itself - the label, the checkbox and its
        // hint, plus the two sliders and their labels and hints.
        Assert.True(AudioSettingsSource.SpeechGateCount(qml) >= 3, "speech gates");

        Assert.True(AudioSettingsSource.ThePacketLossCapIsScaled(bridge), "packet loss scaled");
        Assert.True(AudioSettingsSource.TheWifiThresholdIsNotScaled(bridge), "wifi not scaled");
    }

    /// <summary>And the property names are the ones PP142 read off the QML.</summary>
    [Fact]
    public void ThePropertyNamesAreTheQmlsOwn()
    {
        Assert.Equal("audioBufferSize", PreferenceNames.For(Preferences.Find(AudioBuffer.Key)!));
        Assert.Equal("audioVolume", PreferenceNames.For(Preferences.Find(AudioVolumeSetting.Key)!));
        Assert.Equal(
            "packetLossReportedMax", PreferenceNames.For(Preferences.Find(LossThresholds.PacketLossKey)!));
        Assert.Equal(
            "wifiDroppedNotifPercent",
            PreferenceNames.For(Preferences.Find(LossThresholds.WifiDroppedKey)!));
    }
}

