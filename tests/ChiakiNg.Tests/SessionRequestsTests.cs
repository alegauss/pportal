using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the four bodies a session is set up with, and what they call things.
/// </summary>
public class SessionRequestsTests
{
    private static byte[] Blob(byte first)
        => [.. Enumerable.Range(0, SessionRequests.DataLength).Select(i => (byte)(first + i))];

    private static string Unescape(string escaped)
        => escaped.Replace("\\\"", "\"", StringComparison.Ordinal);

    private static string? Core()
    {
        string? path = SessionRequestsSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THREE FIELDS, ONE WORD. accountId, deviceUniqueId and platform are all the literal "me" -
    /// three different things, one placeholder, because the server fills them from the token.
    ///
    /// A port that helpfully sent the real values would send what the official app never sends.
    /// </summary>
    [Fact]
    public void TheCreateBodySaysMeForAllThreeIdentityFields()
    {
        string body = SessionRequests.Create("push-context");

        using JsonDocument? json = JsonC.Parse(body);
        Assert.NotNull(json);

        JsonElement? member = JsonC.ArrayAt(
            JsonC.Get(JsonC.ArrayAt(JsonC.Get(json.RootElement, "remotePlaySessions"), 0), "members"), 0);

        Assert.NotNull(member);
        foreach (string field in SessionRequests.CreateIdentityFields)
            Assert.Equal("me", JsonC.String(JsonC.Get(member, field)));

        Assert.Equal(3, SessionRequests.CreateIdentityFields.Count);
    }

    /// <summary>And the push context id is the one thing in it that is actually this session's.</summary>
    [Fact]
    public void ThePushContextIsTheOnlyRealValueInTheCreateBody()
        => Assert.Contains("\"pushContextId\":\"abc-123\"", SessionRequests.Create("abc-123"), StringComparison.Ordinal);

    /// <summary>
    /// THE ACCOUNT ID IS AN INTEGER IN ONE PAYLOAD AND A STRING IN TWO OTHERS. Same value, same
    /// session, two types - and PP183 established json-c would have coerced either way, so nothing
    /// forced the difference.
    /// </summary>
    [Fact]
    public void TheAccountIdIsBareHereAndQuotedElsewhere()
    {
        string payload = SessionRequests.StartPayload(4242, "sid", Blob(1), Blob(100));

        Assert.Contains("\\\"accountId\\\":4242,", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"accountId\\\":\\\"4242", payload, StringComparison.Ordinal);

        // The two that quote it.
        Assert.Contains(
            "\\\"accountId\\\":\\\"4242\\\"", SessionMessageWriter.LocalPeerAddress(4242), StringComparison.Ordinal);
        Assert.Contains(
            "\"accountId\":\"4242\"",
            SessionMessageWriter.Envelope("body", 4242, "duid", "PS5"),
            StringComparison.Ordinal);
    }

    /// <summary>The start payload is escaped, and unescapes into readable JSON.</summary>
    [Fact]
    public void TheStartPayloadIsEscapedAndUnescapesCleanly()
    {
        string payload = SessionRequests.StartPayload(4242, "a-session-id", Blob(1), Blob(100));

        using JsonDocument? json = JsonC.Parse(Unescape(payload));
        Assert.NotNull(json);

        Assert.Equal(4242, JsonC.Int64(JsonC.Get(json.RootElement, "accountId")));
        Assert.Equal("a-session-id", JsonC.String(JsonC.Get(json.RootElement, "sessionId")));
        Assert.Equal("Windows", JsonC.String(JsonC.Get(json.RootElement, "clientType")));
        Assert.Equal(0, JsonC.Int(JsonC.Get(json.RootElement, "roomId")));
    }

    /// <summary>And it rides inside the envelope's initialParams string, like PP196's body does.</summary>
    [Fact]
    public void TheEnvelopeCarriesThePayloadAsAString()
    {
        string payload = SessionRequests.StartPayload(4242, "sid", Blob(1), Blob(100));
        string envelope = SessionRequests.StartEnvelope("00ff", payload, "PS5");

        using JsonDocument? json = JsonC.Parse(envelope);
        Assert.NotNull(json);

        string? initial = JsonC.String(
            JsonC.Get(JsonC.Get(JsonC.Get(json.RootElement, "commandDetail"), "parameters"), "initialParams"));

        Assert.NotNull(initial);

        using JsonDocument? inner = JsonC.Parse(initial);
        Assert.NotNull(inner);
        Assert.Equal(4242, JsonC.Int64(JsonC.Get(inner.RootElement, "accountId")));
    }

    /// <summary>
    /// THIS CLIENT HAS THREE NAMES FOR ITSELF - "Windows" in the start payload, "REMOTE_PLAY" in
    /// the local peer address (PP196), and "me" in the create body. None is derived from another.
    /// </summary>
    [Fact]
    public void TheClientCallsItselfThreeDifferentThings()
    {
        string[] names =
        [
            SessionRequests.ClientType,
            SessionMessageWriter.ClientPlatform,
            SessionRequests.CreatePlaceholder,
        ];

        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["Windows", "REMOTE_PLAY", "me"], names);
    }

