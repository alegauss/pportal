using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the connection request, its silently-defaulted MAC, and the decode this port does not copy.
/// </summary>
public class ConnectionRequestTests
{
    private static string Base64Of(int length)
        => Convert.ToBase64String([.. Enumerable.Range(0, length).Select(i => (byte)i)]);

    private static string Request(
        string? mac = "aa:bb:cc:dd:ee:ff",
        string? skey = null,
        string? hashedId = null,
        string natType = "2")
        => $$"""
        {
          "sid": 12345, "peerSid": 67890,
          "skey": "{{skey ?? Base64Of(16)}}",
          "natType": {{natType}},
          "defaultRouteMacAddr": "{{mac}}",
          "localHashedId": "{{hashedId ?? Base64Of(20)}}",
          "candidate": [{"type":"LOCAL","addr":"10.0.0.4","mappedAddr":"203.0.113.9","port":9295,"mappedPort":41234}]
        }
        """;

    private static ConnectionRequest? Read(string json)
    {
        using JsonDocument? document = JsonC.Parse(json);
        Assert.NotNull(document);
        return ConnectionRequestReader.Read(document.RootElement);
    }

    /// <summary>An ordinary request, with its candidate list.</summary>
    [Fact]
    public void AnOrdinaryRequestReads()
    {
        ConnectionRequest? request = Read(Request());

        Assert.NotNull(request);
        Assert.Equal(12345u, request.Value.Sid);
        Assert.Equal(67890u, request.Value.PeerSid);
        Assert.Equal(2, request.Value.NatType);
        Assert.Equal(16, request.Value.Skey.Length);
        Assert.Equal(20, request.Value.LocalHashedId.Length);
        Assert.Single(request.Value.Candidates);
        Assert.Equal(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, request.Value.DefaultRouteMac);
    }

    /// <summary>
    /// THE SILENT DEFAULT. A MAC of any length but seventeen is left as six zeros, with no error -
    /// so a malformed address does not fail the request, it becomes a route nobody has.
    ///
    /// A port that validated it would refuse offers the Qt client accepts; one that read the first
    /// six octets it found would invent an address.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("aa:bb:cc")]
    [InlineData("aa:bb:cc:dd:ee:ff:00")]
    [InlineData("not a mac at all")]
    public void AMacOfTheWrongLengthBecomesZerosRatherThanAnError(string mac)
    {
        ConnectionRequest? request = Read(Request(mac: mac));

        Assert.NotNull(request);
        Assert.Equal(new byte[6], request.Value.DefaultRouteMac);
    }

    /// <summary>And one of the right length with rubbish in it is zeros too, not garbage.</summary>
    [Fact]
    public void AMacOfTheRightLengthWithRubbishIsZeros()
    {
        ConnectionRequest? request = Read(Request(mac: "zz:zz:zz:zz:zz:zz"));

        Assert.NotNull(request);
        Assert.Equal(new byte[6], request.Value.DefaultRouteMac);
    }

    /// <summary>
    /// natType must be a NUMBER. The core tests json_type_int specifically, so a "2" invalidates
    /// the request rather than being coerced - which is the opposite of what json-c's accessors
    /// would do if it asked one of them (PP183).
    /// </summary>
    [Fact]
    public void ANatTypeSentAsAStringInvalidatesTheRequest()
        => Assert.Null(Read(Request(natType: "\"2\"")));

    /// <summary>
    /// The port sizes both base64 fields by the DESTINATION, so a key that decoded longer is
    /// refused rather than written past the end of sixteen bytes.
    ///
    /// This is where the port deliberately diverges: the core passes the input's length as skey's
    /// capacity, which is safe only while the key is exactly sixteen bytes. A latent overflow is
    /// not behaviour to reproduce.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(24)]
    public void AKeyOfTheWrongLengthIsRefusedRatherThanWritten(int length)
        => Assert.Null(Read(Request(skey: Base64Of(length))));

    /// <summary>And the hashed id the same, which is what the core already does for that one.</summary>
    [Fact]
    public void AHashedIdOfTheWrongLengthIsRefused()
        => Assert.Null(Read(Request(hashedId: Base64Of(19))));

    /// <summary>Something that is not base64 at all is refused rather than throwing.</summary>
    [Fact]
    public void AKeyThatIsNotBase64IsRefused()
        => Assert.Null(Read(Request(skey: "!!!!")));

    private const string OneCandidate =
        """{"type":"LOCAL","addr":"10.0.0.4","mappedAddr":"203.0.113.9","port":9295,"mappedPort":41234}""";

    /// <summary>
    /// ONE BAD CANDIDATE FAILS THE WHOLE REQUEST - PP195. The core's per-field guards jump to
    /// invalid_schema, which is the exit for the entire message rather than for the candidate being
    /// read, so there are no good ones left to salvage.
    ///
    /// This reader first dropped the bad one and kept the rest, which would have the port negotiate
    /// against a candidate list the Qt client never assembled.
    /// </summary>
    [Fact]
    public void AnUnreadableCandidateInvalidatesTheWholeRequest()
        => Assert.Null(Read(Request().Replace(
            OneCandidate, OneCandidate + ",{\"addr\":\"no type here\"}", StringComparison.Ordinal)));

    /// <summary>An empty candidate list is a request with nowhere to go, but still a request.</summary>
    [Fact]
    public void AnEmptyCandidateListStillReads()
    {
        ConnectionRequest? request = Read(
            Request().Replace(OneCandidate, "", StringComparison.Ordinal));

        Assert.NotNull(request);
        Assert.Empty(request.Value.Candidates);
    }

    /// <summary>
    /// natType is held to json_type_int and not merely to "a number", so a whole number written as
    /// a double is refused as well - the rule both ports are held to one file over (PP195).
    /// </summary>
    [Fact]
    public void ANatTypeSentAsADoubleInvalidatesTheRequestToo()
        => Assert.Null(Read(Request(natType: "2.0")));

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheRequestsRulesAreStillTheQtCores()
    {
        string? path = ConnectionRequestSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(ConnectionRequestSource.TheMacIsStillLengthGated(core), "seventeen or zeros");
        Assert.True(ConnectionRequestSource.NatTypeMustStillBeAnInt(core), "an int and not a string");
        Assert.True(
            ConnectionRequestSource.TheTwoDecodesStillDisagree(core),
            "the two base64 decodes still size their buffers differently");
    }
}
