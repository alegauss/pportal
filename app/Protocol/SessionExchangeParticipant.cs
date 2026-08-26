using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP392, PP23's next module: the session channel replayed as a conversation.
///
/// PP332 built the managed request and PP333 put its answer through PP33's parser, and both were
/// asserted directly - the request compared character for character, the answer read into its three
/// fields. What neither did was replay them as an EXCHANGE: nothing said the request comes first,
/// or that the answer is consumed after it, or that nothing else is said on that channel.
///
/// §PP294's warning is about exactly that gap. A table of message-in, message-out pairs passes while
/// missing the ordering entirely, and two assertions about two halves are that table.
///
/// THIS SIDE SPEAKS FIRST, which is why the harness needed <see cref="IExchangeParticipant.Opening"/>.
/// The control channel is console-opened and could be driven by arrivals alone; the session channel
/// is client-opened, so a capture of it begins with a Sent entry that no arrival precedes.
///
/// THE REQUEST IS PRODUCED REDACTED, because the recording is. PP325's header rule took RP-Registkey
/// out and PP88's took the console's address, and a participant that emitted the real ones would
/// diverge on two lines that are missing from the corpus by design rather than by accident.
///
/// AND THE ANSWER IS READ, NOT IGNORED. A participant that says one thing and discards everything
/// afterwards matches this recording too, so <see cref="Answer"/> exposes what it parsed and the
/// test asserts the three fields a session turns on.
/// </summary>
public sealed class SessionExchangeParticipant : IExchangeParticipant
{
    private readonly ChiakiTarget target;
    private readonly string host;
    private readonly byte[] registKey;

    /// <summary>Builds one for a target, a console address and a registration key.</summary>
    public SessionExchangeParticipant(ChiakiTarget target, string host, byte[] registKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(registKey);

        this.target = target;
        this.host = host;
        this.registKey = registKey;
    }

    /// <summary>What the console answered, once it has. Null until then.</summary>
    public SessionResponseFields? Answer { get; private set; }

    /// <summary>How many things arrived on this channel, so silence can be told from a match.</summary>
    public int Received { get; private set; }

    /// <summary>
    /// The request, redacted the way the recording is.
    ///
    /// Both rules, in the order PP332's own comparison applies them: the header sanitiser takes the
    /// registration key, the log sanitiser takes the address.
    /// </summary>
    public IReadOnlyList<string> Opening(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel != ChiakiMessageTap.SessionChannel)
            return [];

        string request = SessionHandshake.Request(target, host, registKey);

        return [SessionLogSanitizer.Sanitize(SessionHeaderSanitizer.Sanitize(request))];
    }

    /// <summary>
    /// The answer, read into the fields a session turns on - and nothing said back.
    ///
    /// The session channel is one request and one reply. Everything the client does with what it
    /// learnt happens on the control channel, which is a different socket and a different
    /// participant.
    /// </summary>
    public IReadOnlyList<string> Receive(string channel, string payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        if (channel != ChiakiMessageTap.SessionChannel)
            return [];

        Received++;
        Answer = SessionHandshake.ReadAnswer(payload);

        return [];
    }
}
