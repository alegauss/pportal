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
        Candidate? candidate = Read(
            """{"type":"STUN","addr":"10.0.0.4","addrMapped":"203.0.113.9","port":9295,"mappedPort":41234}""");

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
    [InlineData("""{"addr":"10.0.0.4"}""")]
    [InlineData("""{"type":42,"addr":"10.0.0.4"}""")]
    [InlineData("""{"type":null,"addr":"10.0.0.4"}""")]
    public void AMissingOrNonStringTypeInvalidatesTheCandidate(string json)
        => Assert.Null(Read(json));

    /// <summary>And a missing address does too, since a candidate without one addresses nothing.</summary>
    [Fact]
    public void AMissingAddressInvalidatesItAsWell()
        => Assert.Null(Read("""{"type":"LOCAL"}"""));

    /// <summary>
    /// A candidate whose type is present but unknown is still a candidate - which is the whole
    /// point of the fallback, and the case a symmetric port would drop.
    /// </summary>
    [Fact]
    public void AnUnknownButPresentTypeStillReads()
    {
        Candidate? candidate = Read("""{"type":"MOONBEAM","addr":"10.0.0.4"}""");

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
    }
}
