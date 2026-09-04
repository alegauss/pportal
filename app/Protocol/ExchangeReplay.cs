namespace ChiakiNg.Protocol;

/// <summary>
/// An implementation being replayed against a recording.
/// </summary>
public interface IExchangeParticipant
{
    /// <summary>
    /// Takes one thing the console said, and answers with everything it wants to say back.
    /// </summary>
    /// <returns>
    /// Payloads in the order they would be sent, or empty. Empty is the common answer - most
    /// received messages produce nothing.
    /// </returns>
    IReadOnlyList<string> Receive(string channel, string payload);

    /// <summary>
    /// PP392: what this says before anything is received, for a conversation IT opens.
    ///
    /// The harness drove participants entirely by arrivals, which is right for the control channel
    /// - the console speaks first there - and impossible for the session channel, where the client
    /// sends the request. A capture that opens with a Sent entry could not be replayed at all: no
    /// arrival precedes it, so Receive is never called, and the verdict is "expected a request,
    /// sent nothing" about an implementation that would have sent exactly that.
    ///
    /// Empty by default, so a participant that only answers says so by not overriding it.
    /// </summary>
    /// <param name="channel">The conversation being opened.</param>
    IReadOnlyList<string> Opening(string channel) => [];
}

/// <summary>Why a replay stopped, or that it did not.</summary>
public enum DivergenceKind
{
    /// <summary>The recording was replayed to the end and everything matched.</summary>
    None,

    /// <summary>Something was sent where the recording expected something else.</summary>
    WrongPayload,

    /// <summary>The recording expected a message and the implementation sent nothing.</summary>
    NothingSent,

    /// <summary>The implementation sent more than the recording ever expected.</summary>
    UnexpectedSend,
}

/// <summary>Where a replay went wrong.</summary>
/// <param name="Kind">What sort of divergence, or None.</param>
/// <param name="EntryIndex">Which entry of the recording, or -1 where the divergence is past its end.</param>
/// <param name="AtMicroseconds">That entry's offset, for a reader matching it against the file.</param>
/// <param name="Channel">Which conversation.</param>
/// <param name="Expected">What the recording has.</param>
/// <param name="Actual">What the implementation produced.</param>
public readonly record struct Divergence(
    DivergenceKind Kind, int EntryIndex, long AtMicroseconds,
    string Channel, string? Expected, string? Actual)
{
    /// <summary>Whether the replay matched all the way through.</summary>
    public bool Matched => Kind == DivergenceKind.None;

    /// <summary>A sentence naming the entry, for a test that fails.</summary>
    public override string ToString() => Kind switch
    {
        DivergenceKind.None => "matched",
        DivergenceKind.WrongPayload =>
            $"entry {EntryIndex} at {AtMicroseconds}us on {Channel}: expected {Expected}, sent {Actual}",
        DivergenceKind.NothingSent =>
            $"entry {EntryIndex} at {AtMicroseconds}us on {Channel}: expected {Expected}, sent nothing",
        DivergenceKind.UnexpectedSend =>
            $"after the recording ended, on {Channel}: sent {Actual}, which nothing expected",
        _ => "unknown",
    };
}

/// <summary>
/// PP297: a recording replayed against an implementation, stopping at the first thing it says
/// differently.
///
/// This is the comparison the four untested modules need. The eleven that are ported were judged by
/// running the managed side and the C side over the same buffers and diffing - a session cannot be,
/// because it is a state machine over a socket with no console in the room. What can be done is
/// hand it what the console said, in order, and check it says back what the C said back.
///
/// FIRST divergence, not all of them
/// ---------------------------------
/// A state machine that diverges once is not a state machine that diverged once. Everything after
/// the first difference is a different conversation, and reporting forty mismatches when one thing
/// went wrong at entry three buries the only one that means anything. So the replay stops.
///
/// The recording is the expectation and not the input
/// --------------------------------------------------
/// Only the Received entries are fed in. The Sent ones are what the implementation has to produce
/// on its own - handing them over would be replaying the C's answers to itself and calling the
/// agreement a result.
/// </summary>
public static class ExchangeReplay
{
    /// <summary>
    /// PP23: the same, scoped to the conversation one participant owns.
    ///
    /// A capture holds more than one conversation on more than one socket - PP297's has an HTTP
    /// request and its answer beside a control channel - and <see cref="Run(ExchangeRecording,
    /// IExchangeParticipant)"/> replays all of it. So a participant that implements the control
    /// channel and nothing else diverges on the recording's FIRST entry, which is an HTTP request
    /// it was never going to send, and the verdict names a protocol failure that is a scoping one.
    ///
    /// The channels really are separate: different sockets, different framing, different code. What
    /// was missing is a way to say so.
    /// </summary>
    /// <param name="recording">The whole capture.</param>
    /// <param name="participant">The implementation being judged.</param>
    /// <param name="channel">The one conversation it owns.</param>
    public static Divergence RunChannel(
        ExchangeRecording recording, IExchangeParticipant participant, string channel)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(channel);

        var scoped = new ExchangeRecording();
        foreach (ExchangeEntry entry in recording.Entries.Where(
                     e => string.Equals(e.Channel, channel, StringComparison.Ordinal)))
        {
            scoped.Add(entry.AtMicroseconds, entry.Direction, entry.Channel, entry.Payload);
        }

        return Run(scoped, participant);
    }

    /// <summary>
    /// Replays every entry, in order, and answers where it first diverged.
    /// </summary>
    public static Divergence Run(ExchangeRecording recording, IExchangeParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(participant);

        var outbox = new Queue<(string Channel, string Payload)>();

        // PP392: what the participant says first, before any arrival. A conversation the console
        // opens produces nothing here and is driven entirely by Receive, as before.
        foreach (string channel in recording.Entries
                     .Select(e => e.Channel)
                     .Distinct(StringComparer.Ordinal))
        {
            foreach (string opening in participant.Opening(channel))
                outbox.Enqueue((channel, opening));
        }

        for (int i = 0; i < recording.Entries.Count; i++)
        {
            ExchangeEntry entry = recording.Entries[i];

            if (entry.Direction == ExchangeDirection.Received)
            {
                foreach (string reply in participant.Receive(entry.Channel, entry.Payload))
                    outbox.Enqueue((entry.Channel, reply));

                continue;
            }

            if (outbox.Count == 0)
            {
                return new Divergence(
                    DivergenceKind.NothingSent, i, entry.AtMicroseconds, entry.Channel,
                    entry.Payload, null);
            }

            (string Channel, string Payload) sent = outbox.Dequeue();
            if (!string.Equals(sent.Payload, entry.Payload, StringComparison.Ordinal))
            {
                return new Divergence(
                    DivergenceKind.WrongPayload, i, entry.AtMicroseconds, entry.Channel,
                    entry.Payload, sent.Payload);
            }
        }

        // Anything still queued was sent that the recording never saw. That is a divergence too and
        // the easiest one to miss: a replay that only checked what it was asked for would call an
        // implementation that talks too much a match.
        if (outbox.Count > 0)
        {
            (string Channel, string Payload) extra = outbox.Dequeue();
            return new Divergence(
                DivergenceKind.UnexpectedSend, -1, 0, extra.Channel, null, extra.Payload);
        }

        return default;
    }
}
