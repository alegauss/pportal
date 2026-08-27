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

    /// <summary>
    /// PP413: AN ARM MAY ONLY ACKNOWLEDGE A PACKET NUMBER IT READ.
    ///
    /// The four arms that ack a packet read the counter off message.data + 2 first. The unknown arm
    /// reads nothing, so it acks nothing - it used to ack the variable anyway, carrying whatever a
    /// sibling submessage of the same datagram left there.
    /// </summary>
    [Theory]
    [InlineData((byte)0x12, true)]
    [InlineData((byte)0x26, true)]
    [InlineData((byte)0x36, true)]
    [InlineData((byte)0x24, true)]
    [InlineData((byte)0x02, false)]
    [InlineData((byte)0xC0, false)]
    [InlineData((byte)0x99, false)]
    [InlineData((byte)0x41, false)]
    public void OnlyAnArmThatReadsTheCounterAcksAPacket(byte subtype, bool acks)
    {
        Assert.Equal(acks, CtrlRudpSubtypes.ReadsAnAckCounter(subtype));

        // The two are the same question, which is the rule rather than a coincidence.
        Assert.Equal(
            CtrlRudpSubtypes.ReadsAnAckCounter(subtype), CtrlRudpSubtypes.AcksAPacket(subtype));
    }

    /// <summary>
    /// WHY ZERO WAS THE WORST POSSIBLE ACCIDENT, computed rather than asserted in prose.
    ///
    /// The send buffer frees every packet at or older than the acked seqnum, and RFC 1982 puts
    /// nearly half the space older than zero. So the value the unknown arm reached for when no
    /// sibling had set it prunes 32768 of 65536 seqnums.
    /// </summary>
    [Fact]
    public void AckingZeroWouldPruneHalfTheSpace()
    {
        Assert.Equal(32768, CtrlRudpSubtypes.SeqNumsPrunedByAcking(0));

        // Not a property of zero alone - it is what an ack MEANS - but zero is the one an arm that
        // read nothing lands on, and it is reached by an unrecognised subtype rather than by a bug
        // in the arms that do read.
        Assert.Equal(32768, CtrlRudpSubtypes.SeqNumsPrunedByAcking(1000));
        Assert.False(CtrlRudpSubtypes.AcksAPacket(0x99));
    }

    /// <summary>
    /// PP413: and the reader says NO to the arm as it used to be.
    ///
    /// A drift check that cannot see the shape it exists to refuse is worth nothing, and asserting
    /// that against a synthetic body costs nothing and does not require putting the defect back.
    /// The comment case is the one that matters: the explanation of the removal names the call, so a
    /// reader that did not strip comments would be satisfied by the prose describing the absence.
    /// </summary>
    [Fact]
    public void TheReaderRefusesTheArmAsItUsedToBe()
    {
        const string acking = """
            switch(message.subtype)
            {
                default:
                    CHIAKI_LOGI(log, "unknown");
                    chiaki_rudp_ack_packet(ctrl->session->rudp, ack_counter);
                    chiaki_rudp_send_ack_message(ctrl->session->rudp, remote_counter);
                    break;
            }
            """;

        Assert.False(CtrlRudpSubtypesSource.TheUnknownArmStillAcksNoPacket(acking));

        // The fixed shape, which is what the real file must look like.
        const string fixedArm = """
            switch(message.subtype)
            {
                default:
                    CHIAKI_LOGI(log, "unknown");
                    // PP413: no chiaki_rudp_ack_packet(ctrl->session->rudp, ack_counter) here.
                    chiaki_rudp_send_ack_message(ctrl->session->rudp, remote_counter);
                    break;
            }
            """;

        Assert.True(CtrlRudpSubtypesSource.TheUnknownArmStillAcksNoPacket(fixedArm));

        // And an arm that stopped acking altogether is not "acks no packet" either.
        const string silent = """
            switch(message.subtype)
            {
                default:
                    CHIAKI_LOGI(log, "unknown");
                    break;
            }
            """;

        Assert.False(CtrlRudpSubtypesSource.TheUnknownArmStillAcksNoPacket(silent));
    }

    /// <summary>
    /// PP414: the offset helper's contract, which used to be takion's.
    ///
    /// The comment above it promised "the offset of the mac ... or -1 if unknown", copied verbatim
    /// from takion_packet_type_mac_offset. Both halves were wrong here: the value is where the ctrl
    /// header starts, and the default answers 2 rather than a sentinel.
    ///
    /// THE DEFAULT IS AN ANSWER, NOT A FALLBACK. The caller reaches it with 0x36 and 0x02, for which
    /// 2 is correct - so a port written from the comment would move where the two commonest subtypes
    /// are read from. That is the claim this asserts, against the code.
    /// </summary>
    [Fact]
    public void TheOffsetHelpersContractIsItsOwn()
    {
        string? path = CtrlRudpSubtypesSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            CtrlRudpSubtypesSource.TheOffsetHelperIsStillFileLocal(core),
            "the offset helper is no longer static, so it has linkage nothing uses again");
        Assert.True(
            CtrlRudpSubtypesSource.TheOffsetHelperReturnsNoSentinel(core),
            "the helper answers -1 now, which is a case its caller does not check for");
        Assert.True(
            CtrlRudpSubtypesSource.TheDefaultOffsetIsStillTheTablesDefault(core),
            "the default arm and this port's table disagree about 0x36 and 0x02");
        Assert.True(
            CtrlRudpSubtypesSource.TheOffsetHelpersCommentIsItsOwn(core),
            "takion's MAC-offset comment is back on ctrl.c's data-offset helper");

        // And the two subtypes that land on the default are still the ones that make 2 an answer.
        Assert.Equal(2, CtrlRudpSubtypes.DataOffsetFor(0x36));
        Assert.Equal(2, CtrlRudpSubtypes.DataOffsetFor(0x02));
    }

    /// <summary>
    /// And takion.c KEEPS the sentence, because there it is true.
    ///
    /// Without this the check above is satisfied by deleting the comment everywhere, which would
    /// lose a contract that does hold - takion's helper really does answer -1.
    /// </summary>
    [Fact]
    public void TakionKeepsTheCommentThatIsTrueOfIt()
    {
        string? path = SanitizerSource.LocateRelative(@"lib\src\takion.c");
        if (path is null)
            return;

        string takion = File.ReadAllText(path);

        Assert.Contains(
            "offset of the mac of size CHIAKI_GKCRYPT_GMAC_SIZE", takion, StringComparison.Ordinal);
        Assert.Contains("return -1;", takion, StringComparison.Ordinal);
    }

    /// <summary>PP414: and every new reader answers no to an empty file.</summary>
    [Fact]
    public void TheOffsetHelpersReadersAnswerNoToAnEmptyFile()
    {
        Assert.False(CtrlRudpSubtypesSource.TheOffsetHelperIsStillFileLocal(""));
        Assert.False(CtrlRudpSubtypesSource.TheOffsetHelperReturnsNoSentinel(""));
        Assert.False(CtrlRudpSubtypesSource.TheDefaultOffsetIsStillTheTablesDefault(""));

        // PP272: the comment check is an absence, so it is anchored on the helper being present -
        // otherwise it would answer yes about a file it never read. The reflected sweep in
        // DriftReadsTheFileTests caught the first version of it, which was not.
        Assert.False(CtrlRudpSubtypesSource.TheOffsetHelpersCommentIsItsOwn(""));
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

        // PP413: and the unknown arm still acks no packet, while still acking the message.
        Assert.True(
            CtrlRudpSubtypesSource.TheUnknownArmStillAcksNoPacket(thread),
            "the unknown arm acks a packet counter again, or stopped acking the message at all");
        Assert.True(
            CtrlRudpSubtypesSource.EveryPacketAckStillFollowsARead(thread),
            "a packet ack no longer has a read of its own in front of it");
    }
}
