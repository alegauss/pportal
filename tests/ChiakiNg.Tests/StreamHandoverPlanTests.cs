using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP752, under PP707: the handoff decided, so PP696's commit has something to aim at.
///
/// PP707's third criterion holds a decision rather than code, and the commit that edits the C waits
/// on PP707 - so the decision has to exist first. These hold it to session.c, because a plan naming
/// a call that moved would send that commit at a line which is not there.
/// </summary>
public class StreamHandoverPlanTests(ITestOutputHelper output)
{
    private static string? Session()
    {
        string? path = StreamHandoverPlanSource.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// EXACTLY ONE OF THE SEVEN BECOMES MANAGED, which is the decision in one number.
    ///
    /// The port has a counterpart for the run and for nothing else on that thread: the handshake
    /// key, the ecdh pair and the three mutex calls are the session's own.
    /// </summary>
    [Fact]
    public void OnlyTheRunBecomesManaged()
    {
        output.WriteLine(string.Join(
            "\n", StreamHandoverPlan.Decisions.Select(one => $"{one.Step,-14} {one.Fate}")));

        Assert.Equal([HandoverStep.Run], StreamHandoverPlan.Managed);

        // Every step of PP28's order has a decision, and no decision names a step it does not have.
        Assert.Equal(
            [.. SessionStreamHandover.Order],
            [.. StreamHandoverPlan.Decisions.Select(one => one.Step)]);
    }

    /// <summary>Every decision says what decides it, because a fate with no reason is a guess.</summary>
    [Fact]
    public void EveryDecisionGivesAReason()
        => Assert.All(
            StreamHandoverPlan.Decisions,
            one => Assert.False(string.IsNullOrWhiteSpace(one.Why)));

    /// <summary>
    /// THE CALL THE PLAN REPLACES IS STILL THE ONE SESSION.C MAKES.
    ///
    /// Named by its text, so a signature that moved upstream would leave the commit editing a line
    /// that is not there - and the plan is the only thing that would have known.
    /// </summary>
    [Fact]
    public void TheReplacedCallIsStillInTheC()
    {
        if (Session() is not { } source)
            return;

        // PP758: or has been replaced, which is this plan carried out. The plan aims a commit at a
        // line, so both "the line moved" and "the line is gone" are answers - and only one of them
        // means the plan failed.
        if (FramePathConsumers.SessionShape() != ConsumerShape.Asking)
        {
            Assert.False(StreamHandoverPlanSource.TheReplacedCallIsStillThere(source));
            Assert.True(
                FramePathConsumers.WasActuallyRead(ConsumerKind.Session, source),
                "session.c makes no such call, and holds none of what survives the flip either");

            return;
        }

        Assert.True(
            StreamHandoverPlanSource.TheReplacedCallIsStillThere(source),
            $"session.c no longer makes {StreamHandoverPlan.ReplacedCall}");
    }

    /// <summary>
    /// AND IT IS STILL UNLOCKED ACROSS, which is why six of the seven stay in the C.
    ///
    /// The plan's whole argument is that the run happens with the state mutex released, because
    /// ctrl's thread, the stop path and every handler take it. A replacement that ran under the
    /// lock would be a session nothing could stop - correct-looking until somebody quits one.
    /// </summary>
    [Fact]
    public void TheRunIsStillUnlockedAcross()
    {
        // PP28's own model, which is a claim about the port and holds on either shape.
        Assert.False(SessionStreamHandover.HoldsTheStateMutex(HandoverStep.Run));

        if (Session() is not { } source)
            return;

        // PP758: the bracket is a property of a call that is there. Once the call has gone the
        // question is answered by the run that replaced it, and asserting the bracket would be
        // asking session.c about a line PP696 removed.
        if (FramePathConsumers.SessionShape() != ConsumerShape.Asking)
        {
            Assert.Equal([HandoverStep.Run], StreamHandoverPlan.Managed);
            return;
        }

        Assert.True(
            StreamHandoverPlanSource.TheRunIsStillUnlockedAcross(source),
            "the run is no longer between an unlock and a lock, so the plan is wrong about the one thing it is for");
    }

    /// <summary>
    /// The replacement blocks, which is what keeps the edit to one line.
    ///
    /// Steps five to seven run on the same thread after the call returns, so a replacement that
    /// returned early would run them while the stream was still going.
    /// </summary>
    [Fact]
    public void TheReplacementBlocksForTheSessionsLength()
    {
        Assert.True(StreamHandoverPlan.TheReplacementBlocks);

        // And what replaces it is the run PP746 drove over a socket, named rather than described.
        Assert.Equal("ManagedStreamRun.Run", StreamHandoverPlan.Replacement);
    }

    /// <summary>
    /// A managed session cannot reach the stream phase, which is why the handoff is one step.
    ///
    /// Stated as a value so the day ctrl and senkusha are ported, the sentence that stops being
    /// true is one a check can find rather than a paragraph somebody has to re-read.
    /// </summary>
    [Fact]
    public void AManagedSessionStillCannotReachTheStreamPhase()
    {
        Assert.False(StreamHandoverPlan.AManagedSessionCanReachTheStreamPhase);

        // The two that would change it are models rather than runs: they decide, and nothing calls
        // them from a session. A class that could run one would be a class with a loop of its own.
        Assert.Null(typeof(CtrlLoop).GetMethod("Run"));
        Assert.Null(typeof(SenkushaPlacement).GetMethod("Run"));
    }
}
