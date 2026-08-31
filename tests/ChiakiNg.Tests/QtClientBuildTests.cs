using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP598: the Qt client's build retires in one piece, whenever it retires.
///
/// The decision is made - gui/ stops being a build target, because PP597 showed that PP33's
/// deletion and a compilable client cannot both continue. What is NOT made is the change: the
/// client builds today, and removing the affordance before the deletion that breaks it would take
/// away something that works for nothing.
///
/// So these hold the shape until then. The expensive mistake is not doing it late - it is doing
/// half of it: an argument removed while GuiFreshness stays leaves every checkout that ever built a
/// client permanently red, with nothing able to clear it.
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
    public void TheGateStillOffersTheArgument()
    {
        if (QtClientBuild.LocateCompile() is not { } path)
            return;

        Assert.True(
            QtClientBuild.CompileStillBuildsTheClient(File.ReadAllText(path)),
            $"compile.cmd no longer sets {QtClientBuild.EnableFlag}. If that is PP33's deletion "
                + "landing, the retirement is three pieces: this argument, GuiFreshness, and the "
                + "client path they share - delete GuiFreshness and QtClientBuild in the same "
                + "commit, or a checkout that built a client can never clear Stale again");
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
    /// And the client is still a thing this checkout can build, which is why the retirement waits.
    ///
    /// Held as the argument being offered rather than by running it: a test that built the Qt client
    /// would be a second build system in the suite, and PP22's line about what a runner answers
    /// applies here too. What this records is that the affordance is real today, so removing it now
    /// would be a loss rather than a tidy-up.
    /// </summary>
    [Fact]
    public void TheAffordanceIsRealAndNotAlreadyGone()
    {
        Assert.Equal("gui", QtClientBuild.CompileArgument);

        if (QtClientBuild.LocateCompile() is not { } path)
            return;

        // The argument is parsed, not merely documented: compile.cmd matches it token by token.
        string text = File.ReadAllText(path);
        Assert.Contains($"\"%%~a\"==\"{QtClientBuild.CompileArgument}\"", text, StringComparison.Ordinal);
    }
}
