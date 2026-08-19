using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>What comes back from the token endpoint.</summary>
/// <param name="Expiry">
/// When the access token stops working, as a local wall-clock time - which is what the Qt client
/// stores and therefore what the port has to store.
/// </param>
public sealed record PsnTokens(string AccessToken, string RefreshToken, DateTime Expiry);

/// <summary>
/// PP7: the token exchange, which is the half of the login that needs no browser.
///
/// The code the browser hands back is worth nothing on its own. It is posted to Sony's token
/// endpoint for an access token and a refresh token, and then the access token is spent once more
/// to learn the account id the registration dialog asks for. Three requests, no user interface, and
/// every one of them a shape a port can get subtly wrong and only find out about against a live
/// account.
///
/// Four of those shapes are stated here because they are surprising:
///
///   the form body is NOT form-encoded. The scope goes in with its spaces and colons intact and
///   the code goes in raw, in a body declared application/x-www-form-urlencoded. Sony accepts it;
///   a port that encoded it properly would be sending different bytes to find out whether Sony
///   still does;
///
///   the access token is spent in the PATH. The account lookup is a GET of the token URL with the
///   token appended as a path segment, not a bearer header - and the Basic header goes along too;
///
///   the account id is the decimal user_id read as a 64-bit number and written out as EIGHT
///   LITTLE-ENDIAN BYTES, base64'd. That is the same eight bytes PP14's registration dialog
///   decodes, which is why a wrong byte order here surfaces three screens away as a console that
///   refuses to pair;
///
///   and the expiry is stored in QT's date format, not .NET's - see <see cref="ExpiryFormat"/>.
///   The two libraries read the same letters differently, and both clients share one settings file.
/// </summary>
public static partial class PsnTokenExchange
{
    /// <summary>The account id's size, and the only size the registration dialog accepts.</summary>
    public const int AccountIdSize = 8;

    /// <summary>
    /// The format the Qt client writes the expiry with: `yyyy-MM-dd HH:mm:ss t`.
    ///
    /// Copying it into .NET is the trap. Qt's `t` is the time zone; .NET's `t` is the first letter
    /// of the AM/PM designator, so the same format string writes "... 14:30:00 P". It reads that
    /// back perfectly well, which is why the mistake survives a port's own tests - and it cannot
    /// read what the Qt client wrote, which is the half that matters. The port writes the timestamp
    /// with an explicit zone token and reads only the timestamp, which is what the comparison uses.
    /// </summary>
    public const string ExpiryFormat = "yyyy-MM-dd HH:mm:ss t";

    /// <summary>The part of the expiry both clients agree on, character for character.</summary>
    public const string ExpiryTimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// The buffer the Qt client refreshes within. A token that expires in under a minute is
    /// treated as expired, so a session does not start on one that dies during the handshake.
    /// </summary>
    public static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(60);

    /// <summary>The POST that exchanges a code, or renews a refresh token.</summary>
    public static HttpRequestMessage TokenRequest(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var request = new HttpRequestMessage(HttpMethod.Post, PsnAuth.TokenUrl)
        {
            // Not FormUrlEncodedContent: that would encode the scope's spaces and colons, and the
            // body the Qt client sends has them raw.
            Content = new StringContent(body),
        };

        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        request.Headers.TryAddWithoutValidation("Authorization", PsnAuth.BasicAuthHeader());

        return request;
    }

    /// <summary>
    /// The GET that turns an access token into an account. The token is a path segment - the URL
    /// IS the credential - and the Basic header is sent as well.
    /// </summary>
    public static HttpRequestMessage AccountRequest(string accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);

        var request = new HttpRequestMessage(HttpMethod.Get, AccountUrl(accessToken));
        request.Headers.TryAddWithoutValidation("Authorization", PsnAuth.BasicAuthHeader());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>The account lookup's URL.</summary>
    public static string AccountUrl(string accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        return PsnAuth.TokenUrl + "/" + accessToken;
    }

    /// <summary>
    /// Reads the token response. A field that is not there reads as empty rather than throwing,
    /// because the endpoint's error bodies are JSON too and the caller decides what to do about a
    /// token it did not get.
    /// </summary>
    public static PsnTokens ReadTokens(string json, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        return new PsnTokens(
            Text(root, "access_token"),
            Text(root, "refresh_token"),
            now.AddSeconds(Number(root, "expires_in")));
    }

    /// <summary>
    /// The account id, from the decimal user_id the account endpoint returns.
    ///
    /// Little-endian, eight bytes, base64. The Qt client has a big-endian branch beside it which
    /// cannot run here and would not work if it did - it shifts by the whole width on every
    /// iteration, so every byte would come out the same. Not reproduced: this port is Windows-only,
    /// which is a stated non-goal away from ever needing it.
    /// </summary>
    public static string AccountIdFrom(string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);

