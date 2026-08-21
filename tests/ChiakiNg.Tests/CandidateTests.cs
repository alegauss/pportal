using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the candidate, where the reader and the writer do not agree.
/// </summary>
public class CandidateTests
{
    private static Candidate? Read(string json)
    {
        using JsonDocument? document = JsonC.Parse(json);
        Assert.NotNull(document);
        return CandidateReader.Read(document.RootElement);
    }

    /// <summary>A whole candidate, with one field replaced by whatever is being tested.</summary>
    private static string Candidate(string key, string value)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "\"LOCAL\"",
            ["addr"] = "\"10.0.0.4\"",
            ["mappedAddr"] = "\"203.0.113.9\"",
            ["port"] = "9295",
            ["mappedPort"] = "41234",
        };

        if (value.Length == 0)
            fields.Remove(key);
        else
            fields[key] = value;

        return "{" + string.Join(",", fields.Select(f => $"\"{f.Key}\":{f.Value}")) + "}";
    }

    /// <summary>The three the reader names.</summary>
    [Theory]
    [InlineData("LOCAL", CandidateType.Local)]
    [InlineData("STUN", CandidateType.Stun)]
    [InlineData("DERIVED", CandidateType.Derived)]
    public void TheThreeNamedWordsReadAsThemselves(string word, CandidateType expected)
        => Assert.Equal(expected, CandidateReader.TypeOf(word));

    /// <summary>
    /// THE ASYMMETRY. "STATIC" is written and never read: the reader falls through to Static for
    /// it, exactly as it does for a word PSN has not invented yet.
    ///
    /// A port with the obvious symmetric table would be wrong twice - refusing a future type the
    /// Qt client carries on with, and turning its own writer's output into a parse error for the
    /// one type the reader never names.
    /// </summary>
    [Theory]
    [InlineData("STATIC")]
    [InlineData("SOMETHING_PSN_ADDS_LATER")]
    [InlineData("local")]
    [InlineData("")]
    public void EverythingElseFallsThroughToStatic(string word)
        => Assert.Equal(CandidateType.Static, CandidateReader.TypeOf(word));

    /// <summary>And the writer does produce the word the reader never compares against.</summary>
    [Fact]
    public void TheWriterProducesOneMoreWordThanTheReaderNames()
    {
        Assert.Equal(4, CandidateReader.Written.Count);
        Assert.Equal(3, CandidateReader.Recognised.Count);

        Assert.Equal("STATIC", CandidateReader.Written[CandidateType.Static]);
        Assert.DoesNotContain("STATIC", CandidateReader.Recognised.Keys);
    }

    /// <summary>
    /// These are plain values and not a mask, unlike the two flag enums beside them. A port that
    /// made all three masks for consistency would give this one values nothing compares against.
    /// </summary>
    [Fact]
    public void TheTypesAreNotAMask()
    {
        Assert.Equal(0, (int)CandidateType.Static);
        Assert.Equal(1, (int)CandidateType.Local);
        Assert.Equal(2, (int)CandidateType.Stun);
        Assert.Equal(3, (int)CandidateType.Derived);

        // Three is two bits, which no flags enumeration of four members would produce.
        Assert.Equal(3, (int)CandidateType.Derived);
    }

    /// <summary>An ordinary candidate, with both the address it knows and the one a NAT gave it.</summary>
    [Fact]
    public void ACandidateCarriesBothAddresses()
    {
        Candidate? candidate = Read(Candidate("type", "\"STUN\""));

        Assert.NotNull(candidate);
        Assert.Equal(CandidateType.Stun, candidate.Value.Type);
        Assert.Equal("10.0.0.4", candidate.Value.Address);
        Assert.Equal("203.0.113.9", candidate.Value.MappedAddress);
        Assert.Equal(9295, candidate.Value.Port);
        Assert.Equal(41234, candidate.Value.MappedPort);
    }

    /// <summary>
    /// A missing or non-string type invalidates the whole candidate rather than defaulting - the
    /// fallback is for a type that is PRESENT and unrecognised, and the two are not the same.
    ///
    /// This is also where this parser differs from the notification envelope two files away, which
    /// reads a missing dataType as Unknown and carries on (PP190). Both are reproduced.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("null")]
    public void AMissingOrNonStringTypeInvalidatesTheCandidate(string value)
        => Assert.Null(Read(Candidate("type", value)));

    /// <summary>
    /// EVERY FIELD IS REQUIRED - PP195. Each of the five jumps to invalid_schema when it is
    /// missing, and there is no default anywhere in the reader.
    ///
    /// The version this file shipped with defaulted the last three, so it accepted candidates the
    /// Qt client refuses. That direction is the quiet one: a half-filled offer would connect here
    /// and fail there, and nothing in a round trip through this port's own writer would show it.
    /// </summary>
    [Theory]
    [InlineData("type")]
    [InlineData("addr")]
    [InlineData("mappedAddr")]
    [InlineData("port")]
    [InlineData("mappedPort")]
    public void EveryMissingFieldInvalidatesTheCandidate(string key)
        => Assert.Null(Read(Candidate(key, "")));

    /// <summary>
    /// THE KEY IS mappedAddr. addrMapped is what the C STRUCT MEMBER is called, and what this
    /// reader first looked for - a name that a fixture written beside it agrees with, so only
    /// reading the core's key back out finds the mistake.
    /// </summary>
    [Fact]
    public void TheMappedAddressIsKeyedForTheWireAndNotForTheStruct()
    {
        Assert.Equal("mappedAddr", CandidateReader.MappedAddressField);

        Candidate? candidate = Read(
            """{"type":"LOCAL","addr":"10.0.0.4","addrMapped":"203.0.113.9","port":1,"mappedPort":2}""");

        Assert.Null(candidate);
    }

    /// <summary>
    /// Both ports must be json_type_int, so a port sent as text or as a double invalidates the
    /// candidate rather than being coerced - the rule natType is held to (PP194), one file over.
    /// </summary>
    [Theory]
    [InlineData("port", "\"9295\"")]
    [InlineData("port", "9295.0")]
    [InlineData("mappedPort", "\"41234\"")]
    [InlineData("mappedPort", "41234.5")]
    public void APortThatIsNotAnIntInvalidatesTheCandidate(string key, string value)
        => Assert.Null(Read(Candidate(key, value)));

    /// <summary>
    /// A candidate whose type is present but unknown is still a candidate - which is the whole
    /// point of the fallback, and the case a symmetric port would drop.
    /// </summary>
    [Fact]
    public void AnUnknownButPresentTypeStillReads()
    {
        Candidate? candidate = Read(Candidate("type", "\"MOONBEAM\""));

        Assert.NotNull(candidate);
        Assert.Equal(CandidateType.Static, candidate.Value.Type);
        Assert.Equal("10.0.0.4", candidate.Value.Address);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheCandidatesRulesAreStillTheQtCores()
    {
        string? path = CandidateSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(CandidateSource.TheReaderStillNamesThree(core), "three read, one fallen through to");
        Assert.True(CandidateSource.TheWriterStillProducesFour(core), "four written");
        Assert.True(CandidateSource.TheTypesAreStillNotFlags(core), "values, not a mask");
        Assert.True(CandidateSource.AMissingTypeIsStillInvalid(core), "a missing type is invalid");
        Assert.True(CandidateSource.EveryFieldIsStillGuarded(core), "all five guarded, by name and type");
        Assert.True(
            CandidateSource.OneBadCandidateStillFailsTheMessage(core),
            "one bad candidate still fails the message");
    }

    /// <summary>
    /// And the five the port reads are the five the core reads - so a field PSN adds, or one that
    /// stops being required, turns this red rather than being silently unread.
    /// </summary>
    [Fact]
    public void TheFiveFieldsAreNamedOnceAndCheckedAgainstTheCore()
    {
        Assert.Equal(5, CandidateSource.Fields.Count);
        Assert.Equal(
            ["type", "addr", "mappedAddr", "port", "mappedPort"],
            CandidateSource.Fields.Select(f => f.Key));
    }
}
