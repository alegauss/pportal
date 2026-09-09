using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP844: no control names a file a deletion is aiming at, and the build graph agrees with each.
///
/// PP27's commit carried two failures of this shape - a positive control asserting takion.c was in
/// the build graph, and a reader proving a list was lib's by finding src/takion.c in it. Each fix
/// was one line, and each wrote the rule as prose. The cost was never the edit: a deletion commit
/// already has a long red tail of models turning over, and two entries in it pointed at their own
/// fixtures, so a reader had to decide per failure whether the tree broke or the furniture did.
/// </summary>
public class ControlFilesTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE, BEFORE THE DELETION. No control is aimed at by anything arranged.
    ///
    /// Inert on this tree, because both flips landed under PP27 and nothing is arranged - which is
    /// the state FlipTrial's own note describes and keeps its apparatus for.
    /// </summary>
    [Fact]
    public void NothingArrangedIsAimedAtAControl()
    {
        output.WriteLine(ControlFiles.Claimed.Count == 0
            ? "nothing arranged"
            : "claimed: " + string.Join(", ", ControlFiles.Claimed));

        IReadOnlyList<string> aimed = ControlFiles.Aimed();

        Assert.True(
            aimed.Count == 0,
            "a control names a file an arranged deletion claims:\n  " + string.Join("\n  ", aimed));
    }

    /// <summary>
    /// THE RULE, AFTER IT. Every control's polarity is what the build graph says.
    ///
    /// This is the half that is live, and it is the one that would have fired on PP27's day - one
    /// commit later than <see cref="NothingArrangedIsAimedAtAControl"/>, and saying which fixture
    /// moved instead of leaving two failures wearing their readers' names.
    /// </summary>
    [Fact]
    public void TheBuildGraphAgreesWithEveryControl()
    {
        if (SuiteFreshness.Locate(SuiteFreshness.BuildGraphRelativePath) is not { } path)
            return;

        IReadOnlyList<string> apart = ControlFiles.Disagreeing(File.ReadAllText(path));

        Assert.True(
            apart.Count == 0,
            "the build graph disagrees with a control:\n  " + string.Join("\n  ", apart));
    }

    /// <summary>
    /// Both polarities have a member, so the check above cannot be vacuous in either direction.
    ///
    /// PP271's rule applied to a rule about controls, which is not a joke: a table that was all
    /// positive would make Disagreeing's negative arm unreachable, and that arm is PP720's case.
    /// </summary>
    [Fact]
    public void BothPolaritiesAreRepresented()
    {
        Assert.Contains(ControlFiles.All, one => one.MustBeCompiled);
        Assert.Contains(ControlFiles.All, one => !one.MustBeCompiled);

        ControlFile negative = Assert.Single(ControlFiles.All, one => !one.MustBeCompiled);
        Assert.Equal(SuiteFreshness.OutOfTheGraph, negative.RelativePath, StringComparer.Ordinal);
    }

    /// <summary>
    /// The rows are the SAME files the readers use, not a second copy of them.
    ///
    /// StreamConnectionConsumers.SurvivesTheFlip is the declaration and this is the join: a row
    /// added here for a file that reader stopped naming would make the table agree with nothing,
    /// which is how a census becomes furniture of its own.
    /// </summary>
    [Fact]
    public void TheRowsAgreeWithStreamConnectionConsumers()
    {
        string[] rows =
        [
            .. ControlFiles.All
                .Where(one => one.Where == "StreamConnectionConsumers")
                .Select(one => one.RelativePath)
                .Order(StringComparer.OrdinalIgnoreCase)
        ];

        Assert.Equal(StreamConnectionConsumers.SurvivesTheFlip.Count, rows.Length);

        foreach (string survivor in StreamConnectionConsumers.SurvivesTheFlip)
        {
            Assert.Contains(rows, one => ControlFiles.SameFile(one, $@"lib\{survivor}"));
        }
    }

    /// <summary>
    /// A basename collision is not a match, which is the trap this rule lives next to.
    ///
    /// The suite had its own fec.c and frameprocessor.c beside lib's, and both pairs left the build
    /// in the same commit. A reader comparing basenames would report a control on one as aimed at by
    /// a deletion of the other - a false report inside the one commit that cannot afford noise.
    /// </summary>
    [Fact]
    public void TwoFilesWithOneNameAreNotTheSameFile()
    {
        Assert.False(ControlFiles.SameFile(@"test\fec.c", @"lib\src\fec.c"));

        // Separators and a leading ./ are spelling and not identity.
        Assert.True(ControlFiles.SameFile(@"lib\src\fec.c", "lib/src/fec.c"));
        Assert.True(ControlFiles.SameFile("./lib/src/fec.c", @"lib\src\fec.c"));
        Assert.True(ControlFiles.SameFile(@"LIB\SRC\FEC.C", "lib/src/fec.c"));
    }

    /// <summary>
    /// The aimed reader sees the edit it guards against, on a claim list this test supplies.
    ///
    /// PP27's own case, reconstructed: a file claimed by an arranged flip while a positive control
    /// still names it. The negative control is claimed too and is deliberately NOT reported - a
    /// deletion removing the file a negative control names is about to make that control correct.
    ///
    /// ONE FILE IS TWO READERS' CONTROL, which this test found rather than assumed. session.c is
    /// SuiteFreshness's positive control and one of StreamConnectionConsumers' two survivors, so a
    /// deletion aiming at it costs two failures - which is PP27's shape exactly, where takion.c was
    /// also two. That is the reason the answer is a list of sentences and not a count: the number
    /// says how much noise the commit carries, and per-reader is how it is cleared.
    /// </summary>
    [Fact]
    public void AClaimOnAPositiveControlIsReported()
    {
        // A file exactly one reader uses, so the single case is a single case. Two of the rows are
        // in that position and either will do - taking the first keeps this about the reporting
        // rather than about which reader happens to come first in the table.
        ControlFile sole = ControlFiles.All
            .Where(one => one.MustBeCompiled)
            .First(one => ControlFiles.All.Count(
                other => ControlFiles.SameFile(other.RelativePath, one.RelativePath)) == 1);

        string aimed = Assert.Single(ControlFiles.Aimed([sole.RelativePath]));

        Assert.Contains(sole.RelativePath, aimed, StringComparison.Ordinal);
        Assert.Contains(sole.Where, aimed, StringComparison.Ordinal);

        // And the shared one is reported once per reader that names it.
        Assert.Equal(2, ControlFiles.Aimed([@"lib\src\session.c"]).Count);

        Assert.Empty(ControlFiles.Aimed([SuiteFreshness.OutOfTheGraph]));

        // And a claim on a file no control names is nothing to do with this rule.
        Assert.Empty(ControlFiles.Aimed([@"lib\src\base64.c"]));
    }

    /// <summary>
    /// And the polarity reader sees both of its own edits, on a graph this test supplies.
    /// </summary>
    [Fact]
    public void ThePolarityReaderSeesBothDirections()
    {
        ControlFile positive = ControlFiles.All.First(one => one.MustBeCompiled);

        // A graph naming every control's file: the negative one is then wrong.
        string all = string.Join("\n", ControlFiles.All.Select(one => one.RelativePath.Replace('\\', '/')));

        Assert.Contains(
            ControlFiles.Disagreeing(all),
            one => one.Contains("negative control", StringComparison.Ordinal));

        // And a graph naming none of them: every positive one is wrong, and the negative is right.
        IReadOnlyList<string> empty = ControlFiles.Disagreeing("");

        Assert.Equal(ControlFiles.All.Count(one => one.MustBeCompiled), empty.Count);
        Assert.All(empty, one => Assert.Contains("the fixture moved", one, StringComparison.Ordinal));
        Assert.Contains(positive.RelativePath, string.Join("\n", empty), StringComparison.Ordinal);
    }
}
