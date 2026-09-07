using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP803: the two arranged flips, and the trial that builds each one's shape.
///
/// Every check on either deletion read TEXT, and PP786 found what that misses by building takion's
/// bare shape once by hand: --wrap makes every malloc an undefined reference to __wrap_malloc, and
/// the only definition is in the one file both flips remove. The build failed and every text check
/// was green.
///
/// WHAT A GATE CAN HOLD HERE is that the trial and the arrangement say the same thing. Whether the
/// shape builds is what `flip.cmd` answers, and it is deliberately not part of the gate - it
/// configures a build directory of its own, and the gate is already the slowest thing in a commit.
/// </summary>
public class FlipTrialTests(ITestOutputHelper output)
{
    private static string? Suite()
        => FlipTrial.LocateSuite() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE TRIAL REMOVES WHAT THE MODEL SAYS IT DOES, both ways.
    ///
    /// The assertion that fails without this task: cmake and this class each name a set of files,
    /// and a rename on one side would otherwise leave a trial that quietly removes nothing and
    /// reports the unflipped shape as building.
    /// </summary>
    [Fact]
    public void EachFlipRemovesExactlyWhatTheBuildFileRemoves()
    {
        if (Suite() is not { } suite)
            return;

        foreach (ArrangedFlip flip in FlipTrial.Flips)
        {
            string[] removed = [.. FlipTrial.RemovedBy(suite, flip.Name).Order(StringComparer.Ordinal)];
            string[] declared = [.. flip.Removes.Order(StringComparer.Ordinal)];

            output.WriteLine($"{flip.Name}: {string.Join(", ", removed)}");

            Assert.Equal(declared, removed);
        }
    }

    /// <summary>
    /// And every file either flip removes is one the suite actually builds.
    ///
    /// The weaker direction and the one that rots: a REMOVE_ITEM naming a file that left the list
    /// is a removal of nothing, and cmake does not complain about it.
    /// </summary>
    [Fact]
    public void EveryRemovedFileIsInTheSuitesOwnList()
    {
        if (Suite() is not { } suite)
            return;

        foreach (ArrangedFlip flip in FlipTrial.Flips)
        {
            Assert.All(
                flip.Removes,
                file => Assert.True(
                    FlipTrial.SuiteListsFile(suite, file),
                    $"{flip.Name} removes {file}, which the suite's source list does not hold"));
        }
    }

    /// <summary>
    /// ALLOCBUDGET IS ON BOTH, which is the fact PP786's failure was about.
    ///
    /// It is claimed by either flip and leaves on whichever lands first, and it is where
    /// __wrap_malloc is defined - so a trial of one flip that kept it would build a shape no
    /// landing produces, and would be green about the exact case that was not.
    /// </summary>
    [Fact]
    public void TheAllocationCounterLeavesWithEitherFlip()
    {
        Assert.All(FlipTrial.Flips, flip => Assert.Contains("allocbudget.c", flip.Removes));
        Assert.Equal(2, FlipTrial.Flips.Count);
    }

    /// <summary>
    /// Each switch follows its files rather than being written flat.
    ///
    /// PP803's own trap: a trial that removed the files and left the definition at 1 compiles
    /// main.c's guarded suites against sources that are not there, and the failure would be about
    /// the trial rather than about the flip.
    /// </summary>
    [Fact]
    public void EachSwitchFollowsTheListItDescribes()
    {
        if (Suite() is not { } suite)
            return;

        Assert.All(
            FlipTrial.Flips,
            flip => Assert.True(
                FlipTrial.SwitchFollowsTheList(suite, flip.Switch),
                $"{flip.Switch} is written flat, so a trial would compile guarded suites against "
                    + "sources the flip removed"));
    }

    /// <summary>And the launcher and its steps are both there, which is compile.cmd's own rule.</summary>
    [Fact]
    public void TheLauncherAndItsStepsExist()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.True(File.Exists(Path.Combine(root, FlipTrial.LauncherFileName)));
        Assert.NotNull(FlipTrial.LocateScript());

        // The script takes the flip by NAME, so a name only this class knows would be a launcher
        // nobody can drive.
        string steps = File.ReadAllText(FlipTrial.LocateScript()!);

        Assert.All(
            FlipTrial.Flips,
            flip => Assert.Contains(flip.Name, steps, StringComparison.Ordinal));
    }

    /// <summary>A flip the build file does not know removes nothing, rather than the next one's list.</summary>
    [Fact]
    public void AnUnknownFlipRemovesNothing()
    {
        if (Suite() is not { } suite)
            return;

        Assert.Empty(FlipTrial.RemovedBy(suite, "senkusha"));
    }
}
