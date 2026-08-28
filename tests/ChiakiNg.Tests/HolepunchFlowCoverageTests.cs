using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP508, under PP340: the flow reaches every method the seam declares, shown by running it.
///
/// PP480 held a list against a type. This holds a RUN against both, which is the only one of the
/// three that can catch a method nothing ever calls.
/// </summary>
public class HolepunchFlowCoverageTests
{
    /// <summary>
    /// THE CLAIM: across the flow's runs, the methods invoked equal the seam's seven.
    ///
    /// Equal, not contain. A method the flow calls that the seam has no site for fails here too -
    /// which is the direction a new ask would arrive from.
    /// </summary>
    [Fact]
    public void TheFlowReachesExactlyTheSeamsSevenMethods()
    {
        (IReadOnlySet<string> methods, _) = HolepunchFlowCoverage.Exercise();

        Assert.Equal(7, HolepunchFlowCoverage.SeamMethods.Count);
        Assert.Equal(HolepunchFlowCoverage.SeamMethods.Order(), methods.Order());
    }

    /// <summary>
    /// And the socket getter is seen with both port types, which is the distinction PP429 asked the
    /// seam to keep.
    ///
    /// A flow that fetched the ctrl socket twice would satisfy a count and fail this.
    /// </summary>
    [Fact]
    public void TheSocketGetterIsAskedForBothPorts()
    {
        (_, IReadOnlySet<HolepunchPortType> ports) = HolepunchFlowCoverage.Exercise();

        Assert.Equal(
            Enum.GetValues<HolepunchPortType>().Order(), ports.Order());
    }

    /// <summary>
    /// Fini needs a failing run, which is why the union is taken over three and not one.
    ///
    /// The success path never calls it. This asserts both halves: the good run is six of seven, and
    /// the missing one is Fini.
    /// </summary>
    [Fact]
    public void TheSuccessfulRunAloneIsSixOfSeven()
    {
        var session = new RecordingHolepunchSession();
        _ = new HolepunchConnect(session, _ => new object()).Run();

        IReadOnlySet<string> reached = new SortedSet<string>(session.Invoked, StringComparer.Ordinal);

        Assert.Equal(6, reached.Count);
        Assert.Equal(
            [nameof(IHolepunchSession.Fini)],
            HolepunchFlowCoverage.SeamMethods.Except(reached).Order());
    }

    /// <summary>
    /// A failing run calls Fini exactly once and stops where it failed.
    ///
    /// Once, because the C's two fini sites are two teardown PATHS and not two calls on one.
    /// </summary>
    [Theory]
    [InlineData(HolepunchStep.CreateOffer)]
    [InlineData(HolepunchStep.PunchHole)]
    public void AFailingRunReleasesTheSessionOnce(HolepunchStep failAt)
    {
        var session = new RecordingHolepunchSession { FailAt = failAt };
        HolepunchConnectOutcome outcome = new HolepunchConnect(session, _ => new object()).Run();

        Assert.Equal(failAt, outcome.FailedAt);
        Assert.Equal(1, outcome.FinisCalled);
        Assert.Equal(1, session.Invoked.Count(m => m == nameof(IHolepunchSession.Fini)));

        // And nothing after the failed step ran.
        Assert.DoesNotContain(nameof(IHolepunchSession.GetCtrlPort), session.Invoked);
    }

    /// <summary>
    /// The recording session answers every method the interface declares, so the instrument cannot
    /// be the reason a method looks unreached.
    ///
    /// Reflection over the interface rather than a list, for the same reason PP480 used it: a
    /// method added to the seam has to fail somewhere, and a hand-written list is where it would
    /// not.
    /// </summary>
    [Fact]
    public void TheRecordingSessionAnswersEveryDeclaredMethod()
    {
        Assert.Equal(
            HolepunchSeamJoin.DeclaredMethods.Order(),
            HolepunchFlowCoverage.SeamMethods.Order());

        Assert.True(typeof(IHolepunchSession).IsAssignableFrom(typeof(RecordingHolepunchSession)));
    }
}
