using System.Globalization;
using System.Text;

namespace ChiakiNg.Session;

/// <summary>
/// PP219: what `ChiakiNg.exe --capture-controller` prints.
///
/// The tool that found the defect this task files, kept rather than thrown away. PP218's
/// `--controllers` answers "is there a pad and what does it say it is"; this answers the question
/// after it, which is the one that was wrong: "does pressing it reach this process at all".
///
/// It is split in two because it prints LIVE. A batch report has a window the person holding the
/// pad cannot see the start of - measured the hard way, over five runs of "press now" where the
/// output only appeared afterwards and a silent result could not be told from a mistimed one. So
/// <see cref="Opening"/> goes out before the listening starts, each token is printed as it arrives,
/// and <see cref="Summary"/> closes it.
///
/// It arms a real <see cref="MappingCapture"/> and re-arms after every token, so one run records a
/// sequence. That is NOT what the mapping screen does - there a capture takes one press and stops -
/// and the difference is the point of a diagnostic: the screen wants one binding, this wants to see
/// everything the pad can send.
/// </summary>
public static class CaptureReport
{
    /// <summary>What is printed when the pad was opened and still sent nothing.</summary>
    public const string Silent = "  (nothing arrived)";

    /// <summary>And when there was no pad to open.</summary>
    public const string NoPad = "  (no pad SDL can map)";

    /// <summary>
    /// The header, printed BEFORE listening so the person pressing knows the window is open.
    /// </summary>
    /// <param name="pad">The pad listened to, or null where there was none.</param>
    /// <param name="opened">
    /// Whether SDL gave a handle. Printed separately from the token count because the two together
    /// are the whole diagnosis: opened and silent is a pad nobody pressed, NOT opened and silent is
    /// this task's defect, and a report showing only tokens cannot tell them apart.
    /// </param>
    public static string Opening(SdlPad? pad, bool opened)
    {
        var report = new StringBuilder();

        if (pad is not SdlPad found)
        {
            report.AppendLine(NoPad);
            return report.ToString();
        }

        report.Append(CultureInfo.InvariantCulture, $"[{found.Index}] {found.Name}");
        report.AppendLine();
        report.Append(CultureInfo.InvariantCulture, $"opened: {opened}");
        report.AppendLine();

        return report.ToString();
    }

    /// <summary>One token, as it is printed the moment it arrives.</summary>
    public static string Live(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return "  " + token;
    }

    /// <summary>
    /// The tail: how many arrived, and the sequence with runs collapsed.
    ///
    /// Collapsed because an analog axis sends its whole travel - one trigger pull was measured at
    /// forty-eight events on a real DualSense, and forty-eight identical lines hide the sequence
    /// rather than showing it. The live lines above are not collapsed, because there the point is
    /// that something is happening at all.
    /// </summary>
    public static string Summary(IReadOnlyList<string> tokens, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var report = new StringBuilder();

        report.Append(
            CultureInfo.InvariantCulture,
            $"{tokens.Count} token(s) in {window.TotalSeconds:0}s");
        report.AppendLine();

        if (tokens.Count == 0)
        {
            report.AppendLine(Silent);
            return report.ToString();
        }

        foreach ((string token, int run) in Runs(tokens))
        {
            report.Append(CultureInfo.InvariantCulture, $"  {token}");
            if (run > 1)
                report.Append(CultureInfo.InvariantCulture, $" x{run}");

            report.AppendLine();
        }

        return report.ToString();
    }

    /// <summary>Consecutive equal tokens, as the token and how many of it there were.</summary>
    public static IReadOnlyList<(string Token, int Run)> Runs(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var runs = new List<(string Token, int Run)>();
        foreach (string token in tokens)
        {
            if (runs.Count > 0 && string.Equals(runs[^1].Token, token, StringComparison.Ordinal))
                runs[^1] = (token, runs[^1].Run + 1);
            else
                runs.Add((token, 1));
        }

        return runs;
    }
}

/// <summary>
/// PP219: where the Qt client opens the device, which is why it never meets this.
/// </summary>
public static class PadOpenSource
{
    /// <summary>The Qt client's controller code.</summary>
    public static string? Locate() => MappingSource.Locate();

    /// <summary>
    /// Whether the Qt client still opens the device as part of constructing a Controller - so the
    /// question "who opens it" never comes up there, and arrives here as a pad that does nothing.
    /// </summary>
    public static bool TheQtClientStillOpensOnConstruction(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int constructor = source.IndexOf("Controller::Controller(", StringComparison.Ordinal);
        if (constructor < 0)
            return false;

        int opened = source.IndexOf(
            "controller = SDL_GameControllerOpen(i);", constructor, StringComparison.Ordinal);
        if (opened < 0)
            return false;

        // Inside the constructor rather than merely after it: the destructor's close is the next
        // landmark, and an open past that one would be somebody else's.
        int destructor = source.IndexOf("Controller::~Controller(", StringComparison.Ordinal);
        return destructor < 0 || opened < destructor;
    }

    /// <summary>And whether it still closes what it opened, on the way out.</summary>
    public static bool ItStillClosesWhatItOpened(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Contains("SDL_GameControllerClose(controller);", StringComparison.Ordinal);
    }
}
