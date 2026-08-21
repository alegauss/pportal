using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Keys tab, whose settings key is derived from a translatable string.
/// </summary>
public class KeySettingsTests
{
    /// <summary>
    /// The finding: the storage key is built from the DISPLAY NAME, and the display name comes from
    /// tr(). So the key a binding lives under depends on the interface language.
    /// </summary>
    [Theory]
    [InlineData(ChiakiControllerButton.Cross, "keymap/cross")]
    [InlineData(ChiakiControllerButton.DpadLeft, "keymap/d-pad_left")]
    [InlineData(ChiakiControllerButton.Ps, "keymap/ps")]
    [InlineData(ChiakiControllerButton.L2, "keymap/l2")]
    public void TheStorageKeyIsTheDisplayNameLowered(ChiakiControllerButton button, string key)
    {
        Assert.Equal(key, KeyMap.StorageKeyFor((int)button));
    }

    /// <summary>
    /// The space becomes an underscore and the HYPHEN survives - "D-Pad Left" is `d-pad_left`, so a
    /// port normalising punctuation would write a key the Qt client never reads.
    /// </summary>
    [Fact]
    public void OnlySpacesBecomeUnderscores()
    {
        string key = KeyMap.StorageKeyFor((int)ChiakiControllerButton.DpadRight);

        Assert.Equal("keymap/d-pad_right", key);
        Assert.Contains("-", key);
        Assert.DoesNotContain(" ", key);
    }

