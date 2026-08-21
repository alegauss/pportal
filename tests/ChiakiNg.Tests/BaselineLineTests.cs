using System.Text.Json;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the ledger line the two clients share, and the hostile text that could end it early.
///
/// PP5 made this file the thing the port is MEASURED with: two builds are compared by holding their
/// rows against each other. That only works while both write the same row for the same session, and
/// the place it stops working is a string somebody did not choose - a codec name, an app version, a
/// renderer - carrying a quote.
///
/// The C does not escape those. It REPLACES them: a quote and a backslash become an underscore,
/// and a comma and a colon survive because they are legal inside a JSON string. That is not the
/// obvious answer - a managed port reaching for a JSON serialiser would write \" and produce a
/// different line for the same input, valid on its own and no longer comparable with the other
/// client's.
///
/// So what these assert is that the port hands its text over RAW and inherits the C's rule, rather
/// than sanitising or escaping on the way through and doing it twice.
/// </summary>
public class BaselineLineTests
{
    /// <summary>A codec name that would end the JSON string early if it went in unchanged.</summary>
    private const string Hostile = "h2\"64\\,\"evil\":1";

    /// <summary>What the C turns it into: the two dangerous characters replaced, the rest kept.</summary>
    private const string Sanitised = "h2_64_,_evil_:1";

    private static string FormatWith(Action<SessionBaseline> fill)
    {
        using var baseline = new SessionBaseline();
        fill(baseline);
        return baseline.Format();
    }

    /// <summary>
    /// The quote and the backslash are replaced and the comma and the colon are not. Asserted on
    /// the LINE rather than on a field, because the line is what the other client reads.
    /// </summary>
    [Fact]
    public void TheTwoDangerousCharactersAreReplacedAndTheRestSurvive()
    {
        string line = FormatWith(b => b.SetVideo(Hostile, 1920, 1080, 60, 15000));

        Assert.Contains(Sanitised, line, StringComparison.Ordinal);

        // The half that matters: the injected fragment is not sitting in the line as a key.
        Assert.DoesNotContain("evil\":1", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it is a replacement rather than an escape. A port that escaped would write a backslash
    /// before the quote and keep the quote - a different line, valid on its own, and no longer the
    /// other client's.
    /// </summary>
    [Fact]
    public void TheRuleIsReplacementAndNotEscaping()
    {
        string line = FormatWith(b => b.SetVideo(Hostile, 1920, 1080, 60, 15000));

        Assert.DoesNotContain("\\\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("h2\\", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The property all of that protects: the line parses. Asserted with a real parser rather than
    /// by looking for characters, because a sanitiser that missed a case would produce a line that
    /// still looks plausible and stops being readable.
    /// </summary>
    [Fact]
    public void AHostileSessionStillProducesOneParseableLine()
    {
        string line = FormatWith(b =>
        {
            b.SetVideo(Hostile, 1920, 1080, 60, 15000);
            b.SetAppVersion("1.0\n0");
            b.SetConfig(Hostile, Hostile, 0.05, idrOnFecFailure: true);
        });

        using JsonDocument document = JsonDocument.Parse(line);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("video", out _));
    }

    /// <summary>
    /// A newline in user text is replaced, and the ONLY newline in the line is the terminator the
    /// format writes at the end.
    ///
    /// The distinction is the whole point and it is where the first version of this check was
    /// wrong: the line legitimately ends in a newline, because the ledger is one JSON object per
    /// line and the terminator is part of what format produces. Asserting "no newline at all"
    /// failed against correct code. What matters is that a newline cannot arrive from a string
    /// somebody did not choose - one of those splits a row in two and every reader after it is
    /// reading half a session.
    /// </summary>
    [Fact]
    public void TheOnlyNewlineIsTheTerminator()
    {
        string line = FormatWith(b => b.SetAppVersion("1.0\n0"));

        Assert.EndsWith("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', line[..^1]);
        Assert.DoesNotContain('\r', line);

        // And the text itself came through with the newline replaced rather than dropped.
        Assert.Contains("1.0_0", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// Text is truncated rather than refused. Forty characters go in and the line carries fewer,
    /// which is the C's fixed-size field showing through - a port that grew the field would write
    /// a row the other client's parser reads and the other client's writer could never produce.
    /// </summary>
    [Fact]
    public void OverlongTextIsTruncatedRatherThanRefused()
    {
        const string overlong = "0123456789012345678901234567890123456789";

        string line = FormatWith(b => b.SetVideo(overlong, 1920, 1080, 60, 15000));

        Assert.DoesNotContain(overlong, line, StringComparison.Ordinal);

        // And something of it survived, so this is truncation and not a dropped field.
        Assert.Contains("01234567", line, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    /// <summary>
    /// An ordinary session's line parses too, so the checks above are not passing because every
    /// line happens to.
    /// </summary>
    [Fact]
    public void AnOrdinarySessionParsesAsWell()
    {
        string line = FormatWith(b =>
        {
            b.SetVideo("h264", 1920, 1080, 60, 15000);
            b.SetAppVersion("1.10.0");
        });

        using JsonDocument document = JsonDocument.Parse(line);

        Assert.Equal(
            "h264",
            document.RootElement.GetProperty("video").GetProperty("codec").GetString());
    }
}
