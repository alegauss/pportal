using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP695: a capture that opens and never speaks, judged by the clock rather than by a device.
///
/// PP652's capture opens the default communications endpoint. On a machine whose default is a
/// Bluetooth headset, that endpoint carries a music profile with no microphone, and opening the
/// capture is only meant to make Windows switch. Sometimes it does - the first unit at 222
/// milliseconds, then a steady hundred a second - and sometimes thirty seconds pass with nothing,
/// while Start reports Running with an HRESULT of zero throughout.
///
/// THE SILENT CASE CANNOT BE REPRODUCED BY A TEST that opens a device: it needs a headset in the
/// wrong profile, and PP652's own fixture now routes around it deliberately. So the judgement is a
/// function of two numbers a capture already has, and every state below is driven by handing it
/// those numbers.
/// </summary>
public class CaptureSilenceTests
{
    /// <summary>THE ONE THAT MATTERS: open, past the grace, nothing delivered.</summary>
    [Fact]
    public void OpenAndSilentPastTheGraceIsTheStateThatMatters()
    {
        Assert.Equal(
            CaptureHealth.Silent,
            CaptureSilence.Judge(running: true, CaptureSilence.Grace, units: 0));

        // And well past it, which is the shape actually observed: thirty seconds, nothing.
        Assert.Equal(
            CaptureHealth.Silent,
            CaptureSilence.Judge(running: true, TimeSpan.FromSeconds(30), units: 0));
    }

    /// <summary>Inside the grace it is a start, because every capture begins with no units.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(43)]
    [InlineData(1999)]
    public void InsideTheGraceItIsStarting(int milliseconds)
        => Assert.Equal(
            CaptureHealth.Starting,
            CaptureSilence.Judge(running: true, TimeSpan.FromMilliseconds(milliseconds), units: 0));

    /// <summary>
    /// The two endpoints that DID work are inside the grace, which is what makes it a threshold and
    /// not a guess.
    ///
    /// A wired microphone delivered its first unit at 44 milliseconds and a Bluetooth one at 222.
    /// A grace that called either of them silent would report a working device as broken, and a
    /// person told that twice stops reading the message.
    /// </summary>
    [Fact]
    public void BothMeasuredSuccessesAreWellInsideTheGrace()
    {
        Assert.True(
            CaptureSilence.SlowestSuccess < CaptureSilence.Grace,
            "the grace is below the slowest first unit actually seen, so a working device reads as silent");

        // An order of magnitude of room, which is the claim the docstring makes.
        Assert.True(CaptureSilence.Grace > CaptureSilence.SlowestSuccess * 5);
    }

    /// <summary>One unit is streaming, however long it took.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100000)]
    public void AnyUnitAtAllIsStreaming(long units)
        => Assert.Equal(
            CaptureHealth.Streaming,
            CaptureSilence.Judge(running: true, TimeSpan.FromSeconds(60), units));

    /// <summary>
    /// A capture that spoke and went quiet stays Streaming, which is deliberate.
    ///
    /// This is about an endpoint that NEVER spoke. A dropout mid-session is a different fact with a
    /// different answer, and conflating them would make a person muting their microphone look like
    /// a broken device - which is the false positive that gets a warning ignored.
    /// </summary>
    [Fact]
    public void AnEndpointThatSpokeAndWentQuietIsNotThisState()
        => Assert.Equal(
            CaptureHealth.Streaming,
            CaptureSilence.Judge(running: true, TimeSpan.FromHours(1), units: 1));

    /// <summary>A capture that is not running is not judged at all.</summary>
    [Fact]
    public void NotRunningIsStopped()
    {
        Assert.Equal(CaptureHealth.Stopped, CaptureSilence.Judge(running: false, TimeSpan.Zero, 0));
        Assert.Equal(CaptureHealth.Stopped, CaptureSilence.Judge(running: false, TimeSpan.FromHours(1), 500));
    }

    /// <summary>The grace is a parameter, so the threshold is not baked into the judgement.</summary>
    [Fact]
    public void TheGraceIsAParameter()
    {
        var runningFor = TimeSpan.FromMilliseconds(500);

        Assert.Equal(
            CaptureHealth.Starting,
            CaptureSilence.Judge(true, runningFor, 0, TimeSpan.FromSeconds(1)));

        Assert.Equal(
            CaptureHealth.Silent,
            CaptureSilence.Judge(true, runningFor, 0, TimeSpan.FromMilliseconds(100)));
    }

    /// <summary>
    /// Only the silent state has words, and they name the cause a person can act on.
    ///
    /// A host that narrated every state would train a person to ignore it before the one that
    /// matters arrived. And the message says what to DO - a state a person cannot act on is a state
    /// not worth showing.
    /// </summary>
    [Fact]
    public void OnlyTheSilentStateSaysAnything()
    {
        string? advice = CaptureSilence.Advice(CaptureHealth.Silent, "Headset (Lenovo thinkplus XT80)");

        Assert.NotNull(advice);
        Assert.Contains("Headset (Lenovo thinkplus XT80)", advice, StringComparison.Ordinal);
        Assert.Contains("Bluetooth", advice, StringComparison.Ordinal);
        Assert.Contains("another input device", advice, StringComparison.Ordinal);

        Assert.Null(CaptureSilence.Advice(CaptureHealth.Starting, "x"));
        Assert.Null(CaptureSilence.Advice(CaptureHealth.Streaming, "x"));
        Assert.Null(CaptureSilence.Advice(CaptureHealth.Stopped, "x"));
    }

    /// <summary>And the predicate agrees with the advice, so a caller can ask either.</summary>
    [Theory]
    [InlineData(CaptureHealth.Silent, true)]
    [InlineData(CaptureHealth.Starting, false)]
    [InlineData(CaptureHealth.Streaming, false)]
    [InlineData(CaptureHealth.Stopped, false)]
    public void TheReportingPredicateAgreesWithTheAdvice(CaptureHealth health, bool expected)
    {
        Assert.Equal(expected, CaptureSilence.WorthReporting(health));
        Assert.Equal(expected, CaptureSilence.Advice(health, "x") is not null);
    }
}
