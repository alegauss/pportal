using System.Runtime.InteropServices;
using System.Windows;
using ChiakiNg.Session;

namespace ChiakiNg;

/// <summary>
/// PP1: the application object. PP2 added the one thing it does before drawing anything.
///
/// This host exists so that every screen in Block D is filed against something that already
/// builds. The alternative - a first screen that carries the project, the manifest and the
/// packaging on its back - cannot be reviewed as a screen at all, because a reviewer cannot tell
/// which half is wrong.
///
/// The Qt client stays until Block D empties. Two executables in one tree is the ordinary shape
/// of a port, and the one that is not shipped yet is the one being written.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// A WinExe is built with no console, so writing to stdout from one goes nowhere by default.
    /// Attaching to the parent's console is what makes `ChiakiNg.exe --selftest` print into the
    /// shell that launched it; a run with no parent console (double-clicked) simply fails this
    /// call and the selftest still sets the exit code.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    /// <summary>
    /// Make Console.Write actually reach somewhere. AttachConsole alone is not enough and that
    /// is measured, not assumed: with only the attach, `ChiakiNg.exe --selftest` exited 0 and
    /// printed nothing at all. The process was started with no console, so the CLR had already
    /// bound Console.Out to a null writer, and attaching one afterwards does not rebind it.
    ///
    /// So the standard handle is reopened by hand. A run with no parent console - double-clicked,
    /// or launched detached - fails the attach, gets an invalid handle, and falls through to
    /// leaving Console.Out as it was: the selftest still runs and still sets the exit code, which
    /// is the half a caller can read without a console anyway.
    /// </summary>
    private static void ReopenStdOut()
    {
        // The inherited handle first, and AttachConsole only if there is none.
        //
        // The order is the whole of it, and it cost two wrong versions. AttachConsole does not
        // add a console beside the standard handles - it REPLACES them with the console's. So a
        // run whose stdout was already a file or a pipe loses that redirect the moment it is
        // called, and `ChiakiNg.exe --selftest > out.txt` writes a zero-byte file and exits 0.
        // A redirected caller already has somewhere to write; only a bare double-click or a
        // prompt with no redirect needs a console attached at all.
        if (!Bind())
        {
            AttachConsole(AttachParentProcess);
            Bind();
        }

        static bool Bind()
        {
            try
            {
                Stream stdout = Console.OpenStandardOutput();
                // Stream.Null is what a process with no usable standard handle gets back.
                // Writing to it is legal and silent, so it is checked rather than caught.
                if (stdout == Stream.Null)
                    return false;
                var writer = new StreamWriter(stdout) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
                return true;
            }
            catch (IOException)
            {
                // No usable handle. The exit code is still the half a caller can read.
                return false;
            }
        }
    }

    /// <summary>
    /// PP218: `--controllers`, which prints what SDL sees and exits.
    ///
    /// Its own flag rather than a corner of the selftest, because the selftest is a gate that must
    /// pass on a build machine with no pad, and this is the opposite - a thing you run BECAUSE a
    /// pad is plugged in, whose whole output is the device it found.
    ///
    /// The enumeration goes through the thread rather than beside it: SDL's device tables belong to
    /// whichever thread called SDL_Init, which is what <see cref="SdlThread"/> exists to own.
    /// </summary>
    private static int Controllers()
    {
        using var sdl = new SdlThread();

        SdlStart start = sdl.Start(TimeSpan.FromSeconds(10));
        if (start != SdlStart.Started)
        {
            Console.Error.WriteLine($"SDL did not start ({start}): {sdl.Error}");
            return 1;
        }

        string report = "";
        if (!sdl.Invoke(
                () => report = PadReport.Format(
                    Gamepads.NumJoysticks(), Gamepads.Pads(), Gamepads.LinkedVersion()),
                TimeSpan.FromSeconds(10)))
        {
            Console.Error.WriteLine("the SDL thread did not answer");
            return 1;
        }

        Console.Write(report);
        sdl.Stop(TimeSpan.FromSeconds(10));
        return 0;
    }

    /// <summary>
    /// PP219: `--capture-controller`, which opens the first mappable pad and prints what pressing
    /// it produces.
    ///
    /// The tool that found the defect PP219 files. Opening and closing both happen on the SDL
    /// thread, because the handle belongs to whichever thread called SDL_Init - the same rule as
    /// everything else in <see cref="Gamepads"/>.
    /// </summary>
    private static int CaptureController(TimeSpan window, bool analog)
    {
        // OFF unless asked for, which is the opposite of what a diagnostic usually does. Measured
        // on a DualSense: twenty seconds with it on produced 1684 tokens, one unbroken run of 798
        // being the left stick's Y axis alone, and the eight deliberate presses could not be found
        // by eye. Showing everything here shows nothing.
        var arm = new MappingCapture { AllowAnalogStick = analog };
        var taken = new List<string>();
        var ranges = new AxisRanges();
        object gate = new();

        // The SDL thread ENQUEUES and the main thread prints. SdlThread's own note says the
        // callback runs between two polls and must not block, and writing to a console is I/O -
        // so the token crosses a queue rather than reaching stdout on the thread that owns SDL.
        var pending = new System.Collections.Concurrent.ConcurrentQueue<string>();

        using var sdl = new SdlThread(ev =>
        {
            string? token;
            lock (gate)
            {
                token = arm.Offer(ev);

                // Re-armed, so one run records a sequence. The screen does not do this.
                if (token is not null)
                    arm.Arm();

                // The RANGE is kept whatever the capture did with it, because the question it
                // answers - is this stick resting off centre, or merely not still - is about the
                // values and not about the bindings.
                ranges.Observe(ev);
            }

            if (token is not null)
                pending.Enqueue(token);
        });

        SdlStart start = sdl.Start(TimeSpan.FromSeconds(10));
        if (start != SdlStart.Started)
        {
            Console.Error.WriteLine($"SDL did not start ({start}): {sdl.Error}");
            return 1;
        }

        SdlPad? pad = null;
        IntPtr handle = IntPtr.Zero;

        sdl.Invoke(
            () =>
            {
                pad = Gamepads.Pads().FirstOrDefault();
                if (pad is SdlPad found)
                    handle = Gamepads.OpenController(found.Index);
            },
            TimeSpan.FromSeconds(10));

        lock (gate)
            arm.Arm();

        // Before the listening, not after it: a window whose start nobody can see is one where a
        // silent result cannot be told from a mistimed press.
        Console.Write(CaptureReport.Opening(pad, handle != IntPtr.Zero));
        Console.WriteLine($"listening for {window.TotalSeconds:0}s - press the pad");
        Console.Out.Flush();

        DateTime until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
        {
            while (pending.TryDequeue(out string? token))
            {
                taken.Add(token);
                Console.WriteLine(CaptureReport.Live(token));
                Console.Out.Flush();
            }

            Thread.Sleep(20);
        }

        while (pending.TryDequeue(out string? last))
            taken.Add(last);

        Console.Write(CaptureReport.Summary(taken, window));

        // The axis ranges last, and whether or not the capture was allowed to bind one: a stick
        // that never became a token still says here where it was resting.
        IReadOnlyList<(string Axis, short Low, short High, int Samples)> seen;
        lock (gate)
            seen = ranges.Seen();

        if (seen.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("axes that moved:");
            foreach ((string axis, short low, short high, int samples) in seen)
                Console.WriteLine(CaptureReport.AxisRange(axis, low, high, samples));
        }

        // And where they are NOW, which no event can say: SDL raises one when the value CHANGES,
        // so a stick resting off centre and still is invisible to everything above this line.
        if (handle != IntPtr.Zero)
        {
            var resting = new List<string>();
            sdl.Invoke(
                () =>
                {
                    foreach ((int axis, string name) in Gamepads.ControllerAxis.All)
                    {
                        resting.Add(RestingAxes.Line(
                            name,
                            Gamepads.AxisNow(handle, axis),
                            Gamepads.ControllerAxis.IsTrigger(axis)));
                    }
                },
                TimeSpan.FromSeconds(10));

            Console.WriteLine();
            Console.WriteLine("resting now:");
            foreach (string line in resting)
                Console.WriteLine(line);
        }

        sdl.Invoke(() => Gamepads.CloseController(handle), TimeSpan.FromSeconds(10));
        sdl.Stop(TimeSpan.FromSeconds(10));
        return 0;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            // Environment.Exit and not Shutdown(): base.OnStartup has not run, so StartupUri has
            // not opened MainWindow, and there is no message loop to unwind. Returning instead
            // would open the window behind the test output.
            Environment.Exit(SelfTest.Run());
        }

        if (e.Args.Any(a => string.Equals(a, "--controllers", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            Environment.Exit(Controllers());
        }

        if (e.Args.Any(a => string.Equals(a, "--capture-controller", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();

            // --analog is opt-in: see the note on CaptureController for the measurement that made
            // it so. The axis RANGES are printed either way, because a stick that never became a
            // token still says where it was resting.
            bool analog = e.Args.Any(a => string.Equals(a, "--analog", StringComparison.OrdinalIgnoreCase));
            Environment.Exit(CaptureController(TimeSpan.FromSeconds(20), analog));
        }

        base.OnStartup(e);
    }
}
