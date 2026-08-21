using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the writer that cannot use a JSON library, and the message it deliberately breaks.
/// </summary>
public class SessionMessageWriterTests
{
    private static readonly Candidate[] OneCandidate =
    [
        new(CandidateType.Local, "10.0.0.4", "203.0.113.9", 9295, 41234),
    ];

    private static byte[] Bytes(int count)
        => [.. Enumerable.Range(0, count).Select(i => (byte)i)];

    private static string? Request(
        IReadOnlyList<Candidate>? candidates = null, string? peerAddress = null)
        => SessionMessageWriter.ConnectionRequest(
            12345, 67890, Bytes(16), 2,
            candidates ?? OneCandidate,
            peerAddress ?? SessionMessageWriter.LocalPeerAddress(4242),
            Bytes(20));

    /// <summary>Everything the writer emits is escaped, because it all ends up inside a string.</summary>
    [Fact]
    public void TheRequestIsWrittenEscaped()
    {
        string? request = Request();

        Assert.NotNull(request);
        Assert.Contains("\\\"sid\\\":12345", request, StringComparison.Ordinal);
        Assert.Contains("\\\"skey\\\":\\\"", request, StringComparison.Ordinal);

        // And nowhere is a bare quote, which is what would break the envelope around it.
        Assert.DoesNotContain("\"", request.Replace("\\\"", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE MAC IS ALWAYS EMPTY. It is written from a one-byte buffer holding only the terminator,
    /// so this client never puts its own adapter's address on the wire - and the reader will not
    /// parse a MAC of any length but seventeen (PP194), so the field is write-only and read-never
    /// at both ends at once.
    /// </summary>
    [Fact]
    public void TheRouteMacIsNeverSent()
    {
        string? request = Request();

        Assert.NotNull(request);
        Assert.Equal("", SessionMessageWriter.RouteMacSent);
        Assert.Contains("\\\"defaultRouteMacAddr\\\":\\\"\\\"", request, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE BROKEN FIELD, ON PURPOSE. An absent local peer address is written as the empty string,
    /// which produces "localPeerAddr":, - not JSON at all. Sony's app does it, so the console
    /// expects it, and a serialiser that emitted well-formed output is the one that gets refused.
    /// </summary>
    [Fact]
    public void AnAbsentPeerAddressIsWrittenAsNothingAndBreaksTheJson()
    {
        string? request = Request(peerAddress: "");

        Assert.NotNull(request);
        Assert.Contains("\\\"localPeerAddr\\\":,", request, StringComparison.Ordinal);

        // And it really is unparseable, rather than merely odd-looking.
        Assert.Null(JsonC.Parse(Unescape(request)));
    }

    /// <summary>Where a peer address IS sent, it is this client calling itself REMOTE_PLAY.</summary>
    [Fact]
    public void ThePeerAddressNamesThisClientAsRemotePlay()
    {
        string address = SessionMessageWriter.LocalPeerAddress(4242);

        Assert.Equal(
            "{\\\"accountId\\\":\\\"4242\\\",\\\"platform\\\":\\\"REMOTE_PLAY\\\"}", address);
    }

    /// <summary>
    /// THE TWO ENUMS FAIL DIFFERENTLY. An undefined candidate type aborts the whole serialisation -
    /// there is no partial array to send, because the core's loop jumps out of the function.
    /// </summary>
    [Fact]
    public void AnUndefinedCandidateTypeAbortsTheWholeRequest()
    {
        Candidate[] candidates =
        [
            OneCandidate[0],
            new((CandidateType)99, "10.0.0.5", "203.0.113.10", 1, 2),
        ];

        Assert.Null(SessionMessageWriter.Candidates(candidates));
        Assert.Null(Request(candidates));
    }

    /// <summary>
    /// And an undefined ACTION is written out as the word "UNKNOWN" instead - a word the parser
    /// never compares against, so the message travels looking valid and reads back as nothing.
    /// Same file, same shape of switch, two opposite answers.
    /// </summary>
    [Fact]
    public void AnUndefinedActionIsWrittenRatherThanRefused()
    {
        string message = SessionMessageWriter.ShortMessage((SessionMessageAction)99, 1, 0);

        Assert.Contains("\\\"action\\\":\\\"UNKNOWN\\\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SessionMessageWriter.UnknownAction, SessionMessageEnvelope.Actions.Values);
    }

    /// <summary>The four defined actions are written as the four words the parser reads.</summary>
    [Theory]
    [InlineData(SessionMessageAction.Offer, "OFFER")]
    [InlineData(SessionMessageAction.Result, "RESULT")]
    [InlineData(SessionMessageAction.Accept, "ACCEPT")]
    [InlineData(SessionMessageAction.Terminate, "TERMINATE")]
    public void EachDefinedActionRoundTripsThroughItsWord(SessionMessageAction action, string word)
    {
        Assert.Equal(word, SessionMessageWriter.WordFor(action));
        Assert.Equal(action, SessionMessageEnvelope.ActionOf(word));
    }

    /// <summary>
    /// An ack carries the literal empty object, not an omitted field and not an empty string -
    /// so the one part of this message that is always well-formed is the part with nothing in it.
    /// </summary>
    [Fact]
    public void AnAckCarriesAnEmptyObjectRatherThanNothing()
    {
        string message = SessionMessageWriter.ShortMessage(SessionMessageAction.Result, 7, 0);

        Assert.Contains("\\\"connRequest\\\":{}", message, StringComparison.Ordinal);
        Assert.Equal("{}", SessionMessageWriter.AckConnectionRequest);
    }

    /// <summary>Undoes the escaping the envelope's payload string would undo for us.</summary>
    private static string Unescape(string escaped)
        => escaped.Replace("\\\"", "\"", StringComparison.Ordinal);

    /// <summary>
    /// THE WHOLE WAY ROUND. Writer to envelope to payload to reader - and the empty peer address
    /// survives it only because PP191's repair puts the missing object back. The two halves of the
    /// same deliberate breakage, meeting.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMessageWrittenHereReadsBackHere(bool withPeerAddress)
    {
        string? request = Request(
            peerAddress: withPeerAddress ? SessionMessageWriter.LocalPeerAddress(4242) : "");
        Assert.NotNull(request);

        string body = SessionMessageWriter.Message(SessionMessageAction.Offer, 1, 0, request);
        string envelope = SessionMessageWriter.Envelope(body, 4242, "0011223344556677", "PS5");

        // The envelope is well-formed even when its payload is not - the breakage is one level in.
        using JsonDocument? outer = JsonC.Parse(envelope);
        Assert.NotNull(outer);

        string? payload = JsonC.String(JsonC.Get(outer.RootElement, "payload"));
        Assert.NotNull(payload);

        string? json = SessionMessageEnvelope.JsonFor(payload);
        Assert.NotNull(json);

        using JsonDocument? inner = JsonC.Parse(json);
        Assert.NotNull(inner);

        Assert.Equal(
            SessionMessageAction.Offer,
            SessionMessageEnvelope.ActionOf(JsonC.String(JsonC.Get(inner.RootElement, "action"))));

        ConnectionRequest? read = ConnectionRequestReader.Read(
            JsonC.Get(inner.RootElement, "connRequest"));

        Assert.NotNull(read);
        Assert.Equal(12345u, read.Value.Sid);
        Assert.Equal(67890u, read.Value.PeerSid);
        Assert.Equal(Bytes(16), read.Value.Skey);
        Assert.Equal(Bytes(20), read.Value.LocalHashedId);
        Assert.Equal(OneCandidate, read.Value.Candidates);

        // The MAC this side sent was nothing, so the MAC that side reads is six zeros.
        Assert.Equal(new byte[6], read.Value.DefaultRouteMac);
    }

    /// <summary>
    /// And WITHOUT the repair the same message does not parse - which is what makes the repair a
    /// part of the protocol rather than a convenience.
    /// </summary>
    [Fact]
    public void TheSameMessageWithoutTheRepairDoesNotParse()
    {
        string? request = Request(peerAddress: "");
        Assert.NotNull(request);

        string body = SessionMessageWriter.Message(SessionMessageAction.Offer, 1, 0, request);
        string envelope = SessionMessageWriter.Envelope(body, 4242, "0011223344556677", "PS5");

        using JsonDocument? outer = JsonC.Parse(envelope);
        Assert.NotNull(outer);

        string? payload = JsonC.String(JsonC.Get(outer.RootElement, "payload"));
        Assert.NotNull(payload);

        string? unrepaired = SessionMessageEnvelope.JsonInPayload(payload);
        Assert.NotNull(unrepaired);
        Assert.Null(JsonC.Parse(unrepaired));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheWritersRulesAreStillTheQtCores()
    {
        string? path = SessionMessageWriterSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            SessionMessageWriterSource.ItStillSaysWhyThereIsNoJsonLibrary(core), "no JSON library, and why");
        Assert.True(
            SessionMessageWriterSource.TheEmptyPeerAddressIsStillDeliberate(core), "broken on purpose");
        Assert.True(SessionMessageWriterSource.TheMacIsStillAlwaysEmpty(core), "a one-byte MAC");
        Assert.True(
            SessionMessageWriterSource.AnUndefinedCandidateTypeStillAborts(core), "the candidate aborts");
        Assert.True(
            SessionMessageWriterSource.AnUndefinedActionIsStillWritten(core), "the action is written");
        Assert.True(
            SessionMessageWriterSource.AnAckStillCarriesAnEmptyObject(core), "an ack's empty object");
        Assert.True(
            SessionMessageWriterSource.TheRequestKeysAreStillInOrder(core), "eight keys, in order");
    }

    /// <summary>
    /// And that last check earns its green. The escaping in the format string is three backslashes
    /// deep, so a matcher that quietly found nothing would pass every day - this hands it a core
    /// with the keys swapped and requires a red.
    /// </summary>
    [Fact]
    public void TheKeyOrderCheckFailsOnAReorderedCore()
    {
        string? path = SessionMessageWriterSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        string swapped = core.Replace(
            "\\\\\\\"peerSid\\\\\\\":", "\\\\\\\"zzz\\\\\\\":", StringComparison.Ordinal);
        Assert.NotEqual(core, swapped);
        Assert.False(SessionMessageWriterSource.TheRequestKeysAreStillInOrder(swapped));
    }
}