        if (!long.TryParse(userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            throw new FormatException($"user_id is not a number: '{userId}'.");

        var bytes = new byte[AccountIdSize];
        for (int i = 0; i < AccountIdSize; i++)
        {
            bytes[i] = (byte)(id & 0xFF);
            id >>= 8;
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>The account id out of a whole account response.</summary>
    public static string ReadAccountId(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);
        return AccountIdFrom(Text(document.RootElement, "user_id"));
    }

    /// <summary>
    /// Writes an expiry the Qt client can read back. The timestamp is the agreed part; the trailing
    /// token is the zone, which is written because the format has a field for it and read by
    /// nobody - the comparison is against a local clock on both sides.
    /// </summary>
    public static string WriteExpiry(DateTime expiry)
        => expiry.ToString(ExpiryTimestampFormat, CultureInfo.InvariantCulture)
            + " " + ZoneToken(expiry);

    /// <summary>
    /// Reads one back, or null. The trailing zone token is ignored rather than parsed, because it
    /// is whatever the OS called the zone on the machine that wrote it - and the value is a local
    /// wall clock either way.
    /// </summary>
    public static DateTime? ReadExpiry(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        Match m = TimestampRegex().Match(stored);
        if (!m.Success)
            return null;

        return DateTime.TryParseExact(
            m.Value, ExpiryTimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime value)
            ? value
            : null;
    }

    /// <summary>
    /// Whether a stored token still has enough life left to start a session with. Missing,
    /// unreadable and about-to-die all answer the same way, because they lead to the same refresh.
    /// </summary>
    public static bool NeedsRefresh(string? storedExpiry, DateTime now)
    {
        DateTime? expiry = ReadExpiry(storedExpiry);
        return expiry is null || expiry.Value - now < RefreshBuffer;
    }

    private static string ZoneToken(DateTime at)
    {
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(at);
        return offset == TimeSpan.Zero
            ? "UTC"
            : string.Create(CultureInfo.InvariantCulture,
                $"UTC{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}");
    }

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
            ? number
            : 0;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}")]
    private static partial Regex TimestampRegex();
}

/// <summary>
/// PP7: the token exchange's shapes, held against the Qt client that shares its settings file.
/// </summary>
public static partial class PsnTokenSource
{
    /// <summary>Where the token exchange lives.</summary>
    public const string TokenCpp = @"gui\src\psntoken.cpp";

    /// <summary>Where the account lookup lives.</summary>
    public const string AccountCpp = @"gui\src\psnaccountid.cpp";

    /// <summary>Where the constants and the byte conversion live.</summary>
    public const string AccountHeader = @"gui\include\psnaccountid.h";

    /// <summary>Where the expiry format is declared.</summary>
    public const string SettingsCpp = @"gui\src\settings.cpp";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether the access token is still spent as a path segment.</summary>
    public static bool TheAccessTokenIsAPathSegment(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            @"QString accountInfoUrl = QString(""%1/%2"").arg(PSNAuth::TOKEN_URL).arg(access_token);",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the account id is still eight bytes taken low end first.</summary>
    public static bool TheAccountIdIsEightLittleEndianBytes(string cpp, string header)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(header);

        return cpp.Contains("to_bytes_little_endian(std::stoll(user_id.toStdString()), 8)",
                StringComparison.Ordinal)
            && LittleEndianLoopRegex().IsMatch(header);
    }

    /// <summary>Whether the expiry format is still the one .NET reads differently.</summary>
    public static bool TheExpiryFormatIsStillQts(string settingsCpp)
    {
        ArgumentNullException.ThrowIfNull(settingsCpp);
        return settingsCpp.Contains(@"time_format(""yyyy-MM-dd HH:mm:ss t"")", StringComparison.Ordinal);
    }

    /// <summary>Whether the refresh buffer is still a minute.</summary>
    public static bool TheRefreshBufferIsAMinute(string backendCpp)
    {
        ArgumentNullException.ThrowIfNull(backendCpp);
        return RefreshBufferRegex().IsMatch(backendCpp);
    }

    /// <summary>
    /// Whether the big-endian branch is still the broken one - shifting by the whole width on
    /// every pass, so the loop variable does nothing. Checked so that "not reproduced" stays a
    /// decision about dead code rather than a difference nobody re-read.
    /// </summary>
    public static bool TheBigEndianBranchIsStillBroken(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains("char result = (number >> (8 * num_bytes)) & 0xFF;",
            StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"char result = number & 0xFF;\s*\r?\n\s*byte_array\.append\(result\);\s*\r?\n\s*number >>= 8;")]
    private static partial Regex LittleEndianLoopRegex();

    [GeneratedRegex(
        @"// give 1 minute buffer\s*\r?\n\s*QDateTime now = QDateTime::currentDateTime\(\)\.addSecs\(60\);")]
    private static partial Regex RefreshBufferRegex();
}
