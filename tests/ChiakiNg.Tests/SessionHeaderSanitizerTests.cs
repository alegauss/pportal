using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP325: the session's two secrets, redacted because of the header above them rather than because
/// of what they happen to look like.
///
/// Down the log path neither ever reached a file in the clear, and PP320 is the reason: a
/// chiaki_log_hexdump row is redacted WHOLE, so both were covered by being unreadable. PP323's tap
/// hands the same bytes over structured and that cover is gone, which is what these are about.
///
/// The real session request is the one in lib/src/session.c at session_request_fmt, and the strings
/// here are that format with plausible values put through it.
/// </summary>
public class SessionHeaderSanitizerTests
{
    private const string Request =
        "GET /sie/ps5/rp/sess/init HTTP/1.1\r\n"
        + "Host: 192.168.1.44:9295\r\n"
        + "User-Agent: remoteplay Windows\r\n"
        + "Connection: close\r\n"
        + "Content-Length: 0\r\n"
        + "RP-Registkey: 3e91107c9a4b1f2088c7d5e6a1b2c3d4\r\n"
        + "Rp-Version: 10.0\r\n"
        + "\r\n";

    private const string Response =
        "HTTP/1.1 200 OK\r\n"
        + "RP-Nonce: hK9+Lm/2Qw8vZa1sTb4xYg==\r\n"
        + "RP-Version: 10.0\r\n"
        + "\r\n";

    /// <summary>
    /// THE DEFECT, stated as an assertion so it cannot come back quietly.
    ///
    /// A nonce is base64 - '+', '/', '=' and mixed case - and not one of the ten rules held against
    /// gui/src/sessionlog.cpp matches a token of that shape. This is the log sanitiser doing its job
    /// correctly on input it was never written for, and it is why a second one exists.
    /// </summary>
    [Fact]
    public void TheLogSanitiserAloneLeavesTheNonceInTheClear()
    {
        Assert.Contains("hK9+Lm/2Qw8vZa1sTb4xYg==", SessionLogSanitizer.Sanitize(Response),
            StringComparison.Ordinal);
    }

    /// <summary>And the header rule takes it, which is the fix.</summary>
    [Fact]
    public void TheHeaderRuleTakesTheNonce()
    {
        string clean = SessionHeaderSanitizer.Sanitize(Response);

        Assert.DoesNotContain("hK9+Lm/2Qw8vZa1sTb4xYg==", clean, StringComparison.Ordinal);
        Assert.Contains("RP-Nonce: <redacted>", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// A SHORT registration key, which is the half that was only ever covered by luck.
    ///
    /// The long-hex rule takes any run of 16 or more hex characters, so a full 16-byte key came out
    /// redacted for a reason that had nothing to do with the field it was in. session.c stops at the
    /// first NUL in regist_key, so a shorter one is what reaches the wire - and eight hex characters
    /// go straight through every rule in sessionlog.cpp.
    /// </summary>
    [Fact]
    public void AShortRegistrationKeyIsCoveredWhereTheShapeRuleMissesIt()
    {
        const string head = "RP-Registkey: 3e91107c\r\n";

        Assert.Contains("3e91107c", SessionLogSanitizer.Sanitize(head), StringComparison.Ordinal);
        Assert.DoesNotContain("3e91107c", SessionHeaderSanitizer.Sanitize(head), StringComparison.Ordinal);
    }

    /// <summary>
    /// Case-insensitively, because the two ends of session.c do not agree with each other: the
    /// request is formatted "RP-Registkey" and the answer is read back with strcasecmp.
    /// </summary>
    [Theory]
    [InlineData("RP-Registkey")]
    [InlineData("RP-RegistKey")]
    [InlineData("rp-registkey")]
    [InlineData("RP-REGISTKEY")]
    public void TheSpellingOfTheHeaderDoesNotDecideWhetherItIsCovered(string header)
    {
        string clean = SessionHeaderSanitizer.Sanitize($"{header}: 3e91107c9a4b1f20\r\n");

        Assert.DoesNotContain("3e91107c9a4b1f20", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// The header NAME survives. A recording is read to find out what the exchange looked like, and
    /// a blanked line says nothing where "RP-Nonce: &lt;redacted&gt;" says a nonce was there - which
    /// is also what lets a replay line the two up.
    /// </summary>
    [Fact]
    public void TheFieldSurvivesSoTheShapeOfTheExchangeStillReads()
    {
        string clean = SessionHeaderSanitizer.Sanitize(Request);

        Assert.Contains("RP-Registkey: <redacted>", clean, StringComparison.Ordinal);
        Assert.Contains("GET /sie/ps5/rp/sess/init HTTP/1.1", clean, StringComparison.Ordinal);
        Assert.Contains("Rp-Version: 10.0", clean, StringComparison.Ordinal);
        Assert.Contains("User-Agent: remoteplay Windows", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every name on the list is covered, AND still reads as a header afterwards.
    ///
    /// Both halves, because the value going is the weaker half. The alternation was first written
    /// unwrapped - "(a|b\s*:\s*)" - so the trailing colon bound to the last name only and every
    /// other header matched its bare name, taking the colon into the value group. The secret went
    /// either way; what came out was "RP-Registkey&lt;redacted&gt;", redacted and no longer a header.
    /// Only the surviving field distinguishes the two, so this asserts on it for every name rather
    /// than for whichever one a hand-written case happens to use.
    /// </summary>
    [Fact]
    public void EveryHeaderOnTheListIsCoveredAndStillReadsAsAHeader()
    {
        Assert.NotEmpty(SessionHeaderSanitizer.Secret);

        foreach (string header in SessionHeaderSanitizer.Secret)
        {
            string clean = SessionHeaderSanitizer.Sanitize($"{header}: 5up3rs3cr3t\r\n");

            Assert.DoesNotContain("5up3rs3cr3t", clean, StringComparison.Ordinal);
            Assert.Contains($"{header}: {SessionHeaderSanitizer.Marker}", clean, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A header name inside somebody else's VALUE does not trigger the rule.
    ///
    /// The rule is anchored to the start of a line for this: a value mentioning RP-Nonce is text
    /// about the exchange, and redacting from there would eat the rest of a line that carries no
    /// secret at all.
    /// </summary>
    [Fact]
    public void ANameInsideAValueIsNotAHeader()
    {
        const string head = "RP-Application-Reason: RP-Nonce was rejected\r\n";

        Assert.Equal(head, SessionHeaderSanitizer.Sanitize(head));
    }

    /// <summary>
    /// And the recording composes the two, in that order.
    ///
    /// This is the one that matters: ExchangeRecording.Add is what a recorder calls, and a fix that
    /// lived only in SessionHeaderSanitizer without being wired in would leave the file exactly as
    /// exposed as it was.
    /// </summary>
    [Fact]
    public void TheRecordingStoresNeitherSecret()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Sent, "session", Request);
        recording.Add(1200, ExchangeDirection.Received, "session", Response);

        string written = recording.Write();

        Assert.DoesNotContain("3e91107c9a4b1f2088c7d5e6a1b2c3d4", written, StringComparison.Ordinal);
        Assert.DoesNotContain("hK9+Lm/2Qw8vZa1sTb4xYg==", written, StringComparison.Ordinal);

        // Still a recording of something, rather than a file of markers.
        Assert.Contains("/sie/ps5/rp/sess/init", written, StringComparison.Ordinal);
        Assert.Contains("RP-Nonce", written, StringComparison.Ordinal);
    }
}
