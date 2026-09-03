using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP7: the PSN login, everywhere except inside the browser.
///
/// The Qt client runs the OAuth flow inside a QtWebEngine view - a whole bundled Chromium - and
/// WebView2 replaces the view (<see cref="PsnBrowser"/>). What it does NOT replace is everything
/// around it: the authorize URL,
/// the redirect that has to be recognised, the code pulled out of it, and the token request made
/// with it. That is all string work, it is all in this file, and it is where a port breaks
/// silently: a scope spelled differently or a redirect matched slightly wrong is a login that never
/// completes, on a page the user cannot read an error out of.
///
/// So every constant here is held against gui/include/psnaccountid.h by the selftest. Two clients
/// logging in differently is not a thing anyone would report as a port defect.
/// </summary>
public static class PsnAuth
{
    /// <summary>The application this client identifies itself as.</summary>
    public const string ClientId = "ba495a24-818c-472b-b12d-ff231c1b5745";

    /// <summary>
    /// Paired with the id in the Basic header. It is not a secret in any useful sense - it ships
    /// in every copy of the client - and it is here because the endpoint requires it, not because
    /// anything is being protected.
    /// </summary>
    public const string ClientSecret = "mvaiZkRsAsI1IBkY";

    /// <summary>The page the browser lands on when the login is done, carrying the code.</summary>
    public const string RedirectPage = "https://remoteplay.dl.playstation.net/remoteplay/redirect";

    public const string TokenUrl = "https://auth.api.sonyentertainmentnetwork.com/2.0/oauth/token";

    /// <summary>
    /// The four scopes the session needs. One string, spelled exactly, spaces included - it goes
    /// into a URL query and into a form body, and it is the field most likely to be "tidied".
    /// </summary>
    public const string Scope =
        "psn:clientapp referenceDataService:countryConfig.read "
        + "pushNotification:webSocket.desktop.connect sessionManager:remotePlaySession.system.update";

    /// <summary>The authorize URL, before the device id is appended.</summary>
    public const string LoginUrlPrefix =
        "https://auth.api.sonyentertainmentnetwork.com/2.0/oauth/authorize?service_entity=urn:service-entity:psn"
        + "&response_type=code&client_id=" + ClientId
        + "&redirect_uri=" + RedirectPage
        + "&scope=" + Scope
        + "&request_locale=en_US&ui=pr&service_logo=ps&layout_type=popup&smcid=remoteplay"
        + "&prompt=always&PlatformPrivacyWs1=minimal&";

    /// <summary>CHIAKI_DUID_STR_SIZE, read from the shim rather than assumed.</summary>
    public static int DuidSize => DuidStrSize();

    /// <summary>
    /// PP33: DUID_PREFIX, the fixed half of every client device id.
    ///
    /// Sixteen characters and not a version number anybody here can read. It is what the relay
    /// expects in front of the random half, so it is copied exactly rather than derived.
    /// </summary>
    public const string DuidPrefix = "0000000700410080";

    /// <summary>How many random bytes follow it, written as lowercase hex.</summary>
    public const int DuidRandomBytes = 16;

    /// <summary>
    /// The whole id's length: the prefix plus two hex characters a byte. CHIAKI_DUID_STR_SIZE is
    /// this plus one, and the one is the terminator a C string needs and this does not.
    /// </summary>
    public const int DuidLength = 16 + (DuidRandomBytes * 2);

    /// <summary>
    /// PP33: a client device id, generated here rather than by holepunch.c.
    ///
    /// It identifies this installation to the relay, so the SHAPE is copied exactly and not
    /// invented: a Guid of the right length is not one the relay recognises. The shape is all there
    /// is - a fixed sixteen-character prefix and sixteen random bytes in lowercase hex - which is
    /// why this is the one of the ten holepunch wrappers that can leave without an oracle. There is
    /// nothing to compare but the format, and <see cref="NativeDeviceUid"/> is kept so a test can.
    ///
    /// WHY IT MATTERS THAT THIS ONE MOVED. PP653 asked the linker what still holds holepunch.c in
    /// the build and got ten wrappers back, all in this port's own shim - and this was the only one
    /// of the ten reached by anything the host actually runs. The other nine are PP481's oracle,
    /// exercised by tests that need a console. So PP33's remaining consumer is now entirely a
    /// testing seam, which is a different kind of blocker from a feature.
    ///
    /// Cryptographic randomness, because the C uses chiaki_random_bytes_crypt and an id that
    /// collides is an installation impersonating another one.
    /// </summary>
    public static string GenerateDeviceUid()
    {
        var random = new byte[DuidRandomBytes];
        System.Security.Cryptography.RandomNumberGenerator.Fill(random);

        return DuidPrefix + Convert.ToHexStringLower(random);
    }

