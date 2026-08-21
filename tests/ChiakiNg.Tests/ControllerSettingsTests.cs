using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Controllers tab, whose traps are a capital letter, a unit, and a band.
/// </summary>
public class ControllerSettingsTests
{
    /// <summary>
    /// The one label out of six that is not its own stored word, and it differs only in case. A
    /// port deriving the word from the label loses exactly this choice and no other.
    /// </summary>
    [Fact]
    public void VeryWeakIsStoredWithASmallW()
    {
        StoredChoice intensity = RumbleHapticsChoice.Intensity;

        Assert.Equal("Very Weak", intensity.Labels[1]);
        Assert.Equal("Very weak", intensity.StoredFor(1));

        // The other five are their labels exactly, which is what makes the odd one out easy to miss.
        Assert.Equal("Off", intensity.StoredFor(0));
        Assert.Equal("Weak", intensity.StoredFor(2));
        Assert.Equal("Normal", intensity.StoredFor(3));
        Assert.Equal("Strong", intensity.StoredFor(4));
        Assert.Equal("Very Strong", intensity.StoredFor(5));
    }

    /// <summary>
    /// And the store's own word comes back to the right row, where the capitalised one falls to
    /// Normal - which is the symptom, and it is a preference that resets rather than an error.
    /// </summary>
    [Fact]
    public void TheCapitalisedWordFallsBackToNormal()
    {
        StoredChoice intensity = RumbleHapticsChoice.Intensity;

        Assert.Equal(1, intensity.IndexOf("Very weak"));
        Assert.Equal(intensity.DefaultIndex, intensity.IndexOf("Very Weak"));
        Assert.Equal(3, intensity.DefaultIndex);
    }

    /// <summary>
    /// The four shortcut defaults spell the hint printed beside them. Read as labels rather than
    /// as numbers, because the numbers are indices into this list and mean nothing on their own.
    /// </summary>
    [Fact]
    public void TheFourDefaultsSpellTheHintBesideThem()
    {
        Assert.Equal("L1", DpadTouchShortcut.LabelFor(DpadTouchShortcut.Defaults[0]));
        Assert.Equal("R1", DpadTouchShortcut.LabelFor(DpadTouchShortcut.Defaults[1]));
        Assert.Equal("Dpad Up", DpadTouchShortcut.LabelFor(DpadTouchShortcut.Defaults[2]));
        Assert.Equal("Not Used", DpadTouchShortcut.LabelFor(DpadTouchShortcut.Defaults[3]));
    }

    /// <summary>
    /// The list is a subset of the controller's buttons, not all of them: no L2, no R2, no stick.
    /// A port storing a button value rather than a position in THIS list writes a number the Qt
    /// client reads as a different button.
    /// </summary>
    [Fact]
    public void TheShortcutListIsNotTheKeysTabsList()
    {
        Assert.Equal(17, DpadTouchShortcut.Buttons.Count);
        Assert.Equal("Not Used", DpadTouchShortcut.Buttons[0]);

        Assert.DoesNotContain("L2", DpadTouchShortcut.Buttons);
        Assert.DoesNotContain("R2", DpadTouchShortcut.Buttons);
        Assert.DoesNotContain("Left Stick Up", DpadTouchShortcut.Buttons);

        // The Keys tab's own list, which is longer and carries all of those.
        Assert.Equal(26, KeyMap.Defaults.Count);
    }

    /// <summary>Anything outside the list reads as nothing bound rather than as a crash.</summary>
    [Fact]
    public void AnUnknownShortcutIndexReadsAsNotUsed()
    {
        Assert.Equal("Not Used", DpadTouchShortcut.LabelFor(99));
        Assert.Equal("Not Used", DpadTouchShortcut.LabelFor(-1));
    }

    /// <summary>
    /// The increment is stored in hundredths of a millimetre. 30 is the 0.3 mm the tab prints as
    /// its default hint, and a port storing millimetres would move the pointer a hundredfold.
    /// </summary>
    [Fact]
    public void TheIncrementIsHundredthsOfAMillimetre()
    {
        Assert.Equal(30, DpadTouchIncrementSetting.Default);
        Assert.Equal("0.3 mm", DpadTouchIncrementSetting.Caption(DpadTouchIncrementSetting.Default));
        Assert.Equal("10.79 mm", DpadTouchIncrementSetting.Caption(DpadTouchIncrementSetting.Maximum));
        Assert.Equal("1 mm", DpadTouchIncrementSetting.Caption(100));
    }

