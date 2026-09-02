using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP598: the Qt client's build retires in one piece, whenever it retires.
///
/// The decision was made by PP598 - gui/ stops being a build target, because PP597 showed that
/// PP33's deletion and a compilable client cannot both continue. PP632 made the change, in the same
/// commit that stopped session.c asking, which is what "rides in PP33's own commit" meant.
///
/// SO THESE HAVE TURNED OVER, the way PP591's did when the harness it described was deleted. They
/// held that the affordance was real and must not go early; they hold that it is gone and must not
/// come back. The expensive mistake was never doing it late - it was doing half of it, and the half
/// that would have been left is answered by <see cref="GuiBuildState.Retired"/>.
/// </summary>
public class QtClientBuildTests
{
    /// <summary>
    /// compile.cmd still offers the argument, which is the piece no compiler reads.
    ///
    /// The other two are held by the type system: QtClientBuild names GuiFreshness, so deleting the
    /// check without deleting the record does not build. This is the one that could go silently.
    /// </summary>
    [Fact]
    public void TheGateNoLongerOffersTheArgument()
    {
        if (QtClientBuild.LocateCompile() is not { } path)
            return;

        // PP632: turned over. The argument went in the commit that stopped session.c asking, which
        // is what PP598 meant by "rides in PP33's own commit" - gui/ calls eleven holepunch
        // exports, so it stopped compiling in the same instant.
        Assert.False(
            QtClientBuild.CompileStillBuildsTheClient(File.ReadAllText(path)),
            $"compile.cmd sets {QtClientBuild.EnableFlag} again, and gui/ does not compile - the "
                + "argument would produce a wall of errors rather than a client");
    }

    /// <summary>
    /// And the reader wants the assignment, not the word.
    ///
    /// compile.cmd's own comments quote the flag while explaining PP529, so a plain Contains over
    /// the file is satisfied by the paragraph that describes the affordance rather than by the line
    /// that provides it - PP587's finding, one file over.
    /// </summary>
    [Fact]
    public void ACommentDescribingTheFlagIsNotTheFlag()
    {
        Assert.False(QtClientBuild.CompileStillBuildsTheClient(
            "rem PP529: pass gui to set CHIAKI_ENABLE_GUI=ON, which nothing else does\n"));

        Assert.True(QtClientBuild.CompileStillBuildsTheClient(
            "    if /I \"%%~a\"==\"gui\"       set \"CHIAKI_ENABLE_GUI=ON\"\n"));

        Assert.False(QtClientBuild.CompileStillBuildsTheClient("echo nothing here\n"));
    }

    /// <summary>
    /// The three name one binary, and the record reaches it through GuiFreshness rather than
    /// carrying its own copy.
    ///
    /// That reference is the retirement's guard: a second literal here would let the two drift, and
    /// drift is what makes a half-done retirement possible in the first place.
    /// </summary>
    [Fact]
    public void TheRecordAndTheCheckMeanOneBinary()
    {
        Assert.Equal(GuiFreshness.ClientRelativePath, QtClientBuild.ClientRelativePath);
        Assert.Contains("chiaki.exe", QtClientBuild.ClientRelativePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP599: the client this retires is the same file that drives the C session's PSN path.
    ///
    /// That join is what makes PP533's conversion unnecessary. PP596 holds that the Qt client is the
    /// only thing putting a holepunch handle into a ChiakiSession; this holds that the same file is
    /// the one whose build is going. So after the retirement nobody can enter session.c's PSN path,
    /// and the nine asks are removed rather than converted to the five results Â§PP533 designed.
    ///
    /// Written as an identity between two models rather than as a sentence, because the conclusion
    /// only holds while both name one file. A second PSN driver arriving, or the retirement moving
    /// to some other target, puts the conversion back on the table - and this is what would say so.
    /// </summary>
    [Fact]
    public void TheClientRetiringIsTheOneThatDrivesPsn()
    {
        Assert.Equal(
            HolepunchSessionOwnership.QtClientRelativePath,
            QtClientBuild.PsnDriverRelativePath);

        // And it is still the only one, which is PP596's half of the join.
        Assert.Equal(
            [@"gui\src\streamsession.cpp", @"shim\chiaki_shim.c"],
            HolepunchSessionOwnership.SessionInitCallers);

        Assert.Contains(
            QtClientBuild.PsnDriverRelativePath,
            HolepunchSessionOwnership.SessionInitCallers);
    }

    /// <summary>
    /// PP632: the argument is not parsed any more, which is the half no compiler reads.
    ///
    /// Kept as an assertion rather than deleted with the affordance, and for PP591's reason: what
    /// a removal leaves behind is nothing to check unless somebody writes the absence down. An
    /// argument arriving back - a port of gui/, a merge from upstream - would be a build target for
    /// source that calls eleven exports the library no longer reaches from session.c.
    ///
    /// The NAME is still here on purpose. It is what a returning argument would be spelled, and it
    /// is what the message above has to be able to say.
    /// </summary>
    [Fact]
    public void TheAffordanceIsGoneAndTheNameRemembersIt()
    {
        Assert.Equal("gui", QtClientBuild.CompileArgument);

        if (QtClientBuild.LocateCompile() is not { } path)
            return;

        // The token-by-token match compile.cmd used to make. Its absence is the retirement.
        string text = File.ReadAllText(path);
        Assert.DoesNotContain(
            $"\"%%~a\"==\"{QtClientBuild.CompileArgument}\"", text, StringComparison.Ordinal);
    }
}
