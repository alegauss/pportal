using System.Globalization;
using System.Net.Http;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP7: the token exchange, asserted without an account - which is the whole reason it is a
/// separate piece from the browser.
/// </summary>
public class PsnTokenExchangeTests
{
    /// <summary>
    /// The body goes out unencoded: the scope keeps its spaces and colons in a body declared
    /// form-encoded. A port that used FormUrlEncodedContent would be sending different bytes to
    /// find out whether Sony still accepts them.
    /// </summary>
    [Fact]
    public async Task TheTokenBodyIsNotFormEncoded()
    {
        using HttpRequestMessage request =
            PsnTokenExchange.TokenRequest(PsnAuth.TokenRequestBody("the-code"));

        string body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(PsnAuth.TokenUrl, request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);

        Assert.Contains("code=the-code&", body, StringComparison.Ordinal);
        Assert.Contains("psn:clientapp referenceDataService:countryConfig.read", body, StringComparison.Ordinal);
        Assert.DoesNotContain("%3A", body, StringComparison.Ordinal);
        Assert.DoesNotContain("+reference", body, StringComparison.Ordinal);
    }

    /// <summary>Both grants carry the same header, which is the client id and secret base64'd.</summary>
    [Fact]
    public void BothGrantsCarryTheBasicHeader()
    {
        using HttpRequestMessage code =
            PsnTokenExchange.TokenRequest(PsnAuth.TokenRequestBody("c"));
        using HttpRequestMessage refresh =
            PsnTokenExchange.TokenRequest(PsnAuth.RefreshRequestBody("r"));

        string expected = "Basic " + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{PsnAuth.ClientId}:{PsnAuth.ClientSecret}"));

        Assert.Equal(expected, code.Headers.GetValues("Authorization").Single());
        Assert.Equal(expected, refresh.Headers.GetValues("Authorization").Single());
    }

    /// <summary>
    /// The access token is spent in the PATH, not as a bearer - and the Basic header goes with it.
    /// A port that reached for Authorization: Bearer would be refused by an endpoint that never
    /// asked for one.
    /// </summary>
    [Fact]
    public void TheAccountLookupPutsTheTokenInTheUrl()
    {
        using HttpRequestMessage request = PsnTokenExchange.AccountRequest("abc123");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(PsnAuth.TokenUrl + "/abc123", request.RequestUri!.ToString());
        Assert.StartsWith("Basic ", request.Headers.GetValues("Authorization").Single(),
            StringComparison.Ordinal);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
    }

    /// <summary>The response, read against a clock that is passed in rather than read.</summary>
    [Fact]
    public void TheExpiryIsMeasuredFromWhenTheReplyArrived()
    {
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local);

