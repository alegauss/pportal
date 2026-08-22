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
/// One header is matched case-insensitively and two are not
/// --------------------------------------------------------
/// The C uses strcmp for RP-Nonce and RP-Application-Reason and strcasecmp for RP-Version. That is
/// almost certainly not a decision anybody made, and it is the behaviour: a console answering
/// "rp-version" is understood, one answering "rp-nonce" is not and the session fails with no nonce.
/// Reproduced, because a port that compared all three the same way would differ from the Qt client
/// on exactly the consoles where it mattered - and PP231 is the precedent for reproducing rather
/// than repairing something this port did not introduce.
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
    /// <summary>RP-Nonce, matched exactly as the C matches it.</summary>
    public const string NonceHeader = "RP-Nonce";

    /// <summary>RP-Version, the one matched without regard to case.</summary>
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
            // Ordinal for two of them and OrdinalIgnoreCase for one. Not a tidy rule, and the C's.
            if (string.Equals(header.Key, NonceHeader, StringComparison.Ordinal))
                nonce = header.Value;
            else if (string.Equals(header.Key, VersionHeader, StringComparison.OrdinalIgnoreCase))
                rpVersion = header.Value;
            else if (string.Equals(header.Key, ReasonHeader, StringComparison.Ordinal))
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
    /// PP293: whether session.c still matches these three headers the way this reproduces.
    /// </summary>
    public static bool TheHeaderMatchingIsStillMixed(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("strcmp(header->key, \"RP-Nonce\") == 0", StringComparison.Ordinal)
            && core.Contains("strcasecmp(header->key, \"RP-Version\") == 0", StringComparison.Ordinal)
            && core.Contains("strcmp(header->key, \"RP-Application-Reason\") == 0", StringComparison.Ordinal);
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
