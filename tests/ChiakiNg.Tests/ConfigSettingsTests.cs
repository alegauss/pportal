using ChiakiNg.Native;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Config tab, the ninth - a profile read from the wrong-looking store and a log switch
/// that subtracts rather than selects.
/// </summary>
public class ConfigSettingsTests
{
    /// <summary>
    /// The unnamed profile is shown as a word and stored as nothing. A port storing the word would
    /// create a profile actually called "default" beside the real one.
    /// </summary>
    [Fact]
    public void TheUnnamedProfileIsAWordOnScreenAndNothingInTheStore()
    {
        var model = new ConfigSettingsViewModel();

        Assert.Equal("", model.Profile);
        Assert.Equal("Current Profile: default", model.ProfileCaption);

        model.Profile = "couch";
        Assert.Equal("Current Profile: couch", model.ProfileCaption);
    }

    /// <summary>
    /// Verbose is a bit CLEARED from everything, not a level chosen. Debug survives it being off,
    /// which is what a threshold reading would get wrong.
    /// </summary>
    [Fact]
    public void VerboseSubtractsOneBitAndLeavesDebugAlone()
    {
        var model = new ConfigSettingsViewModel();

        Assert.False(model.VerboseLogs);
        Assert.False(model.LogMask.HasFlag(ChiakiLogLevel.Verbose));
        Assert.True(model.LogMask.HasFlag(ChiakiLogLevel.Debug));
        Assert.True(model.LogMask.HasFlag(ChiakiLogLevel.Error));

        model.VerboseLogs = true;
        Assert.Equal(ChiakiLogLevel.All, model.LogMask);
    }

    /// <summary>The two defaults are the ones printed in the checkboxes' own labels.</summary>
    [Fact]
    public void SanitisingIsOnAndVerboseIsOff()
    {
        var model = new ConfigSettingsViewModel();

        Assert.True(model.SanitizeLogs);
        Assert.False(model.VerboseLogs);
    }

    /// <summary>The About button builds its text from the application's name and an appended -ng.</summary>
    [Fact]
    public void TheAboutButtonAppendsNgToTheApplicationName()
        => Assert.Equal("About Chiaki-ng", ConfigSettingsViewModel.AboutCaption(QtPaths.Application));

    /// <summary>
    /// The profile is passed in rather than read from the tab's own store, which is the whole
    /// point: it lives in the DEFAULT settings, because it names the file everything else is in.
    /// </summary>
    [Fact]
    public void TheProfileComesFromOutsideTheTabsOwnStore()
    {
        var model = new ConfigSettingsViewModel(new StubPreferences(), "couch");

        Assert.Equal("couch", model.Profile);
        Assert.Equal("Current Profile: couch", model.ProfileCaption);
    }

    /// <summary>Every rule above, still stated the same way in the screen, the header and the store.</summary>
    [Fact]
    public void TheConfigTabsRulesAreStillTheQtClients()
    {
        string? qmlPath = ConfigSettingsSource.LocateQml();
        string? cppPath = ConfigSettingsSource.LocateSettingsCpp();
        string? headerPath = ConfigSettingsSource.LocateSettingsHeader();
        if (qmlPath is null || cppPath is null || headerPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(ConfigSettingsSource.TheProfileComesFromTheDefaultStore(cpp), "the other store");
        Assert.True(ConfigSettingsSource.TheUnnamedProfileIsShownAsAWord(qml), "a word on screen");
        Assert.True(ConfigSettingsSource.VerboseIsABitClearedFromAll(cpp), "ALL less one bit");
        Assert.True(
            ConfigSettingsSource.TheTwoLogDefaultsAreStillThese(File.ReadAllText(headerPath)),
            "on and off");
        Assert.True(ConfigSettingsSource.TheDefaultsAreInsideTheCheckboxText(qml), "hints inside");
        Assert.True(ConfigSettingsSource.TheAboutButtonAppendsNg(qml), "-ng appended");
    }

    /// <summary>A store that answers with every declared default, which is all this tab needs.</summary>
    private sealed class StubPreferences : IPreferences
    {
        public string? GetString(string key) => "";

        public bool GetBool(string key) => key == LogSwitches.SanitizeKey;

        public int GetInt(string key) => 0;

        public uint GetUInt(string key) => 0;

        public double GetDouble(string key) => 0;

        public QRectValue? GetRect(string key) => null;

        public byte[]? GetBytes(string key) => null;
    }
}
