using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP530: the guard over the host that answers --recount and --ratchet.
///
/// <see cref="TheHostTheGateBuildsIsNotOlderThanAppSources"/> runs against this checkout. The
/// rest exercise the comparison against files a test writes, because a rule that has only seen a
/// passing arrangement has not been run against the one it exists for - and the arrangement it
/// exists for is precisely the one that was live on this machine for two days.
/// </summary>
public class HostFreshnessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pp530-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Where compile.cmd puts the host, and the path its closing message prints.</summary>
    public const string GateHostRelativePath = @"app\bin\Debug\net10.0-windows\win-x64\ChiakiNg.exe";

    /// <summary>
    /// THE GUARD, against the host the gate builds.
    ///
    /// Not <see cref="HostFreshness.Check()"/>: the process running this is the test host, not
    /// ChiakiNg.exe, so asking about it would date the wrong file and pass for the wrong reason.
    /// </summary>
    [Fact]
    public void TheHostTheGateBuildsIsNotOlderThanAppSources()
    {
        if (SanitizerSource.LocateRelative(GateHostRelativePath) is not { } host)
            return;

        HostBuild build = HostFreshness.Check(host);

        if (build.State == HostBuildState.Stale)
            Assert.Fail(HostFreshness.Explain(build));

        Assert.Equal(HostBuildState.Fresh, build.State);
    }

    /// <summary>The arrangement PP530 was filed for: a source written after the host.</summary>
    [Fact]
    public void ASourceNewerThanTheHostIsStale()
    {
        Arrange(hostAt: DateTime.UtcNow.AddDays(-2), sourceAt: DateTime.UtcNow);

        HostBuild build = HostFreshness.Check(Host(), Sources());

        Assert.Equal(HostBuildState.Stale, build.State);
        Assert.EndsWith("HostCommandLine.cs", build.Newest);
        Assert.Contains("compile.cmd", HostFreshness.Explain(build));
    }

    /// <summary>
    /// And a host built after them is not. Beside the case above because a rule that always
    /// answered Stale would satisfy that one and mean nothing.
    /// </summary>
    [Fact]
    public void AHostNewerThanItsSourcesIsFresh()
    {
        Arrange(hostAt: DateTime.UtcNow, sourceAt: DateTime.UtcNow.AddDays(-2));

        Assert.Equal(HostBuildState.Fresh, HostFreshness.Check(Host(), Sources()).State);
    }

    /// <summary>
    /// Build output is not a source. app\obj holds generated .cs that a build writes and app\bin
    /// holds the host being dated, so a sweep that read either would compare a build against its
    /// own products - and would report every freshly built host stale or fresh by accident.
    /// </summary>
    [Fact]
    public void GeneratedFilesUnderBinAndObjAreNotSources()
    {
        Arrange(hostAt: DateTime.UtcNow.AddHours(-1), sourceAt: DateTime.UtcNow.AddHours(-2));

        foreach (string excluded in HostFreshness.ExcludedDirectories)
        {
            string dir = Path.Combine(Sources(), excluded, "Debug");
            Directory.CreateDirectory(dir);
            string generated = Path.Combine(dir, "ChiakiNg.AssemblyInfo.cs");
            File.WriteAllText(generated, "// generated\n");
            File.SetLastWriteTimeUtc(generated, DateTime.UtcNow);
        }

        Assert.Equal(HostBuildState.Fresh, HostFreshness.Check(Host(), Sources()).State);
    }

    /// <summary>
    /// A name that merely starts with an excluded one is a source. The exclusion is by path
    /// segment: binding.cs is not build output, and a substring rule would decide it was and
    /// would then never notice it changing.
    /// </summary>
    [Fact]
    public void AFileWhoseNameStartsWithAnExcludedDirectoryIsStillASource()
    {
        Arrange(hostAt: DateTime.UtcNow.AddHours(-1), sourceAt: DateTime.UtcNow.AddHours(-2));

        string binding = Path.Combine(Sources(), "Session", "binding.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(binding)!);
        File.WriteAllText(binding, "// a source\n");
        File.SetLastWriteTimeUtc(binding, DateTime.UtcNow);

        HostBuild build = HostFreshness.Check(Host(), Sources());

        Assert.Equal(HostBuildState.Stale, build.State);
        Assert.EndsWith("binding.cs", build.Newest);
    }

    /// <summary>
    /// A published host has no checkout beside it and is not at fault. Kept apart from Fresh so
    /// that "could not compare" can never be read as "compared and was happy".
    /// </summary>
    [Fact]
    public void NoSourcesIsNoCheckoutRatherThanFresh()
    {
        Arrange(hostAt: DateTime.UtcNow, sourceAt: DateTime.UtcNow.AddDays(-2));
        Directory.Delete(Sources(), recursive: true);

        HostBuild build = HostFreshness.Check(Host(), Sources());

        Assert.Equal(HostBuildState.NoCheckout, build.State);
        Assert.Null(build.Newest);
    }

    /// <summary>A host that is not there cannot be dated, and says which.</summary>
    [Fact]
    public void AHostThatIsNotThereIsUnknown()
    {
        Arrange(hostAt: null, sourceAt: DateTime.UtcNow);

        Assert.Equal(HostBuildState.Unknown, HostFreshness.Check(Host(), Sources()).State);
    }

    private string Host() => Path.Combine(root, "ChiakiNg.exe");

    private string Sources() => Path.Combine(root, "app");

    /// <summary>A host and one source, stamped as the caller asks.</summary>
    private void Arrange(DateTime? hostAt, DateTime sourceAt)
    {
        string session = Path.Combine(Sources(), "Session");
        Directory.CreateDirectory(session);

        string source = Path.Combine(session, "HostCommandLine.cs");
        File.WriteAllText(source, "// a source\n");
        File.SetLastWriteTimeUtc(source, sourceAt);

        if (hostAt is not { } at)
            return;

        File.WriteAllText(Host(), "MZ");
        File.SetLastWriteTimeUtc(Host(), at);
    }
}
