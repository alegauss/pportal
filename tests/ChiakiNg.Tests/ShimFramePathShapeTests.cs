using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP670: the two shapes of the frame path's seam, and the side that runs on THIS tree.
///
/// The six differentials now ask <see cref="ShimFramePathShape.WrappingHeader"/> before they call
/// an oracle. This file is what keeps that from being a way of not looking: on a tree that wraps,
/// the fourteen are declared and the build says so; on a tree that is bare, the build says so, the
/// census allows exactly the fourteen, and calling one really does fail at the loader. Exactly one
/// of the two sides runs anywhere, and that is asserted rather than assumed.
/// </summary>
public class ShimFramePathShapeTests(ITestOutputHelper output)
{
    /// <summary>PP630's property: two answers is unmodelled, none is every check declining.</summary>
    [Fact]
    public void ExactlyOneShapeAnswers()
    {
        output.WriteLine($"build: {ShimFramePathShape.OfTheBuild()}");
        Assert.True(ShimFramePathShape.ExactlyOneShapeAnswers());
    }

    /// <summary>
    /// While the build wraps, every one of the fourteen is declared - the set the flip removes is
    /// the set the header has, and nothing has already gone missing one at a time.
    /// </summary>
    [Fact]
    public void WhileItWrapsAllFourteenAreDeclared()
    {
        if (ShimFramePathShape.WrappingHeader() is not { } header)
            return;

        IReadOnlyList<string> declared = ShimFramePathShape.StillDeclaredIn(header);
        output.WriteLine($"{declared.Count} of {ShimFramePathShape.GoneWhenBare.Count} declared");

        Assert.Equal(ShimFramePathShape.GoneWhenBare, declared);

        // And the export the guards ask really answers, rather than the EntryPointNotFound fallback
        // standing in for it: a shim built before PP670 reports Wrapping for the wrong reason.
        Assert.Empty(NativeSeam.ImportsOnlyAFramePathBuildResolves());
    }

    /// <summary>
    /// Once it is bare, the BUILD says so - the declarations survive the flip inside an #ifdef, so
    /// the text alone would say wrapping (PP661) - the census allows exactly the fourteen, and one
    /// of them called is an import the loader cannot resolve.
    /// </summary>
    [Fact]
    public void OnceItIsBareTheExportsAreReallyGone()
    {
        if (ShimFramePathShape.BareHeader() is null)
            return;

        Assert.Equal(ShimShape.Bare, ShimFramePathShape.OfTheBuild());
        Assert.Equal(
            ShimFramePathShape.GoneWhenBare.Order(StringComparer.Ordinal),
            NativeSeam.ImportsOnlyAFramePathBuildResolves().Order(StringComparer.Ordinal));

        // Not from the text: from the DLL. This is the assertion that makes the bare side a check.
        Assert.Throws<EntryPointNotFoundException>(() => FecMatrix.Native(4, 2));
    }

    /// <summary>
    /// The fourteen are exactly the frame-path exports the header declares, by prefix - so a
    /// wrapper added to the shim later is a red here rather than an import the census silently
    /// keeps checking after the flip.
    /// </summary>
    [Fact]
    public void TheFourteenAreEveryFramePathExportTheHeaderDeclares()
    {
        if (ShimFramePathShape.Read() is not { } header)
            return;

        var declared = new List<string>();
        foreach (string line in header.Split('\n'))
        {
            foreach (string prefix in new[] { "chiaki_shim_fec_", "chiaki_shim_frame_processor_", "chiaki_shim_video_receiver_" })
            {
                int at = line.IndexOf(prefix, StringComparison.Ordinal);
                if (at < 0 || !line.Contains("CHIAKI_SHIM_API", StringComparison.Ordinal))
                    continue;

                int end = line.IndexOf('(', at);
                if (end > at)
                    declared.Add(line[at..end].Trim());
            }
        }

        Assert.Equal(
            ShimFramePathShape.GoneWhenBare.Order(StringComparer.Ordinal),
            declared.Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Every one of the six differential files carries at least one guard, and the census counts
    /// them - the gate says how many comparisons a bare build skips (PP663's cost, kept visible).
    /// </summary>
    [Fact]
    public void EveryDifferentialFileIsGuardedAndCounted()
    {
        IReadOnlyList<(GuardedFile File, int Guards)> counted = OracleGuardCensus.Counted();
        if (counted.Count == 0)
            return;

        var framePath = counted.Where(c => c.File.Guard == OracleGuardCensus.FramePathGuard).ToList();
        output.WriteLine(string.Join(", ", framePath.Select(c => $"{Path.GetFileName(c.File.Where)}={c.Guards}")));

        Assert.Equal(6, framePath.Count);
        Assert.All(framePath, c => Assert.True(c.Guards >= 1, $"{c.File.Where} has no frame-path guard"));
    }
}
