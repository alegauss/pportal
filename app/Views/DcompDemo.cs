using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ChiakiNg.Native;

namespace ChiakiNg.Views;

/// <summary>
/// PP284: the window PP163's last question is answered by looking at.
///
/// PP281 built the DirectComposition tree to Commit, PP282 asked it in the arrangement the design
/// needs - the visual BEHIND the window's content - and PP283 asked that of a real WPF window's
/// HWND. All three hold. What none of them can answer is what WPF DRAWS over the visual, because
/// that is a fact about pixels and a composed window does not screenshot reliably: a test claiming
/// to read one would be reporting on its own capture stack.
///
/// So this is apparatus, not an assertion. It puts the two layers on screen in the arrangement the
/// design proposes and says what each one should look like, and a person answers in one glance.
///
/// The reading
/// -----------
/// RED on the right and BLUE on the left is the answer PP163 wants: the visual composes through
/// where WPF draws nothing, and WPF's own content is on top of it where it does.
///
/// ALL BLUE, or blue over white, means WPF's window is painting its whole surface and the visual is
/// behind something opaque. The overlay would work and the video would never be seen, which is the
/// outcome that sends PP163 back to the child-HWND option and costs PP10's screen.
///
/// NO RED ANYWHERE with the left half blue means the visual is not composing at all, whatever the
/// compositor said when it accepted the tree.
///
/// What was actually read, 2026-08-23
/// ----------------------------------
/// A FOURTH thing, which none of the three above describes: solid red over the WHOLE client area.
/// No blue block, no text - WPF's content nowhere at all, with the window chrome still DWM's.
///
/// And --topmost read identically. That is the control and it is what makes this a finding rather
/// than a puzzle: both arrangements produce the same pixel. The visual covers the window's content
/// either way.
///
/// PP319 corrected the CAUSE this entry first gave for that. It said the flag was not being
/// honoured. It is: CreateTargetForHwnd's second argument orders the tree against the window's CHILD
/// WINDOWS, and a redirection bitmap is not a child of anything - so a WPF window's own drawing is
/// UNDER the tree in both arrangements, which is documented behaviour and not a defect. The reading
/// is unchanged and the conclusion is stronger: the arrangement PP163 wanted was never available,
/// so no Windows build will start producing it.
///
/// This does not contradict PP281 to PP283. All three measured that the compositor ACCEPTS the tree
/// and all three still hold - "DirectComposition: attached" prints on both runs. None of them
/// measured a pixel, which is the whole reason this window exists. The premise they were built on -
/// that FALSE puts the tree behind the window's content - is accepted by the API and does not
/// materialise here.
///
/// The consequence is the one the ALL BLUE reading was written to name, reached from the opposite
/// side: PP10's XAML overlay would not be seen. Windows 11 build 26200.
///
/// The transparent background is the mechanism
/// -------------------------------------------
/// HwndTarget.BackgroundColor is what makes a WPF window's redirection surface transparent where
/// nothing is drawn, and it is the documented way to put DirectX under WPF content. It is NOT
/// AllowsTransparency, which forces a layered window and turns hardware acceleration off - that
/// would change the thing being measured, and a measurement of a slower path is not this one's.
/// </summary>
public static class DcompDemo
{
    /// <summary>What the swapchain is cleared to. Strong red, so it cannot be mistaken for chrome.</summary>
    public const double FillRed = 0.85;

    /// <summary>And no green or blue in it, so a blend with the overlay would be visible as one.</summary>
    public const double FillGreen = 0.05;

    /// <summary>Likewise.</summary>
    public const double FillBlue = 0.05;

