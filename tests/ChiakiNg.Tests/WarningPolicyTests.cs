using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP316: the warnings the gate does not read.
///
/// <see cref="EveryGatedProjectRefusesEveryWarning"/> carries the task. Without the csproj change it
/// is red, and it is red in the state this port was in for as long as the four warnings printed:
/// green everywhere while the compiler said, in every run, that a test assertion compared two
/// literals and could not fail.
///
/// The parsing tests beneath it exist because the assertion above is a read of an XML file, and a
/// reader that returns true too easily is a gate that is not there. The one that matters is
/// <see cref="AProjectSayingFalseIsNotAProjectRefusingWarnings"/>: switching this policy off is
/// spelled by editing a value and not by deleting a line.
/// </summary>
public class WarningPolicyTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE TASK. Every project the gate compiles turns every compiler warning into an error.
    ///
    /// PP438: "every" and not "both". The set is read out of ChiakiNg.slnx, which is what compile.cmd
    /// builds, so a project added to the solution is bound by this without anybody editing a list.
    /// </summary>
    [Fact]
    public void EveryGatedProjectRefusesEveryWarning()
    {
        IReadOnlyList<string> projects = WarningPolicy.LocateGatedProjects();
        Assert.True(projects.Count > 0, "not running out of a checkout");

        // Declared and present are separate failures: a solution entry naming a csproj that is not
        // there resolves to nothing, and a policy asserted over what resolved would pass about it.
        Assert.Equal(WarningPolicy.GatedProjects().Count, projects.Count);

        IReadOnlyList<string> printing =
            [.. projects.Where(path => !WarningPolicy.RefusesEveryWarning(File.ReadAllText(path)))];

        output.WriteLine("gated: " + string.Join(", ", WarningPolicy.GatedProjects()));

        Assert.True(
            printing.Count == 0,
            "these projects print warnings instead of failing on them, which is how xUnit2000 named "
                + "a dead assertion in every run for as long as it stood: " + string.Join(", ", printing));
    }

    /// <summary>
    /// And no project walks the policy back out one code at a time. NoWarn is the door.
    /// </summary>
    [Fact]
    public void NothingIsSilencedThatIsNotAccountedFor()
    {
        IReadOnlyList<string> projects = WarningPolicy.LocateGatedProjects();
        Assert.True(projects.Count > 0, "not running out of a checkout");

        IReadOnlyList<string> unaccounted =
        [
            .. projects
                .SelectMany(path => WarningPolicy.SuppressedIn(File.ReadAllText(path)))
                .Where(code => !WarningPolicy.AllowedSuppressions.Contains(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        Assert.True(
            unaccounted.Count == 0,
            "silenced without being named in AllowedSuppressions, so the reason is in a commit "
                + "message rather than in the tree: " + string.Join(", ", unaccounted));
    }

    /// <summary>The value is read, and true is the only answer that passes.</summary>
    [Fact]
    public void ARefusalIsReadOffTheValue()
    {
        Assert.True(WarningPolicy.RefusesEveryWarning(
            "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>"));

        // Whitespace and casing are MSBuild's, not this port's.
        Assert.True(WarningPolicy.RefusesEveryWarning("<TreatWarningsAsErrors> True </TreatWarningsAsErrors>"));
    }

    /// <summary>
    /// THE DIRECTION THAT MATTERS. A project that declares the property and sets it to false is
    /// not refusing anything, and a check testing for the element's presence would call it a pass -
    /// which is the shape a quiet walk-back would take.
    /// </summary>
    [Fact]
    public void AProjectSayingFalseIsNotAProjectRefusingWarnings()
    {
        Assert.False(WarningPolicy.RefusesEveryWarning("<TreatWarningsAsErrors>false</TreatWarningsAsErrors>"));
        Assert.False(WarningPolicy.RefusesEveryWarning("<TreatWarningsAsErrors></TreatWarningsAsErrors>"));
        Assert.False(WarningPolicy.RefusesEveryWarning("<Project><PropertyGroup /></Project>"));
    }

    /// <summary>Suppressed codes come back, and the MSBuild reference beside them does not.</summary>
    [Fact]
    public void TheSuppressionsAreCodesAndNotMSBuildReferences()
    {
        IReadOnlyList<string> codes =
            WarningPolicy.SuppressedIn("<NoWarn>$(NoWarn);WPF0001;CS0108</NoWarn>");

        Assert.Equal(["WPF0001", "CS0108"], codes);
    }

    /// <summary>Two NoWarn elements are one list, and a code named twice is named once.</summary>
    [Fact]
    public void TheSuppressionsAcrossElementsAreOneList()
    {
        IReadOnlyList<string> codes =
            WarningPolicy.SuppressedIn("<NoWarn>WPF0001</NoWarn><NoWarn>CS8123 WPF0001</NoWarn>");

        Assert.Equal(["WPF0001", "CS8123"], codes);
    }

    /// <summary>
    /// The host is the project PP22 already models, named once. A second constant spelling
    /// <c>app\ChiakiNg.csproj</c> is a third copy of a path this tree already has two of.
    /// </summary>
    [Fact]
    public void TheHostIsTheProjectPP22AlreadyNames()
        => Assert.Contains(BuildWorkflow.HostProjectRelativePath, WarningPolicy.GatedProjects());

    /// <summary>
    /// PP438: the set is read out of the solution, and the three it holds today are named.
    ///
    /// The tool is the one that made this necessary: PP436 put it in the solution and it declared no
    /// TreatWarningsAsErrors, so the gate compiled code whose warnings had no recipient.
    /// </summary>
    [Fact]
    public void TheSolutionIsWhatDecidesWhoIsBound()
    {
        IReadOnlyList<string> gated = WarningPolicy.GatedProjects();
        if (gated.Count == 0)
            return;

        // PP271: a reader that found no projects would satisfy every claim below by finding nothing.
        Assert.True(gated.Count >= 3, $"only {gated.Count} projects read out of the solution");

        Assert.Contains(WarningPolicy.TestProjectRelativePath, gated);
        Assert.Contains(@"tools\compare-baselines\CompareBaselines.csproj", gated);
    }

    /// <summary>
    /// PP400: the projects a solution COMMENT names are not projects it builds.
    ///
    /// The folder entry PP436 added names the four it deliberately leaves out - two spikes, the
    /// alloc budget and measure-startup - so a reader of raw solution text would bind this policy to
    /// projects no gate compiles, which is this defect facing the other way.
    /// </summary>
    [Fact]
    public void AProjectNamedOnlyInACommentIsNotGated()
    {
        const string Solution = """
            <Solution>
              <!-- spike/present-path stays out. <Project Path="spike/present-path/PresentPath.csproj" /> -->
              <Project Path="app/ChiakiNg.csproj" />
            </Solution>
            """;

        IReadOnlyList<string> projects = WarningPolicy.ProjectsIn(Solution);

        Assert.Equal([@"app\ChiakiNg.csproj"], projects);
    }

    /// <summary>The slnx spells paths with forward slashes and this port's constants do not.</summary>
    [Fact]
    public void TheSeparatorsAreTheOnesTheRestOfThePortUses()
    {
        Assert.Equal(
            [@"tools\compare-baselines\CompareBaselines.csproj"],
            WarningPolicy.ProjectsIn("""<Project Path="tools/compare-baselines/CompareBaselines.csproj" />"""));
    }

    /// <summary>PP272: and an empty solution builds nothing.</summary>
    [Fact]
    public void AnEmptySolutionBindsNobody()
    {
        Assert.Empty(WarningPolicy.ProjectsIn(""));
        Assert.Empty(WarningPolicy.ProjectsIn("<Solution></Solution>"));
    }

    /// <summary>
    /// PP317. No gated project asks for an assembly the framework reference already supplies.
    ///
    /// This is the half PP316 could not gate: MSB3245 and MSB3243 are MSBuild's warnings and
    /// TreatWarningsAsErrors does not reach them, so what stops them coming back is a check on the
    /// cause rather than on the message. Re-adding either UIAutomation reference turns this red.
    /// </summary>
    [Fact]
    public void NoGatedProjectNamesAnAssemblyTheFrameworkSupplies()
    {
        IReadOnlyList<string> projects = WarningPolicy.LocateGatedProjects();
        Assert.True(projects.Count > 0, "not running out of a checkout");

        IReadOnlyList<string> named =
        [
            .. projects
                .SelectMany(path => WarningPolicy.BareAssemblyReferencesIn(File.ReadAllText(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        Assert.True(
            named.Count == 0,
            "asked for by bare name, which is the pre-SDK path that cannot find them and then "
                + "resolves the conflict it created arbitrarily: " + string.Join(", ", named));
    }

    /// <summary>
    /// PP317: a project's prose is not its project. This check went red on the csproj comment that
    /// explains why the two UIAutomation references were deleted, because the comment spells one
    /// out to say what it is talking about.
    /// </summary>
    [Fact]
    public void WhatAnXmlCommentSaysIsNotWhatTheProjectDeclares()
    {
        Assert.Empty(WarningPolicy.BareAssemblyReferencesIn(
            """<!-- Two bare <Reference Include="UIAutomationClient" /> items used to say so. -->"""));

        // The worse direction: a policy commented out reads as a policy that is not there.
        Assert.False(WarningPolicy.RefusesEveryWarning(
            "<!-- <TreatWarningsAsErrors>true</TreatWarningsAsErrors> -->"));

        Assert.Empty(WarningPolicy.SuppressedIn("<!-- <NoWarn>CS0108</NoWarn> -->"));

        // And a comment spanning lines is one comment, not a line-by-line filter.
        Assert.False(WarningPolicy.RefusesEveryWarning(
            "<!-- once:\n  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n-->"));
    }

    /// <summary>The bare form is the one that goes down the old path, and it is what is read.</summary>
    [Fact]
    public void ABareReferenceIsRead()
    {
        Assert.Equal(
            ["UIAutomationClient", "UIAutomationTypes"],
            WarningPolicy.BareAssemblyReferencesIn(
                """<Reference Include="UIAutomationClient" /><Reference Include="UIAutomationTypes" />"""));
    }

    /// <summary>
    /// And a Reference carrying a HintPath is not the same claim: it names a file on disk, which
    /// is an answer to where an assembly comes from rather than a guess at one.
    /// </summary>
    [Fact]
    public void AReferenceThatNamesAFileIsNotABareOne()
    {
        Assert.Empty(WarningPolicy.BareAssemblyReferencesIn(
            """<Reference Include="Some.Vendor" HintPath="..\lib\Some.Vendor.dll" />"""));

        Assert.Empty(WarningPolicy.BareAssemblyReferencesIn(
            "<Reference Include=\"Some.Vendor\"><HintPath>..\\lib\\Some.Vendor.dll</HintPath></Reference>"));
    }
}
