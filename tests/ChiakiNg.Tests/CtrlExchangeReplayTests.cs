using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP391, PP23's next module: the first replay of a real console exchange against managed code.
///
/// PP293 built the replay harness, PP297 captured a PS5, PP342 modelled what the control channel
/// answers - and until now the harness had only ever been fed recordings the tests wrote
/// themselves. A participant that agrees with a synthetic recording agrees with the test that made
/// it. This asks whether PP342's table would have said what a real console actually heard.
///
/// THE ANSWER IS THE WHOLE OF PP23'S CLAIM, tested: a state machine cannot be compared by running
/// it twice the way a buffer function can, so the oracle is a recorded exchange. This is that
/// sentence executed rather than restated.
/// </summary>
public class CtrlExchangeReplayTests(ITestOutputHelper output)
{
    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        return path is null ? null : ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// PP297's capture was taken with DualSense and keyboard both off, which is why its session-id
    /// burst is three messages and not seven.
    /// </summary>
    private static readonly CtrlFeatures AsCaptured = new(DualSense: false, Keyboard: false);

    /// <summary>THE MEASUREMENT. The managed table replays the real exchange to the end.</summary>
    [Fact]
    public void TheTableReplaysTheRealExchange()
    {
        if (Corpus() is not { } recording)
            return;

        // Scoped to the control channel, because the capture holds two conversations on two sockets
        // and this participant owns one of them. Replaying the whole thing diverged on entry 0 - an
        // HTTP request the control channel was never going to send - and named it a protocol
        // failure, which is why RunChannel exists.
        Divergence divergence = ExchangeReplay.RunChannel(
            recording, new CtrlExchangeParticipant(AsCaptured), "ctrl");

        output.WriteLine(divergence.ToString());

        Assert.True(divergence.Matched, divergence.ToString());
    }

    /// <summary>
    /// And the scope is what makes it possible: unscoped, the same participant diverges on the
    /// first entry of a capture it agrees with completely.
    ///
    /// Kept because a reader meeting RunChannel would otherwise wonder what it buys, and because
    /// the failure it prevents reads as a defect in the model rather than in the call.
    /// </summary>
    [Fact]
    public void UnscopedTheSameParticipantDivergesOnAnHttpRequest()
    {
        if (Corpus() is not { } recording)
            return;

        Divergence divergence = ExchangeReplay.Run(recording, new CtrlExchangeParticipant(AsCaptured));

        Assert.False(divergence.Matched);
        Assert.Equal("session", divergence.Channel);
        Assert.Equal(DivergenceKind.NothingSent, divergence.Kind);
    }

    /// <summary>
    /// And it replayed something: the recording holds the two answers that make it a conversation
    /// rather than a log.
    ///
    /// PP271's lesson. A participant that answered nothing at all would match a recording with no
    /// Sent entries, and nothing in the verdict above would say which it had done.
    /// </summary>
    [Fact]
    public void TheExchangeItReplayedHasAnswersInIt()
    {
        if (Corpus() is not { } recording)
            return;

        IReadOnlyList<ExchangeEntry> sent =
            [.. recording.Entries.Where(e =>
                e.Direction == ExchangeDirection.Sent && e.Channel == "ctrl")];

        // Three heartbeat replies, two microphone toggles and a display-devices request.
        Assert.Equal(6, sent.Count);
        Assert.Equal(3, sent.Count(e => e.Payload.StartsWith("01fe", StringComparison.Ordinal)));
        Assert.Equal(2, sent.Count(e => e.Payload.StartsWith("0036", StringComparison.Ordinal)));
        Assert.Equal(1, sent.Count(e => e.Payload.StartsWith("0910", StringComparison.Ordinal)));
    }

    /// <summary>
    /// THE BURST IS THE PART A PAIR TABLE WOULD HAVE MISSED, which is §PP294's own warning.
    ///
    /// One session id produces three departures, in order, and the microphone is toggled TWICE -
    /// the capture has them 108 microseconds apart. A model that sent one would still match every
    /// other entry.
    /// </summary>
    [Fact]
    public void OneSessionIdProducesTheThreeMessageBurst()
    {
        var participant = new CtrlExchangeParticipant(AsCaptured);

        // As the recording has it: a session id's payload IS the session id, so PP326 redacts it.
        IReadOnlyList<string> burst = participant.Receive("ctrl", "0033 <redacted>");

        Assert.Equal(
            ["0036 00-01-01-59", "0036 00-01-01-59", "0910 00-00-00-00"],
            burst);

        // And a second session id produces nothing, which is what makes it idempotent rather than
        // repeatable - the state moved after the first was answered.
        Assert.Empty(participant.Receive("ctrl", "0033 <redacted>"));
    }

    /// <summary>A heartbeat is answered whatever it carried, and the reply has no payload.</summary>
    [Fact]
    public void AHeartbeatIsAnsweredWithAnEmptyReply()
    {
        var participant = new CtrlExchangeParticipant(AsCaptured);

        Assert.Equal(["01fe "], participant.Receive("ctrl", "00fe "));

        // Even with something in it, which the C warns about and answers anyway.
        Assert.Equal(["01fe "], participant.Receive("ctrl", "00fe 01-02"));
    }

    /// <summary>
    /// The session channel is a different conversation, so a ctrl participant says nothing about
    /// it - including about the HTTP answer the recording opens with.
    /// </summary>
    [Fact]
    public void TheSessionChannelIsNotAnsweredHere()
    {
        var participant = new CtrlExchangeParticipant(AsCaptured);

        Assert.Empty(participant.Receive("session", "HTTP/1.1 200 OK\r\n\r\n"));
    }

    /// <summary>
    /// A message the port has no name for is answered with nothing rather than guessed at - which
    /// is what the capture's 0x41 gets (PP331).
    /// </summary>
    [Fact]
    public void AnUnnamedMessageIsAnsweredWithSilence()
    {
        var participant = new CtrlExchangeParticipant(AsCaptured);

        Assert.Empty(participant.Receive("ctrl", "0041 00-00-00-00-02-01-00-00"));

        // And so is a payload that is not a ctrl rendering at all.
        Assert.Empty(participant.Receive("ctrl", "xx"));
        Assert.Null(CtrlExchangeParticipant.TypeOf("xx"));
    }

    /// <summary>
    /// A burst message with no payload written down THROWS rather than rendering an empty one.
    ///
    /// An invented payload would be compared against the console's and reported as a protocol
    /// divergence, which sends the reader to the wrong half of the problem.
    /// </summary>
    [Fact]
    public void AMessageWithNoWrittenPayloadIsRefused()
    {
        Assert.Throws<KeyNotFoundException>(() => CtrlExchangeParticipant.Render(0x1234));
    }

    /// <summary>
    /// And the features are the recording's, not the model's: with both on, the same session id
    /// produces seven rather than three.
    ///
    /// Stated because the replay above would pass for the wrong reason if the burst were fixed at
    /// three - it would agree with this capture and with no other.
    /// </summary>
    [Fact]
    public void TheBurstFollowsTheFeaturesAndNotTheRecording()
    {
        var everything = new CtrlExchangeParticipant(new CtrlFeatures(DualSense: true, Keyboard: true));

        IReadOnlyList<string> burst = everything.Receive("ctrl", "0033 <redacted>");

        Assert.Equal(7, burst.Count);
        Assert.StartsWith("0013", burst[0], StringComparison.Ordinal);
        Assert.StartsWith("0011", burst[1], StringComparison.Ordinal);
        Assert.EndsWith("00-00-00-00", burst[^1], StringComparison.Ordinal);
    }
}
