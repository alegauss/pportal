using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChiakiNg.Session;

/// <summary>
/// PP226: a screen rendered to a PNG, with no window ever shown.
///
/// This port has 1868 tests and not one of them had ever looked at a pixel. They assert what a view
/// model says and what a source file still contains; nothing asserted what was DRAWN, and PP223
/// shipped a screen that never appeared with the suite green both times it failed.
///
/// OFF-SCREEN, and that is the whole design. claude-tray answers the same problem with two loops and
/// its own notes say which it prefers: a flag that renders the surface off-screen, and a screen-copy
/// script kept for what off-screen cannot reach - because a screen copy can photograph the wrong
/// window, and that project carries the failed capture and the four assertions it grew to stop it.
/// A render of a visual tree cannot photograph anything else: there is no foreground, no Z order and
/// no second instance to be confused with.
///
/// It suits this port better still. The mapping screen's capture modal is an overlay in the same
/// visual tree rather than its own top-level window, so one render covers the screen and the modal
/// together.
///
/// WHAT IT DOES NOT DO is assert on pixels. That is a different and far more brittle proposition,
/// and the value here is that the thing can be SEEN by whoever is working on it - without a pad,
/// without a window, and without asking somebody else to look.
/// </summary>
public static class ScreenCapture
{
    /// <summary>WPF's own device-independent unit, so the PNG is the size the layout asked for.</summary>
    public const double Dpi = 96.0;

    /// <summary>
    /// Measures, arranges and renders an element, and encodes the result as a PNG.
    ///
    /// The measure and arrange are here rather than expected of the caller because a tree that was
    /// never arranged renders as nothing at all - an empty picture that looks like a drawing bug
    /// and is a calling bug.
    /// </summary>
    /// <param name="element">The screen. Need not be in a window, and should not be.</param>
    /// <param name="width">Device-independent width to lay it out at.</param>
    /// <param name="height">And the height.</param>
    /// <param name="background">
    /// What the window would have painted behind it. Not optional in practice and not a decoration:
    /// a screen rendered on its own has NO background, and this application's screens are drawn for
    /// a dark one - so the first capture ever taken here came back as pale grey text on nothing,
    /// correct in every respect and unreadable. A picture nobody can read is the same as no picture.
    /// </param>
    public static byte[] Png(
        FrameworkElement element, int width, int height, Brush? background = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();

        var target = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);

        // The background and the element in one visual, rather than setting a property on the
        // element: a UserControl's Background belongs to whoever wrote it, and a capture that
        // changed it would be a picture of a screen this application does not ship.
        var composed = new DrawingVisual();
        using (DrawingContext canvas = composed.RenderOpen())
        {
            canvas.DrawRectangle(background ?? WindowBackground, null, new Rect(size));
            canvas.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        }

        target.Render(composed);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// What a window paints behind a screen.
    ///
    /// SystemColors.WindowBrush was the obvious answer and is the WRONG one, measured: it is the
    /// classic Win32 palette and does not follow the Fluent theme this application asks for with
    /// ThemeMode. On the machine this was built on it returns white while the application's own
    /// window is dark, so the capture came back as white text on white - less readable than the
    /// transparent background it was added to fix.
    ///
    /// So it is the theme's, looked up by key, with the observed window colour as the fallback for
    /// a build where that key is absent. <see cref="Png"/> takes an override for the caller who
    /// wants the other one.
    /// </summary>
    public static Brush WindowBackground
    {
        get
        {
            // The Fluent theme's own key first. SystemColors is deliberately NOT consulted: it is
            // the classic Win32 palette, it answers white on a machine whose application window is
            // dark, and a capture taken against it is white text on white.
            object? themed = Application.Current?.TryFindResource(ThemeBackgroundKey);
            return themed as Brush ?? Fallback;
        }
    }

    /// <summary>The Fluent theme's background, by the key that theme publishes it under.</summary>
    public const string ThemeBackgroundKey = "ApplicationBackgroundBrush";

    /// <summary>
    /// The colour this application's window was observed painting, for when the theme will not say.
    /// Written down rather than guessed: it is what a run of `--map-controller` actually showed.
    ///
    /// FROZEN, and that is not tidiness. A Brush is a Freezable and an unfrozen one belongs to the
    /// thread that made it; a static one is made by whichever thread reaches this class first, and
    /// every thread after that is refused - "cannot use a DependencyObject that belongs to a
    /// different thread". Captures run on their own STA thread by nature, so the second one to ask
    /// threw. Freezing is what makes it shareable, and it is why WPF's own Brushes statics work.
    /// </summary>
    public static Brush Fallback { get; } = Frozen(Color.FromRgb(0x20, 0x20, 0x20));

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Which of the two the last look would use, so a capture can SAY rather than a reader guess.
    ///
    /// The pair is not interchangeable and the difference is a picture nobody can read, so which
    /// one answered is worth a line of output on every run.
    /// </summary>
    public static string BackgroundSource
        => Application.Current?.TryFindResource(ThemeBackgroundKey) is Brush
            ? ThemeBackgroundKey
            : "the observed window colour";

    /// <summary>
    /// Whether a render drew ANYTHING - the one assertion worth making about a picture without
    /// looking at it.
    ///
    /// A tree that failed to build, or was never arranged, renders as a rectangle of transparent
    /// pixels. It is not a claim that the screen is correct; it is the difference between a picture
    /// and a blank, which is the failure a caller cannot see by checking that a file was written.
    /// </summary>
    public static bool DrewSomething(FrameworkElement element, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(element);

        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();

        var target = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        target.Render(element);

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        target.CopyPixels(pixels, stride, 0);

        // Any pixel with an alpha of its own. Pbgra32 puts alpha last in each four-byte group.
        for (int at = 3; at < pixels.Length; at += 4)
        {
            if (pixels[at] != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The mapping string of a real DualSense over USB, captured 2026-08-21.
    ///
    /// A fixture rather than an invention, so the picture is of the screen people actually see: the
    /// name field is `*` - which is what a DualSense really sends where a name would be - the d-pad
    /// is a hat, the triggers are axes, and `crc` is metadata rather than a control. Every one of
    /// those was measured through the pad rather than assumed, and a made-up string would have had
    /// none of them.
    /// </summary>
    public const string SampleDualSense =
        "030057564c050000e60c000000016800,*,a:b0,b:b1,back:b4,dpdown:h0.4,dpleft:h0.8,dpright:h0.2,"
        + "dpup:h0.1,guide:b5,leftshoulder:b9,leftstick:b7,lefttrigger:a4,leftx:a0,lefty:a1,"
        + "rightshoulder:b10,rightstick:b8,righttrigger:a5,rightx:a2,righty:a3,start:b6,x:b2,y:b3,"
        + "touchpad:b11,misc1:b12,crc:5657,";

    /// <summary>What that pad calls itself, which is the fallback the `*` above resolves to.</summary>
    public const string SampleName = "DualSense Wireless Controller";
}
