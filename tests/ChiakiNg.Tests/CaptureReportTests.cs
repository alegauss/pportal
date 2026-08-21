using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP219: the diagnostic that found the defect, and the rule it found.
///
/// The measurement itself is in the changelog and cannot be re-run here: it needs a DualSense and
/// a person pressing it. What is asserted is the report's shape - which exists to make the two
/// silences distinguishable, and to put the header out BEFORE the window rather than after - and
/// that the Qt client still opens on construction, which is why the question never came up there.
/// </summary>
public class CaptureReportTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static readonly SdlPad Pad = new(0, "DualSense Wireless Controller", "030057564c05,*,a:b0,");

    /// <summary>
    /// The two silences, told apart. Opened and silent is a pad nobody pressed; not opened and
    /// silent is this task's defect, and both end with zero tokens.
    /// </summary>
    [Fact]
    public void TheTwoSilencesAreDistinguishable()
    {
        string unopened = CaptureReport.Opening(Pad, opened: false);
        string quiet = CaptureReport.Opening(Pad, opened: true);

        Assert.Contains("opened: False", unopened, StringComparison.Ordinal);
        Assert.Contains("opened: True", quiet, StringComparison.Ordinal);
        Assert.NotEqual(unopened, quiet);

        // And the tail is the same for both, which is why the header has to carry the difference.
        Assert.Equal(CaptureReport.Summary([], Window), CaptureReport.Summary([], Window));
        Assert.Contains(CaptureReport.Silent, CaptureReport.Summary([], Window), StringComparison.Ordinal);
    }

    /// <summary>
    /// The pad is named in the OPENING, so it is on screen before the window starts. A person
    /// holding a controller cannot press into a window whose start they never saw.
    /// </summary>
    [Fact]
    public void TheOpeningNamesThePadBeforeAnyToken()
    {
        string opening = CaptureReport.Opening(Pad, opened: true);

        Assert.Contains("[0] DualSense Wireless Controller", opening, StringComparison.Ordinal);
        Assert.DoesNotContain("token(s)", opening, StringComparison.Ordinal);
    }

    /// <summary>And no pad at all is a third answer, with nothing to say about opening.</summary>
    [Fact]
    public void NoPadIsItsOwnAnswer()
    {
        string opening = CaptureReport.Opening(null, opened: false);

        Assert.Contains(CaptureReport.NoPad, opening, StringComparison.Ordinal);
        Assert.DoesNotContain("opened:", opening, StringComparison.Ordinal);
    }

    /// <summary>A live line is one token, printed the moment it arrives and not collapsed.</summary>
    [Fact]
    public void ALiveLineIsOneToken()
    {
        Assert.Equal("  b0", CaptureReport.Live("b0"));
        Assert.Equal("  h0.1", CaptureReport.Live("h0.1"));
    }

    /// <summary>
    /// The tail collapses runs. One trigger pull was measured at forty-eight events on a real pad,
    /// and forty-eight identical lines hide the sequence rather than showing it.
    /// </summary>
    [Fact]
    public void TheSummaryCollapsesRuns()
    {
        string[] tokens = ["h0.1", "h0.0", "a4", "a4", "a4", "b0"];

        Assert.Equal(
            [("h0.1", 1), ("h0.0", 1), ("a4", 3), ("b0", 1)],
            CaptureReport.Runs(tokens));

        string summary = CaptureReport.Summary(tokens, Window);

        Assert.Contains("a4 x3", summary, StringComparison.Ordinal);
        Assert.Contains("6 token(s) in 20s", summary, StringComparison.Ordinal);
    }

    /// <summary>A token that comes back later is a new run, not the same one.</summary>
    [Fact]
    public void ATokenThatReturnsIsANewRun()
        => Assert.Equal(
            [("a4", 2), ("h0.1", 1), ("a4", 1)],
            CaptureReport.Runs(["a4", "a4", "h0.1", "a4"]));

    /// <summary>
    /// Why the Qt client never meets this: it opens the device as part of constructing a
    /// Controller, so nothing there ever has a pad enumerated and unopened.
    /// </summary>
    [Fact]
    public void TheQtClientStillOpensOnConstruction()
    {
        string? file = PadOpenSource.Locate();
        if (file is null)
            return;

        string source = File.ReadAllText(file);

        Assert.True(
            PadOpenSource.TheQtClientStillOpensOnConstruction(source),
            "opened while constructing");
        Assert.True(PadOpenSource.ItStillClosesWhatItOpened(source), "and closed on the way out");
    }
}