    /// <summary>
    /// The two X-axis names contradict their enum names: X_UP is "Right" and X_DOWN is "Left", while
    /// the Y axis agrees with itself. PP5's sign asymmetry, showing up as a label.
    /// </summary>
    [Fact]
    public void TheHorizontalAxisNamesAreInverted()
    {
        Assert.Equal("Left Stick Right", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickLeftXUp));
        Assert.Equal("Left Stick Left", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickLeftXDown));

        // The vertical pair does not invert.
        Assert.Equal("Left Stick Up", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickLeftYUp));
        Assert.Equal("Left Stick Down", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickLeftYDown));

        // Which means the storage keys invert too.
        Assert.Equal(
            "keymap/left_stick_right", KeyMap.StorageKeyFor((int)ControllerButtonExt.AnalogStickLeftXUp));
    }

    /// <summary>And the same inversion on the right stick.</summary>
    [Fact]
    public void TheRightStickInvertsTheSameWay()
    {
        Assert.Equal("Right Stick Right", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickRightXUp));
        Assert.Equal("Right Stick Left", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickRightXDown));
        Assert.Equal("Right Stick Up", KeyMap.NameOf((int)ControllerButtonExt.AnalogStickRightYUp));
    }

    /// <summary>
    /// The rows are ordered BY BUTTON VALUE, because a QMap is - not in the order the name table is
    /// written. A port listing buttons in a hand-chosen order would draw a different grid.
    /// </summary>
    [Fact]
    public void TheRowsAreOrderedByButtonValue()
    {
        var values = KeyMap.Defaults.Select(row => row.ButtonValue).ToList();

        Assert.Equal(values.OrderBy(v => v).ToList(), values);
        Assert.Equal(26, KeyMap.Defaults.Count);

        // Cross is 1 and therefore first; the stick half-axes are above 1<<18 and therefore last.
        Assert.Equal((int)ChiakiControllerButton.Cross, values[0]);
        Assert.True(values[^1] > (int)ChiakiControllerButton.R2);
    }

    /// <summary>
    /// The default key names are QKeySequence's spellings, abbreviations included: Escape is "Esc"
    /// and the bracket keys are the characters. A port writing "Escape" stores what the Qt client
    /// reads back as nothing.
    /// </summary>
    [Theory]
    [InlineData(ChiakiControllerButton.Ps, "Esc")]
    [InlineData(ChiakiControllerButton.Cross, "Return")]
    [InlineData(ChiakiControllerButton.Box, "\\")]
    [InlineData(ChiakiControllerButton.Moon, "Backspace")]
    public void TheDefaultKeyNamesAreQKeySequenceSpellings(ChiakiControllerButton button, string keyName)
    {
        Assert.Equal(keyName, KeyMap.DefaultKeyNameFor((int)button));
        Assert.NotEqual("Escape", KeyMap.DefaultKeyNameFor((int)ChiakiControllerButton.Ps));
    }

    /// <summary>The stick half-axes default to the bracket, sign and page keys, abbreviated too.</summary>
    [Fact]
    public void TheStickDefaultsAreCharactersAndAbbreviations()
    {
        Assert.Equal("]", KeyMap.DefaultKeyNameFor((int)ControllerButtonExt.AnalogStickLeftXUp));
        Assert.Equal("[", KeyMap.DefaultKeyNameFor((int)ControllerButtonExt.AnalogStickLeftXDown));
        Assert.Equal("Ins", KeyMap.DefaultKeyNameFor((int)ControllerButtonExt.AnalogStickLeftYUp));
        Assert.Equal("PgUp", KeyMap.DefaultKeyNameFor((int)ControllerButtonExt.AnalogStickRightYUp));
    }

    /// <summary>
    /// The map is defaults-then-overrides, so a store holding nothing still shows every row - the
    /// tab is never partly empty.
    /// </summary>
    [Fact]
    public void AnEmptyStoreStillShowsEveryRow()
    {
        IReadOnlyList<KeyBinding> rows = KeyMap.Read(_ => null);

        Assert.Equal(KeyMap.Defaults.Count, rows.Count);
        Assert.Equal(KeyMap.Defaults, rows);
    }

    /// <summary>And a stored binding replaces just that row.</summary>
    [Fact]
    public void AStoredBindingReplacesOneRow()
    {
        IReadOnlyList<KeyBinding> rows = KeyMap.Read(
            key => key == "keymap/cross" ? "Space" : null);

        KeyBinding cross = rows.First(r => r.ButtonValue == (int)ChiakiControllerButton.Cross);
        Assert.Equal("Space", cross.KeyName);

        KeyBinding moon = rows.First(r => r.ButtonValue == (int)ChiakiControllerButton.Moon);
        Assert.Equal("Backspace", moon.KeyName);
    }

    /// <summary>
    /// These keys are deliberately NOT in PP2's declared table, because their names are computed
    /// from a translatable string - so the set of keys is not knowable from the source. Asserted so
    /// the omission reads as a decision rather than as a gap in the transcription.
    /// </summary>
    [Fact]
    public void TheKeymapKeysAreOutsideTheDeclaredTable()
    {
        foreach (KeyBinding row in KeyMap.Defaults)
            Assert.Null(Preferences.Find(KeyMap.StorageKeyFor(row.ButtonValue)));

        // The table covers `settings/` and this prefix is not that.
        Assert.StartsWith("keymap/", KeyMap.KeyPrefix);
        Assert.DoesNotContain(
            Preferences.All.Keys, key => key.StartsWith("keymap/", StringComparison.Ordinal));

        // The two checkboxes on the same tab ARE declared, which is the contrast.
        Assert.NotNull(Preferences.Find("settings/keyboard_enabled"));
        Assert.NotNull(Preferences.Find("settings/mouse_touch_enabled"));
    }

    /// <summary>
    /// Rebinding returns the key and value the store should receive, and moves the row - in that
    /// order, because the QML's callback writes the label rather than re-reading the mapping.
    /// </summary>
    [Fact]
    public void RebindingMovesTheRowAndNamesTheStoreWrite()
    {
        var model = new KeySettingsViewModel();
        int index = model.Bindings.ToList().FindIndex(
            row => row.ButtonValue == (int)ChiakiControllerButton.Cross);

        (string key, string value) = model.Rebind(index, "Space");

        Assert.Equal("keymap/cross", key);
        Assert.Equal("Space", value);
        Assert.Equal("Space", model.Bindings[index].KeyName);

        // The button value and name are untouched - only the key moved.
        Assert.Equal((int)ChiakiControllerButton.Cross, model.Bindings[index].ButtonValue);
        Assert.Equal("Cross", model.Bindings[index].ButtonName);
    }

    /// <summary>An out-of-range rebind names nothing rather than throwing.</summary>
    [Fact]
    public void AnOutOfRangeRebindNamesNothing()
    {
        var model = new KeySettingsViewModel();
        Assert.Equal(("", ""), model.Rebind(-1, "Space"));
        Assert.Equal(("", ""), model.Rebind(999, "Space"));
    }

    /// <summary>
    /// Clear restores the defaults rather than emptying the grid - the map is initialised from
    /// defaults and then overridden, so clearing the store brings them back.
    /// </summary>
    [Fact]
    public void ClearRestoresTheDefaultsRatherThanEmptying()
    {
        var model = new KeySettingsViewModel();
        model.Rebind(0, "Space");
        Assert.Equal("Space", model.Bindings[0].KeyName);

        model.Clear();

        Assert.Equal(KeyMap.Defaults.Count, model.Bindings.Count);
        Assert.Equal(KeyMap.Defaults[0].KeyName, model.Bindings[0].KeyName);
    }

    /// <summary>The two checkboxes default to on, which the store's table says.</summary>
    [Fact]
    public void BothCheckboxesDefaultToOn()
    {
        var model = new KeySettingsViewModel(new FakePreferences());

        Assert.True(model.KeyboardEnabled);
        Assert.True(model.MouseTouchEnabled);
        Assert.Equal(true, Preferences.Find("settings/keyboard_enabled")!.Default);
        Assert.Equal(true, Preferences.Find("settings/mouse_touch_enabled")!.Default);
    }

    /// <summary>Every rule above is still the Qt client's.</summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? qmlPath = KeySettingsSource.LocateQml();
        string? cppPath = KeySettingsSource.LocateSettingsCpp();
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(KeySettingsSource.TheGridComesFromControllerMapping(qml), "the misnamed property");
        Assert.True(KeySettingsSource.TheStorageKeyComesFromTheDisplayName(cpp), "key from name");
        Assert.True(KeySettingsSource.TheDisplayNameIsTranslated(cpp), "name is translated");
        Assert.True(KeySettingsSource.TheHorizontalAxisNamesAreInverted(cpp), "x axis names");
        Assert.True(KeySettingsSource.TheMapStartsFromDefaults(cpp), "defaults then overrides");
        Assert.True(KeySettingsSource.TheCallbackWritesTheLabel(qml), "callback writes the label");
        Assert.True(KeySettingsSource.TheGridIsThreeColumns(qml), "three columns");
    }
}
