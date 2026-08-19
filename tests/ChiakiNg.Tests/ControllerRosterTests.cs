using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP128: what a hotplug event triggers, which is the part that can be checked without a pad.
///
/// The event itself cannot be - PP118 measured SDL rewriting `which` on any device event a caller
/// pushes, so a synthesised arrival is not a pad. Everything downstream of it is ordinary logic
/// over a device list, and that is where the two mistakes live: keying the roster on positions
/// instead of identities, and letting a buttonless motion controller count as a pad.
/// </summary>
public class ControllerRosterTests
{
    private const string MotionGuid = "030000004c0500006802000011010000";
    private static readonly HashSet<string> Motion = new(StringComparer.Ordinal) { MotionGuid };

    private static SdlDevice Pad(int instanceId, int buttons = 15, string guid = "0000ffff0000ffff0000ffff00000000")
        => new(IsGameController: true, guid, buttons, instanceId);

    [Fact]
    public void AnEmptyRosterStartsEmptyAndReportsNoChange()
    {
        var roster = new ControllerRoster(Motion);

        Assert.False(roster.Reconcile([]));
        Assert.Empty(roster.Attached);
    }

    [Fact]
    public void AnArrivalIsAChangeAndTheSameSetAgainIsNot()
    {
        var roster = new ControllerRoster(Motion);

        Assert.True(roster.Reconcile([Pad(7)]));
        Assert.Equal<int[]>([7], [.. roster.Attached]);

        // Only on a difference. The poll runs every 4ms (PP118's interval), so a roster that
        // reported every reconcile would redraw every screen bound to it 250 times a second.
        Assert.False(roster.Reconcile([Pad(7)]));
    }

    /// <summary>
    /// The roster is keyed by INSTANCE ID, so the same pad at a different index is the same pad.
    ///
    /// This is the mistake worth guarding: unplugging the first of two controllers shifts every
    /// index after it, and a roster keyed on positions would report the survivor as having left
    /// and a new one as having arrived - closing the wrong handle in between.
    /// </summary>
    [Fact]
    public void ThePadIsIdentifiedByItsInstanceIdAndNotItsPosition()
    {
        var roster = new ControllerRoster(Motion);

        roster.Reconcile([Pad(4), Pad(9)]);

        // Pad 4 leaves, so pad 9 moves from index 1 to index 0. Its identity did not change.
        Assert.True(roster.Reconcile([Pad(9)]));
        Assert.Equal<int[]>([9], [.. roster.Attached]);

        // And enumerating it at a different position again is not a change at all.
        Assert.False(roster.Reconcile([Pad(9)]));
    }

    [Fact]
    public void ADeviceSdlHasNoMappingForIsNotAPad()
    {
        var roster = new ControllerRoster(Motion);

        Assert.False(roster.Reconcile([new SdlDevice(false, "0000ffff0000ffff0000ffff00000000", 15, 3)]));
        Assert.Empty(roster.Attached);
    }

    /// <summary>
    /// A motion controller with no buttons is skipped. SDL reports it as a game controller
    /// because it has a mapping for it, and leaving it in shows a pad nobody can press anything
    /// on - and tells the console a controller is connected.
    /// </summary>
    [Fact]
    public void AButtonlessMotionControllerIsNotAPad()
    {
        var roster = new ControllerRoster(Motion);

        Assert.False(roster.Reconcile([Pad(5, buttons: 0, guid: MotionGuid)]));
        Assert.Empty(roster.Attached);
    }

    /// <summary>
    /// And the filter is narrow in both directions: the SAME GUID with buttons is a real pad, and
    /// a buttonless device with an ordinary GUID is left alone. This is a list of things known to
    /// enumerate as controllers they are not, rather than a rule about empty devices.
    /// </summary>
    [Fact]
    public void TheFilterNeedsBothTheGuidAndTheAbsentButtons()
    {
        var roster = new ControllerRoster(Motion);

        Assert.True(roster.Reconcile([Pad(5, buttons: 6, guid: MotionGuid)]));
        Assert.Equal<int[]>([5], [.. roster.Attached]);

        var other = new ControllerRoster(Motion);
        Assert.True(other.Reconcile([Pad(6, buttons: 0)]));
        Assert.Equal<int[]>([6], [.. other.Attached]);
    }

    /// <summary>
    /// The GUID list is the Qt client's. Nobody porting this owns most of the devices it names,
    /// so a list that drifted would be invisible on every machine a developer has and wrong on
    /// exactly the ones it exists for.
    ///
    /// 49 literals and 45 distinct values: the Qt initialiser repeats four, and its QSet drops
    /// them exactly as this does. Both numbers are asserted so that the difference is a recorded
    /// fact rather than something the next reader counts and mistakes for four the port lost.
    /// </summary>
    [Fact]
    public void TheMotionGuidListIsTheQtClientsOwn()
    {
        string? file = RosterSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);
        IReadOnlyList<string> literals = RosterSource.Literals(text);
        IReadOnlySet<string> guids = RosterSource.MotionControllerGuids(text);

        Assert.Equal(49, literals.Count);
        Assert.Equal(45, guids.Count);
        Assert.Equal(4, literals.Count - guids.Count);

        Assert.Contains(MotionGuid, guids);
        Assert.All(guids, g => Assert.Equal(32, g.Length));
    }

    /// <summary>And the two rules are still the Qt client's, not just the port's.</summary>
    [Fact]
    public void TheTwoRulesAreStillInTheQtClient()
    {
        string? file = RosterSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(RosterSource.RosterIsKeyedByInstanceId(text), "keyed by instance id");
        Assert.True(RosterSource.OnlyEmitsOnChange(text), "emits only on change");
    }
}