    /// <summary>
    /// The same id from holepunch.c, kept as the oracle for the format.
    ///
    /// AND ITS out_size IS NOT THE LENGTH WRITTEN. The C does <c>*out_size += sprintf(...)</c> for
    /// the prefix and again per byte, so what comes back is the buffer size it was GIVEN plus the
    /// 48 characters it wrote - 97 on a 49-byte buffer. A comment here used to say the returned
    /// size counted the terminator, which is a different wrong number and a more plausible one.
    ///
    /// The terminator is what this reads, so the arithmetic never mattered; the fallback is what
    /// made it worth correcting. It used to be <c>end &lt; 0 ? size : end</c>, and on a buffer with
    /// no NUL in it that asks for 97 bytes of a 49-byte array. Unreachable - sprintf terminates -
    /// and wrong in the direction that throws rather than the one that truncates.
    /// </summary>
    public static string NativeDeviceUid()
    {
        var buf = new byte[DuidSize];
        int size = buf.Length;
        int err = GenerateClientDeviceUid(buf, ref size);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_holepunch_generate_client_device_uid failed: {(ChiakiError)err}.");

        int end = Array.IndexOf(buf, (byte)0);
        return Encoding.UTF8.GetString(buf, 0, end < 0 ? buf.Length : end);
    }

    /// <summary>
    /// What the C hands back in out_size, given the buffer size it was handed.
    ///
    /// Stated as arithmetic so a test can hold it: it is the input plus everything sprintf wrote,
    /// which is the prefix and two characters for each random byte.
    /// </summary>
    public static int NativeOutSizeFor(int bufferSize) => bufferSize + DuidLength;

    /// <summary>The URL the browser is pointed at, for one device id.</summary>
    public static string LoginUrl(string deviceUid)
    {
        ArgumentNullException.ThrowIfNull(deviceUid);
        return LoginUrlPrefix + "duid=" + deviceUid + "&";
    }

    /// <summary>
    /// Whether a URL the browser navigated to is the one carrying the code.
    ///
    /// A prefix test and not an equality one, because the redirect arrives with the query string
    /// attached - which is the whole point of it.
    /// </summary>
    public static bool IsRedirect(string? url)
        => url is not null && url.StartsWith(RedirectPage, StringComparison.Ordinal);

    /// <summary>
    /// The authorization code out of a redirect, or null when there is none - which is what a
    /// cancelled login looks like, and is not the same as a URL that is not the redirect at all.
    /// </summary>
    public static string? CodeFrom(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!IsRedirect(url))
            return null;

        int q = url.IndexOf('?', StringComparison.Ordinal);
        if (q < 0)
            return null;

        foreach (string pair in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && pair[..eq] == "code")
            {
                string value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// What <c>initPsnAuth</c> says about a URL that is not the redirect at all - the paste path's
    /// error, and the only one of the two a user can act on.
    /// </summary>
    public const string InvalidUrlMessage =
        "[E] Invalid URL: Please make sure you have copy and pasted the URL correctly.";

    /// <summary>
    /// What it says about the redirect with no code on it, which is a login backed out of rather
    /// than a URL typed wrong. Two messages and not one, because they are two different mistakes.
    /// </summary>
    public const string InvalidCodeMessage = "[E] Invalid code from redirect url.";

    /// <summary>
    /// The error one redirect URL produces, or null when it carries a code. The order is the
    /// backend's: not-the-redirect is decided before the code is looked for, so a pasted address
    /// bar full of nothing gets the message about pasting rather than the one about codes.
    /// </summary>
    public static string? RedirectError(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!IsRedirect(url))
            return InvalidUrlMessage;

        return CodeFrom(url) is null ? InvalidCodeMessage : null;
    }

    /// <summary>The form body that exchanges a code for a token.</summary>
    public static string TokenRequestBody(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return $"grant_type=authorization_code&code={code}&scope={Scope}&redirect_uri={RedirectPage}&";
    }

    /// <summary>The form body that renews one. Same shape, different grant.</summary>
    public static string RefreshRequestBody(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        return $"grant_type=refresh_token&refresh_token={refreshToken}&scope={Scope}&redirect_uri={RedirectPage}&";
    }

    /// <summary>The Basic header: the id and the secret, joined by a colon and base64'd.</summary>
    public static string BasicAuthHeader()
        => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_duid_str_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DuidStrSize();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_generate_client_device_uid",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GenerateClientDeviceUid(byte[] buf, ref int size);
}
