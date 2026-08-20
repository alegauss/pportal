using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Stream tab, whose twelve controls are one matrix and whose resolution has three
/// representations.
/// </summary>
public class StreamSettingsTests
{
    /// <summary>
    /// The finding: index, enum value and stored word are three different things for one setting.
    /// A port that skipped a layer writes a number where the client writes a word, and the client
    /// then falls back to its default.
    /// </summary>
    [Theory]
    [InlineData(0, 1, "360p")]
    [InlineData(1, 2, "540p")]
    [InlineData(2, 3, "720p")]
    [InlineData(3, 4, "1080p")]
    public void TheResolutionHasThreeRepresentations(int index, int preset, string stored)
    {
        Assert.Equal(preset, StreamResolution.PresetForIndex(index));
        Assert.Equal(index, StreamResolution.IndexForPreset(preset));
        Assert.Equal(stored, StreamResolution.StoredForPreset(preset));
        Assert.Equal(preset, StreamResolution.LocalPs4.PresetForStored(stored));

        // The index is not the stored word's position plus nothing - it is off by one from the enum.
        Assert.NotEqual(index, preset);
    }

    /// <summary>An unrecognised word is the row's default, not an error and not 360p.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("1440p")]
    public void AnUnrecognisedWordIsTheRowsDefault(string? stored)
    {
        Assert.Equal(3, StreamResolution.LocalPs4.PresetForStored(stored));
        Assert.Equal(4, StreamResolution.LocalPs5.PresetForStored(stored));
        Assert.Equal(3, StreamResolution.RemotePs5.PresetForStored(stored));
    }

    /// <summary>
    /// Remote PS5 defaults to 720p where local PS5 defaults to 1080p - the only pair that differs,
    /// and the reason the four rows cannot share one label list.
    /// </summary>
    [Fact]
    public void ThePs5DefaultsDifferByConnection()
    {
        Assert.Equal(4, StreamResolution.LocalPs5.DefaultPreset);
        Assert.Equal(3, StreamResolution.RemotePs5.DefaultPreset);
        Assert.Equal(3, StreamResolution.LocalPs4.DefaultPreset);
        Assert.Equal(3, StreamResolution.RemotePs4.DefaultPreset);

        // Which the table PP2 transcribed agrees with, word for word.
        Assert.Equal("1080p", Preferences.Find(StreamResolution.LocalPs5.Key)!.Default);
        Assert.Equal("720p", Preferences.Find(StreamResolution.RemotePs5.Key)!.Default);
    }

    /// <summary>
    /// Three distinct label lists across four rows, and the default marker sits on a different entry
    /// in each. One shared list marks the wrong one somewhere.
    /// </summary>
    [Fact]
    public void ThereAreThreeLabelListsForFourRows()
    {
        var lists = StreamSettingsViewModel.Rows
            .Select(row => string.Join("|", StreamResolution.For(row.Console, row.Network).Labels))
            .Distinct()
            .ToList();

        Assert.Equal(3, lists.Count);

        // And each row's marker is on its own default.
        foreach ((StreamConsole console, StreamNetwork network) in StreamSettingsViewModel.Rows)
        {
            StreamResolution row = StreamResolution.For(console, network);
            string marked = row.Labels[StreamResolution.IndexForPreset(row.DefaultPreset)];
            Assert.Contains("(Default)", marked);
        }
    }

    /// <summary>The frame rate is arithmetic: the store holds 30 or 60, the combo 0 or 1.</summary>
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    public void TheFrameRateIsArithmetic(int index, int rate)
    {
        Assert.Equal(rate, StreamFps.RateForIndex(index));
        Assert.Equal(index, StreamFps.IndexForRate(rate));

        // Storing the index instead asks the console for one frame per second.
        Assert.NotEqual(index, rate);
    }

