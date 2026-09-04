using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP641: the overlay layer's three shapes, priced, and the one rule the price turns on.
///
/// PP641 filed a question. Its section named three shapes - a bitmap per update, WPF with SDR
/// accepted, a rebuild against the compositor - and said choosing needs the cost of each.
///
/// THE FIRST SHAPE'S PRICE WAS A PREMISE, NOT A MEASUREMENT. The section prices it as "a
/// full-screen copy at HUD update rate", and spike/overlay-draw asked the machine instead. A
/// composition visual carries its own size, and PP10's HUD measures 156x138: rendering the tree at
/// that size costs 128 microseconds, and rendering it across a 4K plane costs 18,390 - more than a
/// whole 60fps frame. So the option as described was never available, and the option as it is costs
/// under one percent of a frame.
///
/// These tests hold three things. The rule, against a real visual tree measured here. The
/// arithmetic that turns a microsecond figure into a verdict. And the numbers, read out of the
/// spike's committed file rather than transcribed - PP666's lesson, because a table copied from a
/// measurement is a claim wearing the measurement's authority.
/// </summary>
public class OverlayDrawTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE: a HUD-sized surface, not a plane-sized one, measured on a real WPF tree.
    ///
    /// Built and measured here rather than asserted from the spike's number, so this fails if the
    /// rule stops holding for a tree that grew. The margin is what matters: three orders of
    /// magnitude of headroom do not vanish into a few more rows of text.
    ///
    /// Through PP618's one runner, which is the only apartment this suite starts - a
    /// FrameworkElement is a DispatcherObject and cannot be built on the runner's thread. The Func
    /// overload is what PP620's five sites moved to: the size comes back rather than being assigned
    /// into a captured local from another thread.
    /// </summary>
    [Fact]
    public void TheSurfaceIsSizedByTheHudAndNotByThePlane()
    {
        var plane = new Size(3840, 2160);

        Size wanted = Winwright.InApp.Apartment.Run(() =>
        {
            FrameworkElement hud = Hud();
            hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return hud.DesiredSize;
        });

        Size surface = OverlayDraw.SurfaceSizeFor(wanted, plane);

        output.WriteLine($"the HUD wants {wanted}, the surface is {surface}, the plane is {plane}");

        Assert.Equal(wanted.Width, surface.Width, 3);
        Assert.Equal(wanted.Height, surface.Height, 3);

        // The finding, stated as a relation rather than as the two numbers: the HUD's area is a
        // small fraction of the plane's, which is what makes the copy small.
        double fraction = surface.Width * surface.Height / (plane.Width * plane.Height);
        Assert.True(fraction > 0.0, "the HUD measured to nothing, so the relation below is vacuous");
        Assert.True(fraction < 0.01, $"the HUD covers {fraction:P2} of the plane, which is not a corner");
    }

    /// <summary>A HUD larger than the screen is clamped, because that is a layout bug and not a surface.</summary>
    [Fact]
    public void AHudLargerThanThePlaneIsClamped()
    {
        Size surface = OverlayDraw.SurfaceSizeFor(new Size(5000, 4000), new Size(1920, 1080));

        Assert.Equal(1920, surface.Width);
        Assert.Equal(1080, surface.Height);
    }

    /// <summary>
    /// An unmeasured tree asks for infinity, and infinity is not a surface size.
    ///
    /// WPF returns PositiveInfinity from DesiredSize for an element never given a constraint, and
    /// allocating that is a crash rather than a big buffer. The plane is the honest fallback.
    /// </summary>
    [Theory]
    [InlineData(double.PositiveInfinity, 100.0)]
    [InlineData(100.0, double.PositiveInfinity)]
    [InlineData(double.NaN, 100.0)]
    public void AnUnmeasuredTreeFallsBackToThePlane(double width, double height)
    {
        var plane = new Size(1920, 1080);

        Assert.Equal(plane, OverlayDraw.SurfaceSizeFor(new Size(width, height), plane));
    }

    /// <summary>
    /// THE FIRST CRITERION, the measured half: the spike's numbers, read from its file.
    ///
    /// Three rows, and the relation between them is the finding. The HUD's own bounds fit inside a
    /// frame with room to spare; the 4K plane does not fit inside one at all.
    /// </summary>
    [Fact]
    public void TheRecordedTimingsSayTheDescribedOptionNeverFitAFrame()
    {
        if (OverlayDraw.RecordedTimings() is not { } timings)
            return;

        Assert.Equal(3, timings.Count);

        OverlayTiming own = timings[0];
        OverlayTiming plane = timings[2];

        foreach (OverlayTiming timing in timings)
        {
            output.WriteLine(
                $"{timing.What}: {timing.Width}x{timing.Height} costs "
                    + $"{OverlayDraw.FractionOfAFrame(timing, 60.0):P1} of a 60fps frame");
        }

        // The HUD's own bounds: comfortably inside a frame, and the HUD updates far below frame rate.
        Assert.True(
            OverlayDraw.FractionOfAFrame(own, 60.0) < 0.05,
            $"the HUD's own bounds cost {OverlayDraw.FractionOfAFrame(own, 60.0):P1} of a frame");

        // The 4K plane: over a whole frame, which is why the section's option was not an option.
        Assert.True(
            OverlayDraw.FractionOfAFrame(plane, 60.0) > 1.0,
            $"the 4K plane costs {OverlayDraw.FractionOfAFrame(plane, 60.0):P1} of a frame");
    }

    /// <summary>And the rows are the shapes they say they are, so the two above are not the same row.</summary>
    [Fact]
    public void TheRecordedRowsAreTheHudAndTheTwoPlanes()
    {
        if (OverlayDraw.RecordedTimings() is not { } timings)
            return;

        Assert.Equal([156, 1920, 3840], [.. timings.Select(one => one.Width)]);
        Assert.Equal([138, 1080, 2160], [.. timings.Select(one => one.Height)]);

        // And the byte counts are the areas, which is what says a row is the surface it claims.
        foreach (OverlayTiming timing in timings)
            Assert.Equal((long)timing.Width * timing.Height * 4, timing.Bytes);
    }

    /// <summary>
    /// THE FIRST CRITERION, the other half: two of the three shapes have no time price.
    ///
    /// Saying so is the point. A comparison that gave all three a microsecond figure would look
    /// complete while comparing one thing three times.
    /// </summary>
    [Fact]
    public void OnlyTheBitmapShapeIsPricedInTime()
    {
        Assert.True(OverlayDraw.IsPricedInTime(OverlayShape.BitmapPerUpdate));
        Assert.False(OverlayDraw.IsPricedInTime(OverlayShape.WpfAndAcceptSdr));
        Assert.False(OverlayDraw.IsPricedInTime(OverlayShape.RebuildAgainstCompositor));

        Assert.Equal(OverlayShape.BitmapPerUpdate, OverlayDraw.Chosen);
    }

    /// <summary>
    /// The rebuild's price, from the ledger rather than from a feeling.
    ///
    /// PP10 and PP12 shipped in four commits writing 2,126 lines across 24 files. Block C's p90 is
    /// 499 lines, so the rebuild is four times the block's largest ordinary task - which is the
    /// figure that makes it the most expensive of the three even though it costs no microseconds.
    /// </summary>
    [Fact]
    public void TheRebuildIsFourTimesTheBlocksLargestOrdinaryTask()
    {
        double times = (double)OverlayDraw.RebuildLines / OverlayDraw.BlockCp90Lines;

        output.WriteLine($"{OverlayDraw.RebuildLines} lines over {OverlayDraw.RebuildFiles} files, {times:F1}x the p90");

        Assert.True(times > 4.0, $"the rebuild is {times:F1}x the p90, which is not the claim made");
    }

    /// <summary>The frame budget is arithmetic, and it says no about nothing.</summary>
    [Theory]
    [InlineData(60.0, 16666.67)]
    [InlineData(30.0, 33333.33)]
    [InlineData(120.0, 8333.33)]
    public void TheFrameBudgetIsOneOverTheRate(double fps, double expected)
        => Assert.Equal(expected, OverlayDraw.FrameBudgetMicroseconds(fps), 1);

    /// <summary>PP272: a rate that is not a rate is refused rather than divided by.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-60.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ARateThatIsNotARateHasNoBudget(double fps)
        => Assert.Equal(0.0, OverlayDraw.FrameBudgetMicroseconds(fps));

    /// <summary>PP10's HUD, in the shape the spike timed: five stat lines over a translucent ground.</summary>
    private static FrameworkElement Hud()
    {
        var stack = new StackPanel { Margin = new Thickness(12) };

        foreach (string line in (string[])
            ["24.3 Mbps", "18.5 ms", "2.1 frames", "0 lost", "1920x1080 60fps"])
        {
            stack.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 2),
            });
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Child = stack,
        };
    }
}
