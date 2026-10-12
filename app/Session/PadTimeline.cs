using System.Diagnostics;
using System.Text;

namespace ChiakiNg.Session;

/// <summary>
/// PP854: what the pad path did, second by second, so a stop has a moment.
///
/// A pad stops driving the console after the window loses focus, and every total this run printed
/// stayed healthy: the pump delivered to the last instant, the sink took 14237 states, the wire
/// took 5608. A total cannot say whether half of it happened before the click and none after.
///
/// THREE SERIES, BECAUSE ONE CAN HIDE ANOTHER. A DualSense reports its sensors hundreds of times a
/// second, so "the pump is alive" can be the sensors alone while the buttons and sticks have
/// stopped. Inputs are what the translation took, sensors are what the fusion took, and states are
/// what reached the wire - a second where the first is zero and the other two are not is the pad's
/// input going quiet beneath a pump that never did.
/// </summary>
public sealed class PadTimeline
{
    /// <summary>The series, in the order a row is printed.</summary>
    public enum Series
    {
        /// <summary>Buttons, sticks and triggers the translation took.</summary>
        Inputs,

        /// <summary>Sensor readings the fusion took.</summary>
        Sensors,

        /// <summary>Feedback states handed to the wire.</summary>
        States,
    }

    /// <summary>An hour, which is longer than any trial; a later second is counted in the last.</summary>
    public const int Seconds = 3600;

    private readonly int[,] counts = new int[3, Seconds];
    private readonly Func<TimeSpan> elapsed;
    private int last = -1;

    /// <param name="clock">Elapsed time since the run began; a stopwatch when omitted.</param>
    public PadTimeline(Func<TimeSpan>? clock = null)
    {
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }

        elapsed = clock;
    }

    /// <summary>Count one of <paramref name="series"/> in the current second.</summary>
    public void Mark(Series series)
    {
        int second = Math.Clamp((int)elapsed().TotalSeconds, 0, Seconds - 1);

        Interlocked.Increment(ref counts[(int)series, second]);
        InterlockedMax(ref last, second);
    }

    /// <summary>How many of <paramref name="series"/> fell in <paramref name="second"/>.</summary>
    public int At(Series series, int second) => Volatile.Read(ref counts[(int)series, second]);

    /// <summary>
    /// The first second from which inputs stay at zero to the end while states keep going - the
    /// moment the pad went quiet under a wire that did not - or null where that never happened.
    /// </summary>
    public int? InputsWentQuiet()
    {
        int end = Volatile.Read(ref last);
        if (end < 0)
            return null;

        int quiet = end + 1;
        while (quiet > 0 && At(Series.Inputs, quiet - 1) == 0)
            quiet--;

        // Never had input, or never went quiet before the end.
        if (quiet == 0 || quiet > end)
            return null;

        for (int second = quiet; second <= end; second++)
        {
            if (At(Series.States, second) > 0)
                return quiet;
        }

        return null;
    }

    /// <summary>One line per series, one number per second, from the first second to the last.</summary>
    public string Render()
    {
        int end = Volatile.Read(ref last);
        var built = new StringBuilder();

        foreach (Series series in Enum.GetValues<Series>())
        {
            built.Append(series.ToString().ToLowerInvariant()).Append(':');

            for (var second = 0; second <= end; second++)
                built.Append(' ').Append(At(series, second));

            built.AppendLine();
        }

        return built.ToString();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen = Volatile.Read(ref target);

        while (value > seen)
        {
            int was = Interlocked.CompareExchange(ref target, value, seen);
            if (was == seen)
                return;

            seen = was;
        }
    }
}
