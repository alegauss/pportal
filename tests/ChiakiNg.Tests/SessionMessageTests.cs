using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the session message envelope, the gap in its flags, and the JSON PSN gets wrong.
/// </summary>
public class SessionMessageTests
{
    /// <summary>
    /// THE GAP. Offer is 1 and Result is 4; nothing is 2. A port that closed it would give its
    /// Accept the value the core calls Result, and a RESULT from the console would be handled as
    /// an ACCEPT - on the wrong branch, with every field present and plausible.
    /// </summary>
    [Fact]
    public void TheActionValuesSkipTheBitWorthTwo()
    {
        Assert.Equal(1, (int)SessionMessageAction.Offer);
        Assert.Equal(4, (int)SessionMessageAction.Result);
        Assert.Equal(8, (int)SessionMessageAction.Accept);
        Assert.Equal(16, (int)SessionMessageAction.Terminate);

        // Nothing at all is 2, which is the whole finding.
        Assert.DoesNotContain(
            SessionMessageEnvelope.Actions.Keys,
            a => (int)a == 2);

        // And a tidied enumeration would collide: its third entry would be this one's second.
        Assert.Equal((int)SessionMessageAction.Result, 1 << 2);
    }

    /// <summary>The four words, and anything else is Unknown.</summary>
    [Theory]
    [InlineData("OFFER", SessionMessageAction.Offer)]
    [InlineData("RESULT", SessionMessageAction.Result)]
    [InlineData("ACCEPT", SessionMessageAction.Accept)]
    [InlineData("TERMINATE", SessionMessageAction.Terminate)]
    [InlineData("offer", SessionMessageAction.Unknown)]
    [InlineData("", SessionMessageAction.Unknown)]
    [InlineData(null, SessionMessageAction.Unknown)]
    public void TheWordNamesTheAction(string? word, SessionMessageAction expected)
        => Assert.Equal(expected, SessionMessageEnvelope.ActionOf(word));

    /// <summary>Unknown is in no mask, so an action nobody knows wakes nobody.</summary>
    [Fact]
    public void UnknownIsInNoMask()
    {
        SessionMessageAction everything =
            SessionMessageAction.Offer | SessionMessageAction.Result
            | SessionMessageAction.Accept | SessionMessageAction.Terminate;

        Assert.False(SessionMessageEnvelope.Matches(SessionMessageAction.Unknown, everything));
        Assert.True(SessionMessageEnvelope.Matches(SessionMessageAction.Result, everything));

        // A caller waiting for a result is not woken by an accept, which the gap is what protects.
        Assert.False(
            SessionMessageEnvelope.Matches(SessionMessageAction.Accept, SessionMessageAction.Result));
    }

    /// <summary>
    /// The payload is a STRING that is not JSON: the JSON begins after a marker, and everything
    /// before it is discarded unread.
    /// </summary>
    [Fact]
    public void TheJsonBeginsAfterTheMarker()
    {
        const string payload = "v=1&sid=abc&body={\"action\":\"OFFER\"}";

        Assert.Equal("{\"action\":\"OFFER\"}", SessionMessageEnvelope.JsonInPayload(payload));
    }

    /// <summary>A payload with no marker is nothing, rather than being parsed as itself.</summary>
    [Fact]
    public void APayloadWithNoMarkerIsNothing()
        => Assert.Null(SessionMessageEnvelope.JsonInPayload("{\"action\":\"OFFER\"}"));

    /// <summary>
    /// THE ONE PSN GETS WRONG. When the console has no local peer address the field is sent with
    /// no value at all - a colon followed by a comma - and every conforming parser refuses it.
    ///
    /// Asserted by PARSING the repaired text, not by comparing strings: what matters is that the
    /// result is readable, and a repair that produced different-but-valid text would still be
    /// right.
    /// </summary>
    [Fact]
    public void TheMissingPeerAddressIsRepairedIntoParseableJson()
    {
        const string broken = """{"action":"OFFER","localPeerAddr":,"reqId":7}""";

        // The sender's own text is refused, which is why the repair exists.
        Assert.Null(JsonC.Parse(broken));

        string repaired = SessionMessageEnvelope.RepairPeerAddr(broken);

        using JsonDocument? document = JsonC.Parse(repaired);
        Assert.NotNull(document);

        Assert.Equal("OFFER", JsonC.String(JsonC.Get(document.RootElement, "action")));
        Assert.Equal(7, JsonC.Int(JsonC.Get(document.RootElement, "reqId")));
    }

    /// <summary>
    /// The repair is narrow. A message without the field, and one whose field already has an
    /// object, are returned untouched - a repair that ran over every empty value would be a
    /// lenient parser wearing a fix's name, which PP183 decided against.
    /// </summary>
    [Theory]
    [InlineData("""{"action":"OFFER"}""")]
    [InlineData("""{"localPeerAddr":{"a":1}}""")]
    [InlineData("""{"localPeerAddr":{}}""")]
    public void AMessageThatIsNotBrokenIsUntouched(string json)
        => Assert.Equal(json, SessionMessageEnvelope.RepairPeerAddr(json));

    /// <summary>And the two steps together, from the payload string a notification carries.</summary>
    [Fact]
    public void ThePayloadBecomesParseableJson()
    {
        const string payload = """v=1&body={"action":"ACCEPT","localPeerAddr":,"reqId":3}""";

        string? json = SessionMessageEnvelope.JsonFor(payload);
        Assert.NotNull(json);

        using JsonDocument? document = JsonC.Parse(json);
        Assert.NotNull(document);

        Assert.Equal(
            SessionMessageAction.Accept,
            SessionMessageEnvelope.ActionOf(JsonC.String(JsonC.Get(document.RootElement, "action"))));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheEnvelopesRulesAreStillTheQtCores()
    {
        string? path = SessionMessageSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(SessionMessageSource.TheActionsStillSkipABit(core), "the gap at two");
        Assert.True(SessionMessageSource.ThePayloadIsStillAtThatPointer(core), "four levels down");
        Assert.True(SessionMessageSource.TheJsonStillStartsAfterAMarker(core), "after body=");
        Assert.True(SessionMessageSource.ThePeerAddrIsStillRepaired(core), "the repaired field");
    }
}
