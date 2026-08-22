using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP297: the replay, against implementations built to agree and to disagree in each way.
///
/// There is no captured exchange yet, so every recording here is synthetic - which is the right
/// way round: the harness has to be known-good before a real recording is trusted to judge a port
/// with it. A replay that reported "matched" for the wrong reason would make the first real
/// comparison worthless and look like success.
/// </summary>
public class ExchangeReplayTests
{
    /// <summary>An implementation that answers from a script, so a test can make it agree or not.</summary>
    private sealed class Scripted(params string[][] replies) : IExchangeParticipant
    {
        private int at;

        public int Received { get; private set; }

        public IReadOnlyList<string> Receive(string channel, string payload)
        {
            Received++;
            return at < replies.Length ? replies[at++] : [];
        }
    }

    private static ExchangeRecording Recording(params (ExchangeDirection Direction, string Payload)[] entries)
    {
        var recording = new ExchangeRecording();
        long at = 0;
        foreach ((ExchangeDirection direction, string payload) in entries)
        {
            recording.Add(at, direction, "session", payload);
            at += 1000;
        }

        return recording;
    }

    /// <summary>An implementation that says exactly what the recording has, matches.</summary>
    [Fact]
    public void AnAgreeingImplementationMatches()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "hello"),
            (ExchangeDirection.Sent, "hi"),
            (ExchangeDirection.Received, "bye"));

        Divergence result = ExchangeReplay.Run(recording, new Scripted(["hi"], []));

        Assert.True(result.Matched, result.ToString());
    }

    /// <summary>One that says something else is caught, with the entry named.</summary>
    [Fact]
    public void AWrongPayloadIsCaughtAndNamed()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "hello"),
            (ExchangeDirection.Sent, "hi"));

        Divergence result = ExchangeReplay.Run(recording, new Scripted(["goodbye"]));

        Assert.Equal(DivergenceKind.WrongPayload, result.Kind);
        Assert.Equal(1, result.EntryIndex);
        Assert.Equal(1000, result.AtMicroseconds);
        Assert.Equal("hi", result.Expected);
        Assert.Equal("goodbye", result.Actual);
    }

    /// <summary>One that stays silent where the recording expected a message is caught too.</summary>
    [Fact]
    public void SilenceWhereSomethingWasExpectedIsCaught()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "hello"),
            (ExchangeDirection.Sent, "hi"));

        Divergence result = ExchangeReplay.Run(recording, new Scripted([]));

        Assert.Equal(DivergenceKind.NothingSent, result.Kind);
        Assert.Equal("hi", result.Expected);
        Assert.Null(result.Actual);
    }

    /// <summary>
    /// And one that talks too much, which is the divergence a lazier replay would miss.
    ///
    /// Checking only what the recording asked for would call an implementation that sends a message
    /// nobody expected a match - and an extra message on a control channel is a real defect, not a
    /// harmless one.
    /// </summary>
    [Fact]
    public void AnExtraMessageAtTheEndIsCaught()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "hello"),
            (ExchangeDirection.Sent, "hi"));

        Divergence result = ExchangeReplay.Run(recording, new Scripted(["hi", "and another thing"]));

        Assert.Equal(DivergenceKind.UnexpectedSend, result.Kind);
        Assert.Equal("and another thing", result.Actual);
    }

    /// <summary>
    /// The replay stops at the FIRST divergence rather than reporting every one.
    ///
    /// Everything after a state machine's first difference is a different conversation, so the
    /// second mismatch is not evidence of anything. The count of Receive calls is how this is
    /// measured: a replay that carried on would keep feeding entries in.
    /// </summary>
    [Fact]
    public void ItStopsAtTheFirstDivergence()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "one"),
            (ExchangeDirection.Sent, "a"),
            (ExchangeDirection.Received, "two"),
            (ExchangeDirection.Sent, "b"),
            (ExchangeDirection.Received, "three"));

        var scripted = new Scripted(["wrong"], ["b"], []);
        Divergence result = ExchangeReplay.Run(recording, scripted);

        Assert.Equal(DivergenceKind.WrongPayload, result.Kind);
        Assert.Equal(1, result.EntryIndex);

        // Only the first Received entry ever reached it.
        Assert.Equal(1, scripted.Received);
    }

    /// <summary>
    /// Only the Received entries are fed in.
    ///
    /// Handing the implementation the Sent ones would be replaying the C's own answers back to it
    /// and calling the agreement a result. Three entries, two of them Received, so two calls.
    /// </summary>
    [Fact]
    public void OnlyTheReceivedEntriesAreFedIn()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "one"),
            (ExchangeDirection.Sent, "a"),
            (ExchangeDirection.Received, "two"));

        var scripted = new Scripted(["a"], []);
        Assert.True(ExchangeReplay.Run(recording, scripted).Matched);
        Assert.Equal(2, scripted.Received);
    }

    /// <summary>An empty recording matches anything that stays quiet, and nothing that does not.</summary>
    [Fact]
    public void AnEmptyRecordingIsAnEmptyExpectation()
    {
        Assert.True(ExchangeReplay.Run(new ExchangeRecording(), new Scripted()).Matched);
    }

    /// <summary>A recording read back off disk replays the same as the one in memory.</summary>
    [Fact]
    public void AWrittenRecordingReplaysTheSame()
    {
        ExchangeRecording recording = Recording(
            (ExchangeDirection.Received, "hello\twith a tab"),
            (ExchangeDirection.Sent, "multi\nline"));

        ExchangeRecording? read = ExchangeRecording.Read(recording.Write());
        Assert.NotNull(read);

        Assert.True(ExchangeReplay.Run(read, new Scripted(["multi\nline"])).Matched);
    }
}