        PsnTokens tokens = PsnTokenExchange.ReadTokens(
            """{"access_token":"at","refresh_token":"rt","expires_in":3600}""", now);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        Assert.Equal(now.AddHours(1), tokens.Expiry);
    }

    /// <summary>An error body is JSON too, so a missing field is empty rather than a throw.</summary>
    [Fact]
    public void AResponseWithoutTokensReadsAsEmptyRatherThanThrowing()
    {
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local);

        PsnTokens tokens = PsnTokenExchange.ReadTokens("""{"error":"invalid_grant"}""", now);

        Assert.Equal("", tokens.AccessToken);
        Assert.Equal("", tokens.RefreshToken);
        Assert.Equal(now, tokens.Expiry);
    }

    /// <summary>
    /// The account id: a decimal user_id read as a 64-bit number and written LOW BYTE FIRST. The
    /// same eight bytes PP14's dialog decodes, which is why a byte order mistake here shows up
    /// three screens away as a console that will not pair.
    /// </summary>
    [Fact]
    public void TheAccountIdIsEightLittleEndianBytes()
    {
        string encoded = PsnTokenExchange.AccountIdFrom("1");

        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, Convert.FromBase64String(encoded));

        // And a value that fills more than one byte, so the order is actually visible.
        Assert.Equal(
            new byte[] { 0x02, 0x01, 0, 0, 0, 0, 0, 0 },
            Convert.FromBase64String(PsnTokenExchange.AccountIdFrom("258")));
    }

    /// <summary>
    /// And what comes out is exactly what the registration dialog accepts - eight bytes through
    /// PP14's own decoder. The two ends of the login meet here and nowhere else.
    /// </summary>
    [Fact]
    public void TheAccountIdIsWhatTheRegistrationDialogAccepts()
    {
        string id = PsnTokenExchange.ReadAccountId("""{"user_id":"6890123456789012345"}""");

        Assert.Equal(8, LenientBase64.Decode(id).Length);

        RegistrationRequest request = Registration.Prepare(
            "10.0.0.5", ConsoleTarget.Ps5, id, "12345678", "",
            out RegistrationRefusal refusal)!;

        Assert.Equal(RegistrationRefusal.None, refusal);
        Assert.Equal(LenientBase64.Decode(id), request.AccountId);
    }

    /// <summary>A user_id that is not a number is a failure with a name, not a silent zero.</summary>
    [Fact]
    public void AUserIdThatIsNotANumberIsRefused()
        => Assert.Throws<FormatException>(() => PsnTokenExchange.AccountIdFrom("not-a-number"));

    /// <summary>
    /// Qt's format letters are not .NET's, which is the trap this whole pair of methods exists to
    /// avoid. Qt's `t` is the time zone and .NET's is the first letter of the AM/PM designator, so
    /// the same format string writes an afternoon as "... 14:30:00 P".
    ///
    /// It round-trips through itself, which is exactly why it would survive a port's own tests -
    /// and it cannot read what the Qt client wrote, which is the half that matters, because both
    /// clients read one settings file.
    /// </summary>
    [Fact]
    public void TheQtFormatStringMeansSomethingElseInDotNet()
    {
        var when = new DateTime(2026, 8, 19, 14, 30, 0, DateTimeKind.Local);

        string naive = when.ToString(PsnTokenExchange.ExpiryFormat, CultureInfo.InvariantCulture);

        Assert.EndsWith(" P", naive, StringComparison.Ordinal);

        // It reads its own output back, so nothing local complains.
        Assert.True(DateTime.TryParseExact(
            naive, PsnTokenExchange.ExpiryFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _));

        // And it cannot read the Qt client's, which is the failure that matters.
        Assert.False(DateTime.TryParseExact(
            "2026-08-19 14:30:00 BRT", PsnTokenExchange.ExpiryFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _),
            "a naive port cannot read the expiry the other client stored");

        Assert.Equal(when, PsnTokenExchange.ReadExpiry("2026-08-19 14:30:00 BRT"));
    }

    /// <summary>The port's own pair round-trips, and the timestamp half is the agreed one.</summary>
    [Fact]
    public void TheExpiryRoundTripsAndItsTimestampIsTheAgreedPart()
    {
        var when = new DateTime(2026, 8, 19, 14, 30, 0, DateTimeKind.Local);

        string written = PsnTokenExchange.WriteExpiry(when);

        Assert.StartsWith("2026-08-19 14:30:00 ", written, StringComparison.Ordinal);
        Assert.Equal(when, PsnTokenExchange.ReadExpiry(written));
    }

    /// <summary>
    /// And a value the QT client wrote reads back, whatever its machine called the time zone -
    /// the trailing token is ignored rather than parsed, because both sides compare local clocks.
    /// </summary>
    [Theory]
    [InlineData("2026-08-19 14:30:00 BRT")]
    [InlineData("2026-08-19 14:30:00 GMT-03:00")]
    [InlineData("2026-08-19 14:30:00 E. South America Standard Time")]
    [InlineData("2026-08-19 14:30:00")]
    public void AnExpiryWrittenByTheQtClientReadsBack(string stored)
        => Assert.Equal(
            new DateTime(2026, 8, 19, 14, 30, 0),
            PsnTokenExchange.ReadExpiry(stored));

    /// <summary>Anything unreadable is null rather than a date nobody chose.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("whenever")]
    public void AnUnreadableExpiryIsNull(string? stored)
        => Assert.Null(PsnTokenExchange.ReadExpiry(stored));

    /// <summary>
    /// A token with under a minute left is treated as expired, so a session does not begin on one
    /// that dies during the handshake. Missing and unreadable take the same path, because they
    /// lead to the same refresh.
    /// </summary>
    [Fact]
    public void ATokenWithUnderAMinuteLeftIsAlreadyExpired()
    {
        var now = new DateTime(2026, 8, 19, 14, 0, 0);

        Assert.False(PsnTokenExchange.NeedsRefresh("2026-08-19 14:01:01 UTC+00:00", now));
        Assert.True(PsnTokenExchange.NeedsRefresh("2026-08-19 14:00:59 UTC+00:00", now));
        Assert.True(PsnTokenExchange.NeedsRefresh(null, now));
        Assert.True(PsnTokenExchange.NeedsRefresh("whenever", now));
    }

    /// <summary>And every shape above is still the Qt client's own.</summary>
    [Fact]
    public void TheExchangeIsStillTheQtClients()
    {
        string? account = PsnTokenSource.Locate(PsnTokenSource.AccountCpp);
        string? header = PsnTokenSource.Locate(PsnTokenSource.AccountHeader);
        string? settings = PsnTokenSource.Locate(PsnTokenSource.SettingsCpp);
        string? backend = PsnTokenSource.Locate(@"gui\src\qmlbackend.cpp");
        if (account is null || header is null || settings is null || backend is null)
            return;

        string accountCpp = File.ReadAllText(account);
        string headerText = File.ReadAllText(header);

        Assert.True(PsnTokenSource.TheAccessTokenIsAPathSegment(accountCpp),
            "the access token is still a path segment");
        Assert.True(PsnTokenSource.TheAccountIdIsEightLittleEndianBytes(accountCpp, headerText),
            "the account id is still eight bytes taken low end first");
        Assert.True(PsnTokenSource.TheExpiryFormatIsStillQts(File.ReadAllText(settings)),
            "the expiry format is still the one .NET reads differently");
        Assert.True(PsnTokenSource.TheRefreshBufferIsAMinute(File.ReadAllText(backend)),
            "the refresh buffer is still a minute");
    }

    /// <summary>
    /// And the branch that is deliberately not ported is still the broken one. If it were ever
    /// fixed upstream, "Windows-only, so it cannot run" stops being the whole reason to skip it.
    /// </summary>
    [Fact]
    public void TheBigEndianBranchIsStillDeadAndStillWrong()
    {
        string? header = PsnTokenSource.Locate(PsnTokenSource.AccountHeader);
        if (header is null)
            return;

        Assert.True(PsnTokenSource.TheBigEndianBranchIsStillBroken(File.ReadAllText(header)),
            "it still shifts by the whole width on every pass");
    }
}
