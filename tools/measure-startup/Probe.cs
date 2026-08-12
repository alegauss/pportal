using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MeasureStartup;

/// <summary>Result of one cold-start run. Every field is either measured or explicitly absent.</summary>
internal readonly record struct StartupResult(
    bool WindowAppeared,
    double ToWindowMs,
    double ToResponsiveMs,
    long WorkingSetBytes,
    long PrivateBytes,
    string? Failure,
    string WindowTitle = "")
{
    public static StartupResult Failed(string why) => new(false, 0, 0, 0, 0, why);
}

/// <summary>
/// Launches a build and times it to its first visible top-level window, then to that window being
/// responsive, then reads its memory once it has settled.
///
/// "First visible window" is what is measured, and it is named that rather than "cold start to the
/// console list", which is what §PP46 asks for. The distinction is deliberate: reaching the console
/// list is an application-level event and this harness is outside the application, so claiming it
/// would be claiming more than was observed. The window becoming responsive is the closest external
/// proxy - the UI thread is pumping messages, which for this application means the first screen is
/// up. If a stronger mark is wanted later, the app has to emit it.
/// </summary>
internal static class Probe
{
    public static StartupResult Run(string exePath, int timeoutMs, int idleSettleMs, string arguments = "")
    {
        if (!File.Exists(exePath))
            return StartupResult.Failed($"not found: {exePath}");

        var psi = new ProcessStartInfo(exePath)
        {
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exePath))!,
            UseShellExecute = false,
            // Redirected and drained rather than inherited: the build under test writes to stdout,
            // and letting that interleave with the measurement makes the report unreadable. Drained
            // asynchronously because a child that fills an unread pipe blocks, which would show up
            // here as a slow cold start.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process? p = null;
        try
        {
            var sw = Stopwatch.StartNew();
            p = Process.Start(psi);
            if (p is null)
                return StartupResult.Failed("Process.Start returned null");
            p.OutputDataReceived += static (_, _) => { };
            p.ErrorDataReceived += static (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            double toWindow = -1, toResponsive = -1;
            IntPtr window = IntPtr.Zero;

            while (sw.Elapsed.TotalMilliseconds < timeoutMs)
            {
                if (p.HasExited)
                    return StartupResult.Failed($"process exited early with code {p.ExitCode}");

                if (window == IntPtr.Zero)
                {
                    window = FindVisibleTopLevelWindow(p.Id);
                    if (window != IntPtr.Zero)
                        toWindow = sw.Elapsed.TotalMilliseconds;
                }
                else if (IsResponsive(window))
                {
                    toResponsive = sw.Elapsed.TotalMilliseconds;
                    break;
                }
                Thread.Sleep(5);
            }

            if (window == IntPtr.Zero)
                return StartupResult.Failed($"no visible top-level window within {timeoutMs}ms");
            if (toResponsive < 0)
                return StartupResult.Failed($"window appeared at {toWindow:F0}ms but never became responsive");

            // Let it settle before reading memory: the working set right after the first paint is
            // still climbing, and a number taken there measures the sampling instant.
            Thread.Sleep(idleSettleMs);
            p.Refresh();
            long ws = p.WorkingSet64;
            long priv = p.PrivateMemorySize64;

            // The title is reported so a reader can tell the console list from an error dialog. A
            // modal "failed to load settings" box is a visible top-level window too, and timing one
            // would produce a cold-start number for a build that never started.
            return new StartupResult(true, toWindow, toResponsive, ws, priv, null, WindowTitle(window));
        }
        catch (Exception ex)
        {
            return StartupResult.Failed(ex.Message);
        }
        finally
        {
            TryKill(p);
        }
    }

    private static void TryKill(Process? p)
    {
        if (p is null)
            return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch
        {
            // Nothing useful to do: the measurement is already taken and a leaked process is the
            // caller's to notice. Swallowing here keeps a teardown failure from masking a result.
        }
        finally
        {
            p.Dispose();
        }
    }

    private static IntPtr FindVisibleTopLevelWindow(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != (uint)pid)
                return true;
            // Skip zero-sized and message-only windows: a splash-less Qt app creates helper windows
            // that are visible but have no client area, and timing to one of those would report a
            // cold start that nothing was drawn in.
            if (!GetClientRect(hwnd, out RECT r) || r.Right - r.Left < 200 || r.Bottom - r.Top < 200)
                return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsResponsive(IntPtr hwnd) =>
        SendMessageTimeout(hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, 250, out _) != IntPtr.Zero;

    public static string WindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam,
        uint flags, uint timeoutMs, out IntPtr result);
}
