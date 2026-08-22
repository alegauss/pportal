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
    /// <returns>0 where the tree attached, 2 where it did not - the same shape --selftest uses.</returns>
    public static int Run()
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
            hwnd, SwapchainFormat.Rgb10A2, topmost: false,
            FillRed, FillGreen, FillBlue, out DcompStage stage);

        Console.WriteLine($"DirectComposition: {(attached is null ? $"FAILED at {stage}" : "attached")}");
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
