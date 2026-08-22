using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP270: the guard PP269 did not leave behind.
///
/// <see cref="TheShimThisRunLoadedIsNotOlderThanItsSources"/> is the one that runs against this
/// checkout. The rest exercise the comparison itself, against files a test writes, because a rule
/// that only ever sees one arrangement is a rule nobody has tested.
/// </summary>
public class NativeFreshnessTests
{
    /// <summary>
    /// THE GUARD. Fails when the library a run loaded is older than the sources it is built from.
    ///
    /// A failure rather than a warning: PP269's DLL was four hours stale through a run that ended
    /// green, and a line on standard error would have scrolled past.
    /// </summary>
    [Fact]
    public void TheShimThisRunLoadedIsNotOlderThanItsSources()
    {
        // Something has to have called into it for there to be a path at all.
        _ = NativeBase64.Encode(new byte[3], new byte[5]);

        Freshness freshness = NativeFreshness.Check(ChiakiNative.LoadedFrom);

        // In a checkout it must actually COMPARE. A verdict of no-sources here would be the check
        // passing for the reason it exists to catch, which is how the first version of it behaved.
        Assert.Equal(FreshnessVerdict.Fresh, freshness.Verdict);
        Assert.NotNull(freshness.Newest);

        if (freshness.Verdict == FreshnessVerdict.Stale)
            Assert.Fail(NativeFreshness.Explain(freshness));
    }

    /// <summary>A run that never loaded it has nothing to compare, and says so.</summary>
    [Fact]
    public void ARunThatNeverLoadedItSaysSo()
    {
        Assert.Equal(FreshnessVerdict.NotLoaded, NativeFreshness.Check(null).Verdict);
        Assert.Equal(FreshnessVerdict.NotLoaded, NativeFreshness.Check("").Verdict);

        // A path that is not there either - a recorded name from another machine, say.
        Assert.Equal(
            FreshnessVerdict.NotLoaded,
            NativeFreshness.Check(Path.Combine(Path.GetTempPath(), "no-such-shim.dll")).Verdict);
    }

    /// <summary>And a host with no checkout beside it is ordinary rather than stale.</summary>
    [Fact]
    public void AHostWithNoSourcesIsNotStale()
    {
        string library = Path.GetTempFileName();
        string empty = Directory.CreateTempSubdirectory().FullName;

        try
        {
            Assert.Equal(
                FreshnessVerdict.NoSources, NativeFreshness.Check(library, empty).Verdict);

            Assert.Equal(
                FreshnessVerdict.NoSources,
                NativeFreshness.Check(library, Path.Combine(empty, "gone")).Verdict);
        }
        finally
        {
            File.Delete(library);
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>A source written after the library is what stale means.</summary>
    [Fact]
    public void ASourceWrittenAfterTheLibraryIsStale()
    {
        string library = Path.GetTempFileName();
        string sources = Directory.CreateTempSubdirectory().FullName;

        try
        {
            string source = Path.Combine(sources, "chiaki_shim.c");
            File.WriteAllText(source, "/* newer */");

            File.SetLastWriteTimeUtc(library, DateTime.UtcNow.AddHours(-4));
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow);

            Freshness freshness = NativeFreshness.Check(library, sources);

            Assert.Equal(FreshnessVerdict.Stale, freshness.Verdict);
            Assert.Equal(source, freshness.Newest);

            // And what it would tell a reader names both files.
            string explained = NativeFreshness.Explain(freshness);
            Assert.Contains(library, explained, StringComparison.Ordinal);
            Assert.Contains(source, explained, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(library);
            Directory.Delete(sources, recursive: true);
        }
    }

    /// <summary>
    /// A library written after its sources is fresh, and one written at the same instant is too -
    /// otherwise a filesystem whose stamps agree to the second fails every run.
    /// </summary>
    [Fact]
    public void EqualStampsCountAsFresh()
    {
        string library = Path.GetTempFileName();
        string sources = Directory.CreateTempSubdirectory().FullName;

        try
        {
            string source = Path.Combine(sources, "chiaki_shim.h");
            File.WriteAllText(source, "/* same instant */");

            DateTime instant = DateTime.UtcNow.AddMinutes(-1);
            File.SetLastWriteTimeUtc(library, instant);
            File.SetLastWriteTimeUtc(source, instant);

            Assert.Equal(FreshnessVerdict.Fresh, NativeFreshness.Check(library, sources).Verdict);

            // And plainly newer is plainly fresh.
            File.SetLastWriteTimeUtc(library, instant.AddMinutes(1));
            Assert.Equal(FreshnessVerdict.Fresh, NativeFreshness.Check(library, sources).Verdict);
        }
        finally
        {
            File.Delete(library);
            Directory.Delete(sources, recursive: true);
        }
    }

    /// <summary>It looks at headers as well as sources, both of which are built into the library.</summary>
    [Fact]
    public void HeadersCountAsSourcesToo()
    {
        Assert.Contains("*.c", NativeFreshness.SourcePatterns);
        Assert.Contains("*.h", NativeFreshness.SourcePatterns);
    }
}
