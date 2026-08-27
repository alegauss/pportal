using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP407: eight stop-pipe inits in lib/src take a failure path and two asserted instead.
///
/// The assert is absent from the shipped build, so the session went on holding a handle every later
/// wait would fail on - and the failure arrives far from here, as a notification that never comes.
/// </summary>
public class StopPipeChecksTests(ITestOutputHelper output)
{
    /// <summary>THE TASK. Every site in the tree looks at what it got back.</summary>
    [Fact]
    public void EveryStopPipeInitIsChecked()
    {
        string? directory = StopPipeChecks.Locate();
        if (directory is null)
            return;

        IReadOnlyDictionary<string, IReadOnlyList<string>> sites = StopPipeChecks.Sites(directory);
        int total = sites.Values.Sum(a => a.Count);

        var asserted = new List<string>();
        foreach ((string file, IReadOnlyList<string> after) in sites)
        {
            foreach (string statement in after)
            {
                output.WriteLine($"{file,-18} {statement}");
                if (statement.StartsWith("assert", StringComparison.Ordinal))
                    asserted.Add($"{file}: {statement}");
            }
        }

        // PP271: a reader that stopped matching would find no site to object to and pass.
        Assert.True(
            total >= StopPipeChecks.Floor,
            $"{total} call sites found, below the floor of {StopPipeChecks.Floor}");

        Assert.Empty(asserted);
    }

    /// <summary>
    /// And holepunch.c in particular, which is where the two were.
    ///
    /// Named as well as counted: the rule above would pass on a tree that had deleted the file.
    /// </summary>
    [Fact]
    public void TheTwoInHolepunchAreAmongThem()
    {
        string? directory = StopPipeChecks.Locate();
        if (directory is null)
            return;

        string source = File.ReadAllText(Path.Combine(directory, "remote", "holepunch.c"));

        Assert.Equal(2, StopPipeChecks.InFile(source).Count);
        Assert.True(StopPipeChecks.EverySiteIsChecked(source), "holepunch.c asserts a stop pipe again");
    }

    /// <summary>PP272: and a file that creates no stop pipe answers no.</summary>
    [Fact]
    public void AFileWithNoStopPipeAnswersNo()
    {
        Assert.False(StopPipeChecks.EverySiteIsChecked(""));
        Assert.Empty(StopPipeChecks.InFile(""));

        // The two shapes, told apart.
        Assert.False(StopPipeChecks.EverySiteIsChecked(
            "err = chiaki_stop_pipe_init(&p);\n\tassert(err == CHIAKI_ERR_SUCCESS);"));
        Assert.True(StopPipeChecks.EverySiteIsChecked(
            "err = chiaki_stop_pipe_init(&p);\n\tif(err != CHIAKI_ERR_SUCCESS)\n\t\tgoto fail;"));

        // A note quoting the shape it replaced is not the shape - PP399, PP400, PP401, PP403.
        Assert.Empty(StopPipeChecks.InFile("// was chiaki_stop_pipe_init(&p); assert(err);"));
    }
}
