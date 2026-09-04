using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP705: the exclusion four sweeps each wrote for themselves, asked of the swept file instead.
///
/// Five classes here sweep the assembly for something nothing may say, and each of them is a file
/// containing every phrase it forbids - so each has to be skipped by its own sweep. Every one wrote
/// that skip itself, seven clauses of it, and PP691 made the cost visible by adding a fifth such
/// file and having to edit two of the four to say so.
///
/// THE EXCLUSION IS A PROPERTY OF THE SWEPT FILE. A recording file says so in a comment and every
/// sweep asks, so a sixth arriving needs no edit anywhere else. That is the difference between this
/// and a shared list of names: a list is the same four names in one place, and a census added
/// without touching it does not go red where it was written - it turns the OTHER sweeps into false
/// reports of an offender, which is the failure somebody fixes by softening a sweep.
/// </summary>
public class PhraseCensusTests(ITestOutputHelper output)
{
    /// <summary>The files under app/ that record the phrases they judge, by name.</summary>
    private static readonly string[] Recording =
    [
        "ComSignatures.cs",
        "LibRepairCensus.cs",
        "ManagedBoundaryRule.cs",
        "MicrophoneSurface.cs",
        "RoadmapProseReaders.cs",
    ];

    /// <summary>
    /// And one in the test project, which is a recording file for a different reason.
    ///
    /// ComSignatures sweeps app/, tests/, spike/ and tools/, and its own TESTS declare the defect on
    /// purpose - four of them, which is what the check reported on its first run. So the fixture
    /// carries the marker too, and the shape stops being "app/ has censuses and tests do not".
    /// </summary>
    private const string FixtureFile = @"tests\ChiakiNg.Tests\ComSignaturesTests.cs";

    /// <summary>
    /// EVERY CENSUS SAYS SO ITSELF, which is what makes the four sweeps agree without a list.
    ///
    /// Read from the files rather than asserted about a constant: the marker is only worth anything
    /// if it is actually in them, and a class that lost it would be reported by every other sweep as
    /// an offender rather than failing where it was edited.
    /// </summary>
    [Fact]
    public void EveryRecordingFileCarriesTheMarker()
    {
        if (SanitizerSource.LocateDirectory("app") is not { } root)
            return;

        foreach (string name in Recording)
        {
            string[] found = Directory.GetFiles(root, name, SearchOption.AllDirectories);
            Assert.True(found.Length == 1, $"{name} is not in app/ exactly once");

            Assert.True(
                PhraseCensus.RecordsFile(found[0]),
                $"{name} judges phrases and does not carry PhraseCensus.Marker, so every sweep in "
                    + "this assembly will report it as an offender");
        }

        if (SanitizerSource.LocateRelative(FixtureFile) is { } fixture)
            Assert.True(PhraseCensus.RecordsFile(fixture), $"{FixtureFile} does not carry the marker");
    }

    /// <summary>
    /// And nothing else does, so the marker is not a way of opting out of a sweep.
    ///
    /// The risk this shape carries and the one a list does not: a file that finds itself reported
    /// can silence the report by declaring itself a census. So the set is asserted, and a sixth
    /// arriving is a deliberate edit here rather than a quiet one over there.
    /// </summary>
    [Fact]
    public void OnlyThoseFilesCarryIt()
    {
        if (SanitizerSource.LocateDirectory("app") is not { } root)
            return;

        string[] carrying =
        [
            .. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(one => !one.Contains(@"\obj\", StringComparison.Ordinal))
                .Where(one => !one.Contains(@"\bin\", StringComparison.Ordinal))
                .Where(PhraseCensus.RecordsFile)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine(string.Join(", ", carrying));

        // PhraseCensus.cs itself is one, because it spells the marker. Nothing there is forbidden by
        // any of the five, so being skipped costs nothing.
        Assert.Equal(
            Recording.Append("PhraseCensus.cs").Order(StringComparer.Ordinal),
            carrying);
    }

    /// <summary>
    /// The four sweeps are all empty, which is what says the predicate replaced the clauses.
    ///
    /// Each of these passed before with its own exclusion written by hand. What they hold now is
    /// that the shared answer is the same answer - a predicate that excluded nothing would report
    /// every census as an offender, and one that excluded everything would report none of anything.
    /// </summary>
    [Fact]
    public void TheFourSweepsAreStillEmpty()
    {
        if (SanitizerSource.LocateDirectory("app") is null)
            return;

        Assert.Empty(LibRepairCensus.FilesStatingTheFalsePremise());
        Assert.Empty(ManagedBoundaryRule.ManagedFilesPromisingIt());
        Assert.Empty(ComSignatures.UnpreservedInTheTree());

        // And the one that is not about a forbidden phrase but about a surface: the capture sweep
        // names the files that capture, and its census is not one of them.
        Assert.DoesNotContain(
            "Session\\MicrophoneSurface.cs",
            MicrophoneSurface.FilesThatCapture(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// And they still SEE something, so the predicate did not empty them by excluding the tree.
    ///
    /// PP271, and it is the assertion that matters most here: three of the four above pass by
    /// finding nothing, and a sweep that read no files at all would pass them all.
    /// </summary>
    [Fact]
    public void TheSweepsStillReadTheTree()
    {
        if (SanitizerSource.LocateDirectory("app") is null)
            return;

        Assert.NotEmpty(MicrophoneSurface.FilesThatCapture());
        Assert.NotEmpty(ComSignatures.FilesDeclaringComInterfaces());
    }

    /// <summary>The predicate reads text, so a file that moves keeps its answer.</summary>
    [Fact]
    public void TheMarkerIsReadFromTheTextAndNotFromAPath()
    {
        Assert.True(PhraseCensus.Records($"// {PhraseCensus.Marker}, so sweeps skip it."));
        Assert.False(PhraseCensus.Records("// an ordinary file that judges nothing"));
        Assert.False(PhraseCensus.Records(string.Empty));
    }

    /// <summary>Sweepable drops build output as well, which all four used to write twice each.</summary>
    [Fact]
    public void SweepableDropsBuildOutput()
    {
        string[] paths =
        [
            @"C:\r\app\Session\Thing.cs",
            @"C:\r\app\obj\Debug\Thing.g.cs",
            @"C:\r\app\bin\Release\Thing.cs",
        ];

        Assert.Equal([@"C:\r\app\Session\Thing.cs"], PhraseCensus.Sweepable(paths));
    }
}
