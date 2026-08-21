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
    /// PP220: what an axis actually read, over a run.
    ///
    /// The question a token cannot answer. SDL raises an axis event whenever the value CHANGES, by
    /// one or by twenty thousand, so a stream of them proves the stick is not still and proves
    /// nothing about where it is resting. Noise around centre and a stick pinned off centre produce
    /// the same flood; only the range tells them apart.
    /// </summary>
    /// <param name="token">The axis's token, e.g. <c>a1</c>.</param>
    /// <param name="low">The lowest value seen.</param>
    /// <param name="high">The highest.</param>
    /// <param name="samples">How many events it took.</param>
    public static string AxisRange(string token, short low, short high, int samples)
    {
        ArgumentNullException.ThrowIfNull(token);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"  {token}  {low}..{high}  ({AxisRanges.Extent(low, high):P1} of full scale, {samples} sample(s))");
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
/// PP220: where each axis rested, gathered as the events go past.
///
/// Separate from the capture because it answers a different question. The capture asks "what should
/// this press be called" and binds on any motion at all; this asks "where is this stick actually
/// sitting", which is the only thing that tells ordinary noise from a stick resting off centre.
/// Both watch the same stream and neither needs the other.
/// </summary>
public sealed class AxisRanges
{
    private readonly Dictionary<string, (short Low, short High, int Samples)> seen =
        new(StringComparer.Ordinal);

    /// <summary>Full scale, as SDL_JoyAxisEvent.value's own type gives it.</summary>
    public const double FullScale = 32768.0;

    /// <summary>
    /// Offers one event. Anything that is not axis motion is ignored, so a caller can hand it the
    /// whole stream.
    /// </summary>
    public void Observe(SdlEvent ev)
    {
        if (ev.Type != Gamepads.EventType.JoyAxisMotion)
            return;

        string axis = "a" + ev.Index;

        (short low, short high, int samples) = seen.TryGetValue(axis, out var already)
            ? already
            : (short.MaxValue, short.MinValue, 0);

        seen[axis] = (Math.Min(low, ev.AxisValue), Math.Max(high, ev.AxisValue), samples + 1);
    }

    /// <summary>Every axis that moved, by token, in token order.</summary>
    public IReadOnlyList<(string Axis, short Low, short High, int Samples)> Seen()
        => [.. seen
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value.Low, entry.Value.High, entry.Value.Samples))];

    /// <summary>
    /// The furthest from centre an axis got, as a fraction of full scale.
    ///
    /// The number the question turns on, and deliberately not a verdict: this port does not decide
    /// what counts as a worn stick. A few tenths of a percent is the noise every analog axis has;
    /// a reading that stays in the tens of percent with nobody touching it is a different thing,
    /// and the person holding the pad is better placed to say which they are looking at.
    /// </summary>
    public static double Extent(short low, short high)
        => Math.Max(Math.Abs((double)low), Math.Abs((double)high)) / FullScale;
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
