using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>A store that answers from a dictionary, so a branch the user took can be exercised.</summary>
internal sealed class FakePreferences : IPreferences
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

    public FakePreferences Set(string key, object? value)
    {
        values[key] = value;
        return this;
    }

    private T Fallback<T>(string key, T stored)
    {
        if (values.TryGetValue(key, out object? value) && value is T typed)
            return typed;

        return stored;
    }

    public string? GetString(string key)
        => values.TryGetValue(key, out object? v)
            ? v as string
            : (string?)Preferences.Find(key)!.Default;

    public bool GetBool(string key)
        => Fallback(key, (bool)(Preferences.Find(key)!.Default ?? false));

    public int GetInt(string key)
        => Fallback(key, (int)(Preferences.Find(key)!.Default ?? 0));

    public uint GetUInt(string key)
        => Fallback(key, (uint)(Preferences.Find(key)!.Default ?? 0u));

    public double GetDouble(string key)
        => Fallback(key, (double)(Preferences.Find(key)!.Default ?? 0.0));

    public QRectValue? GetRect(string key) => null;

    public byte[]? GetBytes(string key) => null;
}

/// <summary>
/// PP16: the General tab, and the finding that makes it worth taking first - a combo index that
/// is not what the store holds.
/// </summary>
public class GeneralSettingsTests
{
    /// <summary>
    /// The index is an enum value and the store holds a string. Storing the index writes 2 where
    /// the Qt client writes "ask", and the Qt client then finds no key for it, falls back to its
    /// default, and the choice is gone - with nothing thrown and nothing logged.
    /// </summary>
    [Theory]
    [InlineData(0, "nothing")]
    [InlineData(1, "sleep")]
    [InlineData(2, "ask")]
    public void ADisconnectChoiceIsStoredAsAString(int index, string stored)
    {
        Assert.Equal(stored, ActionChoice.Disconnect.StoredFor(index));
        Assert.Equal(index, ActionChoice.Disconnect.IndexOf(stored));
    }

    [Theory]
    [InlineData(0, "nothing")]
    [InlineData(1, "sleep")]
    public void ASuspendChoiceIsStoredAsAString(int index, string stored)
    {
        Assert.Equal(stored, ActionChoice.Suspend.StoredFor(index));
        Assert.Equal(index, ActionChoice.Suspend.IndexOf(stored));
    }

    /// <summary>
    /// The two enums are not one. Index 0 is "nothing" in both, which is exactly what would make a
    /// shared converter look correct - and "ask" is a disconnect string only.
    /// </summary>
    [Fact]
    public void TheTwoActionsAreNotInterchangeable()
    {
        Assert.True(ActionChoice.Disconnect.Recognises("ask"));
        Assert.False(ActionChoice.Suspend.Recognises("ask"));

        // So a suspend setting holding "ask" is not index 2, it is the suspend default.
        Assert.Equal(ActionChoice.Suspend.DefaultIndex, ActionChoice.Suspend.IndexOf("ask"));
        Assert.Equal(2, ActionChoice.Disconnect.Labels.Count - 1);
    }

    /// <summary>
    /// An unrecognised string is the default, not an error - `QMap::key(v, default)`. A settings
    /// file from a newer version, or edited by hand, has to leave the screen usable.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("hibernate")]
    public void AnUnrecognisedStoredValueTakesTheDefault(string? stored)
    {
        Assert.Equal(ActionChoice.Disconnect.DefaultIndex, ActionChoice.Disconnect.IndexOf(stored));
        Assert.Equal(ActionChoice.Suspend.DefaultIndex, ActionChoice.Suspend.IndexOf(stored));
    }

    /// <summary>
    /// And the disconnect default is the LAST choice, not the first. An index-based port that
    /// defaulted to 0 would change what a fresh install does on disconnect from asking to nothing.
    /// </summary>
    [Fact]
    public void TheDisconnectDefaultIsAskAndTheSuspendDefaultIsNothing()
    {
        Assert.Equal("ask", ActionChoice.Disconnect.StoredFor(ActionChoice.Disconnect.DefaultIndex));
        Assert.Equal("Ask", ActionChoice.Disconnect.Labels[ActionChoice.Disconnect.DefaultIndex]);

        Assert.Equal("nothing", ActionChoice.Suspend.StoredFor(ActionChoice.Suspend.DefaultIndex));

        // The table PP2 transcribed agrees, which is the other half of the same statement.
        Assert.Equal("ask", Preferences.Find(ActionChoice.Disconnect.Key)!.Default);
        Assert.Equal("nothing", Preferences.Find(ActionChoice.Suspend.Key)!.Default);
    }

    /// <summary>A tab with no store shows the Qt defaults rather than zeroes.</summary>
    [Fact]
    public void AnEmptyStoreGivesTheQtDefaults()
    {
        var model = new GeneralSettingsViewModel(new FakePreferences());

        Assert.Equal(2, model.DisconnectIndex);
        Assert.Equal("ask", model.DisconnectStored);
        Assert.Equal(0, model.SuspendIndex);
        Assert.Equal("nothing", model.SuspendStored);
        Assert.Equal(0, model.AudioVideoDisabled);
        Assert.False(model.StreamerMode);
        Assert.True(model.StreamMenuEnabled);
        Assert.Equal(new[] { 9, 10, 11, 12 },
            new[] { model.Shortcut1, model.Shortcut2, model.Shortcut3, model.Shortcut4 });
    }

