using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP439: the oracle's size is recorded, and the gate still asks for it.
///
/// ctest reports the C suite as one test. The enforcement lives in scripts/test-windows.sh because
/// that is where ctest runs; what this holds is that the script has not stopped asking.
/// </summary>
public class CSuiteFloorTests(ITestOutputHelper output)
{
    /// <summary>
    /// The floor is a number, and one big enough to be the whole suite.
    /// </summary>
    [Fact]
    public void TheFloorIsARecordedCount()
    {
        if (CSuiteFloor.LocateFloor() is not { } path)
            return;

        int? floor = CSuiteFloor.Read(File.ReadAllText(path));
        output.WriteLine($"floor: {floor}");

        Assert.NotNull(floor);

        // PP271 as a constant: a floor of 1 would let the suite shrink to nothing and still pass.
        Assert.True(
            floor >= CSuiteFloor.PlausibleMinimum,
            $"the floor is {floor}, which is too small to be the suite it stands for");
    }

    /// <summary>
    /// THE GATE STILL ASKS. -V is what carries munit's count, and --output-on-failure is the flag
    /// this replaced - it prints the summary only on a red, which is the run where the count is not
    /// the question.
    /// </summary>
    [Fact]
    public void TheGateAsksCtestForTheCountAndReadsTheFloor()
    {
        if (CSuiteFloor.LocateGate() is not { } path)
            return;

        string script = File.ReadAllText(path);

        Assert.True(
            CSuiteFloor.AsksCtestForTheCount(script),
            "the gate no longer asks ctest for verbose output, so munit's count is unavailable "
                + "and the floor is a file nothing compares against");

        Assert.True(
            CSuiteFloor.ReadsTheFloor(script),
            "the gate does not name the floor file, so the count is printed and not enforced");
    }

    /// <summary>
    /// PP68 and PP70's property: a captured run prints nothing while it hangs, so both the failure
    /// path and the timeout path empty the capture to the screen.
    ///
    /// Counted, not tested for presence: one cat would satisfy a word search and leave the timeout
    /// path silent, and the timeout path is the one those two tasks were about.
    /// </summary>
    [Fact]
    public void AFailedOrHangingRunPrintsWhatItCaptured()
    {
        if (CSuiteFloor.LocateGate() is not { } path)
            return;

        int prints = CSuiteFloor.PrintsTheCaptureCount(File.ReadAllText(path));
        output.WriteLine($"cat of the capture: {prints} place(s)");

        Assert.True(
            prints >= 2,
            $"the capture is emptied to the screen in {prints} place(s); the failure path and the "
                + "timeout path both need it, or a hang prints nothing at all");
    }

    /// <summary>
    /// The value is the last number outside a comment, which is what lets the file carry a header.
    /// </summary>
    [Fact]
    public void TheHeaderIsNotTheValue()
    {
        const string Floor = """
            # PP439: the size of the oracle.
            #
            # 145 on 2026-08-27, when the count was first taken, of which 64 are the fec cases.
            145
            """;

        Assert.Equal(145, CSuiteFloor.Read(Floor));

        // A file that is only a header records nothing, and says so rather than answering zero.
        Assert.Null(CSuiteFloor.Read("# 145 is mentioned here and is not the value\n"));
    }

    /// <summary>
    /// PP400: a comment naming a flag is not the gate passing it.
    ///
    /// The block this change added discusses --output-on-failure at length to say what it replaced,
    /// so a reader of flat text would find the old flag still in use.
    /// </summary>
    [Fact]
    public void ACommentNamingTheOldFlagIsNotTheOldFlag()
    {
        const string Script = """
            	# PP439: -V rather than --output-on-failure, because the number this gate needs is
            	# one that --output-on-failure discards on a green run.
            	timeout "$TEST_TIMEOUT" "$CTEST" --test-dir "$BUILD_DIR" -V >"$ctest_out" 2>&1
            """;

        Assert.True(CSuiteFloor.AsksCtestForTheCount(Script));

        // And the real thing is caught: the old flag in code, not in prose.
        Assert.False(CSuiteFloor.AsksCtestForTheCount(
            "\ttimeout 120 \"$CTEST\" --test-dir build --output-on-failure -V\n"));
    }

    /// <summary>PP272: and an empty script asks for nothing and reads nothing.</summary>
    [Fact]
    public void AnEmptyScriptEnforcesNothing()
    {
        Assert.False(CSuiteFloor.AsksCtestForTheCount(""));
        Assert.False(CSuiteFloor.ReadsTheFloor(""));
        Assert.Equal(0, CSuiteFloor.PrintsTheCaptureCount(""));
        Assert.Null(CSuiteFloor.Read(""));
    }

    /// <summary>
    /// And the floor file is one the C suite's own gate names, not a path only this class knows.
    /// </summary>
    [Fact]
    public void TheTwoHalvesNameTheSameFile()
    {
        if (CSuiteFloor.LocateGate() is not { } path)
            return;

        string leaf = Path.GetFileName(CSuiteFloor.FloorRelativePath);
        Assert.Contains(leaf, File.ReadAllText(path), StringComparison.Ordinal);
    }
}
