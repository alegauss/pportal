using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP397, under PP23: which payloads never reach a recording, asked of the channel as well as the
/// type.
///
/// PP326 keyed that answer to the ctrl message type, and for a long time ctrl was the only channel
/// the rule was consulted for. PP394 and PP395 added two more, and neither numbers its messages the
/// same way - a ctrl message carries 0x33, a stream message carries a protobuf payload type between
/// 0 and 25. One list across three numbering schemes means something different in each.
///
/// THE LEAK WAS REAL. A BIG carries the session id, which is the very value PP326 redacts one
/// channel over, and nothing in a ctrl-keyed list could name it.
/// </summary>
public class MessageSecretsTests
{
    /// <summary>THE DEFECT. A BIG's payload is not recordable.</summary>
    [Fact]
    public void ABigIsNotRecordable()
    {
        Assert.False(
            MessageSecrets.MayRecord(ChiakiMessageTap.StreamChannel, MessageSecrets.StreamSecret["BIG"]));
    }

    /// <summary>
    /// And what it was before: the same message judged by the ctrl list, which says yes.
    ///
    /// Kept so the leak is named rather than described. Payload type 0 is a BIG on the stream
    /// channel and nothing at all in the ctrl numbering, so the old rule recorded it in the clear.
    /// </summary>
    [Fact]
    public void TheCtrlListWouldHaveRecordedIt()
    {
        Assert.True(CtrlMessageSecrets.MayRecord(MessageSecrets.StreamSecret["BIG"]));
    }

    /// <summary>The console's half of the key exchange is not recordable either.</summary>
    [Fact]
    public void ABangIsNotRecordable()
    {
        Assert.False(
            MessageSecrets.MayRecord(ChiakiMessageTap.StreamChannel, MessageSecrets.StreamSecret["BANG"]));
    }

    /// <summary>
    /// AND STREAMINFO IS, which is the decision that keeps the oracle worth having.
    ///
    /// It carries the audio and video headers - the thing a replay of this channel exists to judge.
    /// Redacting it would buy nothing and cost everything, so the rule is asserted in both
    /// directions rather than only against leaking.
    /// </summary>
    [Theory]
    [InlineData(3)]   // HEARTBEAT
    [InlineData(5)]   // CORRUPTFRAME
    [InlineData(8)]   // DISCONNECT
    [InlineData(13)]  // STREAMINFO
    [InlineData(14)]  // STREAMINFOACK
    [InlineData(21)]  // CONTROLLERCONNECTION
    [InlineData(25)]  // IDRREQUEST
    public void TheRestOfTheStreamChannelIsRecordable(ushort payloadType)
    {
        Assert.True(MessageSecrets.MayRecord(ChiakiMessageTap.StreamChannel, payloadType));
    }

    /// <summary>
    /// Senkusha carries no credential, and that is recorded as an empty set rather than left out.
    ///
    /// Its BIG sets session_key, launch_spec and encrypted_key to the empty string; the rest is MTU
    /// sizes and echo commands.
    /// </summary>
    [Fact]
    public void SenkushaCarriesNoSecret()
    {
        Assert.Empty(MessageSecrets.SenkushaSecret);

        foreach (ushort type in (ushort[])[0, 1, 3, 8, 13, 25])
            Assert.True(MessageSecrets.MayRecord(ChiakiMessageTap.SenkushaChannel, type));
    }

    /// <summary>
    /// PP418: AND THE C IS WHAT SAYS SO, not the paragraph above.
    ///
    /// The empty set was held by prose and by an Assert.Empty that restated the constant. PP396 is
    /// about to publish the first capture of this channel into a corpus that is a file in a public
    /// repository, and a redaction that is right for a reason nothing checks is right until somebody
    /// edits the other file.
    /// </summary>
    [Fact]
    public void SenkushasBigStillCarriesNothingInTheCore()
    {
        if (MessageSecretsSource.LocateSenkusha() is not { } path)
            return;

        Assert.True(
            MessageSecretsSource.SenkushasBigStillCarriesNothing(File.ReadAllText(path)),
            "senkusha's BIG carries something now, and its redaction set is empty");
    }

    /// <summary>
    /// And the stream's BIG still carries the session id, which is WHY it is redacted.
    ///
    /// The other direction, and not decoration: a redaction whose reason has gone sits there looking
    /// deliberate, and the next reader cannot tell it from one that still earns its place.
    /// </summary>
    [Fact]
    public void TheStreamsBigStillCarriesTheSessionId()
    {
        if (MessageSecretsSource.LocateStream() is not { } path)
            return;

        Assert.True(
            MessageSecretsSource.TheStreamsBigStillCarriesTheSessionId(File.ReadAllText(path)),
            "the stream's BIG no longer carries the session id, so BIG's redaction has no reason");

        // And BIG is in fact the type that gets redacted there.
        Assert.False(MessageSecrets.MayRecord(
            ChiakiMessageTap.StreamChannel, MessageSecrets.StreamSecret["BIG"]));
    }

