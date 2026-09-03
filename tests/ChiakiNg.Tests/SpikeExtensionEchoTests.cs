using System.Text.Json;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP644: the NVIDIA extension read-back carries nothing, held against the two runs that prove it
/// rather than against the sentence that says so.
///
/// PP47 set an NVIDIA stream extension, called <c>VideoProcessorGetStreamExtension</c> to read it
/// back, and got <c>version=0 method=0 enable=0</c>. Its README first read that as "the driver does
/// not recognise this GUID", corrected itself to "the driver wrote nothing, and nothing more", and
/// still ended the paragraph calling the echo a hint.
///
/// PP49 removed the hedge's last support. It sets a DIFFERENT NVIDIA extension - true HDR, an
/// interface of its own - on the same card and the same driver, and that one works: 2,073,580 of
/// 2,073,600 pixels move. Its read-back echoes the same three zeros.
///
/// SO THE PAIR IS THE EVIDENCE, and the pair is two committed files in this tree. One run engaged
/// and one did not; both echoed identical zeros. That is a claim about data rather than about
/// prose, which is why it can be asserted at all - a test over the READMEs would only check that a
/// paragraph contains the words it contains.
///
/// It goes red rather than stale. If either run is re-taken on a driver that starts writing the
/// buffer, the echo stops being identical, and the paragraph in both spikes has to be re-read
/// instead of quietly becoming false.
/// </summary>
public class SpikeExtensionEchoTests(ITestOutputHelper output)
{
    /// <summary>PP47's run: the extension was accepted and the picture did not change.</summary>
    private const string Upscale = @"spike\video-upscale\release-4060-no-engage.json";

    /// <summary>PP49's: a different extension on the same card, and it worked.</summary>
    private const string Hdr = @"spike\video-hdr\release-4060-engaged.json";

    private static JsonElement? Run(string relative)
        => SanitizerSource.LocateRelative(relative) is { } path
            ? JsonDocument.Parse(File.ReadAllText(path)).RootElement
            : null;

    /// <summary>
    /// The two runs disagree about engagement and agree about the echo, which is the whole finding.
    ///
    /// Both halves are asserted. Without the disagreement this would pass on two runs that both
    /// failed to engage, where identical echoes say nothing; without the agreement there is no
    /// finding at all.
    /// </summary>
    [Fact]
    public void TheEchoIsTheSameWhetherTheExtensionEngagedOrNot()
    {
        if (Run(Upscale) is not { } upscale || Run(Hdr) is not { } hdr)
            return;

        bool upscaleEngaged = upscale.GetProperty("engaged").GetBoolean();
        bool hdrEngaged = hdr.GetProperty("engaged").GetBoolean();

        string upscaleEcho = Echo(upscale);
        string hdrEcho = Echo(hdr);

        output.WriteLine($"video-upscale engaged={upscaleEngaged} echo={upscaleEcho}");
        output.WriteLine($"video-hdr     engaged={hdrEngaged} echo={hdrEcho}");

        Assert.False(upscaleEngaged, "PP47's committed run engaged, so it is no longer the negative half");
        Assert.True(hdrEngaged, "PP49's committed run did not engage, so there is no positive half");

        Assert.True(
            upscaleEcho == hdrEcho,
            $"the read-back differs between the two runs - '{upscaleEcho}' against '{hdrEcho}' - so "
                + "it may distinguish an extension that engaged from one that did not, and both "
                + "spikes' READMEs say it cannot");
    }

    /// <summary>
    /// And the echo they agree on is zeros, not some other shared value.
    ///
    /// Separate from the test above because the two claims fail for different reasons. Two runs
    /// agreeing on a NON-zero echo would mean the driver does write the buffer and the number is
    /// simply not about engagement, which is a different paragraph from the one both READMEs carry.
    /// </summary>
    [Fact]
    public void AndWhatBothEchoedIsNothingAtAll()
    {
        if (Run(Hdr) is not { } hdr)
            return;

        Assert.Contains("version=0 method=0 enable=0", Echo(hdr), StringComparison.Ordinal);
    }

    /// <summary>
    /// The read-back is still made, which is what PP644 decided rather than assumed.
    ///
    /// The alternative was deleting the call from both spikes. It was refused because the two
    /// committed runs record its output: a spike whose source no longer produces the JSON beside it
    /// is a record nobody can re-take. So the field has to keep existing - and a run written without
    /// it would leave this quietly passing on a missing property, which is why the shape is asserted
    /// rather than the sentence about it.
    /// </summary>
    [Fact]
    public void BothRunsStillRecordTheReadBack()
    {
        if (Run(Upscale) is not { } upscale || Run(Hdr) is not { } hdr)
            return;

        foreach (JsonElement run in (JsonElement[])[upscale, hdr])
        {
            Assert.True(
                run.TryGetProperty("set_extension", out JsonElement echo),
                "a committed run has no set_extension field, so the call was dropped and the two "
                    + "runs beside it cannot be re-taken from their own source");
            Assert.StartsWith("set accepted;", echo.GetString(), StringComparison.Ordinal);
        }
    }

    private static string Echo(JsonElement run) => run.GetProperty("set_extension").GetString() ?? "";
}
