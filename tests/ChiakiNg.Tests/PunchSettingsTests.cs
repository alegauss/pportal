using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP265: three knobs, two dials.
///
/// <see cref="ARefusedValueLooksLikeAnAcceptedOne"/> carries the task, and
/// <see cref="TheDiagnosisCanBeWatchedAndNotUndone"/> is what it costs a caller.
/// </summary>
public class PunchSettingsTests
{
    /// <summary>
    /// THE FINDING. Zero and negatives leave the previous value, and nothing distinguishes that
    /// from having set it.
    /// </summary>
    [Fact]
    public void ARefusedValueLooksLikeAnAcceptedOne()
    {
        (SettingOutcome refused, int kept) = PunchSettings.Apply(current: 75, asked: 0);

        Assert.Equal(SettingOutcome.RefusedSilently, refused);
        Assert.Equal(75, kept);

        (SettingOutcome negative, int alsoKept) = PunchSettings.Apply(75, -4);
        Assert.Equal(SettingOutcome.RefusedSilently, negative);
        Assert.Equal(75, alsoKept);

        // And the caller is told neither.
        Assert.False(PunchSettings.TheOutcomeIsReported);
    }

    /// <summary>A positive value takes.</summary>
    [Fact]
    public void APositiveValueTakes()
    {
        (SettingOutcome outcome, int value) = PunchSettings.Apply(75, 12);

        Assert.Equal(SettingOutcome.Applied, outcome);
        Assert.Equal(12, value);
    }

    /// <summary>The defaults a refusal leaves standing on a fresh session.</summary>
    [Fact]
    public void TheDefaultsARefusalLeavesStanding()
    {
        Assert.Equal(75, PunchSettings.DefaultFor(PunchSetting.GuessCount));
        Assert.Equal(250, PunchSettings.DefaultFor(PunchSetting.SocketCount));

        // Which are PP33's own constants, not a second copy of them.
        Assert.Equal(PortGuessing.RandomAllocationGuesses, PunchSettings.DefaultGuessCount);
        Assert.Equal(PortGuessing.RandomAllocationSocks, PunchSettings.DefaultSocketCount);
    }

    /// <summary>
    /// Three settings a caller can change, two it can only read - and the two are the ones PP253
    /// writes when it overrules the measurement.
    /// </summary>
    [Fact]
    public void TheDiagnosisCanBeWatchedAndNotUndone()
    {
        Assert.Equal(
            [PunchSetting.AllocationIncrement, PunchSetting.RandomAllocation],
            PunchSettings.WrittenOnlyByTheCode);

        foreach (PunchSetting setting in PunchSettings.WrittenOnlyByTheCode)
        {
            Assert.False(PunchSettings.IsSettable(setting));
            Assert.True(PunchSettings.IsReadable(setting));
        }

        // And PP253 writes exactly those two.
        NatWriteBack written = NatDiagnosis.WriteBackFor(NatVerdict.Rewriting);
        Assert.True(written.Writes);
        Assert.True(written.RandomAllocation);
        Assert.Equal(NatDiagnosis.AssertedIncrement, written.Increment);
    }

    /// <summary>The three that are settable are settable, and none of them readable back.</summary>
    [Theory]
    [InlineData(PunchSetting.GuessCount)]
    [InlineData(PunchSetting.SocketCount)]
    [InlineData(PunchSetting.ForceGuessing)]
    public void TheThreeKnobsAreWriteOnly(PunchSetting setting)
    {
        Assert.True(PunchSettings.IsSettable(setting));
        Assert.False(PunchSettings.IsReadable(setting));
    }

    /// <summary>A fresh session carries the sentinel, so the STUN test runs once.</summary>
    [Fact]
    public void AFreshSessionCarriesTheSentinel()
    {
        Assert.Equal(StunLookup.NotMeasured, PunchSettings.DefaultAllocationIncrement);
        Assert.False(PunchSettings.DefaultRandomAllocation);
        Assert.False(PunchSettings.DefaultForceGuessing);

        Assert.Equal(
            StunCall.AllocationTest,
            StunLookup.CallFor(PunchSettings.DefaultAllocationIncrement, ipv4: true));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheSettingsAreStillTheCores()
    {
        string? file = PunchSettingsSource.Locate();
        string? headerFile = PunchSettingsSource.LocateHeader();
        if (file is null || headerFile is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchSettingsSource.BothSettersStillRefuseSilently(core),
            "both counted setters still keep a value only if positive, and return nothing");
        Assert.True(
            PunchSettingsSource.TheFlagStillTakesAnything(core), "the flag still takes anything");
        Assert.True(
            PunchSettingsSource.TheDefaultsAreStillThose(core),
            "and the defaults are still set where the session is created");

        Assert.True(
            PunchSettingsSource.TheReadOnlyPairIsStillReadOnly(File.ReadAllText(headerFile)),
            "the pair the code writes to itself still has a getter and no setter");

        // The reach of each, counted rather than described.
        Assert.True(PunchSettingsSource.ReadsOf(core, "port_guessing_count") > 1);
        Assert.True(PunchSettingsSource.ReadsOf(core, "port_guessing_socks") > 1);
        Assert.True(PunchSettingsSource.ReadsOf(core, "force_port_guessing") > 1);
    }
}
