using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the five frame stages, separate from each other and from the handoff.
///
/// PP179 found that the port carried these fields and could not fill them, so a session wrote five
/// zeros whatever it did. The C's fixture pushes one distinguishable value per stage precisely
/// because "a stage filed under another stage's name" is the defect it exists to catch - and a row
/// of zeros cannot be caught by it, which is worse than being caught.
///
/// These are test/sessionbaseline.c's two stage cases, run from the managed side now that there is
/// a way to push one.
/// </summary>
public class BaselineStageTests
{
    private static readonly FrameStageTimer[] Stages = Enum.GetValues<FrameStageTimer>();

    /// <summary>
    /// One stage takes the sample and the other four do not. Every stage is tried, because the
    /// mislabelling this guards against is a selector bound to the wrong member - which shows on
    /// one stage and not on the rest.
    /// </summary>
    [Theory]
    [InlineData(FrameStageTimer.Receive)]
    [InlineData(FrameStageTimer.Reorder)]
    [InlineData(FrameStageTimer.Reassemble)]
    [InlineData(FrameStageTimer.Correct)]
    [InlineData(FrameStageTimer.Decode)]
    public void ASampleLandsInOneStageAndNoOther(FrameStageTimer pushed)
    {
        using var baseline = new SessionBaseline();

        baseline.PushStage(pushed, 2000);

        foreach (FrameStageTimer stage in Stages)
        {
            ulong expected = stage == pushed ? 1ul : 0ul;
            Assert.Equal(expected, baseline.StageSamples(stage));
        }
    }

    /// <summary>
    /// And it does not land in the handoff. The present stage IS the handoff and is not a sixth
    /// accumulator - a caller that pushed it as a stage would count it twice, once there and once
    /// in the latency estimate.
    /// </summary>
    [Fact]
    public void AStageIsNotTheHandoff()
    {
        using var baseline = new SessionBaseline();

        baseline.PushStage(FrameStageTimer.Reorder, 2000);

        Assert.Equal(1ul, baseline.StageSamples(FrameStageTimer.Reorder));
        Assert.Equal(0ul, baseline.HandoffSamples);
    }

    /// <summary>
    /// A stage is not in the latency estimate either. That sum is input, network and handoff; a
    /// reorder queue counted there as well would inflate the one number a session is judged by.
    /// </summary>
    [Fact]
    public void AStageIsNotInTheLatencyEstimate()
    {
        using var baseline = new SessionBaseline();

        baseline.PushStage(FrameStageTimer.Reorder, 2000);

        Assert.Equal(0ul, baseline.LatencyEstimateUs);

        // And the three that ARE in it move it, so the zero above is the stage being excluded
        // rather than the estimate being inert.
        baseline.PushHandoff(900);
        Assert.True(baseline.LatencyEstimateUs > 0);
    }

    /// <summary>The handoff and the input-to-wire are separate from each other too.</summary>
    [Fact]
    public void TheHandoffAndTheInputAreSeparate()
    {
        using var baseline = new SessionBaseline();

        baseline.PushHandoff(5000);
        Assert.Equal(1ul, baseline.HandoffSamples);

        baseline.PushInputToWire(70);
        Assert.Equal(1ul, baseline.HandoffSamples);
    }

    /// <summary>
    /// A selector nobody meant is ignored rather than folded into a neighbour. Putting it in the
    /// first stage would be exactly the mislabelling the stages are kept apart to prevent.
    /// </summary>
    [Fact]
    public void AnUnknownStageIsIgnoredRatherThanFolded()
    {
        using var baseline = new SessionBaseline();

        baseline.PushStage((FrameStageTimer)99, 2000);
        baseline.PushStage((FrameStageTimer)(-1), 2000);

        foreach (FrameStageTimer stage in Stages)
            Assert.Equal(0ul, baseline.StageSamples(stage));

        Assert.Equal(0ul, baseline.HandoffSamples);
    }

    /// <summary>
    /// And the line now carries what was pushed, which is the point of the whole seam: the field
    /// set (PP179) had these keys and zeros behind them.
    /// </summary>
    [Fact]
    public void TheStagesReachTheLedgerLine()
    {
        using var baseline = new SessionBaseline();

        // The C fixture's own numbers, one distinguishable value per stage.
        baseline.PushStage(FrameStageTimer.Receive, 40);
        baseline.PushStage(FrameStageTimer.Receive, 60);
        baseline.PushStage(FrameStageTimer.Reorder, 1100);
        baseline.PushStage(FrameStageTimer.Reassemble, 3000);
        baseline.PushStage(FrameStageTimer.Correct, 250);
        baseline.PushStage(FrameStageTimer.Decode, 4200);
        baseline.PushStage(FrameStageTimer.Decode, 9000);

        // The other two accumulators, so the claim below is about a FULLY filled session. Without
        // them the handoff and the input-to-wire legitimately read zero samples and the check
        // would fail against correct code - which is how the first version of it did.
        baseline.PushHandoff(900);
        baseline.PushInputToWire(400);

        string line = baseline.Format();

        Assert.Contains("\"receive\":", line, StringComparison.Ordinal);

        // Nothing left at zero samples, which is exactly what the port wrote for five of these
        // fields before this seam existed.
        Assert.DoesNotContain("\"samples\":0", line, StringComparison.Ordinal);

        Assert.Equal(2ul, baseline.StageSamples(FrameStageTimer.Receive));
        Assert.Equal(2ul, baseline.StageSamples(FrameStageTimer.Decode));
    }
}