    /// <summary>
    /// The bitrate is stored in kbps and shown in Mbps, and a stored ZERO means "follow the
    /// resolution" rather than "no bitrate".
    /// </summary>
    [Theory]
    [InlineData(1u, 2)]
    [InlineData(2u, 6)]
    [InlineData(3u, 10)]
    [InlineData(4u, 15)]
    public void AZeroBitrateFollowsTheResolution(uint preset, int expectedMbps)
    {
        Assert.Equal(expectedMbps, StreamBitrate.SliderValue(0, (int)preset));
        Assert.Equal(expectedMbps, StreamBitrate.DefaultMbpsFor((int)preset));

        // A real stored value wins over the default.
        Assert.Equal(30, StreamBitrate.SliderValue(30000, (int)preset));
        Assert.Equal(30000u, StreamBitrate.StoredFor(30));
    }

    /// <summary>
    /// The fallback tests the DIVISION for truthiness, not the stored value - so 500 kbps gives 0.5
    /// and is NOT a fallback, even though 0.5 is below the slider's own floor of 2.
    /// </summary>
    [Fact]
    public void OnlyAnExactZeroFallsBack()
    {
        Assert.Equal(0.5, StreamBitrate.SliderValue(500, 3));
        Assert.NotEqual(StreamBitrate.DefaultMbpsFor(3), StreamBitrate.SliderValue(500, 3));
        Assert.True(StreamBitrate.SliderValue(500, 3) < StreamBitrate.MinimumMbps);

        Assert.Equal(StreamBitrate.DefaultMbpsFor(3), StreamBitrate.SliderValue(0, 3));
    }