    /// <summary>The two blobs are sixteen bytes and base64 to exactly the buffer's width.</summary>
    [Fact]
    public void TheTwoBlobsFitTheirBufferExactly()
    {
        string base64 = Convert.ToBase64String(Blob(1));

        Assert.Equal(SessionRequests.DataBase64Length, base64.Length);
        Assert.Equal(SessionRequests.DataBase64Buffer, SessionRequests.DataBase64Length + 1);
    }

    /// <summary>The wake-up body carries them unescaped, with the version it claims.</summary>
    [Fact]
    public void TheWakeupBodyCarriesTheBlobsAndTheVersion()
    {
        string body = SessionRequests.WakeupEnvelope(Blob(1), Blob(100), "a-session-id");

        using JsonDocument? json = JsonC.Parse(body);
        Assert.NotNull(json);

        JsonElement? data = JsonC.Get(json.RootElement, "data");
        Assert.Equal("10.0", JsonC.String(JsonC.Get(data, "protocolVer")));
        Assert.Equal("Windows", JsonC.String(JsonC.Get(data, "clientType")));
        Assert.Equal(Convert.ToBase64String(Blob(1)), JsonC.String(JsonC.Get(data, "data1")));
        Assert.Equal("remotePlay", JsonC.String(JsonC.Get(json.RootElement, "dataTypeSuffix")));
    }

    /// <summary>
    /// AND customData1 IS LENGTH-GATED BEFORE IT IS DECODED - exactly thirty-two characters, which
    /// is the check that runs before PP192's two-round decode.
    /// </summary>
    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void CustomData1MustBeExactlyThirtyTwoCharacters(int length, bool accepted)
        => Assert.Equal(accepted, SessionRequests.CustomData1IsTheRightLength(new string('a', length)));

    /// <summary>And a missing one is not the right length either.</summary>
    [Fact]
    public void AMissingCustomData1IsNotTheRightLength()
        => Assert.False(SessionRequests.CustomData1IsTheRightLength(null));

    /// <summary>
    /// ONE field in all four templates carries a space after its colon, and it is the wake-up
    /// body's roomId - the same field in the start payload does not. A transcription tell, counted
    /// rather than described so it cannot be tidied away unnoticed.
    /// </summary>
    [Fact]
    public void ExactlyOneTemplateFieldHasASpaceAfterItsColon()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.Equal(1, SessionRequestsSource.SpacedColons(core));

        // And it is that one: the wake-up body has it, the start payload does not.
        Assert.Contains("\"roomId\": 0,", SessionRequests.WakeupEnvelope(Blob(1), Blob(2), "s"), StringComparison.Ordinal);
        Assert.Contains(
            "\\\"roomId\\\":0,", SessionRequests.StartPayload(1, "s", Blob(1), Blob(2)), StringComparison.Ordinal);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheBodiesRulesAreStillTheQtCores()
    {
        string? core = Core();
        if (core is null)
            return;

        Assert.True(SessionRequestsSource.ItStillSaysWhyTheyAreTemplates(core), "templates, and why");
        Assert.True(SessionRequestsSource.TheCreateBodyStillSaysMeThreeTimes(core), "three times me");
        Assert.True(SessionRequestsSource.TheAccountIdIsStillTwoTypes(core), "bare here, quoted there");
        Assert.True(SessionRequestsSource.TheClientStillNamesItselfThatWay(core), "Windows, and ten point oh");
        Assert.True(SessionRequestsSource.CustomData1IsStillLengthGated(core), "thirty-two before decoding");
        Assert.True(SessionRequestsSource.TheBlobsAreStillCryptoRandom(core), "crypto random, twenty-five wide");
    }
}
