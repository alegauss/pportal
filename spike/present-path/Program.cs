using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace PresentPath;

/// <summary>
/// PP43: measure the two present paths before the renderer task chooses one.
///
/// Usage: present-path --path hwnd|d3dimage [--frames 600] [--warmup 60]
///                     [--width 1920] [--height 1080] [--out report.json]
///
/// One path per process, so neither warms the other up. Two numbers come out of each run: what
/// the presentation step costs on the render thread, and the cadence the path actually achieves.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] argv)
    {
        var args = Args.Parse(argv);
        if (args is null)
        {
            Console.Error.WriteLine("usage: present-path --path hwnd|d3dimage [--frames N] [--warmup N] [--width W] [--height H] [--out FILE]");
            return 2;
        }

        IPresenter presenter = args.Path == "hwnd"
            ? new HwndPathPresenter()
            : new D3DImagePathPresenter();

        var present = new Stats("present_us");
        var cadence = new Stats("frame_to_frame_us");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window
        {
            Title = $"present-path {args.Path}",
            Width = args.Width / 2.0,
            Height = args.Height / 2.0,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Black,
            Content = presenter.Element,
        };

        int frame = 0;
        long lastTick = 0;
        string? failure = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        void OnRendering(object? sender, EventArgs e)
        {
            try
            {
                double us = presenter.PresentOneFrame(frame);
                long now = clock.ElapsedTicks;
                if (frame >= args.Warmup)
                {
                    present.Push(us);
                    if (lastTick != 0)
                        cadence.Push((now - lastTick) * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
                lastTick = now;
                frame++;

                if (frame >= args.Warmup + args.Frames)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    Capture.Save(window, presenter, args);
                    window.Close();
                    app.Shutdown();
                }
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                CompositionTarget.Rendering -= OnRendering;
                window.Close();
                app.Shutdown();
            }
        }

        window.Loaded += (_, _) =>
        {
            try
            {
                presenter.Initialise(args.Width, args.Height);
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                window.Close();
                app.Shutdown();
            }
        };

        // HwndHost builds its child HWND when it is loaded into the visual tree, which happens
        // before the window's own Loaded fires - so initialising from Loaded is enough for both
        // paths, and Initialise throws rather than guesses if that ever stops being true.
        app.Run(window);
        presenter.Dispose();

        if (failure is not null)
        {
            Console.Error.WriteLine($"FAILED ({args.Path}): {failure}");
            return 1;
        }

        Console.WriteLine($"path      : {args.Path} - {presenter.Describe()}");
        Console.WriteLine($"frames    : {present.Count} measured, {args.Warmup} warmup discarded, {args.Width}x{args.Height}");
        Console.WriteLine(present.ToString());
        Console.WriteLine(cadence.ToString());

        Report.Write(args, presenter, present, cadence);
        return 0;
    }
}

internal sealed record Args(string Path, int Frames, int Warmup, int Width, int Height, string Out)
{
    public static Args? Parse(string[] a)
    {
        string path = "", outFile = "";
        int frames = 600, warmup = 60, width = 1920, height = 1080;
        for (int i = 0; i < a.Length - 1; i++)
        {
            switch (a[i])
            {
                case "--path": path = a[++i]; break;
                case "--frames": frames = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--warmup": warmup = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--width": width = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--height": height = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--out": outFile = a[++i]; break;
            }
        }
        if (path != "hwnd" && path != "d3dimage")
            return null;
        if (outFile.Length == 0)
            outFile = $"present-path-{path}.json";
        return new Args(path, frames, warmup, width, height, outFile);
    }
}