    /// <summary>
    /// The finding that spans the three: choosing a resolution also ZEROES the row's bitrate, which
    /// is how "follow the resolution" is written down. A port writing only the resolution leaves a
    /// bitrate tuned for the old one, with nothing on screen to say so.
    /// </summary>
    [Fact]
    public void ChoosingAResolutionZeroesTheRowsBitrate()
    {
        var model = new StreamSettingsViewModel();

        model.SetBitrateMbps(StreamConsole.Ps4, StreamNetwork.Local, 40);
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));

        // 360p, whose default is 2 Mbps.
        model.SetResolutionIndex(StreamConsole.Ps4, StreamNetwork.Local, 0);

        Assert.Equal(0u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(2, model.BitrateMbps(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(2, model.DefaultBitrateMbps(StreamConsole.Ps4, StreamNetwork.Local));
    }

    /// <summary>And it zeroes only that row's - the other three are untouched.</summary>
    [Fact]
    public void ItZeroesOnlyItsOwnRow()
    {
        var model = new StreamSettingsViewModel();

        foreach ((StreamConsole console, StreamNetwork network) in StreamSettingsViewModel.Rows)
            model.SetBitrateMbps(console, network, 40);

        model.SetResolutionIndex(StreamConsole.Ps5, StreamNetwork.Remote, 0);

        Assert.Equal(0u, model.StoredBitrate(StreamConsole.Ps5, StreamNetwork.Remote));
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps5, StreamNetwork.Local));
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Remote));
    }

    /// <summary>Choosing a frame rate does NOT zero anything, which is the contrast.</summary>
    [Fact]
    public void ChoosingAFrameRateTouchesNothingElse()
    {
        var model = new StreamSettingsViewModel();
        model.SetBitrateMbps(StreamConsole.Ps4, StreamNetwork.Local, 40);

        model.SetFpsIndex(StreamConsole.Ps4, StreamNetwork.Local, 0);

        Assert.Equal(30, model.Rate(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(40000u, model.StoredBitrate(StreamConsole.Ps4, StreamNetwork.Local));
    }

    /// <summary>An empty store gives the Qt defaults, per row.</summary>
    [Fact]
    public void AnEmptyStoreGivesTheQtDefaults()
    {
        var model = new StreamSettingsViewModel(new FakePreferences());

        Assert.Equal("720p", model.ResolutionStored(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal("720p", model.ResolutionStored(StreamConsole.Ps4, StreamNetwork.Remote));
        Assert.Equal("1080p", model.ResolutionStored(StreamConsole.Ps5, StreamNetwork.Local));
        Assert.Equal("720p", model.ResolutionStored(StreamConsole.Ps5, StreamNetwork.Remote));

        foreach ((StreamConsole console, StreamNetwork network) in StreamSettingsViewModel.Rows)
        {
            Assert.Equal(60, model.Rate(console, network));
            Assert.Equal(1, model.FpsIndex(console, network));
            Assert.Equal(0u, model.StoredBitrate(console, network));
        }

        // And a fresh install's bitrates are the resolution defaults, not zero on screen.
        Assert.Equal(10, model.BitrateMbps(StreamConsole.Ps4, StreamNetwork.Local));
        Assert.Equal(15, model.BitrateMbps(StreamConsole.Ps5, StreamNetwork.Local));
    }

    /// <summary>The console selector is dialog state, so it is not remembered.</summary>
    [Fact]
    public void TheConsoleSelectorIsNotAPreference()
    {
        var model = new StreamSettingsViewModel(new FakePreferences());

        Assert.Equal(StreamConsole.Ps4, model.SelectedConsole);
        Assert.True(model.Ps4Visible);
        Assert.False(model.Ps5Visible);

        model.SelectedConsole = StreamConsole.Ps5;
        Assert.False(model.Ps4Visible);
        Assert.True(model.Ps5Visible);

        // A second dialog starts on PS4 again - there is no key for it.
        Assert.Equal(StreamConsole.Ps4, new StreamSettingsViewModel(new FakePreferences()).SelectedConsole);
        Assert.Null(Preferences.All.Keys.FirstOrDefault(k => k.Contains("selected_console")));
    }

    /// <summary>Every rule above is still the Qt client's, in whichever file holds it.</summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? qmlPath = StreamSettingsSource.LocateQml();
        string? cppPath = GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        foreach ((StreamConsole console, StreamNetwork network) in StreamSettingsViewModel.Rows)
        {
            string where = $"{console}/{network}";

            Assert.True(StreamSettingsSource.ResolutionIsOffByOne(qml, console, network), where);
            Assert.True(StreamSettingsSource.ResolutionZeroesTheBitrate(qml, console, network), where);
            Assert.True(StreamSettingsSource.FpsIsArithmetic(qml, console, network), where);
            Assert.True(
                StreamSettingsSource.BitrateIsKbpsStoredAndMbpsShown(qml, console, network), where);
            Assert.True(StreamSettingsSource.TheStoreHoldsAWord(cpp, console, network), where);

            Assert.True(
                GeneralSettingsSource.ComboOffers(qml, StreamResolution.For(console, network).Labels),
                where + " labels");
        }

        Assert.True(StreamSettingsSource.TheBitrateDefaultsAre(qml), "bitrate defaults");
        Assert.True(StreamSettingsSource.ThePresetWordsAre(cpp), "preset words");
        Assert.True(GeneralSettingsSource.ComboOffers(qml, StreamFps.Labels), "fps labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qml, new[] { "PS4", "PS5" }), "console labels");
    }

    /// <summary>And the twelve property names are the ones PP142 read off the QML.</summary>
    [Fact]
    public void ThePropertyNamesAreTheQmlsOwn()
    {
        Assert.Equal(
            "resolutionLocalPS4", PreferenceNames.For(Preferences.Find(StreamResolution.LocalPs4.Key)!));
        Assert.Equal(
            "resolutionRemotePS5", PreferenceNames.For(Preferences.Find(StreamResolution.RemotePs5.Key)!));
        Assert.Equal(
            "fpsLocalPS4",
            PreferenceNames.For(Preferences.Find(StreamFps.KeyFor(StreamConsole.Ps4, StreamNetwork.Local))!));
        Assert.Equal(
            "bitrateRemotePS5",
            PreferenceNames.For(
                Preferences.Find(StreamBitrate.KeyFor(StreamConsole.Ps5, StreamNetwork.Remote))!));
    }
}
