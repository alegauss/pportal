using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP361, under PP294: the rudp subtype switch that says it is wrong, and a log that said the
/// opposite of what it sent.
/// </summary>
public class CtrlRudpSubtypesTests
{
    /// <summary>
    /// THREE SUBTYPES FALL INTO A FOURTH, and the fallthrough is what makes them work.
    ///
    /// 0x12, 0x26 and 0x36 acknowledge the packet their payload names and then drop into 0x02 with
    /// no break, so each also acknowledges the message and takes the ctrl bytes it carried. Four
    /// independent arms would look tidier and would stop doing both for three of them.
    /// </summary>
    [Theory]
    [InlineData((byte)0x12)]
    [InlineData((byte)0x26)]
    [InlineData((byte)0x36)]
    public void TheThreeFallIntoTheFourth(byte subtype)
    {
        Assert.Equal(RudpAction.AckPacketThenTake, CtrlRudpSubtypes.ActionFor(subtype));
        Assert.True(CtrlRudpSubtypes.TakesCtrlBytes(subtype));
    }

    /// <summary>The rest of the table, arm by arm.</summary>
    [Theory]
    [InlineData((byte)0x02, RudpAction.AckThenTake, true)]
    [InlineData((byte)0x24, RudpAction.AckPacketOnly, false)]
    [InlineData((byte)0xC0, RudpAction.Finish, false)]
    [InlineData((byte)0x99, RudpAction.UnknownThenTake, true)]
    public void EachOtherSubtypeDoesItsOwnThing(byte subtype, RudpAction action, bool takes)
    {
        Assert.Equal(action, CtrlRudpSubtypes.ActionFor(subtype));
        Assert.Equal(takes, CtrlRudpSubtypes.TakesCtrlBytes(subtype));
    }

    /// <summary>
    /// The data offset is per-subtype, and the unknown arm cannot ask - it assumes four.
    /// </summary>
    [Theory]
    [InlineData((byte)0x12, 8)]
    [InlineData((byte)0x26, 6)]
    [InlineData((byte)0x36, 2)]
    [InlineData((byte)0x02, 2)]
    public void TheDataOffsetIsPerSubtype(byte subtype, int offset)
    {
        Assert.Equal(offset, CtrlRudpSubtypes.DataOffsetFor(subtype));
    }

    /// <summary>And the unknown arm's offset is a constant it could not derive.</summary>
    [Fact]
    public void TheUnknownArmAssumesFour()
    {
        Assert.Equal(4, CtrlRudpSubtypes.UnknownDataOffset);
    }

    /// <summary>
    /// The consistency check is the only thing in front of the bytes - PP347 added the second.
    /// </summary>
    [Theory]
    [InlineData(20, 2, 10u, true)]
    [InlineData(20, 2, 11u, false)]
    [InlineData(9, 2, 0u, false)]
    [InlineData(10, 2, 0u, true)]
    public void AMessageIsWellFormedOnlyWhenItsLengthsAgree(
        int dataSize, int offset, uint announced, bool wellFormed)
    {
        Assert.Equal(wellFormed, CtrlRudpSubtypes.IsWellFormed(dataSize, offset, announced));
    }

    /// <summary>
    /// THE MICROPHONE'S THIRD BYTE IS THE FLAG, and the corpus confirms which way round.
    ///
    /// ctrl_enable_features toggles twice with muted false, and PP297's capture holds
    /// 00-01-01-59 twice. So an unmuted toggle writes one, and the log used to say "unmute" for the
    /// muted case - the opposite.
    /// </summary>
    [Fact]
    public void TheMicrophoneToggleMatchesTheCapture()
    {
        Assert.Equal<byte[]>([0, 1, 1, 89], CtrlMicrophone.TogglePayload(muted: false));
        Assert.Equal<byte[]>([0, 1, 0, 89], CtrlMicrophone.TogglePayload(muted: true));

        // Read back, so the word and the byte cannot disagree again.
        Assert.False(CtrlMicrophone.MutedIn(CtrlMicrophone.TogglePayload(false)));
        Assert.True(CtrlMicrophone.MutedIn(CtrlMicrophone.TogglePayload(true)));

        Assert.Equal("unmute", CtrlMicrophone.WordFor(CtrlMicrophone.TogglePayload(false)));
        Assert.Equal("mute", CtrlMicrophone.WordFor(CtrlMicrophone.TogglePayload(true)));
    }

    /// <summary>And the capture really does hold that payload, twice.</summary>
    [Fact]
    public void TheCaptureHoldsTwoUnmutedToggles()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        if (path is null)
            return;

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        if (recording is null)
            return;

        IReadOnlyList<ExchangeEntry> toggles =
            [.. recording.Entries.Where(e => e.Payload.StartsWith("0036 ", StringComparison.Ordinal))];

        Assert.Equal(2, toggles.Count);
        Assert.All(toggles, t => Assert.Equal("0036 00-01-01-59", t.Payload));
    }

    /// <summary>The connect payload says nothing at all.</summary>
    [Fact]
    public void TheMicrophoneConnectSaysNothing()
    {
        Assert.Equal<byte[]>([0, 0], CtrlMicrophone.ConnectPayload());
    }

    /// <summary>And ctrl.c still has both halves the way this reproduces them.</summary>
    [Fact]
    public void CtrlStillDeclaresBoth()
    {
        string? path = CtrlRudpSubtypesSource.Locate();
        if (path is null)
            return;

        string? thread = CFunction.BodyIn(path, "static void *ctrl_thread_func");
        string? toggle = CFunction.BodyIn(path, "ctrl_message_toggle_microphone");

        Assert.NotNull(thread);
        Assert.NotNull(toggle);

        Assert.True(
            CtrlRudpSubtypesSource.TheThreeStillFallThrough(thread),
            "the three subtypes no longer fall into 0x02, so three of them stopped acking and reading");
        Assert.True(
            CtrlRudpSubtypesSource.TheSwitchStillSaysItIsWrong(thread),
            "the switch no longer admits its shape is wrong - which is either a fix or a lost warning");
        Assert.True(
            CtrlRudpSubtypesSource.TheUnknownArmStillAssumesFour(thread),
            "the unknown arm no longer assumes an offset of four");
        Assert.True(
            CtrlRudpSubtypesSource.TheMicrophoneLogStillAgreesWithTheByte(toggle),
            "the microphone log and the byte it writes disagree again");
    }
}
