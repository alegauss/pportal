using System.Runtime.InteropServices;
using System.Windows;

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

        base.OnStartup(e);
    }
}
