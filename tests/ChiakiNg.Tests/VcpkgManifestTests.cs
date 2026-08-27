using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP230: the manifest CI installs from, against the build graph it has to satisfy.
///
/// The gap this was written for: PP21 moved libplacebo out of the Qt client's branch, making it
/// something every configure asks for, and the manifest still listed what the Qt client needed. On
/// a machine with MSYS2 that costs nothing and is invisible; on a runner it is a configure that
/// never starts.
/// </summary>
public class VcpkgManifestTests(ITestOutputHelper output)
{
    /// <summary>A manifest in the shape vcpkg accepts, for the parsing tests.</summary>
    private const string Manifest = """
        {
          "name": "chiaki-ng",
          "dependencies": [ "pkgconf", "openssl", { "name": "curl", "features": ["ws"] } ]
        }
        """;

    /// <summary>Only what sits at column zero counts, because indented means conditional.</summary>
    [Fact]
    public void OnlyTheUnconditionalCounts()
    {
        const string cmake = """
            find_package(PkgConfig REQUIRED)
            pkg_check_modules(LIBPLACEBO REQUIRED libplacebo>=7.349.0 IMPORTED_TARGET)
            if(CHIAKI_ENABLE_GUI)
            	find_package(SDL2 MODULE REQUIRED)
            endif()
            find_package(FFMPEG COMPONENTS avcodec)
            """;

        IReadOnlySet<string> required = VcpkgManifest.RequiredUnconditionally(cmake);

        Assert.Contains("PkgConfig", required);
        Assert.Contains("libplacebo", required);

        // Indented: a choice CI makes rather than something the build cannot begin without.
        Assert.DoesNotContain("SDL2", required);

        // Not REQUIRED: the same reasoning one step along.
        Assert.DoesNotContain("FFMPEG", required);
    }

    /// <summary>A pkg_check_modules names a MODULE, not the variable cmake stores it in.</summary>
    [Fact]
    public void TheModuleIsWhatWouldBeInstalled()
    {
        IReadOnlySet<string> required = VcpkgManifest.RequiredUnconditionally(
            "pkg_check_modules(LIBPLACEBO REQUIRED libplacebo>=7.349.0 IMPORTED_TARGET)\n");

        Assert.Contains("libplacebo", required);
        Assert.DoesNotContain("LIBPLACEBO_VAR", required);
        Assert.Single(required);
    }

    /// <summary>Both manifest shapes are read: a bare string and an object with a name.</summary>
    [Fact]
    public void BothManifestShapesAreRead()
    {
        IReadOnlySet<string> declared = VcpkgManifest.Declared(Manifest);

        Assert.Contains("pkgconf", declared);
        Assert.Contains("curl", declared);
        Assert.Equal(3, declared.Count);
    }

    /// <summary>
    /// The name cmake uses is not always the port's. PkgConfig is satisfied by pkgconf, and a
    /// check that lower-cased and hoped would report a package that IS there as missing.
    /// </summary>
    [Fact]
    public void APackageWithADifferentPortNameIsNotMissing()
    {
        IReadOnlyList<string> missing =
            VcpkgManifest.Missing("find_package(PkgConfig REQUIRED)\n", Manifest);

        Assert.Empty(missing);
    }

    /// <summary>
    /// A host tool is not a package. nanopb's generator needs a Python 3 on PATH, so cmake asks
    /// for it REQUIRED at the top level - and no manifest entry would put one there. Reported as
    /// missing on this check's first run, which is how the distinction got written down.
    /// </summary>
    [Fact]
    public void AHostToolIsNotAMissingPackage()
    {
        const string cmake = "find_package(PythonInterp 3 REQUIRED)\n";

        Assert.Contains("PythonInterp", VcpkgManifest.RequiredUnconditionally(cmake));
        Assert.Empty(VcpkgManifest.Missing(cmake, Manifest));
    }

    /// <summary>And one nobody declared is named.</summary>
    [Fact]
    public void WhatIsRequiredAndUndeclaredIsNamed()
    {
        IReadOnlyList<string> missing = VcpkgManifest.Missing(
            "pkg_check_modules(LIBPLACEBO REQUIRED libplacebo>=7.349.0 IMPORTED_TARGET)\n",
            Manifest);

        Assert.Equal(["libplacebo"], missing);
    }

