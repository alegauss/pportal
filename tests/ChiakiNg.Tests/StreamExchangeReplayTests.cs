using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP424, under PP23: the stream connection's handshake, replayed against PP396's capture.
///
/// The fourth and last of PP23's channels. What makes this one different from the three before it is
/// that PP423 left the BANG's verdict readable, so the replay asserts what the console DECIDED and
/// not only the order it said things in.
/// </summary>
public class StreamExchangeReplayTests
{
    /// <summary>
    /// THE REPLAY. Message for message against what the console actually said.
    ///
    /// Scoped to the entries up to and including the console's STREAMINFOACK. The DISCONNECT after it
    /// goes at teardown rather than in reply to anything, so a participant driven by arrivals cannot
    /// produce it - a scope rather than an omission.
    /// </summary>
    [Fact]
    public void TheHandshakeReplaysAgainstTheCapture()
    {
        if (Handshake() is not { } handshake)
            return;

        // PP271: a replay over nothing matches, so the fixture is asserted before the verdict.
        // BIG, BANG, the console's STREAMINFO, the three answers, and its ack.
        Assert.Equal(7, handshake.Entries.Count);

        var participant = new StreamExchangeParticipant();

        Divergence divergence = ExchangeReplay.RunChannel(
            handshake, participant, ChiakiMessageTap.StreamChannel);

        Assert.True(divergence.Matched, divergence.ToString());

        // And it got all the way through.
        Assert.True(participant.StreamInfoAnswered);
        Assert.True(participant.MicrophoneAcknowledged);
    }

    /// <summary>
    /// THE PROPERTY PP423 BOUGHT. The console's verdict is in the corpus and the replay reads it.
    ///
    /// Four checks, in the order streamconnection.c makes them: the version accepted, the encrypted
    /// key accepted, and both key fields present. The keys themselves are zeroed - their lengths are
    /// what survives, and that is exactly what the presence checks ask about.
    /// </summary>
    [Fact]
    public void TheBangsVerdictIsReadFromTheCapture()
    {
        if (Handshake() is not { } handshake)
            return;

        var participant = new StreamExchangeParticipant();
        _ = ExchangeReplay.RunChannel(handshake, participant, ChiakiMessageTap.StreamChannel);

        BangVerdict verdict = Assert.NotNull(participant.Verdict);

        Assert.True(verdict.VersionAccepted);
        Assert.True(verdict.EncryptedKeyAccepted);
        Assert.True(verdict.PublicKeyBytes > 0);
        Assert.True(verdict.SignatureBytes > 0);
        Assert.True(verdict.Accepted);
    }

    /// <summary>
    /// A refusal on any of the four is a refusal, and each one alone is enough.
    ///
    /// Which is the whole point of reading them: a BANG that arrived is not a BANG that agreed, and
    /// before PP423 a replay could not tell the two apart.
    /// </summary>
    [Theory]
    [InlineData(false, true, 65, 32)]
    [InlineData(true, false, 65, 32)]
    [InlineData(true, true, 0, 32)]
    [InlineData(true, true, 65, 0)]
    public void AnyOneRefusalIsARefusal(
        bool version, bool encryptedKey, int publicKey, int signature)
    {
        var verdict = new BangVerdict(version, encryptedKey, publicKey, signature);

        Assert.False(verdict.Accepted);
    }

    /// <summary>And all four together are an acceptance.</summary>
    [Fact]
    public void AllFourTogetherAreAnAcceptance()
    {
        Assert.True(new BangVerdict(true, true, 65, 32).Accepted);
    }

    /// <summary>
    /// A BANG this cannot walk is a refusal rather than an acceptance.
    ///
    /// Including the marker: a whole-redacted BANG carries no fields, so reading it must not answer
    /// "accepted" about a message nothing could see. That is the shape PP423 removed, and a corpus
    /// recorded before it would still be replayable against this - as a refusal.
    /// </summary>
    [Theory]
    [InlineData("0001 <redacted>")]
    [InlineData("0001 ")]
    [InlineData("")]
    [InlineData("0001 08-01")]
    public void ABangItCannotWalkIsARefusal(string payload)
    {
        BangVerdict verdict = StreamExchangeParticipant.ReadVerdict(payload);

        Assert.False(verdict.Accepted);
        Assert.False(verdict.VersionAccepted);
        Assert.Equal(0, verdict.PublicKeyBytes);
    }

