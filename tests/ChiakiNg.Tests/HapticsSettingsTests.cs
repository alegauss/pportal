using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP858: the Controllers tab's two haptics rows, read the way a session reads them.
///
/// The stream run typed Normal and the default override at its own call sites, so moving either
/// slider changed the registry and nothing a session did. These hold the reader and the join.
/// </summary>
public class HapticsSettingsTests
{
    /// <summary>
    /// A stored Off and a stored half come back as the player's Off and the matrix's half - which
    /// is the setting reaching the two things it governs.
    /// </summary>
    [Fact]
    public void TheStoredValuesComeBackAsTheSessionsSettings()
    {
        var stored = new FakePreferences()
            .Set(RumbleHapticsChoice.Intensity.Key, "Off")
            .Set(HapticOverrideSetting.Key, 0.5);

        HapticsSettings settings = HapticsSettings.From(stored);

        Assert.Equal(RumbleHapticsIntensity.Off, settings.Rumble);
        Assert.Equal(0.5, settings.Override);
        Assert.Equal(0.5f, DualSenseHaptics.Matrix(4, settings.Override)[4]);
    }

    /// <summary>
    /// The stored WORD maps through the combo's own list, the lower-case "Very weak" included; an
    /// install that never touched either row reads Normal and the console's own intensity.
    /// </summary>
    [Fact]
    public void TheWordMapsThroughTheCombosListAndAbsenceIsTheDefault()
    {
        Assert.Equal(
            RumbleHapticsIntensity.VeryWeak,
            HapticsSettings.From(new FakePreferences().Set(RumbleHapticsChoice.Intensity.Key, "Very weak")).Rumble);

        Assert.Equal(
            RumbleHapticsIntensity.VeryStrong,
            HapticsSettings.From(new FakePreferences().Set(RumbleHapticsChoice.Intensity.Key, "Very Strong")).Rumble);

        HapticsSettings untouched = HapticsSettings.From(new FakePreferences());

        Assert.Equal(RumbleHapticsIntensity.Normal, untouched.Rumble);
        Assert.True(HapticOverrideSetting.IsConsoleSetting(untouched.Override));
    }

    /// <summary>And the run hands both to the player and the actuators, rather than typing them.</summary>
    [Fact]
    public void TheRunPassesBothRatherThanTypingThem()
    {
        string? run = SanitizerSource.LocateRelative(Path.Combine("app", "Session", "StreamRun.cs"));
        if (run is null)
            return;

        string code = CCall.Code(File.ReadAllText(run));

        Assert.True(CCall.CalledWith(code, "phase.Haptics.Arm", "pad, hapticsSettings.Rumble"));
        Assert.True(CCall.CalledWith(code, "DualSenseHaptics.Open", "hapticsSettings.Override"));
        Assert.DoesNotContain("RumbleHapticsIntensity.Normal", code, StringComparison.Ordinal);
    }
}
