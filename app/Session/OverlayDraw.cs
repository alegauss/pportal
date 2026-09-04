using System.Text.Json;
using System.Windows;

namespace ChiakiNg.Session;

/// <summary>One way of getting PP10's HUD into the compositor's overlay surface.</summary>
public enum OverlayShape
{
    /// <summary>Render the visual tree to a bitmap and upload it. PP641's first, and the chosen one.</summary>
    BitmapPerUpdate,

    /// <summary>Keep the HUD in WPF and accept SDR while it is up. PP319 rejected this once already.</summary>
    WpfAndAcceptSdr,

    /// <summary>Rebuild the HUD against the compositor, which costs PP10 and PP12 a second time.</summary>
    RebuildAgainstCompositor,
}

/// <summary>One timed shape from the spike, as its committed file records it.</summary>
/// <param name="What">The shape's own label.</param>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
/// <param name="RenderMedianUs">Median microseconds to render the tree into a bitmap of that size.</param>
/// <param name="CopyMedianUs">Median microseconds to copy the pixels out.</param>
/// <param name="Bytes">What an upload would carry.</param>
public readonly record struct OverlayTiming(
    string What,
    int Width,
    int Height,
    double RenderMedianUs,
    double CopyMedianUs,
    long Bytes);

/// <summary>
/// PP641: what draws the HUD into the overlay layer, and why the answer was not the expensive one.
///
/// PP319 chose the compositor tree and PP322 read it: an eight-bit premultiplied surface composes
/// above the ten-bit plane. What draws into the upper surface was never built, and PP641 filed the
/// question with three shapes and no price on any of them.
///
/// THE PREMISE WAS THE EXPENSIVE PART, AND IT WAS NOT A PROPERTY OF THE OPTION. PP641's section
/// prices the first shape as "a full-screen copy at HUD update rate". A composition visual carries
/// its own size and offset, and PP10's HUD is a corner of text: nothing requires the overlay surface
/// to be the plane's size. spike/overlay-draw measured both, and the two are three orders of
/// magnitude apart - 128 microseconds at the HUD's own 156x138, against 18,390 across a 4K plane,
/// which is MORE THAN AN ENTIRE 60fps FRAME. So the option as described cannot be done at all, and
/// the option as it actually is costs under one percent of a frame at a rate far below frame rate.
///
/// THE OTHER TWO ARE PRICED WITHOUT A MACHINE. Accepting SDR has no time cost; it has a quality
/// cost, and PP319 already weighed and rejected it - restating that is not a new reading. Rebuilding
/// against the compositor is priced from the ledger the way `weight` prices anything: the commits
/// that shipped PP10 and PP12 wrote <see cref="RebuildLines"/> lines across <see cref="RebuildFiles"/>
/// files, which is four times Block C's p90.
///
/// NOTHING HERE IS TYPED FROM THE SPIKE. <see cref="RecordedTimings"/> reads the committed release
/// file, because PP666's lesson is that a table nobody drives is a claim only its own test reads -
/// and a table transcribed from a measurement is the same claim with the measurement's authority
/// borrowed. The one number this class states of itself is the frame budget, which is arithmetic.
/// </summary>
public static class OverlayDraw
{
    /// <summary>The spike's committed reading, which is where every timing below comes from.</summary>
    public const string ReadingRelativePath = @"spike\overlay-draw\release-wpf-hud.json";

    /// <summary>The shape PP641 chose, and the reason it is not the one its section priced.</summary>
    public const OverlayShape Chosen = OverlayShape.BitmapPerUpdate;

    /// <summary>
    /// What PP10 and PP12's commits wrote, which is what rebuilding them costs a second time.
    ///
    /// Two commits each: PP10's HUD and its in-stream menu, PP12's focus chain and its vocabulary.
    /// Read with `git show --numstat` rather than estimated, the way `weight` derives anything.
    /// </summary>
    public const int RebuildLines = 2126;

    /// <summary>And across how many files, which is what an agent holds in context at once.</summary>
    public const int RebuildFiles = 24;

    /// <summary>Block C's p90 line count, which is what makes the rebuild four times a large task.</summary>
    public const int BlockCp90Lines = 499;

    /// <summary>The reading, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(ReadingRelativePath);

    /// <summary>
    /// The spike's timings, read from its file.
    ///
    /// Null outside a checkout, and empty where the file is there and holds none - the two are
    /// different, and a caller deciding whether to assert needs to tell them apart.
    /// </summary>
    public static IReadOnlyList<OverlayTiming>? RecordedTimings()
    {
        if (Locate() is not { } path)
            return null;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("timings", out JsonElement timings))
            return [];

        var found = new List<OverlayTiming>();

        foreach (JsonElement one in timings.EnumerateArray())
        {
            found.Add(new OverlayTiming(
                one.GetProperty("What").GetString() ?? string.Empty,
                one.GetProperty("Width").GetInt32(),
                one.GetProperty("Height").GetInt32(),
                one.GetProperty("RenderMedianUs").GetDouble(),
                one.GetProperty("CopyMedianUs").GetDouble(),
                one.GetProperty("Bytes").GetInt64()));
        }

        return found;
    }

    /// <summary>How long one frame lasts, in microseconds.</summary>
    public static double FrameBudgetMicroseconds(double fps)
        => fps <= 0.0 || !double.IsFinite(fps) ? 0.0 : 1_000_000.0 / fps;

    /// <summary>
    /// What a shape costs as a fraction of one frame, so a number becomes a verdict.
    ///
    /// Above 1.0 the work does not fit in a frame at all, which is what the 4K row says and is the
    /// whole reason PP641's described option was never available.
    /// </summary>
    public static double FractionOfAFrame(OverlayTiming timing, double fps)
    {
        double budget = FrameBudgetMicroseconds(fps);
        return budget <= 0.0 ? double.PositiveInfinity : (timing.RenderMedianUs + timing.CopyMedianUs) / budget;
    }

    /// <summary>
    /// THE RULE: the overlay surface is the HUD's own size, never the video plane's.
    ///
    /// This is the finding as code. A composition visual is positioned by its offset and sized by
    /// itself, so the surface to allocate is what the element asks for through Measure - clamped to
    /// the plane, because a HUD larger than the screen is a layout bug and not a bigger surface.
    /// </summary>
    public static Size SurfaceSizeFor(Size hudWanted, Size plane)
    {
        if (!double.IsFinite(hudWanted.Width) || !double.IsFinite(hudWanted.Height))
            return plane;

        return new Size(
            Math.Min(Math.Max(hudWanted.Width, 0.0), plane.Width),
            Math.Min(Math.Max(hudWanted.Height, 0.0), plane.Height));
    }

    /// <summary>
    /// Whether a shape's price is a time at all.
    ///
    /// Two of the three are not, and saying so is half of PP641's first criterion: accepting SDR
    /// costs quality and rebuilding costs work, and pretending either has a microsecond figure
    /// would make the comparison look complete while comparing one thing.
    /// </summary>
    public static bool IsPricedInTime(OverlayShape shape) => shape == OverlayShape.BitmapPerUpdate;
}