    /// <summary>
    /// THE STREAMINFO IS ANSWERED WITH THREE, IN THAT ORDER.
    ///
    /// The ack, the controller connection, then the microphone's own STREAMINFO. A port sending the
    /// same three in another order would send the console a handshake it reads differently, and
    /// §PP294's warning applies: the set is the same and only the order tells them apart.
    /// </summary>
    [Fact]
    public void TheStreamInfoIsAnsweredWithThreeInOrder()
    {
        var participant = new StreamExchangeParticipant();

        IReadOnlyList<string> answers = participant.Receive(
            ChiakiMessageTap.StreamChannel, "000d 08-0d");

        Assert.Equal(
            [
                StreamExchangeParticipant.Render(StreamExchangeParticipant.StreamInfoAck()),
                StreamExchangeParticipant.Render(
                    StreamExchangeParticipant.ControllerConnection(dualSense: false)),
                StreamExchangeParticipant.Render(
                    StreamExchangeParticipant.MicrophoneStreamInfo()),
            ],
            answers);
    }

    /// <summary>
    /// The controller connection is the one message here that a setting changes.
    /// </summary>
    [Fact]
    public void TheControllerTypeFollowsTheDualSenseSetting()
    {
        Assert.Equal(
            StreamExchangeParticipant.DualShock4,
            StreamExchangeParticipant.ControllerConnection(dualSense: false)[^1]);

        Assert.Equal(
            StreamExchangeParticipant.DualSense,
            StreamExchangeParticipant.ControllerConnection(dualSense: true)[^1]);
    }

    /// <summary>
    /// PP422 AND THIS SHARE ONE SOURCE OF TRUTH for the microphone's header.
    ///
    /// The fourteen bytes come from AudioHeaderArguments, so a re-swap of channels and bits would
    /// move this participant and not the corpus - which is what would turn the replay red rather
    /// than letting the two drift apart quietly.
    /// </summary>
    [Fact]
    public void TheMicrophoneHeaderComesFromTheOnePlaceThatBuildsIt()
    {
        byte[] message = StreamExchangeParticipant.MicrophoneStreamInfo();
        byte[] header = AudioHeaderArguments.Microphone();

        Assert.Equal(header, message[^header.Length..]);

        // And the wrapper declares the length it actually carries.
        Assert.Equal(header.Length + 2, message[3]);
        Assert.Equal(header.Length, message[5]);
    }

    /// <summary>
    /// The BIG is the marker, and that is stated rather than worked around.
    ///
    /// It is redacted whole, so building one would produce a value the comparison cannot see. PP392's
    /// session participant can build and redact its request because that channel redacts by field;
    /// this one cannot.
    /// </summary>
    [Fact]
    public void TheBigIsOpenedWithTheMarker()
    {
        var participant = new StreamExchangeParticipant();

        string opening = Assert.Single(
            participant.Opening(ChiakiMessageTap.StreamChannel));

        Assert.Contains(MessageSecrets.Marker, opening, StringComparison.Ordinal);

        // And nothing is opened on a channel this does not own.
        Assert.Empty(participant.Opening(ChiakiMessageTap.SenkushaChannel));
        Assert.Empty(participant.Opening(ChiakiMessageTap.CtrlChannel));
    }

    /// <summary>
    /// Whether the C still makes the four checks in the order this reads them.
    /// </summary>
    [Fact]
    public void TheBangsLadderIsStillTheQtCores()
    {
        string? path = SanitizerSource.LocateRelative(@"lib\src\streamconnection.c");
        if (path is null)
            return;

        string code = CCall.Code(File.ReadAllText(path));

        int version = CCall.Mark(code, "if(!msg.bang_payload.version_accepted)");
        int key = CCall.Mark(code, "if(!msg.bang_payload.encrypted_key_accepted)", Math.Max(version, 0));
        int pub = CCall.Mark(code, "if(!ecdh_pub_key_buf.size)", Math.Max(key, 0));
        int sig = CCall.Mark(code, "if(!ecdh_sig_buf.size)", Math.Max(pub, 0));
        int derive = CCall.Mark(code, "chiaki_ecdh_derive_secret(", Math.Max(sig, 0));

        Assert.True(version >= 0, "the BANG no longer checks whether the version was accepted");
        Assert.True(key > version, "the encrypted-key check no longer follows the version one");
        Assert.True(pub > key, "the public key's presence is no longer checked after the flags");
        Assert.True(sig > pub, "the signature's presence is no longer checked after the key's");
        Assert.True(
            derive > sig,
            "the derivation no longer follows all four checks, so this replay's boundary is wrong");
    }

    /// <summary>
    /// The corpus's stream entries up to and including the console's STREAMINFOACK.
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
        var acks = 0;

        foreach (ExchangeEntry entry in recording.Entries.Where(
                     e => e.Channel == ChiakiMessageTap.StreamChannel))
        {
            handshake.Add(entry.AtMicroseconds, entry.Direction, entry.Channel, entry.Payload);

            // The second ack is the console's, which ends the handshake. The first is this side's.
            if (entry.Payload.StartsWith("000e ", StringComparison.Ordinal) && ++acks == 2)
                break;
        }

        return handshake;
    }
}
