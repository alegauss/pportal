using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP421, under PP23: senkusha's handshake replayed against the capture PP396 took.
///
/// PP391 replayed ctrl and PP392 replayed session, both against PP297's capture. This is the third
/// channel, and the first one replayed against a recording of itself.
/// </summary>
public class SenkushaExchangeReplayTests
{
    /// <summary>
    /// THE REPLAY. The handshake, message for message, against what the console actually said.
    ///
    /// Scoped to the entries up to and including BANG. Everything after it is the RTT and MTU
    /// measurement, whose number and order depend on the link that was measured - so a participant
    /// replaying them would agree only with a run that negotiated the same MTU, and PP420 turned
    /// that into a rule rather than a judgement call.
    /// </summary>
    [Fact]
    public void TheHandshakeReplaysAgainstTheCapture()
    {
        if (Handshake() is not { } handshake)
            return;

        // PP271: a replay over nothing matches, so the fixture is asserted before the verdict is
        // read. Four entries: the request, its ack, the BIG and the BANG.
        Assert.Equal(4, handshake.Entries.Count);

        var participant = new SenkushaExchangeParticipant();

        Divergence divergence = ExchangeReplay.RunChannel(
            handshake, participant, ChiakiMessageTap.SenkushaChannel);

        Assert.True(divergence.Matched, divergence.ToString());

        // And it got all the way: the version agreed and the BANG arrived.
        Assert.True(participant.VersionAgreed);
        Assert.True(participant.Finished);
    }

    /// <summary>
    /// SENKUSHA SPEAKS FIRST, which is why PP392's hook is needed here too.
    ///
    /// A capture whose first entry is Sent cannot be replayed by arrivals alone: no arrival precedes
    /// it, so Receive is never called and the verdict would be "expected a request, sent nothing"
    /// about an implementation that sends exactly that.
    /// </summary>
    [Fact]
    public void SenkushaOpensTheConversation()
    {
        var participant = new SenkushaExchangeParticipant();

        Assert.Equal(
            [SenkushaExchangeParticipant.Render(SenkushaMessage.TakionProtocolRequest)],
            participant.Opening(ChiakiMessageTap.SenkushaChannel));

        // And says nothing on a channel that is not its own.
        Assert.Empty(participant.Opening(ChiakiMessageTap.StreamChannel));
        Assert.Empty(participant.Opening(ChiakiMessageTap.CtrlChannel));
    }

    /// <summary>
    /// THE BIG ANSWERS THE ACK, and nothing else does.
    ///
    /// It does not travel with the request: senkusha.c sends the version, waits, and only then sends
    /// the BIG. A participant that answered the BANG with it, or sent it unprompted, would produce
    /// the same set of messages in the wrong order - which is what §PP294 warned a pair table cannot
    /// see.
    /// </summary>
    [Fact]
    public void OnlyTheAckIsAnsweredWithTheBig()
    {
        var participant = new SenkushaExchangeParticipant();

        Assert.Equal(
            [SenkushaExchangeParticipant.Render(SenkushaMessage.Big)],
            participant.Receive(
                ChiakiMessageTap.SenkushaChannel,
                SenkushaExchangeParticipant.Render(SenkushaMessage.TakionProtocolRequestAck)));

        Assert.True(participant.VersionAgreed);
        Assert.False(participant.Finished);
    }

    /// <summary>And the BANG ends the handshake without answering anything.</summary>
    [Fact]
    public void TheBangFinishesAndAnswersNothing()
    {
        var participant = new SenkushaExchangeParticipant();

        Assert.Empty(
            participant.Receive(
                ChiakiMessageTap.SenkushaChannel,
                SenkushaExchangeParticipant.Render(SenkushaMessage.Bang)));

        Assert.True(participant.Finished);
    }