    /// <summary>
    /// And the real pair. This is the assertion that has to hold on every commit: what this
    /// project's build cannot start without is what CI would install.
    /// </summary>
    [Fact]
    public void TheManifestCarriesWhatTheBuildRequires()
    {
        string? cmakePath = VcpkgManifest.LocateCMake();
        string? manifestPath = VcpkgManifest.LocateManifest();

        Assert.True(cmakePath is not null && manifestPath is not null, "not running out of a checkout");

        string cmake = File.ReadAllText(cmakePath);
        string manifest = File.ReadAllText(manifestPath);

        output.WriteLine(
            "unconditional: " + string.Join(", ", VcpkgManifest.RequiredUnconditionally(cmake)));

        IReadOnlyList<string> missing = VcpkgManifest.Missing(cmake, manifest);

        Assert.True(
            missing.Count == 0,
            "vcpkg.json does not carry what the build requires unconditionally, so a runner with "
                + "nothing installed cannot configure: " + string.Join(", ", missing));
    }

    /// <summary>
    /// PP434: a graph, on a reader that is not this checkout's.
    ///
    /// Column zero descends and indented does not, which is the same convention the lookups are read
    /// by. The real graph has to agree with the manifest and so cannot be the fixture for a walk that
    /// finds something the root file hid.
    /// </summary>
    [Fact]
    public void TheWalkDescendsThroughColumnZeroOnly()
    {
        var graph = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"CMakeLists.txt"] = """
                add_subdirectory(lib)
                if(CHIAKI_ENABLE_GUI)
                	add_subdirectory(gui)
                endif()
                """,
            [@"lib\CMakeLists.txt"] = """
                find_package(Threads REQUIRED)
                find_package(SomethingNobodyPackaged REQUIRED)
                add_subdirectory(protobuf)
                """,
            [@"lib\protobuf\CMakeLists.txt"] = "find_package(AlsoUnpackaged REQUIRED)\n",

