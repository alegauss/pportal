using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP22: the workflow that builds this port, against the tree it builds.
///
/// The gap these were written for: nothing on this machine reads .github/workflows. A file renamed
/// here, a framework bumped in the csproj, a configure that stops going through vcpkg - each of
/// them leaves a green local build and a red push, and the report arrives from a runner minutes
/// later with a message about a path rather than about the rename that caused it.
/// </summary>
public class BuildWorkflowTests(ITestOutputHelper output)
{
    /// <summary>A workflow in the shape this port writes them, for the parsing tests.</summary>
    private const string Workflow = """
        name: build
        on: [push, pull_request]
        jobs:
          windows:
            runs-on: windows-2022
            steps:
              - uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: 10.0.x
              - run: >
                  cmake -S . -B build
                  -DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_INSTALLATION_ROOT/scripts/buildsystems/vcpkg.cmake
              - run: dotnet build ChiakiNg.slnx -c Debug
              - uses: actions/upload-artifact@v4
                with:
                  path: app/bin/Release/net10.0-windows/win-x64/publish/ChiakiNg.exe
        """;

    /// <summary>A path the repository answers for is picked out of the noise around it.</summary>
    [Fact]
    public void WhatTheRepositoryAnswersForIsFound()
    {
        IReadOnlyList<string> paths = BuildWorkflow.RepositoryPathsIn(Workflow);

        Assert.Contains("ChiakiNg.slnx", paths);

        // An action reference is not a file in this checkout, and neither is a runner label.
        Assert.DoesNotContain(paths, path => path.Contains("actions/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The vcpkg toolchain is rooted on the runner, not here. Read as a repository path it would
    /// report a missing file on every run, which is how a check gets deleted.
    /// </summary>
    [Fact]
    public void APathRootedOnTheRunnerIsNotThisRepositorys()
    {
        Assert.DoesNotContain(
            BuildWorkflow.RepositoryPathsIn(Workflow),
            path => path.Contains("vcpkg.cmake", StringComparison.Ordinal));
    }

    /// <summary>And output is not input: the published exe does not exist before the step that writes it.</summary>
    [Fact]
    public void WhatTheBuildWritesIsNotWhatItReads()
    {
        Assert.True(BuildWorkflow.IsBuildOutput("app/bin/Release/net10.0-windows/win-x64/publish/ChiakiNg.exe"));
        Assert.True(BuildWorkflow.IsBuildOutput("build/chiaki-ng-package"));
        Assert.False(BuildWorkflow.IsBuildOutput("app/ChiakiNg.csproj"));
    }

    /// <summary>The framework moniker carries a platform, and a runner installs only the version.</summary>
    [Fact]
    public void OnlyTheVersionHalfOfAMonikerIsInstallable()
    {
        Assert.Equal("10.0", BuildWorkflow.VersionOfFramework("net10.0-windows"));
        Assert.Equal("10.0", BuildWorkflow.VersionOfFramework("net10.0"));
        Assert.Null(BuildWorkflow.VersionOfFramework("netstandard2.0"));
        Assert.Null(BuildWorkflow.VersionOfFramework(null));
    }

    /// <summary>The two halves the version check compares are each read from their own file.</summary>
    [Fact]
    public void TheRequestedVersionAndTheDeclaredFrameworkAreBothRead()
    {
        Assert.Equal("10.0.x", BuildWorkflow.DotnetVersionIn(Workflow));
        Assert.Null(BuildWorkflow.DotnetVersionIn("runs-on: windows-2022\n"));

        Assert.Equal(
            "net10.0-windows",
            BuildWorkflow.TargetFrameworkOf("<TargetFramework>net10.0-windows</TargetFramework>"));
    }

    /// <summary>A configure that does not name the toolchain installs nothing the manifest declares.</summary>
    [Fact]
    public void AConfigureWithoutTheToolchainIsSeen()
    {
        Assert.True(BuildWorkflow.ConfiguresThroughVcpkg(Workflow));
        Assert.False(BuildWorkflow.ConfiguresThroughVcpkg("- run: cmake -S . -B build\n"));
    }

    /// <summary>
    /// PP535: every $env: in the workflow is somewhere pwsh will expand it.
    ///
    /// The configure step passed the vcpkg toolchain file as a bare argument, and pwsh does not
    /// expand $env:X/path there - it hands the program the text. cmake read the literal, refused,
    /// and the job died at its first step on all 39 runs it had ever had, none of them green.
    ///
    /// ConfiguresThroughVcpkg was green throughout, because it asks whether the workflow NAMES the
    /// toolchain file and the workflow did. This asks the question that one cannot: whether the
    /// name will still be a name by the time cmake sees it.
    /// </summary>
    [Fact]
    public void EveryEnvReferenceIsSomewherePwshWillExpandIt()
    {
        if (BuildWorkflow.Locate() is not { } path)
            return;

        var references = BuildWorkflow.EnvReferencesIn(File.ReadAllText(path));

        Assert.NotEmpty(references);
        Assert.All(references, r => Assert.True(r.Quoted,
            $"build.yml:{r.Line} uses $env: in a bare argument, where pwsh passes it through as "
            + $"text rather than expanding it: {r.Text}"));
    }

    /// <summary>
    /// And the sweep can see the shape it was written for. The line as it stood is the one case
    /// that matters, so it is here verbatim: a rule that passed over it would hold nothing.
    /// </summary>
    [Fact]
    public void TheBareArgumentThisReplacedIsReportedUnquoted()
    {
        const string before =
            "          -DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_INSTALLATION_ROOT/scripts/buildsystems/vcpkg.cmake";
        const string after =
            "          -DCMAKE_TOOLCHAIN_FILE=\"$env:VCPKG_INSTALLATION_ROOT/scripts/buildsystems/vcpkg.cmake\"";

        Assert.False(Assert.Single(BuildWorkflow.EnvReferencesIn(before)).Quoted);
        Assert.True(Assert.Single(BuildWorkflow.EnvReferencesIn(after)).Quoted);
    }

    /// <summary>
    /// And the real file. Every path the workflow names is a file this checkout has - which is the
    /// assertion a rename would otherwise leave for a runner to find.
    /// </summary>
    [Fact]
    public void EveryFileTheWorkflowNamesIsInTheTree()
    {
        string? workflowPath = BuildWorkflow.Locate();
        Assert.True(workflowPath is not null, "not running out of a checkout");

        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlyList<string> paths = BuildWorkflow.RepositoryPathsIn(File.ReadAllText(workflowPath));
        output.WriteLine("named: " + string.Join(", ", paths));

        Assert.NotEmpty(paths);

        IReadOnlyList<string> absent =
            [.. paths.Where(path => !File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))];

        Assert.True(
            absent.Count == 0,
            "the build workflow names files this tree does not have, so the next push is red for a "
                + "reason no local build reports: " + string.Join(", ", absent));
    }

    /// <summary>
    /// The runner installs the framework the host targets. Two files, two syntaxes, and nothing
    /// relating them - the same shape as the manifest version check in the csproj, one repository
    /// out.
    /// </summary>
    [Fact]
    public void TheWorkflowInstallsTheFrameworkTheHostTargets()
    {
        string? workflowPath = BuildWorkflow.Locate();
        string? projectPath = BuildWorkflow.LocateHostProject();
        Assert.True(workflowPath is not null && projectPath is not null, "not running out of a checkout");

        string? requested = BuildWorkflow.DotnetVersionIn(File.ReadAllText(workflowPath));
        Assert.True(requested is not null, "the workflow does not install a .NET SDK at all");

        string? targeted =
            BuildWorkflow.VersionOfFramework(BuildWorkflow.TargetFrameworkOf(File.ReadAllText(projectPath)));
        Assert.True(targeted is not null, "app/ChiakiNg.csproj declares no target framework");

        Assert.True(
            requested.StartsWith(targeted, StringComparison.Ordinal),
            $"the workflow installs {requested} and the host targets net{targeted}");
    }

    /// <summary>
    /// Windows, on every job. The non-goal says this application has no other platform, and a
    /// workflow is the one place a second one could be added without touching a line of code.
    /// </summary>
    [Fact]
    public void EveryJobRunsOnWindows()
    {
        string? workflowPath = BuildWorkflow.Locate();
        Assert.True(workflowPath is not null, "not running out of a checkout");

        IReadOnlySet<string> runners = BuildWorkflow.RunnersIn(File.ReadAllText(workflowPath));

        Assert.NotEmpty(runners);
        Assert.All(runners, runner => Assert.StartsWith("windows", runner, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// PP36: a red assertion has somewhere to stop something, and the result can be read.
    ///
    /// Before it, .github/workflows held one file and it linted the roadmap: nothing on a push
    /// could fail for a reason about the code. PP22 put both suites in the job, which answers the
    /// first half; this holds the second, which is that a red run leaves a list of test names
    /// behind rather than a log to scroll.
    /// </summary>
    [Fact]
    public void BothSuitesRunAndTheirResultsSurviveARedRun()
    {
        string? workflowPath = BuildWorkflow.Locate();
        Assert.True(workflowPath is not null, "not running out of a checkout");

        string workflow = File.ReadAllText(workflowPath);

        Assert.True(BuildWorkflow.RunsBothSuites(workflow),
            "the workflow no longer runs both suites, so half this port's assertions cannot turn a "
                + "branch red");
        Assert.True(BuildWorkflow.PublishesTestResults(workflow),
            "the test results are not kept, or are kept only when the job succeeds - which is the "
                + "one run they are wanted on");
    }

    /// <summary>The two halves of the results check are separable, and the second is the fragile one.</summary>
    [Fact]
    public void AnUploadThatSkipsOnFailureIsNotPublishing()
    {
        const string always = """
            - run: ctest --output-junit junit-unit.xml
            - run: dotnet test x.csproj
            - if: always()
              uses: actions/upload-artifact@v4
            """;

        Assert.True(BuildWorkflow.RunsBothSuites(always));
        Assert.True(BuildWorkflow.PublishesTestResults(always));

        // The same steps with the condition dropped: written, uploaded on green, gone on red.
        const string onlyOnGreen = """
            - run: ctest --output-junit junit-unit.xml
            - run: dotnet test x.csproj
            - uses: actions/upload-artifact@v4
            """;

        Assert.True(BuildWorkflow.RunsBothSuites(onlyOnGreen));
        Assert.False(BuildWorkflow.PublishesTestResults(onlyOnGreen));

        // And one suite alone is not both.
        Assert.False(BuildWorkflow.RunsBothSuites("- run: ctest --output-on-failure\n"));
    }

    /// <summary>
    /// And the configure goes through vcpkg, which is what gives PP230's manifest check a reader.
    /// </summary>
    [Fact]
    public void TheNativeSideIsConfiguredThroughVcpkg()
    {
        string? workflowPath = BuildWorkflow.Locate();
        Assert.True(workflowPath is not null, "not running out of a checkout");

        Assert.True(
            BuildWorkflow.ConfiguresThroughVcpkg(File.ReadAllText(workflowPath)),
            "the workflow configures without the vcpkg toolchain, so vcpkg.json installs nothing "
                + "and the manifest check PP230 added guards a file no build reads");
    }
}
