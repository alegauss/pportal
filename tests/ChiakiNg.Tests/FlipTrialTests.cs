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
    ///
    /// PP27: BOTH FLIPS LANDED, so the two sides now have to agree that nothing is arranged. That
    /// direction is the one that rots quietly - REMOVE_ITEM on an absent file is a silent no-op, so
    /// an arm left behind would configure, print its message and change nothing.
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

        // PP271: the loop above is empty on this tree, so the claim it would make is made here
        // instead - the two names that WERE arranged remove nothing now, asked of the build file.
        Assert.Empty(FlipTrial.RemovedBy(suite, "takion"));
        Assert.Empty(FlipTrial.RemovedBy(suite, "framepath"));
    }

    /// <summary>
    /// And every file either flip removes is one the suite actually builds.
    ///
    /// The weaker direction and the one that rots: a REMOVE_ITEM naming a file that left the list
    /// is a removal of nothing, and cmake does not complain about it.
    ///
    /// PP27: which is now the whole of it. Nothing is arranged, so what this holds is the other end
    /// of the same rule - every file the landed flips took is one the suite's list no longer holds.
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

        Assert.All(
            FlipTrial.Departed,
            file => Assert.False(
                FlipTrial.SuiteListsFile(suite, file),
                $"{file} left with PP27's flip and the suite's source list still holds it"));
    }

    /// <summary>
    /// ALLOCBUDGET WENT WITH THEM, which is the fact PP786's failure was about.
    ///
    /// It was claimed by either flip and left on whichever landed first, and it is where
    /// __wrap_malloc is defined - so the --wrap block had to follow it out or the link would name
    /// every allocation site in libchiaki, munit and jerasure. Asserted as its absence from both the
    /// list and the link options, because those are two edits and PP803 derived the second from the
    /// first exactly so they could not part company.
    /// </summary>
    [Fact]
    public void TheAllocationCounterLeftWithThem()
    {
        Assert.Empty(FlipTrial.Flips);
        Assert.Contains("allocbudget.c", FlipTrial.Departed);

        if (Suite() is not { } suite)
            return;

        Assert.False(FlipTrial.SuiteListsFile(suite, "allocbudget.c"));
        Assert.Contains("allocbudget.c\" IN_LIST", suite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each switch follows its files rather than being written flat.
    ///
    /// PP803's own trap: a trial that removed the files and left the definition at 1 compiles
    /// main.c's guarded suites against sources that are not there, and the failure would be about
    /// the trial rather than about the flip.
    ///
    /// PP27: read off <see cref="FlipTrial.Switches"/> rather than off the flips, because the
    /// switches outlived them. main.c still guards on both and both answer zero, so a definition
    /// hard-coded now would compile identically today and be wrong the moment anything came back.
    /// </summary>
    [Fact]
    public void EachSwitchFollowsTheListItDescribes()
    {
        if (Suite() is not { } suite)
            return;

        Assert.NotEmpty(FlipTrial.Switches);
        Assert.All(
            FlipTrial.Switches,
            definition => Assert.True(
                FlipTrial.SwitchFollowsTheList(suite, definition),
                $"{definition} is written flat, so it no longer says anything about the list"));
    }

    /// <summary>
    /// And the launcher and its steps are both there, which is compile.cmd's own rule.
    ///
    /// PP27: kept with no flip to run, deliberately. What PP786's failure bought was the instrument,
    /// and a third arranged deletion should cost a row and an arm rather than rebuilding it.
    /// </summary>
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

        // And it still names the variable it sets, which is the half that survives an empty list.
        Assert.Contains(FlipTrial.CacheVariable, steps, StringComparison.Ordinal);
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