            // Reached only behind an option, so nothing in it is the manifest's business.
            [@"gui\CMakeLists.txt"] = "find_package(Qt6 REQUIRED COMPONENTS Core)\n",
        };

        string? Read(string relative) => graph.TryGetValue(relative, out string? text) ? text : null;

        IReadOnlyList<string> reached = VcpkgManifest.Reachable(Read);
        output.WriteLine("reached: " + string.Join(", ", reached));

        Assert.Equal(
            [@"CMakeLists.txt", @"lib\CMakeLists.txt", @"lib\protobuf\CMakeLists.txt"], reached);

        IReadOnlySet<string> required = VcpkgManifest.RequiredAcrossGraph(Read);

        // The whole point: found in a file the root-only reader never opens.
        Assert.Contains("SomethingNobodyPackaged", required);
        Assert.Contains("AlsoUnpackaged", required);
        Assert.DoesNotContain("Qt6", required);

        // And the root-only reader is where it was, which is what makes the difference the finding.
        Assert.DoesNotContain(
            "SomethingNobodyPackaged", VcpkgManifest.RequiredUnconditionally(graph["CMakeLists.txt"]));

        Assert.Equal(
            ["AlsoUnpackaged", "SomethingNobodyPackaged"],
            VcpkgManifest.MissingAcrossGraph(Read, Manifest));
    }

    /// <summary>
    /// PP434: and third-party/ is excluded by the rule, not by its name.
    ///
    /// Every add_subdirectory in that aggregator is indented, so the walk reads it, finds nothing
    /// unconditional and descends no further - which is why curl's thirty REQUIRED lookups, all for
    /// TLS backends this build does not enable, never enter the answer.
    /// </summary>
    [Fact]
    public void AnAggregatorThatAddsNothingUnconditionallyEndsTheWalk()
    {
        var graph = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"CMakeLists.txt"] = "add_subdirectory(third-party)\n",
            [@"third-party\CMakeLists.txt"] = """
                if(NOT CHIAKI_USE_SYSTEM_NANOPB)
                	add_subdirectory(nanopb EXCLUDE_FROM_ALL)
                endif()
                """,
            [@"third-party\nanopb\CMakeLists.txt"] = "find_package(Protobuf REQUIRED)\n",
        };

        string? Read(string relative) => graph.TryGetValue(relative, out string? text) ? text : null;

        Assert.Equal(
            [@"CMakeLists.txt", @"third-party\CMakeLists.txt"], VcpkgManifest.Reachable(Read));

        Assert.Empty(VcpkgManifest.RequiredAcrossGraph(Read));

        // The hyphen in the directory name is read, and EXCLUDE_FROM_ALL does not become a name.
        Assert.Equal(
            ["third-party"], VcpkgManifest.UnconditionalSubdirectories(graph["CMakeLists.txt"]));

        Assert.Empty(VcpkgManifest.UnconditionalSubdirectories(graph[@"third-party\CMakeLists.txt"]));
    }

    /// <summary>
    /// PP434: the real graph is more than the root file, asserted so the scope cannot quietly shrink.
    ///
    /// lib/CMakeLists.txt is where openssl, opus and libevent are asked for, and the root-only reader
    /// never opened it. Threads is what proves the walk arrived: it sits at column zero there and
    /// nowhere in the root.
    /// </summary>
    [Fact]
    public void TheRealWalkReachesLibAndNotGui()
    {
        if (VcpkgManifest.ReadFromCheckout() is not { } read)
            return;

        IReadOnlyList<string> reached = VcpkgManifest.Reachable(read);
        output.WriteLine("reached: " + string.Join(", ", reached));

        // PP271: a walk that read nothing would satisfy every claim below by finding nothing.
        Assert.True(reached.Count >= 3, $"the walk reached only {reached.Count} files");

        Assert.Contains(@"CMakeLists.txt", reached);
        Assert.Contains(@"lib\CMakeLists.txt", reached);

        // Behind CHIAKI_ENABLE_GUI, which CI turns off. A choice is not a manifest obligation.
        Assert.DoesNotContain(@"gui\CMakeLists.txt", reached);
        Assert.DoesNotContain(@"test\CMakeLists.txt", reached);

        IReadOnlySet<string> graph = VcpkgManifest.RequiredAcrossGraph(read);
        Assert.Contains("Threads", graph);

        if (VcpkgManifest.LocateCMake() is { } root)
        {
            Assert.DoesNotContain(
                "Threads", VcpkgManifest.RequiredUnconditionally(File.ReadAllText(root)));
        }
    }

    /// <summary>
    /// PP434: and the assertion that now has to hold on every commit - the WHOLE graph against the
    /// manifest, which is what a runner with nothing installed would hit.
    /// </summary>
    [Fact]
    public void TheManifestCarriesWhatTheWholeGraphRequires()
    {
        if (VcpkgManifest.ReadFromCheckout() is not { } read)
            return;
        if (VcpkgManifest.LocateManifest() is not { } manifestPath)
            return;

        IReadOnlySet<string> required = VcpkgManifest.RequiredAcrossGraph(read);
        output.WriteLine("across the graph: " + string.Join(", ", required));

        IReadOnlyList<string> missing =
            VcpkgManifest.MissingAcrossGraph(read, File.ReadAllText(manifestPath));

        Assert.True(
            missing.Count == 0,
            "vcpkg.json does not carry what the build requires unconditionally somewhere in the "
                + "graph, so a runner with nothing installed cannot configure: "
                + string.Join(", ", missing));
    }

    /// <summary>PP272: and an empty file adds no subdirectory and requires nothing.</summary>
    [Fact]
    public void AnEmptyFileReachesNothing()
    {
        Assert.Empty(VcpkgManifest.UnconditionalSubdirectories(""));
        Assert.Empty(VcpkgManifest.RequiredUnconditionally(""));
        Assert.Empty(VcpkgManifest.Reachable(_ => null));
        Assert.Empty(VcpkgManifest.RequiredAcrossGraph(_ => null));
    }
}
