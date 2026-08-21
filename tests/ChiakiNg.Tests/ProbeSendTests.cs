using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP244: the send loop, and a flag that means less than it says.
///
/// <see cref="AListOfUnsupportedFamiliesReportsSuccess"/> carries the task: every candidate is
/// skipped, nothing is sent, and the loop reports no failure - so the wait that follows is waiting
/// for answers to probes that do not exist.
/// </summary>
public class ProbeSendTests
{
    private static ProbeAttempt Unsupported()
        => ProbeSend.Attempt(resolved: true, ProbeArm.Unsupported, socketOpen: false, sendSucceeded: false);

    private static ProbeAttempt Sent()
        => ProbeSend.Attempt(resolved: true, ProbeArm.Ipv4, socketOpen: true, sendSucceeded: true);

    /// <summary>Each family takes its own arm, and anything else takes none.</summary>
    [Fact]
    public void EachFamilyTakesItsOwnArm()
    {
        Assert.Equal(ProbeArm.Ipv4, ProbeSend.ArmFor(AddressFamily.InterNetwork));
        Assert.Equal(ProbeArm.Ipv6, ProbeSend.ArmFor(AddressFamily.InterNetworkV6));
        Assert.Equal(ProbeArm.Unsupported, ProbeSend.ArmFor(AddressFamily.Unix));
    }

    /// <summary>
    /// THE FLAG. Nothing was sent, nothing could have been, and the loop reports no failure.
    /// </summary>
    [Fact]
    public void AListOfUnsupportedFamiliesReportsSuccess()
    {
        ProbeAttempt[] turns = [Unsupported(), Unsupported(), Unsupported()];

        Assert.False(ProbeSend.AnyProbeSent(turns));
        Assert.False(ProbeSend.ReportsFailure(turns));
    }

    /// <summary>And a closed socket does the same - the guard skips the send, the clear still runs.</summary>
    [Fact]
    public void AClosedSocketAlsoClearsTheFlag()
    {
        ProbeAttempt turn = ProbeSend.Attempt(
            resolved: true, ProbeArm.Ipv4, socketOpen: false, sendSucceeded: false);

        Assert.False(turn.ProbeSent);
        Assert.True(turn.ClearsFailed);
    }

    /// <summary>
    /// Only a resolution failure - or a send that was attempted and failed - leaves the flag alone,
    /// which is the whole of what it reports.
    /// </summary>
    [Fact]
    public void OnlyResolutionAndSendFailuresLeaveTheFlag()
    {
        Assert.False(
            ProbeSend.Attempt(resolved: false, ProbeArm.Ipv4, true, true).ClearsFailed);

        Assert.False(
            ProbeSend.Attempt(resolved: true, ProbeArm.Ipv4, socketOpen: true, sendSucceeded: false)
                .ClearsFailed);

        // Every candidate failing to resolve is the one case the loop reports.
        Assert.True(ProbeSend.ReportsFailure(
            [ProbeSend.Attempt(false, ProbeArm.Ipv4, true, true)]));
    }

    /// <summary>
    /// THE UNREACHABLE CLAUSE, and the distinction that matters: it is not a no-op. For a static
    /// candidate it would suppress the fan-out - it simply can never be read as anything but false,
    /// because the flag is declared false each iteration and assigned only inside the block it
    /// guards.
    ///
    /// So a port that dropped the clause as dead would get the behaviour right by the wrong
    /// reasoning, and would be silently wrong the day the flag became reachable.
    /// </summary>
    [Fact]
    public void TheSentClauseIsUnreachableRatherThanInert()
    {
        Assert.False(ProbeSend.TheOnlyReachableSentValue);

        // It WOULD change the answer - for exactly one type, over the one arm that has the path.
        Assert.True(ProbeSend.TheClauseWouldMatterIfItCouldBeReached(
            ProbeArm.Ipv4, CandidateType.Static, randomAllocation: true));

        Assert.False(ProbeSend.TheClauseWouldMatterIfItCouldBeReached(
            ProbeArm.Ipv4, CandidateType.Stun, randomAllocation: true));
        Assert.False(ProbeSend.TheClauseWouldMatterIfItCouldBeReached(
            ProbeArm.Ipv6, CandidateType.Static, randomAllocation: true));
        Assert.False(ProbeSend.TheClauseWouldMatterIfItCouldBeReached(
            ProbeArm.Ipv4, CandidateType.Static, randomAllocation: false));
    }

    /// <summary>What the arm therefore reduces to: the type test, over IPv4, with the flag on.</summary>
    [Fact]
    public void TheRandomAllocationArmIsTheTypeTestAlone()
    {
        Assert.True(ProbeSend.TakesTheRandomAllocationPath(
            ProbeArm.Ipv4, CandidateType.Static, randomAllocation: true, alreadySent: false));
        Assert.True(ProbeSend.TakesTheRandomAllocationPath(
            ProbeArm.Ipv4, CandidateType.Stun, randomAllocation: true, alreadySent: false));

        Assert.False(ProbeSend.TakesTheRandomAllocationPath(
            ProbeArm.Ipv4, CandidateType.Local, randomAllocation: true, alreadySent: false));

        // And IPv6 has no such path at all, which is the asymmetry - port guessing is IPv4 only.
        Assert.False(ProbeSend.TakesTheRandomAllocationPath(
            ProbeArm.Ipv6, CandidateType.Stun, randomAllocation: true, alreadySent: false));
    }

    /// <summary>
    /// The stale code survives the loop. It is discarded by the return being a literal, not by
    /// anything resetting it.
    /// </summary>
    [Fact]
    public void AStaleErrorCodeSurvivesTheLoop()
    {
        ProbeAttempt[] turns = [ProbeSend.Attempt(false, ProbeArm.Ipv4, true, true), Sent()];

        Assert.True(ProbeSend.StaleErrorSurvives(turns));

        // But the loop as a whole reports no failure, which is correct - one probe did leave.
        Assert.False(ProbeSend.ReportsFailure(turns));
        Assert.True(ProbeSend.AnyProbeSent(turns));
    }

    /// <summary>The port buffer fits the largest port with nothing to spare.</summary>
    [Fact]
    public void ThePortBufferFitsExactly()
    {
        Assert.True(ProbeSend.PortFits(ushort.MaxValue));
        Assert.Equal(6, ProbeSend.PortBuffer);

        // Five digits and the terminator is the whole of it.
        Assert.Equal(ProbeSend.PortBuffer, ushort.MaxValue.ToString(null as IFormatProvider).Length + 1);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheSendLoopIsStillTheCores()
    {
        string? file = ProbeSendSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(ProbeSendSource.TheFlagStillStartsTrue(core), "the flag still starts true");
        Assert.True(
            ProbeSendSource.TheFlagIsStillClearedPastTheSwitch(core),
            "and is still cleared past the arm that sends nothing");
        Assert.True(
            ProbeSendSource.TheDeadClauseIsStillThere(core),
            "the clause that cannot be false is still there");
        Assert.True(
            ProbeSendSource.TheSuccessReturnIsStillALiteral(core),
            "the success return is still a literal");
        Assert.True(ProbeSendSource.ThePortBufferIsStillExact(core), "the port buffer still exact");
        Assert.True(
            ProbeSendSource.TheUnsupportedLogStillDoesNotNameTheFunction(core),
            "and the unsupported-family log still the only one not naming the function");
    }
}