    /// <summary>
    /// The measurement is answered with nothing, which is the boundary stated rather than discovered.
    /// </summary>
    [Theory]
    [InlineData(SenkushaMessage.Senkusha)]
    [InlineData(SenkushaMessage.Disconnect)]
    public void TheMeasurementIsNotModelled(SenkushaMessage message)
    {
        var participant = new SenkushaExchangeParticipant();

        Assert.Empty(
            participant.Receive(
                ChiakiMessageTap.SenkushaChannel,
                SenkushaExchangeParticipant.Render(message)));
    }

    /// <summary>A payload with no readable type is answered with nothing rather than guessed at.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("not hex at all")]
    public void APayloadWithNoTypeIsAnsweredWithNothing(string payload)
    {
        Assert.Null(SenkushaExchangeParticipant.TypeOf(payload));
        Assert.Empty(
            new SenkushaExchangeParticipant().Receive(ChiakiMessageTap.SenkushaChannel, payload));
    }

    /// <summary>
    /// PP418 ON THE WIRE. The BIG this sends is the one whose credential fields are empty.
    ///
    /// PP418 read that out of senkusha.c. This is the byte-level copy of the same fact, and the two
    /// agreeing is what makes the redaction set's emptiness a checked property rather than two
    /// independent readings that happen to match.
    /// </summary>
    [Fact]
    public void TheBigThisSendsCarriesThreeEmptyFields()
    {
        byte[] big = SenkushaExchangeParticipant.Payloads[(ushort)SenkushaMessage.Big];

        // type = BIG, then big_payload of 8 bytes.
        Assert.Equal([0x08, 0x00, 0x12, 0x08], big[..4]);

        // client_version 9, then session_key, launch_spec and encrypted_key at zero length each.
        Assert.Equal([0x08, 0x09, 0x12, 0x00, 0x1a, 0x00, 0x22, 0x00], big[4..]);
    }

    /// <summary>
    /// Whether the C still climbs the ladder in the order this replays, and sets each state BEFORE
    /// the send it waits on.
    /// </summary>
    [Fact]
    public void TheLadderIsStillTheQtCores()
    {
        string? path = SanitizerSource.LocateRelative(@"lib\src\senkusha.c");
        if (path is null)
            return;

        string code = CCall.Code(File.ReadAllText(path));

        int expectAck = CCall.Mark(code, "senkusha->state = STATE_EXPECT_PROTOCOL_ACK;");
        int sendVersion = CCall.Mark(code, "senkusha_set_version(senkusha)", Math.Max(expectAck, 0));
        int expectBang = CCall.Mark(code, "senkusha->state = STATE_EXPECT_BANG;", Math.Max(sendVersion, 0));
        int sendBig = CCall.Mark(code, "senkusha_send_big(senkusha)", Math.Max(expectBang, 0));

        Assert.True(expectAck >= 0, "senkusha no longer waits on a protocol ack");
        Assert.True(
            sendVersion > expectAck,
            "the version request is sent before the state that waits for its answer");
        Assert.True(
            expectBang > sendVersion,
            "the BANG wait no longer follows the version request");
        Assert.True(
            sendBig > expectBang,
            "the BIG is sent before the state that waits for its answer");
    }

    /// <summary>
    /// The corpus's senkusha entries up to and including the BANG, or null outside a checkout.
    /// </summary>
    private static ExchangeRecording? Handshake()
    {
        string? path = SanitizerSource.LocateRelative(FourChannelCorpusTests.RelativePath);
        if (path is null)
            return null;

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        if (recording is null)
            return null;

        var handshake = new ExchangeRecording();
        foreach (ExchangeEntry entry in recording.Entries.Where(
                     e => e.Channel == ChiakiMessageTap.SenkushaChannel))
        {
            handshake.Add(entry.AtMicroseconds, entry.Direction, entry.Channel, entry.Payload);

            if (SenkushaExchangeParticipant.TypeOf(entry.Payload) == SenkushaMessage.Bang)
                break;
        }

        return handshake;
    }
}
