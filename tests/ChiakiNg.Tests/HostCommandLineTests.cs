using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP306: the flags the host answers, against the ones it matches.
///
/// The list is prose and the dispatch is code, and the two have no reader in common - which is the
/// shape every drift check in this port is about. A flag added to OnStartup with no line beside it
/// is undiscoverable; a line describing a flag nobody dispatches is worse, because it is an
/// instruction that does nothing.
/// </summary>
public class HostCommandLineTests(ITestOutputHelper output)
{
    /// <summary>Help is asked for in four spellings and none of them reaches the window.</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void TheListIsAskedForHoweverThePersonKnowsHow(string flag)
        => Assert.True(HostCommandLine.IsHelp([flag]));

    /// <summary>And nothing else is.</summary>
    [Fact]
    public void ARunWithNoFlagIsNotAskingForTheList()
    {
        Assert.False(HostCommandLine.IsHelp([]));
        Assert.False(HostCommandLine.IsHelp(["--selftest"]));
    }

    /// <summary>
    /// A misspelling is refused rather than opening the application, which is the whole defect.
    /// </summary>
    [Fact]
    public void AFlagNothingAnswersIsNamed()
    {
        Assert.Equal(["--self-test"], HostCommandLine.Unrecognised(["--self-test"]));
        Assert.Equal(["--recounts"], HostCommandLine.Unrecognised(["--recounts"]));

        // Every declared flag passes, help included.
        Assert.Empty(HostCommandLine.Unrecognised([.. HostCommandLine.Flags.Select(f => f.Name)]));
        Assert.Empty(HostCommandLine.Unrecognised(["--help"]));
    }

    /// <summary>
    /// The argument --capture-mapping takes is a path, and a path is not a flag.
    ///
    /// Refusing a bare word would turn "ChiakiNg.exe --capture-mapping out.png" into an error about
    /// out.png, which is the kind of strictness that gets a check deleted rather than fixed.
    /// </summary>
    [Fact]
    public void WhatFollowsAFlagIsNotItselfRefused()
    {
        Assert.Empty(HostCommandLine.Unrecognised(["--capture-mapping", "out.png"]));
        Assert.Empty(HostCommandLine.Unrecognised(["--capture-mapping", @"C:\tmp\mapping.png"]));
    }

    /// <summary>Every flag is spelled once and described, because a list is what this is for.</summary>
    [Fact]
    public void EveryFlagCarriesALine()
    {
        Assert.All(HostCommandLine.Flags, flag =>
        {
            Assert.StartsWith("--", flag.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(flag.Summary), $"{flag.Name} has no line");
        });

        Assert.Equal(
            HostCommandLine.Flags.Count,
            HostCommandLine.Flags.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string usage = HostCommandLine.Usage();
        Assert.All(HostCommandLine.Flags, flag => Assert.Contains(flag.Name, usage, StringComparison.Ordinal));
    }

    /// <summary>
    /// PP163: the control the demo's reading is only worth anything against.
    ///
    /// --dcomp-demo on its own runs the arrangement the design rests on, and on 2026-08-23 it drew
    /// solid red over the whole client area - WPF's own content nowhere, which is none of the three
    /// outcomes DcompDemo predicted. That reading has two explanations pointing at different work:
    /// the compositor ignored the arrangement, or WPF drew nothing. --topmost is what separates
    /// them, because running the same tree the other way up answers one and not the other.
    ///
    /// The drift check below would stay green if this flag and its dispatch were deleted together,
    /// which is exactly the deletion that would take the control away and leave the reading
    /// unfalsifiable. So the flag is named here rather than left to a consistency check.
    /// </summary>
    [Fact]
    public void TheDcompDemoCarriesItsControl()
    {
        Assert.Contains(
            HostCommandLine.Flags,
            flag => string.Equals(flag.Name, "--topmost", StringComparison.Ordinal));

        Assert.Empty(HostCommandLine.Unrecognised(["--dcomp-demo", "--topmost"]));
    }

    /// <summary>
    /// THE DRIFT CHECK. What the list describes is what the dispatch matches, both ways.
    ///
    /// A flag in the source and not in the list is undiscoverable, which is PP306 happening again.
    /// A flag in the list and not in the source is an instruction that does nothing, which is
    /// worse - it is read, tried, and answered by the application opening.
    /// </summary>
    [Fact]
    public void TheListAndTheDispatchNameTheSameFlags()
    {
        string? sourcePath = HostCommandLine.LocateSource();
        Assert.True(sourcePath is not null, "not running out of a checkout");

        IReadOnlySet<string> matched = HostCommandLine.FlagsMatchedIn(File.ReadAllText(sourcePath));
        Assert.True(matched.Count >= 5, $"only {matched.Count} flags found in the dispatch - the scan is not working");

        output.WriteLine("dispatched: " + string.Join(", ", matched));

        var declared = new HashSet<string>(
            HostCommandLine.Flags.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        string[] undescribed = [.. matched.Where(flag => !declared.Contains(flag))];
        Assert.True(undescribed.Length == 0,
            "the host answers these and --help does not list them, so they cannot be found: "
                + string.Join(", ", undescribed));

        string[] unanswered = [.. declared.Where(flag => !matched.Contains(flag)).Order(StringComparer.Ordinal)];
        Assert.True(unanswered.Length == 0,
            "--help lists these and nothing dispatches on them, so running one opens the window: "
                + string.Join(", ", unanswered));
    }
}
