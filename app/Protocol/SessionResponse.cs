using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the console answered a session request with.</summary>
/// <param name="Success">Whether the session was granted. See <see cref="SessionResponse"/> for what that means.</param>
/// <param name="Nonce">RP-Nonce, which the crypto is seeded from. Null where the console sent none.</param>
/// <param name="RpVersion">RP-Version, the protocol version the console speaks.</param>
/// <param name="ErrorCode">RP-Application-Reason, read as hexadecimal. Zero where absent.</param>
public readonly record struct SessionResponseFields(
    bool Success, string? Nonce, string? RpVersion, uint ErrorCode);

/// <summary>
/// PP293: parse_session_response, the three headers a session turns on.
///
/// A session request is one HTTP exchange and its answer is three headers. Everything that follows
/// - whether there is a session at all, which protocol version to speak, what to tell the user when
/// there is not - comes out of this function, and all three of its subtleties are the kind that a
/// port normalises away without noticing.
///
/// All three headers are matched case-insensitively
/// ------------------------------------------------
/// PP296, and the one thing here the port does not reproduce. The C used strcmp for RP-Nonce and
/// RP-Application-Reason and strcasecmp for RP-Version - three lines apart, doing the same thing two
/// ways, with nothing saying why. HTTP field names are case-insensitive by specification, so a
/// console answering "rp-nonce" was entitled to and got a failed session with no nonce and no reason
/// code, because that header was strcmp too. PP293 reproduced the mixture on this port's standing
/// rule; PP296 closed it the other way instead, changing session.c in the same commit, so the two
/// sides still agree. <see cref="TheHeaderMatchingIsStillCaseInsensitive"/> holds it there.
///
/// The reason code is HEXADECIMAL
/// ------------------------------
/// strtoul with base 0x10. So "80108b09" is a number and not a parse failure, and a port reading it
/// as decimal would turn every reason above 9 into a different one silently. The reason is what a
/// user is shown when a session is refused, so a wrong one is a wrong sentence on screen.
///
/// Success is not the status code
/// ------------------------------
/// 200 AND a nonce. A 200 with no RP-Nonce is a failure, because the nonce is what the crypto is
/// seeded from and there is nothing to continue with - and a console can answer 200 without one.
/// </summary>
public static class SessionResponse
{
    /// <summary>RP-Nonce, which the crypto is seeded from.</summary>
    public const string NonceHeader = "RP-Nonce";

    /// <summary>RP-Version, the protocol version the console speaks.</summary>
    public const string VersionHeader = "RP-Version";

    /// <summary>RP-Application-Reason, whose value is hexadecimal.</summary>
    public const string ReasonHeader = "RP-Application-Reason";

    /// <summary>The status code that can carry a session, given a nonce with it.</summary>
    public const int Ok = 200;

    /// <summary>Reads the three headers out of a parsed response.</summary>
    public static SessionResponseFields Parse(int code, IReadOnlyList<HttpHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        string? nonce = null;
        string? rpVersion = null;
        uint errorCode = 0;

        foreach (HttpHeader header in headers)
        {
            // PP296: OrdinalIgnoreCase for all three, which is what an HTTP field name is, and what
            // session.c does since the same commit.
            if (string.Equals(header.Key, NonceHeader, StringComparison.OrdinalIgnoreCase))
                nonce = header.Value;
            else if (string.Equals(header.Key, VersionHeader, StringComparison.OrdinalIgnoreCase))
                rpVersion = header.Value;
            else if (string.Equals(header.Key, ReasonHeader, StringComparison.OrdinalIgnoreCase))
                errorCode = ParseReason(header.Value);
        }

        return new SessionResponseFields(code == Ok && nonce is not null, nonce, rpVersion, errorCode);
    }

    /// <summary>
    /// strtoul(value, NULL, 0x10), including what it does with rubbish.
    ///
    /// strtoul does not fail: it reads as many hex digits as it finds and answers zero where it
    /// finds none, so an empty or non-numeric reason is 0 rather than an error. Reproduced, because
    /// a port that threw here would turn a malformed header into a crashed session where the C
    /// shows the user reason zero.
    /// </summary>
    public static uint ParseReason(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        int at = 0;

        // strtoul skips leading whitespace and accepts an optional sign before the digits.
        while (at < value.Length && char.IsWhiteSpace(value[at]))
            at++;

        bool negate = false;
        if (at < value.Length && (value[at] == '+' || value[at] == '-'))
        {
            negate = value[at] == '-';
            at++;
        }

        // ...and with base 16 explicitly, it also accepts a 0x prefix.
        if (at + 1 < value.Length && value[at] == '0' && (value[at + 1] is 'x' or 'X'))
            at += 2;

        uint result = 0;
        bool any = false;
        while (at < value.Length && Uri.IsHexDigit(value[at]))
        {
            result = unchecked((result * 16) + (uint)Convert.ToInt32(value[at].ToString(), 16));
            at++;
            any = true;
        }

        if (!any)
            return 0;

        // strtoul negates in unsigned arithmetic, which wraps rather than failing.
        return negate ? unchecked(0u - result) : result;
    }

    /// <summary>
    /// PP296: whether session.c still matches all three headers without regard to case.
    ///
    /// The opposite question to the two below it, because this is the line PP296 CHANGED rather than
    /// reproduced. A merge from upstream is what puts the two strcmps back, and it would not look
    /// like a conflict - so what has to hold is the presence of three strcasecmps rather than the
    /// absence of anything.
    /// </summary>
    public static bool TheHeaderMatchingIsStillCaseInsensitive(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("strcasecmp(header->key, \"RP-Nonce\") == 0", StringComparison.Ordinal)
            && core.Contains("strcasecmp(header->key, \"RP-Version\") == 0", StringComparison.Ordinal)
            && core.Contains("strcasecmp(header->key, \"RP-Application-Reason\") == 0", StringComparison.Ordinal);
    }

    /// <summary>And whether the reason is still read as hexadecimal.</summary>
    public static bool TheReasonIsStillHex(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("strtoul(header->value, NULL, 0x10)", StringComparison.Ordinal);
    }

    /// <summary>And whether success is still a 200 with a nonce rather than a 200.</summary>
    public static bool SuccessStillNeedsTheNonce(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("response->success = response->nonce != NULL;", StringComparison.Ordinal);
    }
}
