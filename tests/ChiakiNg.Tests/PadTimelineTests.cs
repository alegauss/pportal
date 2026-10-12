using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP854: the pad path second by second, so a stop that a person can name has a moment in the run.
///
/// The live symptom is a pad that stops driving the console after the window loses focus while
/// every total stays healthy. These hold the instrument that can say when: what each second holds,
/// and the verdict that inputs went quiet while the wire did not.
/// </summary>
public class PadTimelineTests
{
    private sealed class Clock
    {
        public TimeSpan Now { get; set; }
    }

    private static (PadTimeline Timeline, Clock Clock) Make()
    {
        var clock = new Clock();
        return (new PadTimeline(() => clock.Now), clock);
    }

    /// <summary>Each mark lands in the second it happened in, per series.</summary>
    [Fact]
    public void AMarkLandsInItsSecond()
    {
        (PadTimeline timeline, Clock clock) = Make();

        timeline.Mark(PadTimeline.Series.Inputs);
        clock.Now = TimeSpan.FromMilliseconds(1500);
        timeline.Mark(PadTimeline.Series.Inputs);
        timeline.Mark(PadTimeline.Series.States);

        Assert.Equal(1, timeline.At(PadTimeline.Series.Inputs, 0));
        Assert.Equal(1, timeline.At(PadTimeline.Series.Inputs, 1));
        Assert.Equal(1, timeline.At(PadTimeline.Series.States, 1));
        Assert.Equal(0, timeline.At(PadTimeline.Series.Sensors, 1));

        Assert.Equal(
            "inputs: 1 1" + Environment.NewLine + "sensors: 0 0" + Environment.NewLine + "states: 0 1" + Environment.NewLine,
            timeline.Render());
    }

    /// <summary>
    /// THE SYMPTOM'S SHAPE: inputs stop at a second while sensors and states go on. That second is
    /// the verdict, which is what the run prints before the rows.
    /// </summary>
    [Fact]
    public void InputsGoingQuietUnderALiveWireIsNamed()
    {
        (PadTimeline timeline, Clock clock) = Make();

        for (var second = 0; second < 10; second++)
        {
            clock.Now = TimeSpan.FromSeconds(second);

            if (second < 4)
                timeline.Mark(PadTimeline.Series.Inputs);

            timeline.Mark(PadTimeline.Series.Sensors);
            timeline.Mark(PadTimeline.Series.States);
        }

        Assert.Equal(4, timeline.InputsWentQuiet());
    }

    /// <summary>
    /// And no verdict where nothing stopped, where nothing ever came in, or where the wire went
    /// quiet with the input - the last is a session ending, not the defect.
    /// </summary>
    [Fact]
    public void NoVerdictWhereNothingStoppedUnderTheWire()
    {
        (PadTimeline steady, Clock steadyClock) = Make();
        for (var second = 0; second < 5; second++)
        {
            steadyClock.Now = TimeSpan.FromSeconds(second);
            steady.Mark(PadTimeline.Series.Inputs);
            steady.Mark(PadTimeline.Series.States);
        }

        Assert.Null(steady.InputsWentQuiet());

        (PadTimeline untouched, Clock untouchedClock) = Make();
        untouchedClock.Now = TimeSpan.FromSeconds(3);
        untouched.Mark(PadTimeline.Series.States);

        Assert.Null(untouched.InputsWentQuiet());

        (PadTimeline ended, Clock endedClock) = Make();
        ended.Mark(PadTimeline.Series.Inputs);
        ended.Mark(PadTimeline.Series.States);
        endedClock.Now = TimeSpan.FromSeconds(3);
        ended.Mark(PadTimeline.Series.Sensors);

        Assert.Null(ended.InputsWentQuiet());

        Assert.Equal(string.Empty, new PadTimeline().Render().Replace("inputs:", "").Replace("sensors:", "").Replace("states:", "").Trim());
    }
}
