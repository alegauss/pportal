using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP843: the frame path's flip state, held four ways, so prose cannot outlive the tree again.
///
/// PP697 turned nine files to the past tense when PP696 flipped. PP763 reverted the flip and turned
/// none of them back, and for a whole block those nine described a deletion that had been undone -
/// with every gate green, because nothing in the tree joined a sentence to a build file. PP27
/// flipped again, which made them true by accident and left PP843 holding a design about a state
/// four commits stale.
///
/// So what is asserted is the AGREEMENT and not the state: the library's source list, the shim's
/// option, the shim the host loaded, and the claim the prose makes are one answer or this is red.
/// </summary>
public class FramePathFlipStateTests(ITestOutputHelper output)
{
    private static string? Read(string? path) => path is null ? null : File.ReadAllText(path);

    /// <summary>
    /// THE RULE, on this tree. Four answers, one question.
    /// </summary>
    [Fact]
    public void TheFourAnswersAgree()
    {
        if (Read(FramePathFlipState.LocateLibrary()) is not { } library)
            return;
        if (Read(FramePathFlipState.LocateShim()) is not { } shim)
            return;
        if (Read(FramePathFlipState.LocateProse()) is not { } prose)
            return;

        ShimShape built = ShimFramePathShape.OfTheBuild();

        output.WriteLine($"sources say {FramePathFlipState.OfTheSources(library, shim)}, "
            + $"the loaded shim says {built}, "
            + $"the option default is {FramePathFlipState.OracleOptionDefault(shim)?.ToString() ?? "(no option)"}");

        IReadOnlyList<string> apart = FramePathFlipState.Disagreements(library, shim, prose, built);

        Assert.True(
            apart.Count == 0,
            "the frame path's flip state is not one answer:\n  " + string.Join("\n  ", apart));
    }

    /// <summary>
    /// The subject is LIVE, which is what the rule above cannot say on its own.
    ///
    /// Four answers that agree because none of them was found is the pass PP630 is about. So the
    /// prose is asserted to still carry its claim and the option to still be an option: on the tree
    /// PP843 shipped on the flip is in place, and each half of that is read from a different file.
    /// </summary>
    [Fact]
    public void TheFlipIsInPlaceAndEachFileSaysSo()
    {
        if (Read(FramePathFlipState.LocateLibrary()) is not { } library)
            return;
        if (Read(FramePathFlipState.LocateShim()) is not { } shim)
            return;
        if (Read(FramePathFlipState.LocateProse()) is not { } prose)
            return;

        Assert.Empty(FramePathFlipState.InTheLibrarySources(library));
        Assert.False(FramePathFlipState.OracleOptionDefault(shim));
        Assert.True(FramePathFlipState.ProseClaimsTheFlip(prose));
        Assert.Equal(ShimShape.Bare, FramePathFlipState.OfTheSources(library, shim));
    }

    /// <summary>
    /// THE CONTROL THAT MATTERS: the build file NAMES all three and compiles none of them.
    ///
    /// PP27's note in lib/CMakeLists.txt explains the deletion at length and spells every one of the
    /// three by name. A reader keyed on the bare filename would therefore report the flip as never
    /// having happened - PP793's finding for exports and PP847's for function bodies, arriving a
    /// third time in a third place. Asserted on the real file, because a synthetic fixture cannot
    /// show that this one actually has the trap in it.
    /// </summary>
    [Fact]
    public void ThePP27NoteNamesTheFilesAndIsNotACompile()
    {
        if (Read(FramePathFlipState.LocateLibrary()) is not { } library)
            return;

        foreach (string file in FramePathFlipState.Files)
        {
            Assert.Contains(file, library, StringComparison.Ordinal);
        }

        Assert.Empty(FramePathFlipState.InTheLibrarySources(library));
    }

