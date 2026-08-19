using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP127: the 47 bytes a DualSense is sent, and the three reports that fill them.
///
/// A wire format to hardware, so the failures are all silent: a field at the wrong offset sets
/// something else, and the pad answers nothing either way. No pad is needed to check it, because
/// what is being checked is the buffer rather than what the pad does with it.
/// </summary>
public class DualSenseEffectsTests
{
    private const byte Intensity = 0x03;

    /// <summary>
    /// Old firmware: values halved, and the enable bit in the FIRST byte rather than the third.
    ///
    /// A port that picked only the modern branch rumbles at half strength on every pad below
    /// 0x0224 - the bit it needs is never set - and one that picked only the old branch sends
    /// half the amplitude to every modern pad. Neither is an error anyone sees.
    /// </summary>
    [Fact]
    public void OldFirmwareHalvesTheRumbleAndUsesTheFirstEnableByte()
    {
        byte[] report = DualSenseEffects.Rumble(200, 100, 0x0223, Intensity);

        Assert.Equal(DualSenseEffects.ReportSize, report.Length);
        Assert.Equal(100, report[DualSenseEffects.Offset.RumbleLeft]);
        Assert.Equal(50, report[DualSenseEffects.Offset.RumbleRight]);

        Assert.Equal(DualSenseEffects.Bit.Rumble1,
            (byte)(report[DualSenseEffects.Offset.EnableBits1] & DualSenseEffects.Bit.Rumble1));
        Assert.Equal(0, report[DualSenseEffects.Offset.EnableBits3]);
    }

    /// <summary>And from 0x0224, whole values under a bit in the third enable byte.</summary>
    [Fact]
    public void NewFirmwareSendsWholeValuesAndUsesTheThirdEnableByte()
    {
        byte[] report = DualSenseEffects.Rumble(200, 100, 0x0224, Intensity);

        Assert.Equal(200, report[DualSenseEffects.Offset.RumbleLeft]);
        Assert.Equal(100, report[DualSenseEffects.Offset.RumbleRight]);

        Assert.Equal(DualSenseEffects.Bit.Rumble3, report[DualSenseEffects.Offset.EnableBits3]);
        Assert.Equal(0,
            (byte)(report[DualSenseEffects.Offset.EnableBits1] & DualSenseEffects.Bit.Rumble1));
    }

    /// <summary>
    /// Left and right are not interchangeable, and they are not in the order the struct reads:
    /// RIGHT is at the lower offset. Asserted with different values so a swap cannot pass.
    /// </summary>
    [Fact]
    public void RightComesBeforeLeftInTheReport()
    {
        byte[] report = DualSenseEffects.Rumble(left: 0x11, right: 0x22, 0x0224, Intensity);

        Assert.Equal(2, DualSenseEffects.Offset.RumbleRight);
        Assert.Equal(3, DualSenseEffects.Offset.RumbleLeft);
        Assert.Equal(0x22, report[2]);
        Assert.Equal(0x11, report[3]);
    }

    /// <summary>
    /// The user's intensity setting rides in an UNNAMED field - rgucUnknown1[4], offset 36 - and
    /// both the rumble and the trigger reports carry it. A port reading the struct's names rather
    /// than its bytes drops it, and the setting then does nothing with no error.
    /// </summary>
    [Fact]
    public void TheIntensityRidesInTheUnnamedField()
    {
        Assert.Equal(Intensity, DualSenseEffects.Rumble(1, 1, 0x0224, Intensity)[36]);
        Assert.Equal(Intensity,
            DualSenseEffects.TriggerEffects(0, new byte[10], 0, new byte[10], Intensity)[36]);
    }

