using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP697, under PP295: the prose turned and the predicates stayed, which is the whole of that step.
///
/// PP623's third step is prose, and PP634 corrected what it means: the predicates ARE the guard, so
/// none is deleted and what turns is the present tense around them. The risk this holds is the
/// reading that step invites - that a question about a file no longer compiled is a dead question -
/// and the four files are still in the tree, so every one of them still runs and still fails the day
/// upstream's text says something else.
/// </summary>
public class FramePathPredicatesTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(ManagedStreamRun).Assembly;

    /// <summary>
    /// THE COUNT MAY RISE AND MAY NOT FALL, which is PP38's ratchet pointed the other way.
    ///
    /// A fall is a predicate deleted, and the reasoning that deletes one is the reasoning PP634
    /// corrected. If it rises, raise the floor in the same commit - a ratchet left loose has given
    /// away exactly what it just gained.
    /// </summary>
    [Fact]
    public void ThePredicatesOverTheFourFilesHaveNotBeenDeletedWithThem()
    {
        IReadOnlyList<FramePathReader> readers = FramePathPredicates.ReadersIn(App);
        int total = FramePathPredicates.TotalIn(App);

        output.WriteLine($"{readers.Count} reader(s), {total} predicate(s), floor {FramePathPredicates.Floor}");
        output.WriteLine(string.Join(
            "\n", readers.Where(one => one.Predicates > 0).Select(one => $"  {one.Type} {one.Predicates}")));

        Assert.True(
            total >= FramePathPredicates.Floor,
            $"{total} predicates over the four files, and the floor is {FramePathPredicates.Floor} - "
                + "a question about them was deleted, which is what PP634 corrected");

        // And the sweep found something to sweep, which an empty assembly would also satisfy.
        Assert.True(readers.Count > 10, $"only {readers.Count} readers name one of the four");
    }

    /// <summary>
    /// The four files are THERE, whichever side of the flip the build is on.
    ///
    /// That is what makes the predicates answerable, and it is the half that does not move: PP696
    /// took them out of the build and left the source, the way PP33 left holepunch.c and PP598 left
    /// gui/. A deletion of the text is a separate decision with its own line.
    ///
    /// PP762: AND THE BUILD SIDE IS ASKED AS A SHAPE, not asserted as one. This said Silent, which
    /// was true for exactly as long as PP696 stood - and PP696 was reverted, because it took the C's
    /// stream connection out and nothing installed the managed replacement. A check that named one
    /// side went red on the commit that put the tree back, which is the third time this file has
    /// learned the same lesson.
    /// </summary>
    [Fact]
    public void TheFourFilesAreInTheTreeOnEitherSideOfTheFlip()
    {
        foreach (string relative in FramePathPredicates.Subjects)
        {
            Assert.True(
                SanitizerSource.LocateRelative(relative) is not null,
                $"{relative} is gone from the tree, so every predicate over it now reads nothing");
        }

        if (SanitizerSource.LocateRelative(@"lib\CMakeLists.txt") is not { } path)
            return;

        // Either shape, and never the half-done one: PP565's rule is that the four move together.
        Assert.NotEqual(
            ConsumerShape.Partial,
            StreamConnectionConsumers.ShapeOfTheList(File.ReadAllText(path)));
    }

    /// <summary>
    /// The two readers themselves, on types rather than on whichever tree this runs against.
    ///
    /// A sweep keyed on a constant is only as good as what it calls a constant, and a subject named
    /// inside an array is the shape every census here uses - so both are asked directly.
    /// </summary>
    [Fact]
    public void TheSweepFindsASubjectAndCountsAQuestion()
    {
        // A reader with one subject and questions over it.
        Assert.True(FramePathPredicates.SubjectsNamedBy(typeof(VideoReceiverSource)) >= 1);
        Assert.True(FramePathPredicates.PredicatesOn(typeof(VideoReceiverSource)) >= 5);

        // A census naming several of the four rather than one.
        Assert.True(FramePathPredicates.SubjectsNamedBy(typeof(FecConsumers)) >= 2);

        // And a class about none of them is not swept in, which is what keeps the count meaningful.
        Assert.Equal(0, FramePathPredicates.SubjectsNamedBy(typeof(SuiteEntryPoint)));
    }
}
