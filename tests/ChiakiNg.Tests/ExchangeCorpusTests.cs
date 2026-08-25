using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP297: the recording that had never been made, now made - and held to what it promised.
///
/// This is a real exchange with a real PS5, taken by `ChiakiNg.exe --capture-exchange`: the console
/// woken out of standby, the session request and its answer, the control channel through login and
/// session id, and the heartbeats that follow. It is the oracle the four untested modules -
/// session.c, ctrl.c, streamconnection.c and senkusha.c - are to be ported against, because a state
/// machine over a socket cannot be compared by running it twice the way a buffer function can.
///
/// THESE ASSERTIONS ARE NOT THE REPLAY. Replaying it against a managed implementation is PP293,
/// PP294 and PP295, and there is nothing yet to replay it against. What is asserted here is that
/// the corpus is a recording of what it claims to be, and - the half that matters more - that
/// nothing in it is a secret. A corpus is a file in a public repository, and every redaction rule
/// this port wrote was written for this file.
/// </summary>
public class ExchangeCorpusTests
{
    /// <summary>Where the capture lives, relative to the repository root.</summary>
    public const string RelativePath = @"tests\corpus\exchange-ps5.txt";

    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(RelativePath);
        return path is null ? null : ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>It parses, and it is the format the replay reads.</summary>
    [Fact]
    public void TheCorpusIsARecording()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.NotEmpty(recording.Entries);
    }

    /// <summary>
    /// Both halves of the opening are there: the session request going out and the answer coming
    /// back. Those are the two chokepoints in session.c, and a capture missing either was armed too
    /// late - which is the failure PP327 arms the recorder before any window to avoid.
    /// </summary>
    [Fact]
    public void TheSessionRequestAndItsAnswerAreBothInIt()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.Contains(recording.Entries, e =>
            e.Channel == "session" && e.Direction == ExchangeDirection.Sent
            && e.Payload.Contains("/sie/ps5/rp/sess/init", StringComparison.Ordinal));

        Assert.Contains(recording.Entries, e =>
            e.Channel == "session" && e.Direction == ExchangeDirection.Received
            && e.Payload.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal));
    }

    /// <summary>
    /// The control conversation reached the two messages that mean it worked.
    ///
    /// A session that fails handshake still records a request and an answer, so those alone do not
    /// say the capture is of a working session. LOGIN and SESSION_ID do: the console does not send
    /// a session id to a client it refused.
    /// </summary>
    [Fact]
    public void TheControlChannelGotThroughLoginAndSessionId()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        IReadOnlyList<string> ctrl =
            [.. recording.Entries.Where(e => e.Channel == "ctrl").Select(e => e.Payload)];

        Assert.Contains(ctrl, p => p.StartsWith("0005 ", StringComparison.Ordinal));
        Assert.Contains(ctrl, p => p.StartsWith("0033 ", StringComparison.Ordinal));

        // And the heartbeats, which are what a session that stayed up looks like.
        Assert.Contains(ctrl, p => p.StartsWith("00fe", StringComparison.Ordinal));
        Assert.Contains(ctrl, p => p.StartsWith("01fe", StringComparison.Ordinal));
    }

    /// <summary>
    /// NOTHING IN IT IS A SECRET, which is the assertion this file exists for.
    ///
    /// Every redaction rule the port wrote was written for this moment, and each is checked against
    /// what the console actually sent rather than against a string a test made up:
    ///
    ///   PP325's header rule took RP-Registkey out of the request and RP-Nonce out of the answer.
    ///   Neither had ever reached a log in the clear, but only because PP320 redacts a hexdump row
    ///   whole - and a structured payload has no such cover.
    ///
    ///   PP326's type list took the payloads of LOGIN and SESSION_ID, which are the credential and
    ///   the session id. Nothing about those bytes looks like a secret; only the type says so.
    ///
    ///   PP88's IPv4 rule took the console's address out of the request's Host header.
    /// </summary>
    [Fact]
    public void NothingInTheCorpusIsASecret()
    {
        string? path = SanitizerSource.LocateRelative(RelativePath);
        if (path is null)
            return;

        string text = File.ReadAllText(path);

        Assert.Contains("RP-Registkey: <redacted>", text, StringComparison.Ordinal);
        Assert.Contains("RP-Nonce: <redacted>", text, StringComparison.Ordinal);
        Assert.Contains("Host: <redacted-ipv4>", text, StringComparison.Ordinal);

        ExchangeRecording? recording = Corpus();
        Assert.NotNull(recording);

        foreach (ExchangeEntry entry in recording.Entries.Where(e => e.Channel == "ctrl"))
        {
            if (!ushort.TryParse(
                    entry.Payload.AsSpan(0, 4), System.Globalization.NumberStyles.HexNumber, null,
                    out ushort type))
            {
                continue;
            }

            // A secret type carries the marker and nothing else; anything else carries no marker.
            Assert.Equal(
                !CtrlMessageSecrets.MayRecord(type),
                entry.Payload.Contains(CtrlMessageSecrets.Marker, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The offsets are ordered and start at zero, which is what makes two recordings comparable.
    ///
    /// PP328 is why this is checked on a real capture and not only on synthetic ticks: the offsets
    /// here came off this machine's counter, and the arithmetic that produces them was wrong on any
    /// counter whose rate is not a whole number of ticks per microsecond.
    /// </summary>
    [Fact]
    public void TheOffsetsAreOrderedAndStartAtZero()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.Equal(0, recording.Entries[0].AtMicroseconds);

        for (var i = 1; i < recording.Entries.Count; i++)
        {
            Assert.True(
                recording.Entries[i].AtMicroseconds >= recording.Entries[i - 1].AtMicroseconds,
                $"entry {i} goes backwards in time");
        }
    }
}
