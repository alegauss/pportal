using System.Text.Json;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP162: the one thing about this suite that cannot be asserted by running it.
///
/// The view tests each open a WPF element on their own STA thread. Two such threads racing through
/// WPF's first-touch static initialisation deadlock, and the suite then fails every view test at
/// once with "the STA thread did not finish" - 30 seconds each, four minutes of red, and every one
/// of them green when run alone. Whether it happens depends on which collections the runner
/// happens to schedule together, so the same source was green twice and red three times.
///
/// xunit.runner.json turns collection parallelism off and the whole suite passes in about a
/// second. The failure mode that outlives the fix is the file not REACHING the runner: it is read
/// from beside the assembly, so a build that stops copying it puts the deadlock back with the
/// config still sitting in the tree looking correct. That is what this asserts.
/// </summary>
public class RunnerConfigTests
{
    [Fact]
    public void TheRunnerConfigIsBesideTheAssemblyAndSerial()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");

        Assert.True(File.Exists(path),
            $"xunit.runner.json is not in {AppContext.BaseDirectory}, so the runner never read it "
            + "and the view tests are racing again");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));

        Assert.True(
            config.RootElement.TryGetProperty("parallelizeTestCollections", out JsonElement parallel),
            "xunit.runner.json says nothing about collection parallelism");
        Assert.False(parallel.GetBoolean(), "collection parallelism must stay off");
    }
}
