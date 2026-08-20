using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Consoles tab - two lists that look alike and identify a row two different ways.
/// </summary>
public class ConsoleSettingsTests
{
    private static RegisteredHost Ps5(string name, byte last) => new()
    {
        ServerNickname = name,
        ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, last],
        Target = (int)ChiakiTarget.Ps5_1,
    };

    private static RegisteredHost Ps4(string name, byte last) => new()
    {
        ServerNickname = name,
        ServerMac = [0x90, 0x47, 0x48, 0x82, 0xfc, last],
        Target = (int)ChiakiTarget.Ps4_10,
    };

    private static HiddenHost HiddenOne(string name, byte last)
        => new(name, [0xaa, 0xbb, 0xcc, 0xdd, 0xee, last]);

    /// <summary>
    /// The caption leads with the ADDRESS and ends with the name - the opposite arrangement to the
    /// one a port would choose, and the generation sits between them.
    /// </summary>
    [Fact]
    public void TheCaptionLeadsWithTheAddressAndEndsWithTheName()
    {
        var model = new ConsoleSettingsViewModel();
        RegisteredHost host = Ps5("Living room", 0x29);

        Assert.Equal("90:47:48:82:fc:29 (PS5, Living room)", model.CaptionFor(host));
        Assert.StartsWith(host.MacText, model.CaptionFor(host));
        Assert.EndsWith("Living room)", model.CaptionFor(host));
    }

    /// <summary>The generation comes from the target, through PP147's threshold rather than a set.</summary>
    [Fact]
    public void TheGenerationComesFromTheTarget()
    {
        var model = new ConsoleSettingsViewModel();

        Assert.Contains("(PS5,", model.CaptionFor(Ps5("a", 1)));
        Assert.Contains("(PS4,", model.CaptionFor(Ps4("b", 2)));
    }

    /// <summary>
    /// Streamer mode replaces the ADDRESS and leaves the NAME - which is the opposite of what
    /// "hidden" suggests, and is the same substitution in both lists.
    /// </summary>
    [Fact]
    public void StreamerModeHidesTheAddressAndNotTheName()
    {
        var model = new ConsoleSettingsViewModel { StreamerMode = true };

        string registered = model.CaptionFor(Ps5("Living room", 0x29));
        Assert.StartsWith("hidden (", registered);
        Assert.Contains("Living room", registered);
        Assert.DoesNotContain("90:47", registered);

        string hidden = model.CaptionFor(HiddenOne("Spare", 0x01));
        Assert.StartsWith("hidden (", hidden);
        Assert.Contains("Spare", hidden);
        Assert.DoesNotContain("aa:bb", hidden);
    }

    /// <summary>And the hidden caption has no generation - two lists, two caption shapes.</summary>
    [Fact]
    public void TheHiddenCaptionHasNoGeneration()
    {
        var model = new ConsoleSettingsViewModel();

        Assert.Equal("aa:bb:cc:dd:ee:01 (Spare)", model.CaptionFor(HiddenOne("Spare", 0x01)));
        Assert.DoesNotContain("PS", model.CaptionFor(HiddenOne("Spare", 0x01)));
    }

    /// <summary>
    /// Auto-connect is a radio group made of N checkboxes over ONE string: ticking a second row
    /// overwrites the setting, so the first unticks itself without any code saying so.
    /// </summary>
    [Fact]
    public void TickingASecondRowUnticksTheFirst()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([Ps5("A", 1), Ps5("B", 2), Ps4("C", 3)], []);

        model.SetAutoConnect(0, true);
        Assert.True(model.IsAutoConnect(0));
        Assert.False(model.IsAutoConnect(1));
        Assert.Equal(0, model.AutoConnectIndex);

        model.SetAutoConnect(1, true);
        Assert.False(model.IsAutoConnect(0));
        Assert.True(model.IsAutoConnect(1));
        Assert.Equal(1, model.AutoConnectIndex);
    }

    /// <summary>Unticking clears the setting to the empty string, so no row is ticked.</summary>
    [Fact]
    public void UntickingClearsTheSetting()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([Ps5("A", 1), Ps5("B", 2)], []);

        model.SetAutoConnect(1, true);
        Assert.Equal("90:47:48:82:fc:02", model.AutoConnectMac);

        model.SetAutoConnect(1, false);
        Assert.Equal("", model.AutoConnectMac);
        Assert.Equal(-1, model.AutoConnectIndex);
    }

    /// <summary>
    /// The tick follows the CONSOLE and not the position, because the setting is a MAC. A list that
    /// comes back in a different order keeps the right row ticked.
    /// </summary>
    [Fact]
    public void TheTickFollowsTheConsoleNotThePosition()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([Ps5("A", 1), Ps5("B", 2), Ps4("C", 3)], []);

        model.SetAutoConnect(2, true);
        Assert.Equal(2, model.AutoConnectIndex);

        // The same three consoles, reordered.
        model.Load([Ps4("C", 3), Ps5("A", 1), Ps5("B", 2)], []);
        Assert.Equal(0, model.AutoConnectIndex);
        Assert.True(model.IsAutoConnect(0));
    }

    /// <summary>
    /// The finding: the two lists identify a row differently for the same kind of operation. Delete
    /// takes an INDEX and unhide takes a MAC.
    /// </summary>
    [Fact]
    public void DeleteIsPositionalAndUnhideIsKeyed()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load(
            [Ps5("A", 1), Ps5("B", 2)],
            [HiddenOne("X", 0x0a), HiddenOne("Y", 0x0b)]);

        // Positional: the argument is the row, and means nothing without the list.
        Assert.Equal(1, model.DeleteArgument(1));

        // Keyed: the argument identifies the console itself.
        Assert.Equal("aa:bb:cc:dd:ee:0b", model.UnhideArgument(1));

        // Which is why reordering breaks one and not the other.
        model.Load(
            [Ps5("B", 2), Ps5("A", 1)],
            [HiddenOne("Y", 0x0b), HiddenOne("X", 0x0a)]);

        Assert.Equal(1, model.DeleteArgument(1));                       // now means console A
        Assert.Equal("aa:bb:cc:dd:ee:0a", model.UnhideArgument(1));     // now means console X
    }

    /// <summary>An out-of-range unhide asks for nothing rather than throwing.</summary>
    [Fact]
    public void AnOutOfRangeUnhideIsEmpty()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([], []);

        Assert.Equal("", model.UnhideArgument(0));
        Assert.False(model.IsAutoConnect(0));
        Assert.Equal(-1, model.AutoConnectIndex);
    }

    /// <summary>The captions are refilled in place, PP159's rule, and follow streamer mode.</summary>
    [Fact]
    public void TheCaptionsRefillInPlaceAndFollowStreamerMode()
    {
        var model = new ConsoleSettingsViewModel();
        model.Load([Ps5("A", 1), Ps5("B", 2)], [HiddenOne("X", 0x0a)]);

        var registered = model.RegisteredCaptions;
        Assert.Equal(2, registered.Count);
        Assert.StartsWith("90:47", registered[0]);
        Assert.Single(model.HiddenCaptions);

        model.StreamerMode = true;

        // Same collection instance, new contents.
        Assert.Same(registered, model.RegisteredCaptions);
        Assert.StartsWith("hidden (", registered[0]);
        Assert.StartsWith("hidden (", model.HiddenCaptions[0]);

        // And a shorter list removes the surplus rather than emptying.
        model.Load([Ps5("A", 1)], []);
        Assert.Single(registered);
        Assert.Empty(model.HiddenCaptions);
    }

    /// <summary>Streamer mode is read from the store, since it is another tab's preference.</summary>
    [Fact]
    public void StreamerModeComesFromTheStore()
    {
        var off = new ConsoleSettingsViewModel(new FakePreferences(), [Ps5("A", 1)], []);
        Assert.False(off.StreamerMode);

        var on = new ConsoleSettingsViewModel(
            new FakePreferences().Set("settings/streamer_mode", true), [Ps5("A", 1)], []);

        Assert.True(on.StreamerMode);
        Assert.StartsWith("hidden (", on.RegisteredCaptions[0]);
    }

    /// <summary>Every rule above is still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (ConsoleSettingsSource.LocateQml() is null)
            return;

        string qml = File.ReadAllText(ConsoleSettingsSource.LocateQml()!);

        Assert.True(
            ConsoleSettingsSource.TheRegisteredCaptionLeadsWithTheAddress(qml), "registered caption");
        Assert.True(ConsoleSettingsSource.TheHiddenCaptionLeadsWithTheAddress(qml), "hidden caption");
        Assert.True(ConsoleSettingsSource.AutoConnectIsOneSettingPerRow(qml), "auto-connect");
        Assert.True(ConsoleSettingsSource.TheTwoListsIdentifyRowsDifferently(qml), "index vs mac");
        Assert.True(ConsoleSettingsSource.BothActionsConfirm(qml), "both confirm");
        Assert.True(ConsoleSettingsSource.TheListsHaveDifferentOwners(qml), "different owners");
    }

    /// <summary>
    /// And the auto-connect setting is BYTES in the store where the screen compares a hex string -
    /// the store's kind says so, and PP2's table is where that was written down.
    /// </summary>
    [Fact]
    public void TheAutoConnectSettingIsBytesInTheStore()
    {
        Assert.Equal(
            QSettingsKind.ByteArray, Preferences.Find("settings/auto_connect_mac")!.Kind);
        Assert.Equal("autoConnectMac", PreferenceNames.For(Preferences.Find("settings/auto_connect_mac")!));

        // The screen's side is the text form, which is what the comparison uses.
        Assert.Equal("90:47:48:82:fc:29", Ps5("A", 0x29).MacText);
    }
}
