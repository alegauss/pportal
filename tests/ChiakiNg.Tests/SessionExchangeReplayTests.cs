using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP392, PP23's next module: the session channel replayed as a conversation.
///
/// PP332 compared the managed request against the recorded one and PP333 read the recorded answer
/// into its fields. Both were assertions about a HALF. §PP294 warns that a table of message-in,
/// message-out pairs passes while missing the ordering entirely, and two half-assertions are that
/// table - neither said the request comes first, nor that the answer is consumed after it, nor that
/// nothing else is said on that channel.
///
/// THIS IS THE CHANNEL THE CLIENT OPENS, which is what PP391's ctrl replay did not have to handle.
/// The console speaks first on ctrl; here the request precedes every arrival, so the harness needed
/// a way for a participant to say what it opens with.
/// </summary>
public class SessionExchangeReplayTests(ITestOutputHelper output)
{
    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        return path is null ? null : ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// The console PP297's capture was taken against, and a key of the shape a registered one
    /// stores. The key's value cannot matter - both sides redact it.
    /// </summary>
    private static SessionExchangeParticipant Participant() =>
        new(ChiakiTarget.Ps5_1, "192.168.1.224", [.. Enumerable.Repeat((byte)0x3e, 16)]);

    /// <summary>THE MEASUREMENT. The managed session half replays the real exchange.</summary>
    [Fact]
    public void TheManagedSessionHalfReplaysTheRealExchange()
    {
        if (Corpus() is not { } recording)
            return;

        SessionExchangeParticipant participant = Participant();

        Divergence divergence = ExchangeReplay.RunChannel(
            recording, participant, ChiakiMessageTap.SessionChannel);

        output.WriteLine(divergence.ToString());

        Assert.True(divergence.Matched, divergence.ToString());
    }

    /// <summary>
    /// And it read what came back, which is what separates a conversation from a monologue.
    ///
    /// A participant that opened with the right request and discarded everything afterwards matches
    /// this recording exactly as well, and the verdict above would not say which it had done.
    /// </summary>
    [Fact]
    public void TheAnswerIsReadAndNotDiscarded()
    {
        if (Corpus() is not { } recording)
            return;

        SessionExchangeParticipant participant = Participant();

        ExchangeReplay.RunChannel(recording, participant, ChiakiMessageTap.SessionChannel);

        Assert.Equal(1, participant.Received);
        Assert.NotNull(participant.Answer);

        // The three a session turns on. RP-Nonce is redacted in the corpus and still present as a
        // FIELD, which is what PP325 kept it for - so the shape is judged without the credential.
        output.WriteLine(participant.Answer.ToString());
    }

    /// <summary>
    /// PP392: without an opening, the same participant cannot replay this channel at all.
    ///
    /// Kept because the gap reads as a defect in the port rather than in the harness: the verdict
    /// is "expected a request, sent nothing" about an implementation that would have sent exactly
    /// that, and nothing in the sentence says the participant was never asked.
    /// </summary>
    [Fact]
    public void WithoutAnOpeningTheRequestCanNeverBeProduced()
    {
        if (Corpus() is not { } recording)
            return;

        Divergence divergence = ExchangeReplay.RunChannel(
            recording, new AnswersOnly(), ChiakiMessageTap.SessionChannel);

        Assert.False(divergence.Matched);
        Assert.Equal(DivergenceKind.NothingSent, divergence.Kind);
        Assert.Equal(0, divergence.EntryIndex);
    }

    /// <summary>A participant driven only by arrivals, which is what every one was before PP392.</summary>
    private sealed class AnswersOnly : IExchangeParticipant
    {
        public IReadOnlyList<string> Receive(string channel, string payload) => [];
    }

    /// <summary>
    /// The opening belongs to its channel and no other, so a session participant scoped to ctrl
    /// says nothing - the same separation PP391 needed in the other direction.
    /// </summary>
    [Fact]
    public void TheOpeningBelongsToOneChannel()
    {
        SessionExchangeParticipant participant = Participant();

        Assert.Empty(participant.Opening(ChiakiMessageTap.CtrlChannel));
        Assert.Single(participant.Opening(ChiakiMessageTap.SessionChannel));

        // And an arrival on the other channel is not read as an answer.
        Assert.Empty(participant.Receive(ChiakiMessageTap.CtrlChannel, "00fe "));
        Assert.Equal(0, participant.Received);
        Assert.Null(participant.Answer);
    }

    /// <summary>
    /// The existing participants are unchanged by the addition, which is what the default answer
    /// buys - PP391's ctrl replay is driven entirely by arrivals and stays that way.
    /// </summary>
    [Fact]
    public void AnAnswerOnlyParticipantOpensWithNothing()
    {
        // Through the INTERFACE, because the default answer lives there. A participant that never
        // opens a conversation does not mention opening at all, which is what makes the addition
        // invisible to PP391's ctrl replay.
        IExchangeParticipant ctrl = new CtrlExchangeParticipant(new CtrlFeatures());

        Assert.Empty(ctrl.Opening(ChiakiMessageTap.CtrlChannel));
        Assert.Empty(ctrl.Opening(ChiakiMessageTap.SessionChannel));
    }
}