    /// <summary>
    /// A trigger effect is a type byte and ten parameters, and both triggers are always sent.
    /// The two blocks do not overlap, which is what a wrong length would produce.
    /// </summary>
    [Fact]
    public void BothTriggerEffectsAreWrittenWhole()
    {
        byte[] left = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        byte[] right = [11, 12, 13, 14, 15, 16, 17, 18, 19, 20];

        byte[] report = DualSenseEffects.TriggerEffects(0x21, left, 0x26, right, Intensity);

        Assert.Equal(0x26, report[DualSenseEffects.Offset.RightTriggerEffect]);
        Assert.Equal(right, report[11..21]);

        Assert.Equal(0x21, report[DualSenseEffects.Offset.LeftTriggerEffect]);
        Assert.Equal(left, report[22..32]);

        Assert.Equal(DualSenseEffects.Bit.LeftTrigger | DualSenseEffects.Bit.RightTrigger,
            report[DualSenseEffects.Offset.EnableBits1]);
    }

    [Fact]
    public void ATriggerEffectMustBeTenBytes()
    {
        Assert.Throws<ArgumentException>(
            () => DualSenseEffects.TriggerEffects(0, new byte[9], 0, new byte[10], Intensity));
        Assert.Throws<ArgumentException>(
            () => DualSenseEffects.TriggerEffects(0, new byte[10], 0, new byte[11], Intensity));
    }

    /// <summary>The mic light and the mute move together, both directions.</summary>
    [Theory]
    [InlineData(true, 0x01, 0x08)]
    [InlineData(false, 0x00, 0x00)]
    public void TheMicLightAndTheMuteMoveTogether(bool muted, int light, int mute)
    {
        byte[] report = DualSenseEffects.Microphone(muted);

        Assert.Equal(light, report[DualSenseEffects.Offset.MicLightMode]);
        Assert.Equal(mute, report[DualSenseEffects.Offset.AudioMuteBits]);
        Assert.Equal(DualSenseEffects.Bit.MicLight | DualSenseEffects.Bit.Mic,
            report[DualSenseEffects.Offset.EnableBits2]);
    }

    /// <summary>
    /// Every offset the port names is the one the Qt struct's own trailing comment claims. Those
    /// comments are what a reader of that struct believes, and this is what keeps the port's
    /// explicit numbers and the C++'s implicit layout from drifting apart.
    /// </summary>
    [Fact]
    public void TheOffsetsAreTheQtStructsOwn()
    {
        string? file = DualSenseSource.Locate();
        if (file is null)
            return;

        IReadOnlyDictionary<string, int> fields = DualSenseSource.FieldOffsets(File.ReadAllText(file));
        Assert.Equal(21, fields.Count);

        Assert.Equal(DualSenseEffects.Offset.EnableBits1, fields["ucEnableBits1"]);
        Assert.Equal(DualSenseEffects.Offset.EnableBits2, fields["ucEnableBits2"]);
        Assert.Equal(DualSenseEffects.Offset.EnableBits3, fields["ucEnableBits3"]);
        Assert.Equal(DualSenseEffects.Offset.RumbleRight, fields["ucRumbleRight"]);
        Assert.Equal(DualSenseEffects.Offset.RumbleLeft, fields["ucRumbleLeft"]);
        Assert.Equal(DualSenseEffects.Offset.MicLightMode, fields["ucMicLightMode"]);
        Assert.Equal(DualSenseEffects.Offset.AudioMuteBits, fields["ucAudioMuteBits"]);
        Assert.Equal(DualSenseEffects.Offset.RightTriggerEffect, fields["rgucRightTriggerEffect"]);
        Assert.Equal(DualSenseEffects.Offset.LeftTriggerEffect, fields["rgucLeftTriggerEffect"]);
        Assert.Equal(DualSenseEffects.Offset.Unknown1, fields["rgucUnknown1"]);

        // And the last field plus its own byte is the report's size, so nothing was appended.
        Assert.Equal(DualSenseEffects.ReportSize, fields["ucLedBlue"] + 1);
    }

    /// <summary>And the firmware split is still where the Qt client puts it.</summary>
    [Fact]
    public void TheFirmwareSplitIsStillTheQtClients()
    {
        string? file = DualSenseSource.Locate();
        if (file is null)
            return;

        Assert.True(DualSenseSource.RumbleIsHalvedBelowFirmware(
            File.ReadAllText(file), DualSenseEffects.WholeRumbleFirmware));
    }
}