    /// <summary>
    /// PP272: every reader sees the edit it guards against.
    ///
    /// The two directions PP843 was filed about are the last two - the tree moving under prose
    /// nobody edited, and prose turned while the flip stands.
    /// </summary>
    [Fact]
    public void TheReadersSeeTheEditsTheyGuardAgainst()
    {
        string file = FramePathFlipState.Files[0];
        string entry = FramePathFlipState.SourcePrefix + file;

        string listed = $"set(SOURCE_FILES\n\t\tsrc/common.c\n\t\t{entry})\n";
        const string emptied = "set(SOURCE_FILES\n\t\tsrc/common.c)\n";

        Assert.Equal([file], FramePathFlipState.InTheLibrarySources(listed));
        Assert.Empty(FramePathFlipState.InTheLibrarySources(emptied));

        // The two things that make an entry, each withheld in turn. A bare name is prose - which is
        // the form PP27's note actually uses - and a quoted entry inside a comment is still a note.
        Assert.Empty(FramePathFlipState.InTheLibrarySources($"\t\t{file}\n"));
        Assert.Empty(FramePathFlipState.InTheLibrarySources($"# PP27 removed {entry} from this list\n"));

        string off = $"option({FramePathFlipState.OracleOption} \"wrap them\" OFF)\n";
        string on = $"option({FramePathFlipState.OracleOption} \"wrap them\" ON)\n";

        Assert.False(FramePathFlipState.OracleOptionDefault(off));
        Assert.True(FramePathFlipState.OracleOptionDefault(on));

        // No option at all is the pre-PP27 shape: the define was set unconditionally.
        Assert.Null(FramePathFlipState.OracleOptionDefault(
            $"target_compile_definitions(chiaki-shim PUBLIC {FramePathFlipState.OracleDefine})\n"));

        Assert.Equal(ShimShape.Bare, FramePathFlipState.OfTheSources(emptied, off));
        Assert.Equal(ShimShape.Wrapping, FramePathFlipState.OfTheSources(listed, on));

        // Halfway through, either way round.
        Assert.Equal(ShimShape.Wrapping, FramePathFlipState.OfTheSources(listed, off));
        Assert.Equal(ShimShape.Wrapping, FramePathFlipState.OfTheSources(emptied, on));

        string claims = $"/// {FramePathFlipState.FlipClaim}, so this file's tense has turned.\n";
        const string silent = "/// the wrappers are here and every differential can run.\n";

        Assert.True(FramePathFlipState.ProseClaimsTheFlip(claims));
        Assert.False(FramePathFlipState.ProseClaimsTheFlip(silent));

        // PP763's day: the files came back and the sentence stayed. This is the assertion that
        // would have been red then, and was not there to be.
        string reverted = Assert.Single(
            FramePathFlipState.Disagreements(listed, on, claims, ShimShape.Wrapping),
            one => one.Contains(FramePathFlipState.FlipClaim, StringComparison.Ordinal));

        Assert.Contains(file, reverted, StringComparison.Ordinal);

        // And the other direction: the flip stands and the claim has been taken out.
        Assert.Contains(
            FramePathFlipState.Disagreements(emptied, off, silent, ShimShape.Bare),
            one => one.Contains("no longer says", StringComparison.Ordinal));
    }

    /// <summary>
    /// A tree that was not rebuilt is its own finding, and not folded into the prose one.
    ///
    /// This is the stale green PP56 fixed showing up a third way: the build files edited, the DLL
    /// beside them still exporting the fourteen, and every text-keyed reader agreeing with the files
    /// rather than with what the host would load.
    /// </summary>
    [Fact]
    public void ASourceTreeAheadOfItsBuildIsNamed()
    {
        const string emptied = "set(SOURCE_FILES\n\t\tsrc/common.c)\n";
        string off = $"option({FramePathFlipState.OracleOption} \"wrap them\" OFF)\n";
        string claims = $"/// {FramePathFlipState.FlipClaim}\n";

        string stale = Assert.Single(
            FramePathFlipState.Disagreements(emptied, off, claims, ShimShape.Wrapping));

        Assert.Contains("without being rebuilt", stale, StringComparison.Ordinal);
    }

    /// <summary>PP272: and the readers say nothing about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNothing()
    {
        Assert.Empty(FramePathFlipState.InTheLibrarySources(""));
        Assert.Null(FramePathFlipState.OracleOptionDefault(""));
        Assert.False(FramePathFlipState.ProseClaimsTheFlip(""));

        // Empty is the WRAPPING side, deliberately: an unreadable build file is not evidence that
        // three files left it, and answering Bare would make a missing file into a flip.
        Assert.Equal(ShimShape.Wrapping, FramePathFlipState.OfTheSources("", ""));
    }

    /// <summary>
    /// The prose file this reads is the one that was wrong, named rather than swept.
    ///
    /// PP697 turned nine files and eight of them are still crediting PP696 for a flip PP763 undid.
    /// That is a wider fact than PP843's line, filed as its own, and naming the single subject here
    /// is what keeps this check about the one the line was about.
    /// </summary>
    [Fact]
    public void TheSubjectIsShimFramePathShape()
    {
        Assert.EndsWith("ShimFramePathShape.cs", FramePathFlipState.ProseRelativePath, StringComparison.Ordinal);

        if (SanitizerSource.LocateRelative(FramePathFlipState.ProseRelativePath) is { } path)
            Assert.True(File.Exists(path));
    }
}