    /// <summary>
    /// PP418: the reader refuses a BIG that carries something, and one that is gone.
    ///
    /// Both failure shapes, against synthetic bodies. The second matters as much: "carries nothing"
    /// must not be satisfiable by a file that stopped building a BIG at all, which is the absence
    /// trap PP272 exists for.
    /// </summary>
    [Fact]
    public void TheReaderRefusesABigThatCarriesSomethingAndOneThatIsGone()
    {
        const string Empty = """
            	msg.has_big_payload = true;
            	msg.big_payload.session_key.arg = "";
            	msg.big_payload.launch_spec.arg = "";
            	msg.big_payload.encrypted_key.arg = "";
            """;

        Assert.True(MessageSecretsSource.SenkushasBigStillCarriesNothing(Empty));

        // One field filled in - the copy-the-shape-that-works mistake.
        Assert.False(MessageSecretsSource.SenkushasBigStillCarriesNothing(
            Empty.Replace(
                "msg.big_payload.session_key.arg = \"\";",
                "msg.big_payload.session_key.arg = session->session_id;",
                StringComparison.Ordinal)));

        // And no BIG at all is not "carries nothing" either.
        Assert.False(MessageSecretsSource.SenkushasBigStillCarriesNothing(""));
        Assert.False(MessageSecretsSource.SenkushasBigStillCarriesNothing(
            "\tmsg.big_payload.session_key.arg = \"\";"));

        // A comment naming the assignment does not satisfy it - PP400's rule.
        Assert.False(MessageSecretsSource.SenkushasBigStillCarriesNothing(
            "// msg.has_big_payload = true; msg.big_payload.session_key.arg = \"\";"));
    }

    /// <summary>PP272: and both readers answer no to an empty file.</summary>
    [Fact]
    public void BothReadersAnswerNoToAnEmptyFile()
    {
        Assert.False(MessageSecretsSource.SenkushasBigStillCarriesNothing(""));
        Assert.False(MessageSecretsSource.TheStreamsBigStillCarriesTheSessionId(""));
    }

    /// <summary>
    /// PP326's six still go on the channel they were decided for, unchanged.
    ///
    /// The point of making the rule channel-aware was not to revisit that decision, and a check
    /// that only tested the new channels would not have noticed it being lost.
    /// </summary>
    [Fact]
    public void TheCtrlChannelStillRedactsPp326sSix()
    {
        foreach (ushort type in CtrlMessageSecrets.SecretTypes)
            Assert.False(MessageSecrets.MayRecord(ChiakiMessageTap.CtrlChannel, type));

        // And an ordinary ctrl message is still recorded.
        Assert.True(MessageSecrets.MayRecord(ChiakiMessageTap.CtrlChannel, (ushort)CtrlMessage.HeartbeatReq));
    }

    /// <summary>
    /// THE NUMBERING SCHEMES DO NOT LEAK INTO EACH OTHER, which is the other half of the finding.
    ///
    /// PP326's SESSION_ID is 0x33. On the stream channel 0x33 is not a payload type at all, and a
    /// rule that redacted it there would silence a message nobody meant - a recording missing
    /// payloads for no stated reason reads as a bad capture rather than a rule misfiring.
    /// </summary>
    [Fact]
    public void ACtrlSecretNumberIsNotSecretOnAnotherChannel()
    {
        const ushort SessionId = 0x33;

        Assert.False(MessageSecrets.MayRecord(ChiakiMessageTap.CtrlChannel, SessionId));
        Assert.True(MessageSecrets.MayRecord(ChiakiMessageTap.StreamChannel, SessionId));
        Assert.True(MessageSecrets.MayRecord(ChiakiMessageTap.SenkushaChannel, SessionId));
    }

    /// <summary>
    /// A message that would not decode is refused on every channel.
    ///
    /// PP326's principle is that a value goes because of the field it sits in. With no field
    /// identified there is no basis to record it, so the safe answer is the only answer.
    /// </summary>
    [Fact]
    public void AMessageThatWouldNotDecodeIsNeverRecorded()
    {
        foreach (string channel in (string[])[
            ChiakiMessageTap.CtrlChannel,
            ChiakiMessageTap.StreamChannel,
            ChiakiMessageTap.SenkushaChannel])
        {
            Assert.False(MessageSecrets.MayRecord(channel, ChiakiMessageTap.UnknownType));
        }
    }

    /// <summary>
    /// The recorder asks the channel-aware rule, so a BIG renders as the marker.
    ///
    /// Asserted through Render rather than only through MayRecord, because the recorder is what
    /// writes the corpus and a rule nothing consults protects nothing.
    /// </summary>
    [Fact]
    public void TheRecorderRedactsABig()
    {
        var big = new TappedMessage(
            ExchangeTapDirection.Sent,
            ChiakiMessageTap.StreamChannel,
            MessageSecrets.StreamSecret["BIG"],
            [0xde, 0xad, 0xbe, 0xef]);

        Assert.Equal($"0000 {MessageSecrets.Marker}", ExchangeRecorder.Render(big));

        // And a heartbeat on the same channel keeps its bytes.
        var heartbeat = new TappedMessage(
            ExchangeTapDirection.Sent, ChiakiMessageTap.StreamChannel, 3, [0x01, 0x02]);

        Assert.Equal("0003 01-02", ExchangeRecorder.Render(heartbeat));
    }
}
