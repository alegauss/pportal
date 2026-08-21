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
}
