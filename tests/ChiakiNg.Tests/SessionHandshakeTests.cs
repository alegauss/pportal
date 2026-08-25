using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP332, continuing PP293: the session request, judged by what the C actually sent.
///
/// This is the first slice of session.c ported against PP297's recording rather than against a
/// reading of the source. The corpus holds the real request that reached a real PS5; the managed
/// builder produces one from the same inputs; put through the same redaction, the two are compared
/// character for character.
///
/// THAT COMPARISON IS THE WHOLE POINT. A port checked against the source it is porting agrees with
/// whatever the reader understood, including the parts they misread. A port checked against the
/// wire agrees with the console or it does not.
/// </summary>
public class SessionHandshakeTests
{
    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        return path is null ? null : ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// THE MANAGED REQUEST IS THE ONE THAT WENT OUT.
    ///
    /// Built from the same target and the same key shape, redacted by the same two rules the
    /// recording was redacted by, and compared against the recorded line. The address and the key
    /// are gone from both sides - which is exactly what makes this runnable in a public checkout,
    /// and costs nothing: what is being compared is the header block, its order and its spelling.
    /// </summary>
    [Fact]
    public void TheManagedRequestMatchesTheOneTheConsoleReceived()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        ExchangeEntry sent = recording.Entries.First(e =>
            e.Channel == "session" && e.Direction == ExchangeDirection.Sent);

        // A 16-byte key, which is what a registered console stores. Its value cannot matter: both
        // sides redact it, and the assertion is about everything around it.
        byte[] registKey = [.. Enumerable.Repeat((byte)0x3e, 16)];

        string built = SessionHandshake.Request(ChiakiTarget.Ps5_1, "192.168.1.224", registKey);
        string redacted = SessionLogSanitizer.Sanitize(SessionHeaderSanitizer.Sanitize(built));

        Assert.Equal(sent.Payload, redacted);
    }

    /// <summary>
    /// And the recorded answer reads into the three fields a session turns on.
    ///
    /// RP-Nonce is redacted in the corpus and still present as a FIELD, which is what PP325 kept it
    /// for: a reader can see the console sent one without the corpus carrying it. That also means
    /// the recording cannot be replayed into a real handshake, which is correct - it is an oracle
    /// for the shape of the exchange, not a credential.
    /// </summary>
    [Fact]
    public void TheRecordedAnswerReadsIntoTheFieldsSessionTurnsOn()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        ExchangeEntry received = recording.Entries.First(e =>
            e.Channel == "session" && e.Direction == ExchangeDirection.Received);

        SessionResponseFields? answer = SessionHandshake.ReadAnswer(received.Payload);

        Assert.NotNull(answer);
        Assert.True(answer.Value.Success);
        Assert.Equal("<redacted>", answer.Value.Nonce);
        Assert.NotNull(answer.Value.RpVersion);
        Assert.Equal(0u, answer.Value.ErrorCode);
    }

    /// <summary>
    /// PP333: it goes through PP33's parser, so libchiaki's quirks come with it.
    ///
    /// The reader this replaced trimmed values and split on newlines, and would have answered
    /// happily here. HttpResponse.Parse refuses a reply that is not one, which is the behaviour the
    /// rest of the port already agrees with libchiaki about.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAResponseIsRefusedRatherThanRead()
    {
        Assert.Null(SessionHandshake.ReadAnswer("RP-Nonce: abc\r\n\r\n"));
        Assert.Null(SessionHandshake.ReadAnswer("HTTP/1.1 200 OK"));
    }

    /// <summary>
    /// Read case-insensitively, because the two ends of session.c disagree with each other: the
    /// request writes "Rp-Version" and PP296 settled the reply is matched without regard to case.
    /// </summary>
    [Fact]
    public void AHeaderIsFoundWhicheverWayItIsSpelled()
    {
        SessionResponseFields? answer = SessionHandshake.ReadAnswer(
            "HTTP/1.1 200 OK\r\nRp-Version: 10.0\r\nRP-NONCE: abc\r\n\r\n");

        Assert.NotNull(answer);
        Assert.Equal("10.0", answer.Value.RpVersion);
        Assert.Equal("abc", answer.Value.Nonce);
    }

    /// <summary>The path is the one branch the target decides, and all three are reproduced.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8, "/sce/rp/session")]
    [InlineData(ChiakiTarget.Ps4_9, "/sce/rp/session")]
    [InlineData(ChiakiTarget.Ps4_10, "/sie/ps4/rp/sess/init")]
    [InlineData(ChiakiTarget.Ps5_1, "/sie/ps5/rp/sess/init")]
    public void ThePathIsTheOneTheTargetChooses(ChiakiTarget target, string path)
    {
        Assert.Equal(path, SessionHandshake.PathFor(target));
    }

    /// <summary>
    /// The key is hex of the bytes BEFORE the first NUL - a different reading of the same field
    /// than the wake credential takes, and using either in the other's place is refused for a
    /// reason naming neither.
    /// </summary>
    [Fact]
    public void TheKeyIsHexOfWhatPrecedesTheFirstNul()
    {
        Assert.Equal("3e91107c", SessionHandshake.RegistKeyHex([0x3e, 0x91, 0x10, 0x7c, 0, 0, 0, 0]));
        Assert.Equal("", SessionHandshake.RegistKeyHex([0, 0x91]));
    }

    /// <summary>
    /// And session.c still declares the format this reproduces.
    ///
    /// The corpus is one console on one firmware. A header reworded upstream would leave this port
    /// sending a request that console has stopped expecting, and the recording would go on agreeing
    /// with the copy rather than with the C.
    /// </summary>
    [Fact]
    public void SessionStillDeclaresTheFormatAndThePaths()
    {
        string? path = SessionHandshakeSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(SessionHandshakeSource.TheFormatIsStill(core), "session_request_fmt has changed");
        Assert.True(SessionHandshakeSource.ThePathsAreStill(core), "the three session paths have changed");
    }
}
