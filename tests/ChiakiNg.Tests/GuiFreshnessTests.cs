using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP529: the guard over the one tree no build in this repository compiles.
///
/// <see cref="TheQtClientIsNotOlderThanTheSourcesBesideIt"/> is the one that runs against this
/// checkout. The rest exercise the comparison itself against files a test writes, because a rule
/// that has only ever seen a passing arrangement has not been run against the case it exists for.
/// </summary>
public class GuiFreshnessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pp529-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE GUARD. Fails when a source under gui/ is newer than the client last built from it.
    ///
    /// NeverBuilt is accepted and is not a pass in disguise - it is asserted as itself, so a run
    /// in a checkout that has never built the client says so rather than reporting a comparison it
    /// did not make. That is the honest limit of this rule: it guards everyone who has built the
    /// client once, which is everyone who edits gui/ and rebuilds, and it cannot guard a clone
    /// that has never built it at all.
    /// </summary>
    [Fact]
    public void TheQtClientIsNotOlderThanTheSourcesBesideIt()
    {
        GuiBuild build = GuiFreshness.Check();

        // PP632: the only two answers left. Nothing builds the client, so nothing about it can be
        // stale - and a binary found here is one somebody built before the retirement, named as
        // such rather than compared against sources it can never be rebuilt from.
        Assert.True(
            build.State is GuiBuildState.NeverBuilt or GuiBuildState.NoCheckout
                or GuiBuildState.Retired,
            $"the freshness check answered {build.State} on a tree where nothing builds a client");

        Assert.Null(build.Newest);

        if (build.State == GuiBuildState.Retired)
            Assert.Contains("retired", GuiFreshness.Explain(build), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP632: a source written after the client used to be what this caught, and the comparison is
    /// kept where it can still be exercised.
    ///
    /// `WouldHaveCheckedIn` is that comparison. It is not called by anything that gates a commit -
    /// there is no client to rebuild - and it is not deleted, because a gui/ that becomes buildable
    /// again needs exactly this rule, and the two details it carries each took a task: equal stamps
    /// count as fresh, and the parent directory is out of scope.
    /// </summary>
    [Fact]
    public void TheComparisonItUsedToMakeIsStillRight()
    {
        Arrange(clientAt: DateTime.UtcNow.AddHours(-4), sourceAt: DateTime.UtcNow);

        GuiBuild build = GuiFreshness.WouldHaveCheckedIn(root);

        Assert.Equal(GuiBuildState.Stale, build.State);
        Assert.EndsWith("qmlbackend.cpp", build.Newest);

        // And the live check answers Retired on the same arrangement, which is the change.
        Assert.Equal(GuiBuildState.Retired, GuiFreshness.CheckIn(root).State);
    }

    /// <summary>
    /// And a client built after them is not. Asserted beside the case above because a rule that
    /// always answered Stale would pass that one and mean nothing.
    /// </summary>
    [Fact]
    public void AClientNewerThanItsSourcesIsFresh()
    {
        Arrange(clientAt: DateTime.UtcNow, sourceAt: DateTime.UtcNow.AddHours(-4));

        // PP632: through the comparison that is kept, because the live one answers Retired now.
        Assert.Equal(GuiBuildState.Fresh, GuiFreshness.WouldHaveCheckedIn(root).State);
    }

    /// <summary>
    /// A checkout where nothing was ever built compares nothing and says which. The two absent
    /// states are kept apart so that a run reporting NeverBuilt cannot be read as a comparison
    /// that came back clean.
    /// </summary>
    [Fact]
    public void NoClientIsNeverBuiltRatherThanFresh()
    {
        Arrange(clientAt: null, sourceAt: DateTime.UtcNow);

        GuiBuild build = GuiFreshness.CheckIn(root);

        Assert.Equal(GuiBuildState.NeverBuilt, build.State);
        Assert.Null(build.Client);
        Assert.Null(build.Newest);
    }

    /// <summary>
    /// The parent directory is out of scope on purpose. gui\CMakeLists.txt is touched by
    /// configure, and a rule that read it would call the client stale immediately after the build
    /// that produced it - which is a guard that cries wolf until somebody deletes it.
    /// </summary>
    [Fact]
    public void TheParentDirectoryIsNotASource()
    {
        Arrange(clientAt: DateTime.UtcNow.AddHours(-4), sourceAt: DateTime.UtcNow.AddHours(-8));

        string cmake = Path.Combine(root, "gui", "CMakeLists.txt");
        File.WriteAllText(cmake, "project(chiaki)\n");
        File.SetLastWriteTimeUtc(cmake, DateTime.UtcNow);

        // PP632: through the kept comparison, for the reason above.
        Assert.Equal(GuiBuildState.Fresh, GuiFreshness.WouldHaveCheckedIn(root).State);
    }

    /// <summary>
    /// A checkout with the client and the sources, stamped as the caller asks. A null client
    /// leaves the binary out, which is the never-built arrangement.
    /// </summary>
    private void Arrange(DateTime? clientAt, DateTime sourceAt)
    {
        string src = Path.Combine(root, "gui", "src");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(Path.Combine(root, "build", "gui"));

        string source = Path.Combine(src, "qmlbackend.cpp");
        File.WriteAllText(source, "int main(){return 0;}\n");
        File.SetLastWriteTimeUtc(source, sourceAt);

        if (clientAt is not { } at)
            return;

        string client = Path.Combine(root, GuiFreshness.ClientRelativePath);
        File.WriteAllText(client, "MZ");
        File.SetLastWriteTimeUtc(client, at);
    }
}
