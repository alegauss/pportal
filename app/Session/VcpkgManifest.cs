using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP230: what the build asks for without being asked, held against what CI would install.
///
/// Two files that have to agree and no reader in common. CMakeLists.txt is read by cmake on a
/// machine where MSYS2 already has everything, so a package missing from the manifest costs nothing
/// there; vcpkg.json is read by vcpkg on a runner where nothing is installed, and a package missing
/// from it is a configure that cannot start. The developer cannot see the gap and CI sees nothing
/// else.
///
/// UNCONDITIONAL ONLY, and that is the whole of the rule. A find_package behind a tri_option is a
/// choice CI makes; a pkg_check_modules at the top level is something the build cannot begin
/// without. Only the second kind belongs in a manifest, and only the second kind is compared here -
/// otherwise every optional decoder would read as a missing dependency and the check would be
/// noise.
/// </summary>
public static partial class VcpkgManifest
{
    /// <summary>The build graph.</summary>
    public const string CMakeRelativePath = "CMakeLists.txt";

    /// <summary>And what CI would install to satisfy it.</summary>
    public const string ManifestRelativePath = "vcpkg.json";

    /// <summary>The two files, or null outside a checkout.</summary>
    public static string? LocateCMake() => SanitizerSource.LocateRelative(CMakeRelativePath);

    /// <summary>The manifest, or null outside a checkout.</summary>
    public static string? LocateManifest() => SanitizerSource.LocateRelative(ManifestRelativePath);

    /// <summary>
    /// How a cmake package name maps to a vcpkg port name, where the two differ.
    ///
    /// Written down rather than lower-cased and hoped for: PkgConfig is satisfied by `pkgconf`, and
    /// a check that guessed would report a package that IS there as missing on its first run.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PortFor { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PkgConfig"] = "pkgconf",
        };

    /// <summary>
    /// Names that are HOST TOOLS rather than packages - a runner has them or does not, and no
    /// manifest entry would change that.
    ///
    /// PythonInterp is the one that put this here. nanopb's generator needs a Python 3 on PATH, so
    /// cmake asks for it REQUIRED at the top level - and it is not a thing vcpkg installs into a
    /// project. Mapping it to a port name would have been the wrong fix twice: no such port, and
    /// the runner image already carries it.
    /// </summary>
    public static IReadOnlySet<string> HostTools { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PythonInterp", "Python3", "Python" };

    /// <summary>
    /// Every package the build requires at the TOP LEVEL - outside any if(), and marked REQUIRED.
    ///
    /// Read by indentation, which is what tells the two apart in this file: everything inside a
    /// conditional is tabbed in, and everything the build cannot start without sits at column zero.
    /// Crude, and correct for the file it reads - a structural cmake parser to answer one question
    /// would be a second build system.
    /// </summary>
    public static IReadOnlySet<string> RequiredUnconditionally(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        var required = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in cmake.Split('\n'))
        {
            // Indented means conditional. The one thing this rests on, and it is the file's own
            // convention rather than an assumption about cmake.
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                continue;

            Match found = FindPackageRegex().Match(line);
            if (found.Success && line.Contains("REQUIRED", StringComparison.Ordinal))
            {
                required.Add(found.Groups[1].Value);
                continue;
            }

            Match module = PkgCheckRegex().Match(line);
            if (module.Success && line.Contains("REQUIRED", StringComparison.Ordinal))
                required.Add(module.Groups[1].Value);
        }

        return required;
    }

    /// <summary>The ports the manifest declares.</summary>
    public static IReadOnlySet<string> Declared(string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);

        using JsonDocument document = JsonDocument.Parse(manifestJson);

        var ports = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("dependencies", out JsonElement dependencies))
            return ports;

        foreach (JsonElement dependency in dependencies.EnumerateArray())
        {
            // vcpkg allows a bare string or an object with a name; both are real manifests.
            string? name = dependency.ValueKind == JsonValueKind.String
                ? dependency.GetString()
                : dependency.TryGetProperty("name", out JsonElement named) ? named.GetString() : null;

            if (!string.IsNullOrEmpty(name))
                ports.Add(name);
        }

        return ports;
    }

    /// <summary>
    /// What the build requires and the manifest does not carry - which is the list a CI run would
    /// discover one package at a time, in whatever order cmake happens to ask.
    /// </summary>
    public static IReadOnlyList<string> Missing(string cmake, string manifestJson)
    {
        IReadOnlySet<string> declared = Declared(manifestJson);

        return [.. RequiredUnconditionally(cmake)
            .Where(package => !HostTools.Contains(package))
            .Select(package => PortFor.TryGetValue(package, out string? port) ? port : package)
            .Where(port => !declared.Contains(port))
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)];
    }

    // find_package(NAME ...
    [GeneratedRegex(@"^find_package\(\s*([A-Za-z0-9_]+)")]
    private static partial Regex FindPackageRegex();

    // pkg_check_modules(VAR REQUIRED name>=version ...) - the MODULE is what vcpkg would install,
    // not the variable cmake stores it in, so the name after REQUIRED is the one that matters.
    [GeneratedRegex(@"^pkg_check_modules\(\s*[A-Za-z0-9_]+\s+REQUIRED\s+([A-Za-z0-9_\-]+)")]
    private static partial Regex PkgCheckRegex();
}
