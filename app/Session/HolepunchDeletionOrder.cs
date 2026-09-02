using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>One step of PP33's deletion, and whether the C changes in it.</summary>
/// <param name="Name">What the step is called.</param>
/// <param name="Detail">What lands in it.</param>
/// <param name="TouchesTheC">Whether it edits lib/ or the build - which is what makes it the flip.</param>
public readonly record struct DeletionStage(string Name, string Detail, bool TouchesTheC);

/// <summary>
/// PP623: the order PP33's deletion lands in, which PP598 assumed was one commit.
///
/// PP598 recorded the decision and its timing together - the Qt client's build retires, and "the
/// retirement rides in PP33's own commit", because taking the affordance away earlier would remove
/// something that works and buy nothing. The timing half was reasoned about a commit understood as
/// nine calls in session.c plus three pieces of build wiring. PP621 measured what else is in it:
/// model classes under app/ quote session.c's holepunch text as a specification, and the tests
/// asserting against those models are the larger half again.
///
/// THE OBJECTION IS NOT TO THE DECISION, IT IS THAT NOBODY CHOSE AN ORDER. A deletion done in one
/// transaction is red from its first edit until its last, with no green tree in between to tell a
/// mistake in the C from a model that was converted wrongly. That is avoidable here, and the shape
/// that avoids it is the one PP597 already named from the other side: give the check a state for
/// the C having stopped asking.
///
/// SO THE MIDDLE STEP IS THE ONLY ONE THAT TOUCHES THE C. Every model is first taught both shapes -
/// the C as it is, and the C with the nine gone - while the C has not moved, so the suite is green
/// after each. The flip is then one commit that edits lib/ and the build and NO test file, because
/// every assertion it moves was already written to accept where it lands. What is left over is
/// dead: a first shape that can no longer occur, deleted afterwards the way PP591 turned the
/// harness's assertions over rather than deleting them.
///
/// This is a plan and not a promise about effort. What it buys is that a session picking PP33 meets
/// three landable commits instead of one diff it cannot finish, and that the one commit which must
/// be atomic is named rather than discovered.
/// </summary>
public static class HolepunchDeletionOrder
{
    /// <summary>The steps, in the order they land.</summary>
    public static IReadOnlyList<DeletionStage> Stages { get; } =
    [
        new(
            "Two-state the models",
            "every model in PP621's census learns the shape session.c has once the nine are gone, "
                + "beside the shape it has now; the C does not move, so the suite is green after each",
            TouchesTheC: false),
        new(
            "Flip the C",
            "the nine asks, both holepunch_session fields and the Qt client's build, in one commit "
                + "that edits no test file because every assertion it moves already accepts where it lands",
            TouchesTheC: true),
        new(
            "Delete the shape that can no longer occur",
            "the models drop the first of their two states, which is a deletion with a green tree "
                + "either side of it",
            TouchesTheC: false),
    ];

    /// <summary>
    /// The step that must be atomic, and the only one.
    ///
    /// Named by its property rather than by its index: what makes it irreducible is that it edits
    /// the C, and a plan that grew a second such step would have two commits that cannot be split
    /// and no way to say which of them this means.
    /// </summary>
    public static DeletionStage Flip => Stages.Single(one => one.TouchesTheC);

    /// <summary>
    /// What the flip has to carry, named through the types that own each piece.
    ///
    /// Through <see cref="QtClientBuild"/> and not copied, for that class's own reason: the
    /// reference is what makes deleting the retirement's pieces without revisiting this plan a build
    /// error rather than a plan that quietly stopped describing the work.
    /// </summary>
    public static IReadOnlyList<string> FlipCarries { get; } =
    [
        HolepunchConsumers.UnprefixedExport,
        HolepunchSessionOwnership.ConnectInfoField,
        QtClientBuild.EnableFlag,
        QtClientBuild.CompileArgument,
    ];

    /// <summary>
    /// Whether an order is landable: nothing before the flip needs the C to have changed, and
    /// nothing after it needs the C to be as it was.
    ///
    /// Read as "exactly one step touches the C, and it is not the first or the last". The first
    /// carries the preparation that makes the flip a one-commit change; the last carries the
    /// clean-up that only becomes possible after it. An order with either missing is a plan that
    /// says one commit while claiming to be three.
    /// </summary>
    public static bool IsLandable(IReadOnlyList<DeletionStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        if (stages.Count(one => one.TouchesTheC) != 1)
            return false;

        int flip = -1;
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].TouchesTheC)
                flip = i;
        }

        return flip > 0 && flip < stages.Count - 1;
    }
}
