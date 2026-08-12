using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PresentPath;

/// <summary>
/// A present path that presents nothing still produces timings, so the run has to be looked at
/// and not only measured. Two captures are taken of every run, and the difference between them
/// is itself a result:
///
///   *-wpf.png    - RenderTargetBitmap, i.e. what WPF believes it composed.
///   *-screen.png - BitBlt from the screen, i.e. what is actually on the glass.
///
/// For the shared-surface path these agree. For the airspace path the WPF capture is empty while
/// the screen capture shows the frame, because a child HWND is not part of WPF's composition -
/// which is exactly the property the renderer task is weighing, made visible rather than argued.
/// </summary>
internal static class Capture
{
    public static void Save(Window window, IPresenter presenter, Args args)
    {
        string stem = Path.GetFileNameWithoutExtension(args.Out);
        try
        {
            SaveWpfComposition(window, stem + "-wpf.png");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"wpf capture failed: {ex.Message}");
        }
        try
        {
            SaveScreen(window, stem + "-screen.png");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"screen capture failed: {ex.Message}");
        }
    }

    private static void SaveWpfComposition(Window window, string file)
    {
        int w = (int)Math.Max(1, window.ActualWidth);
        int h = (int)Math.Max(1, window.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        encoder.Save(fs);
        Console.WriteLine($"wpf capture   : {Path.GetFullPath(file)}");
    }

    private static void SaveScreen(Window window, string file)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out RECT r))
            throw new InvalidOperationException("GetWindowRect failed");

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0)
            throw new InvalidOperationException($"window rect is empty ({w}x{h})");

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr bmp = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, bmp);
        try
        {
            if (!BitBlt(memDc, 0, 0, w, h, screenDc, r.Left, r.Top, SRCCOPY))
                throw new InvalidOperationException($"BitBlt failed: {Marshal.GetLastWin32Error()}");

            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = File.Create(file);
            encoder.Save(fs);
            Console.WriteLine($"screen capture: {Path.GetFullPath(file)}");
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(bmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
}
