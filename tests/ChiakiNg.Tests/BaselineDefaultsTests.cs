using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the two fields that are never empty, and the schema the row belongs to.
///
/// A record with no decoder must not say so with an empty string: the ledger is compared row
/// against row, and "" is a value that sorts and groups with nothing. The C substitutes a word for
/// each - "software" for the decoder, "unknown" for the renderer - and the distinction between
/// them is deliberate:
///
///   a missing DECODER means software, because that is what actually ran. The library refuses a
///   hardware decoder it cannot open rather than falling back, so a name that got there is a name
///   that worked;
///
///   a missing RENDERER means unknown, because nothing was determined - not that a default one was
///   used. Reading the second as the first would put every row from a build that forgot to set it
///   into the same bucket as the rows that really used a software path.
///
/// The port passes these through the seam, so what these check is that it passes them through
/// RAW - a managed side that substituted its own word would write the other client's default in
/// the wrong cases and its own in the rest.
/// </summary>
public class BaselineDefaultsTests
{
    private static string LineWith(string hwDecoder, string renderer)
    {
        using var baseline = new SessionBaseline();
        baseline.SetConfig(hwDecoder, renderer, 0.05, idrOnFecFailure: false);
        return baseline.Format();
    }

    /// <summary>A decoder that was not named is software, because software is what ran.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnnamedDecoderIsSoftware(string? decoder)
    {
        string line = LineWith(decoder!, "opengl");

        Assert.Contains("\"hw_decoder\":\"software\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\"hw_decoder\":\"\"", line, StringComparison.Ordinal);
    }

    /// <summary>And a renderer that was not named is unknown, which is a different claim.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnnamedRendererIsUnknown(string? renderer)
    {
        string line = LineWith("d3d11va", renderer!);

        Assert.Contains("\"renderer\":\"unknown\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\"renderer\":\"\"", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two defaults are different words, which is the finding rather than an accident. One
    /// says what ran and the other says nothing was determined.
    /// </summary>
    [Fact]
    public void TheTwoDefaultsAreNotTheSameWord()
    {
        string line = LineWith("", "");

        Assert.Contains("\"hw_decoder\":\"software\"", line, StringComparison.Ordinal);
        Assert.Contains("\"renderer\":\"unknown\"", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The harder case, and the one that found a defect: a session that never sets them at all.
    ///
    /// chiaki_session_baseline_init is a memset, so both fields are EMPTY STRINGS until a setter
    /// runs - the C's "never empty" guarantee lives in the setters and not in the struct. The seam
    /// used to guard its two calls on a non-null pointer and skip them, which left the "" the C's
    /// own fixture says a row must never contain. A session that simply never reached that code
    /// wrote two of them.
    /// </summary>
    [Fact]
    public void ASessionThatNeverSetsThemStillWritesWords()
    {
        using var baseline = new SessionBaseline();

        string line = baseline.Format();

        Assert.DoesNotContain("\"hw_decoder\":\"\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\"renderer\":\"\"", line, StringComparison.Ordinal);
    }

    /// <summary>A named one is recorded as given, on both fields.</summary>
    [Fact]
    public void ANamedOneIsRecordedAsGiven()
    {
        string line = LineWith("d3d11va", "opengl");

        Assert.Contains("\"hw_decoder\":\"d3d11va\"", line, StringComparison.Ordinal);
        Assert.Contains("\"renderer\":\"opengl\"", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The number the writer puts in the row is the number it was compiled with, and the managed
    /// side pins the same one. Everything else about comparing two rows trusts this to select
    /// which field set they are read against.
    /// </summary>
    [Fact]
    public void TheRowClaimsTheSchemaBothSidesWereBuiltWith()
    {
        string line = LineWith("d3d11va", "opengl");

        Assert.Equal(SessionBaseline.ExpectedSchema, SessionBaseline.Schema);
        Assert.Contains(
            $"\"schema\":{SessionBaseline.ExpectedSchema}",
            line,
            StringComparison.Ordinal);
    }
}
