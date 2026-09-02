using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP623: PP33's deletion has an order, and the one commit that cannot be split is named.
///
/// PP598 held the retirement to PP33's own commit and PP621 measured what else is in that commit.
/// These assert the third thing: that the work between them is three landable steps rather than one
/// diff, and that the step which must be atomic is identified by what it edits rather than by where
/// it happens to sit in a list.
/// </summary>
public class HolepunchDeletionOrderTests
{
    /// <summary>
    /// PP623: exactly one step edits the C, and it is neither the first nor the last.
    ///
    /// THE WHOLE OF THE PLAN, as a property. A step before it is preparation that keeps the tree
    /// green; a step after it is clean-up that only becomes possible once the C has moved. An order
    /// whose first step touches the C has no preparation and is the single transaction PP598
    /// assumed; one whose last step does has left the models describing a tree that is gone.
    /// </summary>
    [Fact]
    public void TheOrderIsLandable()
    {
        Assert.True(HolepunchDeletionOrder.IsLandable(HolepunchDeletionOrder.Stages));
        Assert.Equal(3, HolepunchDeletionOrder.Stages.Count);
        Assert.Equal("Flip the C", HolepunchDeletionOrder.Flip.Name);
    }

    /// <summary>
    /// PP632: the plan says how far it has got, and the flip is behind it.
    ///
    /// A plan that could not say this would be a description of work rather than a record of it -
    /// and the reader who needs it most is the next session, which otherwise reads three steps and
    /// has to work out from the tree which one it is on.
    /// </summary>
    [Fact]
    public void TheFlipIsBehindUs()
    {
        Assert.Equal(2, HolepunchDeletionOrder.Landed);

        // The flip is the second, so "two landed" means it has. Read from the list rather than
        // asserted as an index, because the list is what would move if the order ever changed.
        int flip = 1 + HolepunchDeletionOrder.Stages
            .TakeWhile(one => !one.TouchesTheC)
            .Count();

        Assert.Equal(flip, HolepunchDeletionOrder.Landed);
        Assert.True(HolepunchDeletionOrder.Landed < HolepunchDeletionOrder.Stages.Count);
    }

    /// <summary>
    /// PP623: and the orders that are not landable are refused, so the property is a rule and not a
    /// description of the list beside it.
    /// </summary>
    [Fact]
    public void AnOrderThatCannotLandIsRefused()
    {
        DeletionStage flip = HolepunchDeletionOrder.Flip;
        var prepare = new DeletionStage("prepare", "-", TouchesTheC: false);
        var clean = new DeletionStage("clean", "-", TouchesTheC: false);

        // The single transaction PP598 assumed.
        Assert.False(HolepunchDeletionOrder.IsLandable([flip]));

        // Preparation with nothing after it: the models still carry a shape that cannot occur.
        Assert.False(HolepunchDeletionOrder.IsLandable([prepare, flip]));

        // Clean-up with nothing before it: the flip is atomic again, just later.
        Assert.False(HolepunchDeletionOrder.IsLandable([flip, clean]));

        // Two commits that each cannot be split is a plan with no way to say which one is meant.
        Assert.False(HolepunchDeletionOrder.IsLandable([prepare, flip, flip, clean]));

        Assert.True(HolepunchDeletionOrder.IsLandable([prepare, flip, clean]));
    }

    /// <summary>
    /// PP623: the flip carries the pieces PP598 and PP621 each said it must, named through the types
    /// that own them.
    ///
    /// Through the types on purpose - QtClientBuild's own reason, one level up. A retirement that
    /// deleted <see cref="QtClientBuild"/> without revisiting this plan would not compile, so the
    /// plan cannot go on describing three pieces after one of them stops existing.
    /// </summary>
    [Fact]
    public void TheFlipCarriesTheRetirementAndTheField()
    {
        Assert.Contains(QtClientBuild.EnableFlag, HolepunchDeletionOrder.FlipCarries);
        Assert.Contains(QtClientBuild.CompileArgument, HolepunchDeletionOrder.FlipCarries);
        Assert.Contains(HolepunchSessionOwnership.ConnectInfoField, HolepunchDeletionOrder.FlipCarries);

        // PP564: the export that carries no chiaki_ prefix, which is how a sweep for the nine misses
        // one. A flip commit that missed it would leave session.c calling into holepunch.c.
        Assert.Contains(HolepunchConsumers.UnprefixedExport, HolepunchDeletionOrder.FlipCarries);
    }

    /// <summary>
    /// PP623: and the preparation step's subject is PP621's census, not a list somebody typed.
    ///
    /// The join that keeps the plan sized correctly. If the models to two-state were named here,
    /// the plan would describe the tree as it was on the day it was written - which is the defect
    /// PP621 exists to answer, arriving one level up in the same shape.
    /// </summary>
    [Fact]
    public void ThePreparationStepPointsAtTheCensus()
    {
        DeletionStage prepare = HolepunchDeletionOrder.Stages[0];

        Assert.False(prepare.TouchesTheC);
        Assert.Contains("PP621", prepare.Detail, StringComparison.Ordinal);

        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        // And the census it points at is not empty, so the step has subjects. An empty one would
        // mean the conversion is already done and this plan is describing nothing.
        Assert.Contains(HolepunchOracleReaders.Census(root), one => !one.IsTest);
    }
}