    /// <summary>And a store the user has touched is read rather than defaulted over.</summary>
    [Fact]
    public void AStoredChoiceIsRead()
    {
        var model = new GeneralSettingsViewModel(new FakePreferences()
            .Set("settings/disconnect_action", "sleep")
            .Set("settings/suspend_action", "sleep")
            .Set("settings/audio_video_disabled", 3)
            .Set("settings/streamer_mode", true)
            .Set("settings/stream_menu_enabled", false)
            .Set("settings/stream_menu_shortcut1", 1u));

        Assert.Equal(1, model.DisconnectIndex);
        Assert.Equal(1, model.SuspendIndex);
        Assert.Equal(3, model.AudioVideoDisabled);
        Assert.True(model.StreamerMode);
        Assert.False(model.StreamMenuEnabled);
        Assert.False(model.StreamMenuShortcutsVisible);
        Assert.Equal(1, model.Shortcut1);
    }

    /// <summary>
    /// Audio/Video is the contrast that makes the finding a finding: same tab, same kind of combo,
    /// and this one really is stored as the index it looks like.
    /// </summary>
    [Fact]
    public void AudioVideoIsStoredAsTheIndexItLooksLike()
    {
        Assert.Equal(QSettingsKind.Int, Preferences.Find("settings/audio_video_disabled")!.Kind);
        Assert.Equal(QSettingsKind.String, Preferences.Find(ActionChoice.Disconnect.Key)!.Kind);
        Assert.Equal(QSettingsKind.String, Preferences.Find(ActionChoice.Suspend.Key)!.Kind);
        Assert.Equal(4, GeneralSettingsViewModel.AudioVideoLabels.Count);
    }

    /// <summary>
    /// The three-way cross-check: the stored defaults are 9, 10, 11, 12; the list's entries at
    /// those positions are L1, R1, L3, R3; and the screen prints "(L1+R1+L3+R3)" beside the row.
    /// Reorder the list and two of the three still agree, which is why all three are held here.
    /// </summary>
    [Fact]
    public void TheShortcutDefaultsAreTheLabelTheScreenPrints()
    {
        Assert.Equal("(L1+R1+L3+R3)", new GeneralSettingsViewModel().DefaultShortcutHint());

        Assert.Equal(new[] { "L1", "R1", "L3", "R3" },
            new[] { 9, 10, 11, 12 }.Select(i => GeneralSettingsViewModel.ShortcutLabels[i]));

        if (GeneralSettingsSource.LocateQml() is null)
            return;

        Assert.Equal("(L1+R1+L3+R3)",
            GeneralSettingsSource.ShortcutHint(File.ReadAllText(GeneralSettingsSource.LocateQml()!)));
    }

    /// <summary>Unchecking the stream menu hides the four combos, which is the QML's own rule.</summary>
    [Fact]
    public void TheShortcutRowFollowsTheStreamMenu()
    {
        var model = new GeneralSettingsViewModel();
        Assert.True(model.StreamMenuShortcutsVisible);

        model.StreamMenuEnabled = false;
        Assert.False(model.StreamMenuShortcutsVisible);
    }

    /// <summary>Every rule above is still the Qt client's, in whichever of the three files holds it.</summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? cpp = GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);
        string? header = GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsHeader);
        string? qml = GeneralSettingsSource.LocateQml();
        if (cpp is null || header is null || qml is null)
            return;

        string cppText = File.ReadAllText(cpp);
        string headerText = File.ReadAllText(header);
        string qmlText = File.ReadAllText(qml);

        Assert.True(GeneralSettingsSource.StoredAsStrings(
            cppText, ActionChoice.Disconnect, "disconnect_action_values"), "disconnect strings");
        Assert.True(GeneralSettingsSource.StoredAsStrings(
            cppText, ActionChoice.Suspend, "suspend_action_values"), "suspend strings");

        // The index can only BE the enum value while the header's order holds.
        Assert.True(GeneralSettingsSource.EnumOrderIs(
            headerText, "DisconnectAction", "AlwaysNothing", "AlwaysSleep", "Ask"), "disconnect order");
        Assert.True(GeneralSettingsSource.EnumOrderIs(
            headerText, "SuspendAction", "Nothing", "Sleep"), "suspend order");

        Assert.True(GeneralSettingsSource.ComboOffers(qmlText, ActionChoice.Disconnect.Labels),
            "disconnect labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qmlText, ActionChoice.Suspend.Labels),
            "suspend labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qmlText, GeneralSettingsViewModel.AudioVideoLabels),
            "audio/video labels");
        Assert.True(GeneralSettingsSource.ComboOffers(qmlText, GeneralSettingsViewModel.ShortcutLabels),
            "shortcut labels");

        Assert.True(GeneralSettingsSource.TheShortcutRowFollowsTheStreamMenu(qmlText), "shortcut row");
    }

    /// <summary>
    /// And the names this tab binds are the ones PP142 read off the QML, not ones invented here.
    /// </summary>
    [Fact]
    public void TheTabsPreferencesCarryTheQmlsNames()
    {
        Assert.Equal("disconnectAction", PreferenceNames.For(Preferences.Find(ActionChoice.Disconnect.Key)!));
        Assert.Equal("suspendAction", PreferenceNames.For(Preferences.Find(ActionChoice.Suspend.Key)!));
        Assert.Equal("audioVideoDisabled",
            PreferenceNames.For(Preferences.Find("settings/audio_video_disabled")!));
        Assert.Equal("streamMenuEnabled",
            PreferenceNames.For(Preferences.Find("settings/stream_menu_enabled")!));

        for (int i = 0; i < GeneralSettingsViewModel.ShortcutKeys.Count; i++)
        {
            Assert.Equal($"streamMenuShortcut{i + 1}",
                PreferenceNames.For(Preferences.Find(GeneralSettingsViewModel.ShortcutKeys[i])!));
        }
    }
}
