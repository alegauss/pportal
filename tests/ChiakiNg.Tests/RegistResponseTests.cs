using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP29: the registration response, including the three things about it that differ per family and
/// the two that differ from the session exchange.
/// </summary>
public class RegistResponseTests
{
    private static RegistResponseFields Parse(ChiakiTarget target, params (string Key, string Value)[] headers)
        => RegistResponse.Parse(target, [.. headers.Select(h => new HttpHeader(h.Key, h.Value))]);

    /// <summary>A complete PS5 response yields every field.</summary>
    [Fact]
    public void ACompletePs5ResponseIsRead()
    {
        RegistResponseFields host = Parse(ChiakiTarget.Ps5_1,
            ("PS5-Nickname", "Living Room"),
            ("PS5-RegistKey", "33653931313037630000000000000000"),
            ("RP-Key", "5749d7878fcefd233f72fef07e30e75a"),
            ("PS5-Mac", "904748 82fc29".Replace(" ", "")),
            ("RP-KeyType", "2"));

        Assert.True(host.Complete);
        Assert.Equal("Living Room", host.Nickname);
        Assert.Equal(2u, host.RpKeyType);
        Assert.Equal(RegistResponse.RpKeySize, host.RpKey!.Length);
        Assert.Equal(RegistResponse.MacSize, host.ServerMac!.Length);
    }

    /// <summary>
    /// The family decides three header names, and it comes from the request rather than the reply.
    ///
    /// A PS5's answer read as a PS4 finds none of the three - no name, no key, no MAC - and the
    /// registration fails with a response that was perfectly good.
    /// </summary>
    [Fact]
    public void AResponseReadAsTheWrongFamilyFindsNothing()
    {
        (string, string)[] ps5 =
        [
            ("PS5-Nickname", "Living Room"),
            ("PS5-RegistKey", "33653931313037630000000000000000"),
            ("PS5-Mac", "90474882fc29"),
            ("RP-Key", "5749d7878fcefd233f72fef07e30e75a"),
        ];

        Assert.True(Parse(ChiakiTarget.Ps5_1, ps5).Complete);

        RegistResponseFields asPs4 = Parse(ChiakiTarget.Ps4_10, ps5);
        Assert.False(asPs4.Complete);
        Assert.Null(asPs4.Nickname);
        Assert.Null(asPs4.RegistKey);
        Assert.Null(asPs4.ServerMac);

        // ...and RP-Key is family-independent, so it is still there.
        Assert.NotNull(asPs4.RpKey);
    }

    /// <summary>
    /// RP-KeyType lets the text choose its base, which RP-Application-Reason does not.
    ///
    /// The octal case is the one that surprises: "010" is eight here and would be ten to any port
    /// that reached for a plain decimal parse.
    /// </summary>
    [Theory]
    [InlineData("2", 2u)]
    [InlineData("10", 10u)]
    [InlineData("0x10", 16u)]
    [InlineData("010", 8u)]
    [InlineData("0", 0u)]
    [InlineData("", 0u)]
    [InlineData("rubbish", 0u)]
    public void TheKeyTypeBaseComesFromTheText(string value, uint expected)
        => Assert.Equal(expected, RegistResponse.ParseAutoBase(value));

    /// <summary>
    /// And it really does differ from the session exchange's reason code, which is always hex.
    ///
    /// The same three characters mean different numbers in the two exchanges. Sharing one helper
    /// between them would be wrong about one.
    /// </summary>
    [Fact]
    public void TheTwoExchangesParseTheSameTextDifferently()
    {
        Assert.Equal(8u, RegistResponse.ParseAutoBase("010"));
        Assert.Equal(0x10u, SessionResponse.ParseReason("010"));
    }

    /// <summary>
    /// RP-Key and the MAC must fill their buffers; the registration key need not.
    ///
    /// Refusing a short registration key would refuse consoles the C accepts today, so the
    /// asymmetry is reproduced rather than tidied.
    /// </summary>
    [Fact]
    public void OnlyTwoOfTheThreeHexFieldsRequireTheirFullLength()
    {
        // Eight bytes where sixteen fit: the registration key takes it, RP-Key does not.
        RegistResponseFields host = Parse(ChiakiTarget.Ps4_10,
            ("PS4-RegistKey", "3365393131303763"),
            ("RP-Key", "3365393131303763"),
            ("PS4-Mac", "904748"));

        Assert.NotNull(host.RegistKey);
        Assert.Equal(RegistResponse.RegistKeySize, host.RegistKey!.Length);

        // ...and the tail is zeroed rather than left short.
        Assert.Equal(0, host.RegistKey[15]);

        Assert.Null(host.RpKey);
        Assert.Null(host.ServerMac);
        Assert.False(host.Complete);
    }

    /// <summary>A value that is not hex at all is refused rather than half-read.</summary>
    [Theory]
    [InlineData("zzzz")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef00")]
    public void RubbishHexIsRefused(string value)
        => Assert.Null(RegistResponse.ParseHex(value, RegistResponse.RpKeySize, exact: true));

    /// <summary>The access point fields are family-independent and optional.</summary>
    [Fact]
    public void TheAccessPointFieldsAreOptional()
    {
        RegistResponseFields host = Parse(ChiakiTarget.Ps5_1, ("AP-Ssid", "home"), ("AP-Name", "router"));

        Assert.Equal("home", host.ApSsid);
        Assert.Equal("router", host.ApName);
        Assert.Null(host.ApKey);
        Assert.False(host.Complete);
    }

    /// <summary>THE DRIFT CHECK. The family names, the base and the asymmetry are still the C's.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\regist.c");
        Assert.True(impl is not null, "no lib\\src\\regist.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(RegistResponse.TheHeadersAreStillPerFamily(core),
            "the three per-family header names are no longer chosen by target");
        Assert.True(RegistResponse.TheKeyTypeStillAutoDetectsItsBase(core),
            "RP-KeyType no longer uses base 0, so its octal case has changed meaning");
        Assert.True(RegistResponse.TheRegistKeyStillHasNoLengthCheck(core),
            "RP-Key and the MAC no longer check their exact lengths, so the asymmetry has moved");
    }
}
