using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP785: takion's oracles are askable, and every differential asks before it runs.
///
/// PP780 put a number on what PP27's end state costs: taking takion.c, takionsendbuffer.c and
/// reorderqueue.c out leaves twenty-four symbols undefined, and this shim holds EIGHTEEN of them -
/// twice what lib's own four files hold between them. They are not consumers in the ordinary sense.
/// A managed reorder queue, send buffer, congestion formatter, MAC gate and AV parser are each held
/// against the C they replace, so the deletion removes what proves the replacement right at the
/// same moment it removes the thing being replaced.
///
/// PP662 MEASURED WHAT AN UNGUARDED CALLER COSTS: 128 assertions red at once, on a build where
/// every guard reported the oracle present and every call threw. That is why the question is asked
/// of the DLL and never of the header text - an #ifdef leaves the declarations exactly where a text
/// reader looks for them.
///
/// THIS IS PP623'S FIRST STEP AND NOT THE DELETION. The define is unconditional, every wrapper is
/// still exported, and the three files are still in lib's list. What has changed is that the
/// deletion can now be a CMake line rather than a commit that turns forty assertions red.
/// </summary>
public class TakionOracleShapeTests(ITestOutputHelper output)
{
    /// <summary>
    /// The managed spellings that reach one of the shim's takion wrappers.
    ///
    /// The census below is keyed on these, so a differential that reached the C by some other
    /// spelling would be missed - which is why they are the four wrapper TYPES plus the static
    /// class's own methods rather than a pattern over "takion".
    /// </summary>
    private static IReadOnlyList<string> OracleCalls { get; } =
    [
        "NativeReorderQueue",
        "NativeAvPacketV7",
        "NativeTakionLoopback",
        "new SendBuffer(",
        "Takion.ParseV9(",
        "Takion.FormatCongestion(",
        "Takion.CongestionPacketSize",
        "Takion.WritePacketMac(",
        "Takion.PacketMacWithoutCrypt(",
    ];

    /// <summary>The guard, spelled as every caller spells it.</summary>
    private const string TheGuard = "ShimTakionShape.WrappingHeader()";

    /// <summary>
    /// THE BUILD ANSWERS THE QUESTION, which is the whole of what the export is for.
    ///
    /// True on this tree because the three files are still in lib's list. Asserted as an agreement
    /// between two facts rather than as a constant, so the day one of them moves the other has to
    /// move with it - which is what the deletion is.
    /// </summary>
    [Fact]
    public void TheShimSaysWhetherItCarriesTakionsOracles()
    {
        ShimShape shape = ShimTakionShape.OfTheBuild();
        output.WriteLine($"the built shim is {shape}");

        Assert.True(ShimTakionShape.ExactlyOneShapeAnswers());

        if (TakionConsumers.Locate(@"lib\CMakeLists.txt") is not { } cmake)
            return;

        // The three files PP27's end state removes, still named by lib's own list.
        string list = File.ReadAllText(cmake);
        bool built = TakionConsumers.Leaving.All(
            one => list.Contains(
                one.Replace('\\', '/').Replace("lib/", string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal));

        Assert.Equal(built, shape == ShimShape.Wrapping);
    }

    /// <summary>
    /// EVERY FILE THAT CALLS ONE ASKS FIRST, which is the assertion PP662's 128 red bought.
    ///
    /// Read as CODE and not as text: <see cref="DeadAssertions.CodeOnly"/> strips comments and
    /// blanks string contents, which matters here because this very file spells all nine call
    /// shapes in literals - a reader keyed on flat text would report itself as an unguarded caller
    /// and every other file as guarded by the paragraph above its guard.
    /// </summary>
    [Fact]
    public void EveryDifferentialAsksBeforeItReachesTheC()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string tests = Path.Combine(root, "tests", "ChiakiNg.Tests");
        if (!Directory.Exists(tests))
            return;

        var unguarded = new List<string>();
        var guarded = new List<string>();

        foreach (string file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            // This file is the census and spells every call shape it looks for.
            if (Path.GetFileName(file).Equals(
                    "TakionOracleShapeTests.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string code = DeadAssertions.CodeOnly(File.ReadAllText(file));

            if (!OracleCalls.Any(one => code.Contains(one, StringComparison.Ordinal)))
                continue;

            if (code.Contains(TheGuard, StringComparison.Ordinal))
                guarded.Add(Path.GetFileName(file));
            else
                unguarded.Add(Path.GetFileName(file));
        }

        output.WriteLine($"{guarded.Count} guarded: {string.Join(", ", guarded)}");

        // PP271: a sweep that found nothing would pass about a tree it never read.
        Assert.NotEmpty(guarded);
        Assert.Empty(unguarded);
    }

    /// <summary>
    /// AND THE ALLOWANCE IS DERIVED FROM THE SHAPE, so the deletion edits NativeSeam not at all.
    ///
    /// Empty while the build carries the wrappers - every one of them is checked by the census
    /// exactly as before - and exactly <see cref="ShimTakionShape.GoneWhenBare"/> afterwards. PP656's
    /// rule, and the reason step one is worth being its own task.
    /// </summary>
    [Fact]
    public void TheSeamsAllowanceFollowsTheShapeRatherThanBeingDeclared()
    {
        IReadOnlySet<string> allowed = NativeSeam.ImportsOnlyATakionBuildResolves();

        if (ShimTakionShape.OfTheBuild() == ShimShape.Wrapping)
        {
            Assert.Empty(allowed);
            return;
        }

        Assert.Equal([.. ShimTakionShape.GoneWhenBare.Order(StringComparer.Ordinal)],
            [.. allowed.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// THE NAMES ARE THE HEADER'S, which is what keeps the allowance honest.
    ///
    /// An entry here that the header never declared would be an import the census stopped checking
    /// for no reason at all - the failure PP657 derived its own allowance to avoid. Asserted while
    /// the header still declares them, because that is the side that can tell.
    /// </summary>
    [Fact]
    public void EveryNameTheAllowanceCoversIsOneTheHeaderDeclares()
    {
        if (ShimTakionShape.WrappingHeader() is not { } header)
            return;

        Assert.Equal(
            ShimTakionShape.GoneWhenBare.Count,
            ShimTakionShape.StillDeclaredIn(header).Count);
    }

    /// <summary>
    /// AND THE BARE SIDE IS NOT A WAY OF NOT LOOKING (PP630).
    ///
    /// Every guard above returns early once the oracle goes, so something has to run on the other
    /// side or the whole set becomes a pass that measured nothing. This is that: on a bare build the
    /// exports are really gone, asked of the DLL by calling one and catching what the loader says
    /// rather than by reading the header it is still declared in.
    /// </summary>
    [Fact]
    public void OnABareBuildTheWrappersAreActuallyGone()
    {
        if (ShimTakionShape.BareHeader() is null)
            return;

        Assert.Throws<EntryPointNotFoundException>(() => Takion.CongestionPacketSize);
    }
}