    /// <summary>
    /// Opens the window and returns once it closes.
    /// </summary>
    /// <param name="topmost">
    /// PP163: the control. False is the design's own arrangement and the default, so the reading a
    /// person takes without arguments is still the one this file describes above. True is asked
    /// only to find out whether the flag reaches the compositor at all: the 2026-08-23 reading of
    /// false was solid red over the whole client area - WPF's content nowhere - and if true looks
    /// identical then CreateTargetForHwnd's arrangement is not being honoured on a WPF HWND, which
    /// is a different finding from WPF having drawn nothing.
    /// </param>
    /// <returns>0 where the tree attached, 2 where it did not - the same shape --selftest uses.</returns>
    public static int Run(bool topmost = false)
    {
        using RenderDevice? device =
            ChiakiRender.CreateD3d11(forceSoftware: false) ?? ChiakiRender.CreateD3d11(forceSoftware: true);

        if (device is null)
        {
            Console.Error.WriteLine("no D3D11 device, so there is nothing to compose");
            return 2;
        }

        var window = new Window
        {
            Title = "PP284 - DirectComposition under WPF",
            Width = 900,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            // Transparent, so WPF paints nothing where the overlay is not. An opaque window would
            // hide the visual whatever DirectComposition did, and the demo would answer "no" for a
            // reason that has nothing to do with the question.
            Background = Brushes.Transparent,
            Content = Overlay(),
        };

        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

        // The window's own composition target, which is the half of the mechanism WPF owns. Set
        // before the visual is attached, because the redirection surface is what the visual has to
        // show through and changing it afterwards is a repaint nobody asked for.
        if (HwndSource.FromHwnd(hwnd) is HwndSource source && source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        DcompAttachment? attached = device.AttachDirectComposition(
            hwnd, SwapchainFormat.Rgb10A2, topmost,
            FillRed, FillGreen, FillBlue, out DcompStage stage);

        Console.WriteLine($"DirectComposition: {(attached is null ? $"FAILED at {stage}" : "attached")}");
        Console.WriteLine($"topmost: {topmost} - {(topmost ? "the control; the visual is ASKED to cover WPF" : "the design's arrangement; the visual is asked to sit BEHIND WPF")}");
        Console.WriteLine("Expect RED on the right and BLUE on the left.");
        Console.WriteLine("  red visible  -> WPF composes above the visual, and PP163's option holds");
        Console.WriteLine("  no red       -> WPF's surface is opaque and the video would be hidden");

        if (attached is null)
            return 2;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            attached.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// PP322: the same window with the SECOND visual over the plane, which is the arrangement PP319
    /// chose and the one nobody has looked at.
    ///
    /// PP319 measured that the compositor accepts a container carrying a ten-bit swapchain below and
    /// an eight-bit premultiplied surface above it, and chose on that. That is the same depth PP281
    /// to PP283 reached one layer down, and PP284 then read a pixel none of them had predicted - so
    /// this exists for the same reason that window did, and the choice is not final until it is read.
    ///
    /// The reading
    /// -----------
    /// A GREEN BLOCK inside the red, offset from the corner, is the answer PP319 needs: two visuals
    /// of different formats compose in the order they were given, so an overlay can live above an
    /// HDR video plane and PP10's screen has somewhere to be rebuilt.
    ///
    /// ALL RED, no green anywhere, means the overlay visual is not composing. PP319's choice falls to
    /// SDR on purpose, which is then the only remaining option that keeps PP10's screen at all.
    ///
    /// GREEN WITH NO RED AROUND IT means the plane below is not composing, which would contradict
    /// what PP284 read on this same window and points at the fill rather than at the tree.
    ///
    /// ANY BLUE means WPF's content got above the tree after all, which would re-open PP319 - and
    /// the blue block is on the left of the window for exactly that reason, unchanged from PP284.
    ///
    /// And the half that is not a yes or a no
    /// --------------------------------------
    /// The green block is drawn in two halves: the left one opaque, the right one at HALF alpha,
    /// premultiplied. The right half should read as a green-over-red blend, visibly green and
    /// visibly not the left half. If it reads nearly as red as the surround, the alpha is being
    /// taken twice - which is not an error anywhere, is invisible to every assertion PP319 wrote,
    /// and means an overlay would have to be drawn straight rather than premultiplied. The choice
    /// still holds in that case; the drawing changes.
    /// </summary>
    /// <returns>0 where the two-layer tree attached, 2 where it did not.</returns>
    public static int RunLayers()
    {
        using RenderDevice? device =
            ChiakiRender.CreateD3d11(forceSoftware: false) ?? ChiakiRender.CreateD3d11(forceSoftware: true);

        if (device is null)
        {
            Console.Error.WriteLine("no D3D11 device, so there is nothing to compose");
            return 2;
        }

        var window = new Window
        {
            Title = "PP322 - the overlay above the video plane",
            Width = 900,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Transparent,
            Content = Overlay(),
        };

        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

        if (HwndSource.FromHwnd(hwnd) is HwndSource source && source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Ten bits below and eight above, which is the pairing the choice is about. Asking with one
        // format twice would put a window on screen that demonstrates something easier.
        DcompAttachment? attached = device.AttachLayers(
            hwnd, SwapchainFormat.Rgb10A2, SwapchainFormat.Bgra8,
            FillRed, FillGreen, FillBlue, out LayersStage stage);

        Console.WriteLine($"two-layer tree: {(attached is null ? $"FAILED at {stage}" : "attached")}");
        Console.WriteLine("Expect a GREEN block inside the RED, offset from the corner.");
        Console.WriteLine("  green block      -> the overlay composes above the plane; PP319's choice holds");
        Console.WriteLine("  all red, no green-> the overlay does not compose; the choice falls to SDR on purpose");
        Console.WriteLine("  green, no red    -> the plane below is not composing, which contradicts PP284");
        Console.WriteLine("  any blue         -> WPF got above the tree after all, and PP319 re-opens");
        Console.WriteLine("Its RIGHT half is half-alpha: it should read as green over red, not as red.");
        Console.WriteLine("  nearly red       -> the alpha is taken twice; the choice holds, the drawing changes");

        if (attached is null)
            return 2;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            attached.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// The overlay: opaque on the left, nothing on the right.
    ///
    /// Two halves rather than a border, because the answer is a comparison. A window that is
    /// entirely overlay says nothing about what shows through, and one that is entirely empty says
    /// nothing about what draws on top.
    /// </summary>
    private static UIElement Overlay()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x4E, 0xD8)),
            Child = new TextBlock
            {
                Text = "WPF draws here",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 20,
            },
        };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        // Nothing in column 1, deliberately. That is where the visual has to show through, and
        // putting a label there would be putting WPF content over the very pixels in question.
        return grid;
    }
}
