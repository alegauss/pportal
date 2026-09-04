using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OverlayDraw;

/// <summary>One timed shape, in the units a frame budget is argued in.</summary>
/// <param name="What">The shape - the HUD's own bounds, or a full plane at some resolution.</param>
/// <param name="Width">Pixels across, at 96 DPI.</param>
/// <param name="Height">Pixels down.</param>
/// <param name="RenderMedianUs">Median microseconds for RenderTargetBitmap.Render.</param>
/// <param name="RenderP90Us">The ninetieth percentile, which is what a dropped frame comes from.</param>
/// <param name="CopyMedianUs">Median microseconds to CopyPixels into a managed buffer.</param>
/// <param name="Bytes">The premultiplied BGRA the upload would carry.</param>
internal readonly record struct Timing(
    string What,
    int Width,
    int Height,
    double RenderMedianUs,
    double RenderP90Us,
    double CopyMedianUs,
    long Bytes);

/// <summary>
/// PP641: what drawing PP10's HUD into the compositor's overlay surface actually costs.
///
/// PP641's section names three shapes and prices none. The first - render the visual tree to a
/// bitmap per frame and upload it - is priced there as "a full-screen copy at HUD update rate",
/// and that premise is the thing this measures rather than accepts.
///
/// A HUD is not full-screen. It is a corner of text, and a composition visual carries its own size
/// and offset, so what the overlay surface has to be is the HUD's bounds and not the plane's. If
/// that holds, the first shape's cost is a small copy at a low rate rather than a large one at a
/// high rate, and the three options are not the three the section describes.
///
/// So both are timed: the HUD at its own size, and the same tree on a full 1080p and 4K surface.
/// The difference between the two IS the finding.
///
/// WHAT THIS DOES NOT MEASURE. The upload from the managed buffer into a D3D11 or composition
/// surface is a different call on a different device, and PP650 already measured a full-frame
/// system-memory copy at 2253 microseconds - a number this can be read against. The point here is
/// the WPF half, which is the half PP641 says nobody has built.
/// </summary>
internal static class Program
{
    /// <summary>How many times each shape is timed, after a discarded warm-up.</summary>
    private const int Iterations = 60;

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "result.json";

        FrameworkElement hud = BuildHud();

        // Measured rather than assumed: the HUD's size is what the tree asks for, and that is the
        // number the whole finding turns on.
        hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size wanted = hud.DesiredSize;

        int hudWidth = (int)Math.Ceiling(wanted.Width);
        int hudHeight = (int)Math.Ceiling(wanted.Height);

        Console.WriteLine(
            $"the HUD asks for {hudWidth}x{hudHeight} at 96 DPI");

        var timings = new List<Timing>
        {
            Time("the HUD's own bounds", hudWidth, hudHeight, hud),
            Time("a full 1080p plane", 1920, 1080, BuildHud()),
            Time("a full 4K plane", 3840, 2160, BuildHud()),
        };

        foreach (Timing timing in timings)
        {
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-24} {1,5}x{2,-5} render {3,8:F1} us (p90 {4,8:F1})  copy {5,8:F1} us  {6,10:N0} bytes",
                    timing.What,
                    timing.Width,
                    timing.Height,
                    timing.RenderMedianUs,
                    timing.RenderP90Us,
                    timing.CopyMedianUs,
                    timing.Bytes));
        }

        Timing own = timings[0];
        Timing plane = timings[2];

        Console.WriteLine();
        Console.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "the HUD's own bounds are {0:F0}x cheaper to render than the 4K plane and carry {1:F0}x fewer bytes",
                plane.RenderMedianUs / Math.Max(own.RenderMedianUs, 0.001),
                (double)plane.Bytes / Math.Max(own.Bytes, 1)));

        System.IO.File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    taken = DateTimeOffset.UtcNow,
                    machine = Environment.MachineName,
                    os = Environment.OSVersion.VersionString,
                    dotnet = Environment.Version.ToString(),
                    iterations = Iterations,
                    hud = new { width = hudWidth, height = hudHeight },
                    timings,
                },
                new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"written to {output}");
        return 0;
    }

    /// <summary>
    /// PP10's HUD, as close to the real one as a spike needs.
    ///
    /// Four stats in a stacked panel over a translucent ground, which is the shape StreamStats
    /// formats for. The exact typography does not move the number; what does is that it is TEXT,
    /// so every render runs the glyph path rather than filling a rectangle.
    /// </summary>
    private static FrameworkElement BuildHud()
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

    /// <summary>
    /// One shape, timed.
    ///
    /// A fresh RenderTargetBitmap per iteration, because reusing one is a different question - the
    /// port would reuse, and reuse is the OPTIMISTIC case. Timing the allocation too is what keeps
    /// this from reporting a number the real path cannot reach.
    /// </summary>
    private static Timing Time(string what, int width, int height, FrameworkElement element)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        byte[] buffer = new byte[(long)width * height * 4];
        int stride = width * 4;

        var renders = new List<double>(Iterations);
        var copies = new List<double>(Iterations);

        // One discarded pass: the first render of a tree JITs the glyph path and warms the font
        // cache, and reporting that as the steady state would overstate every option equally.
        Render(width, height, element).CopyPixels(buffer, stride, 0);

        for (int i = 0; i < Iterations; i++)
        {
            long before = Stopwatch.GetTimestamp();
            RenderTargetBitmap bitmap = Render(width, height, element);
            renders.Add(Microseconds(before));

            before = Stopwatch.GetTimestamp();
            bitmap.CopyPixels(buffer, stride, 0);
            copies.Add(Microseconds(before));
        }

        return new Timing(
            what,
            width,
            height,
            Percentile(renders, 0.50),
            Percentile(renders, 0.90),
            Percentile(copies, 0.50),
            buffer.LongLength);
    }

    private static RenderTargetBitmap Render(int width, int height, Visual visual)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static double Microseconds(long since)
        => (Stopwatch.GetTimestamp() - since) * 1_000_000.0 / Stopwatch.Frequency;

    /// <summary>A percentile by nearest rank, which needs no interpolation to defend.</summary>
    private static double Percentile(List<double> values, double fraction)
    {
        values.Sort();
        int at = (int)Math.Ceiling(fraction * values.Count) - 1;
        return values[Math.Clamp(at, 0, values.Count - 1)];
    }
}
