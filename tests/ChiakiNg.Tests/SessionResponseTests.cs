using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP293: the session response's three headers, including the three things about them that are odd.
/// </summary>
public class SessionResponseTests
{
    private static SessionResponseFields Parse(int code, params (string Key, string Value)[] headers)
        => SessionResponse.Parse(code, [.. headers.Select(h => new HttpHeader(h.Key, h.Value))]);

    /// <summary>The ordinary answer: 200, a nonce, a version.</summary>
    [Fact]
    public void AGrantedSessionCarriesItsNonceAndVersion()
    {
        SessionResponseFields fields = Parse(200,
            ("RP-Nonce", "abc123"), ("RP-Version", "10.0"));

        Assert.True(fields.Success);
        Assert.Equal("abc123", fields.Nonce);
        Assert.Equal("10.0", fields.RpVersion);
        Assert.Equal(0u, fields.ErrorCode);
    }

    /// <summary>
    /// A 200 with no nonce is a failure. The status code is not the answer.
    /// </summary>
    [Fact]
    public void TwoHundredWithoutANonceIsAFailure()
    {
        Assert.False(Parse(200, ("RP-Version", "10.0")).Success);

        // ...and a nonce without a 200 is not a success either.
        Assert.False(Parse(403, ("RP-Nonce", "abc123")).Success);
    }

    /// <summary>
    /// PP296: all three headers are matched without regard to case, as HTTP field names are.
    ///
    /// Before it, RP-Version was strcasecmp and the other two were strcmp, so this exact response
    /// produced the version and nothing else: no nonce, no session, and no reason code to say why -
    /// a connection that did not work with nothing in the log naming a header.
    /// </summary>
    [Fact]
    public void EveryHeaderIsMatchedWithoutRegardToCase()
    {
        SessionResponseFields lowered = Parse(200,
            ("rp-nonce", "abc123"), ("rp-version", "10.0"), ("rp-application-reason", "1f"));

        Assert.Equal("10.0", lowered.RpVersion);
        Assert.Equal("abc123", lowered.Nonce);
        Assert.True(lowered.Success);
        Assert.Equal(0x1fu, lowered.ErrorCode);
    }

    /// <summary>And the spellings a console is likelier to send are the same answer.</summary>
    [Theory]
    [InlineData("RP-Nonce")]
    [InlineData("rp-nonce")]
    [InlineData("Rp-Nonce")]
    [InlineData("RP-NONCE")]
    public void TheNonceIsFoundHoweverItIsSpelled(string spelling)
        => Assert.Equal("abc123", Parse(200, (spelling, "abc123")).Nonce);

    /// <summary>
    /// The reason is hexadecimal, which is the difference between the right sentence on screen and
    /// a different one.
    /// </summary>
    [Theory]
    [InlineData("10", 16u)]
    [InlineData("1f", 31u)]
    [InlineData("80108b09", 0x80108b09u)]
    [InlineData("0x20", 32u)]
    [InlineData("FF", 255u)]
    public void TheReasonIsReadAsHex(string value, uint expected)
        => Assert.Equal(expected, SessionResponse.ParseReason(value));

    /// <summary>
    /// And strtoul does not fail, so rubbish is zero rather than an exception.
    ///
    /// A port that threw here would turn a malformed header into a crashed session, where the C
    /// shows the user reason zero and carries on.
    /// </summary>
    [Theory]
    [InlineData("", 0u)]
    [InlineData(null, 0u)]
    [InlineData("zzz", 0u)]
    [InlineData("  2a", 42u)]
    [InlineData("1fXYZ", 31u)]
    public void RubbishIsZeroRatherThanAFailure(string? value, uint expected)
        => Assert.Equal(expected, SessionResponse.ParseReason(value));

    /// <summary>A refusal carries its reason and no nonce.</summary>
    [Fact]
    public void ARefusalCarriesItsReason()
    {
        SessionResponseFields fields = Parse(403, ("RP-Application-Reason", "80108b09"));

        Assert.False(fields.Success);
        Assert.Null(fields.Nonce);
        Assert.Equal(0x80108b09u, fields.ErrorCode);
    }

    /// <summary>THE DRIFT CHECK. session.c still matches, parses and decides these three ways.</summary>
    [Fact]
    public void TheCStillParsesItThisWay()
    {
        string? file = SanitizerSource.LocateRelative(SessionCoreSource.RelativePath);
        Assert.True(file is not null, "no lib\\src\\session.c - this file is describing nothing");

        string core = File.ReadAllText(file);

        Assert.True(SessionResponse.TheHeaderMatchingIsStillCaseInsensitive(core),
            "PP296's strcasecmps are gone from session.c, so the C matches rp-nonce exactly again "
                + "and only the managed side finds it - the fix was made in both");
        Assert.True(SessionResponse.TheReasonIsStillHex(core),
            "RP-Application-Reason is no longer read with base 0x10");
        Assert.True(SessionResponse.SuccessStillNeedsTheNonce(core),
            "success no longer requires the nonce, so a 200 alone may now be a session");
    }
}