    /// <summary>
    /// The haptic multiplier's middle is a BAND, and inside it the session skips the scaling
    /// entirely rather than multiplying by one. The label says so by dropping the number.
    /// </summary>
    [Theory]
    [InlineData(1.0, true, "console setting")]
    [InlineData(0.995, true, "console setting")]
    [InlineData(1.005, true, "console setting")]
    [InlineData(0.99, false, "99 % console setting")]
    [InlineData(1.01, false, "101 % console setting")]
    [InlineData(0.0, false, "0 % console setting")]
    [InlineData(2.0, false, "200 % console setting")]
    public void TheHapticBandIsExclusiveAtBothEnds(double value, bool inBand, string caption)
    {
        Assert.Equal(inBand, HapticOverrideSetting.IsConsoleSetting(value));
        Assert.Equal(caption, HapticOverrideSetting.Caption(value));
    }

    /// <summary>
    /// The five rows below the dpad checkbox appear and disappear together - four combos and a
    /// slider on one boolean, not one control changing.
    /// </summary>
    [Fact]
    public void TheDpadRowsFollowOneCheckbox()
    {
        var model = new ControllerSettingsViewModel();
        Assert.True(model.DpadTouchRowsVisible);

        model.ToggleDpadTouch();
        Assert.False(model.DpadTouchEnabled);
        Assert.False(model.DpadTouchRowsVisible);
    }

    /// <summary>
    /// The two checkboxes are written in different idioms and the two methods keep them apart: one
    /// assigns the control's state, the other flips the setting and ignores it.
    /// </summary>
    [Fact]
    public void TheTwoCheckboxesAreWrittenDifferently()
    {
        var model = new ControllerSettingsViewModel();

        model.SetBackgroundEventsFromCheckbox(false);
        Assert.False(model.BackgroundEvents);
        model.SetBackgroundEventsFromCheckbox(false);
        Assert.False(model.BackgroundEvents);

        // The other one takes no argument at all, so calling it twice returns where it started.
        model.ToggleDpadTouch();
        model.ToggleDpadTouch();
        Assert.True(model.DpadTouchEnabled);
    }

    /// <summary>A fresh tab carries the four defaults and the rumble word for Normal.</summary>
    [Fact]
    public void AFreshTabCarriesTheDefaults()
    {
        var model = new ControllerSettingsViewModel();

        Assert.Equal(DpadTouchShortcut.Defaults, model.Shortcuts);
        Assert.Equal("Normal", model.RumbleStored);
        Assert.Equal("console setting", model.HapticOverrideCaption);
        Assert.Equal("0.3 mm", model.DpadTouchIncrementCaption);
    }

    /// <summary>Setting a slot that does not exist changes nothing rather than throwing.</summary>
    [Fact]
    public void AnUnknownSlotIsIgnored()
    {
        var model = new ControllerSettingsViewModel();

        model.SetShortcut(4, 5);
        model.SetShortcut(-1, 5);

        Assert.Equal(DpadTouchShortcut.Defaults, model.Shortcuts);
    }

    /// <summary>Every rule above, still stated the same way in the three files it was read from.</summary>
    [Fact]
    public void TheControllersTabsRulesAreStillTheQtClients()
    {
        string? qmlPath = ControllerSettingsSource.LocateQml();
        string? cppPath = ControllerSettingsSource.LocateSettingsCpp();
        string? sessionPath = ControllerSettingsSource.LocateStreamSession();
        if (qmlPath is null || cppPath is null || sessionPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(ControllerSettingsSource.TheRumbleIntensityIsStoredAsAWord(cpp), "a word, not an index");
        Assert.True(ControllerSettingsSource.VeryWeakIsStillSpeltWithASmallW(cpp), "small w in the store");
        Assert.True(ControllerSettingsSource.TheComboStillShowsTheCapitalisedOne(qml), "capital W on screen");
        Assert.True(ControllerSettingsSource.TheFourShortcutDefaultsAreStillThese(cpp), "9, 10, 7, 0");
        Assert.True(ControllerSettingsSource.TheIncrementIsStoredInHundredths(cpp, qml), "hundredths");
        Assert.True(
            ControllerSettingsSource.TheHapticBandIsSharedWithTheSession(qml, File.ReadAllText(sessionPath)),
            "one band, two files");
        Assert.True(ControllerSettingsSource.TheTwoCheckboxesAreWrittenDifferently(qml), "two idioms");
    }
}
