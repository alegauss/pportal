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
///
/// PP434: AND "THE BUILD GRAPH" IS NOT ONE FILE. This read the root CMakeLists.txt and nothing else,
/// while the graph reaches lib/, shim/, lib/protobuf/ and third-party/ unconditionally, and gui/ and
/// test/ behind options. lib/CMakeLists.txt is where openssl, opus and libevent are asked for; that
/// all three are in the manifest was true and unmeasured.
///
/// THE RULE IS REACHABILITY, and it is the same convention one level out: a subdirectory added from
/// column zero inherits the root's unconditionality, one added from inside an if does not. It settles
/// third-party/ without a special case - every add_subdirectory in that aggregator is indented, so
/// the walk stops there and curl's thirty REQUIRED lookups never enter the answer.
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
    ///
    /// Threads is PP434's, and it is the same kind for a different reason: lib/CMakeLists.txt asks
    /// for it unconditionally, and what satisfies it is a compiler flag - pthread, or nothing at all
    /// on MSVC. It is here because the graph walk now reaches that file, so the exclusion is a
    /// decision rather than a file the reader never opened.
    /// </summary>
    public static IReadOnlySet<string> HostTools { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PythonInterp", "Python3", "Python", "Threads",
        };

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

    /// <summary>
    /// PP434: the subdirectories a file adds UNCONDITIONALLY, by the same column-zero convention.
    ///
    /// This is what makes the walk terminate honestly. third-party/CMakeLists.txt adds nanopb, curl
    /// and cpp-steam-tools, and all three sit inside an if(NOT CHIAKI_USE_SYSTEM_...) - so the walk
    /// reads that aggregator, finds nothing unconditional in it, and descends no further. Vendored
    /// source declaring dependencies for configurations this build does not enable is excluded by the
    /// rule rather than by a name in a list.
    /// </summary>
    public static IReadOnlyList<string> UnconditionalSubdirectories(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        var directories = new List<string>();

        foreach (string line in cmake.Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                continue;

            Match added = AddSubdirectoryRegex().Match(line);
            if (added.Success)
                directories.Add(added.Groups[1].Value);
        }

        return directories;
    }

    /// <summary>
    /// Every CMake file the build reaches without a decision, root first.
    ///
    /// The reader is passed in so the walk can be tested on a graph that is not this checkout's -
    /// the real one is required to agree with the manifest, and so cannot be the fixture for a
    /// disagreement.
    /// </summary>
    public static IReadOnlyList<string> Reachable(Func<string, string?> readRelative)
    {
        ArgumentNullException.ThrowIfNull(readRelative);

        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();

        // The root directory, named by the empty string so the root file needs no special case.
        pending.Enqueue("");

        while (pending.Count > 0)
        {
            string directory = pending.Dequeue();
            string path = directory.Length == 0
                ? CMakeRelativePath
                : $@"{directory}\{CMakeRelativePath}";

            // A cycle is not a thing cmake permits, but a walk that assumed so would hang rather
            // than fail, and this runs in the gate.
            if (!seen.Add(path))
                continue;

            if (readRelative(path) is not { } text)
                continue;

            order.Add(path);

            foreach (string sub in UnconditionalSubdirectories(text))
                pending.Enqueue(directory.Length == 0 ? sub : $@"{directory}\{sub}");
        }

        return order;
    }

    /// <summary>What the whole reachable graph requires, and not just the root file.</summary>
    public static IReadOnlySet<string> RequiredAcrossGraph(Func<string, string?> readRelative)
    {
        ArgumentNullException.ThrowIfNull(readRelative);

        var required = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Reachable(readRelative))
        {
            if (readRelative(path) is { } text)
                required.UnionWith(RequiredUnconditionally(text));
        }

        return required;
    }

    /// <summary>
    /// A reader over this checkout, or null outside one.
    ///
    /// Null rather than a reader that finds nothing: a graph walk over an absent tree returns an
    /// empty set, and an empty set carries no missing package - PP271's shape, where a check that
    /// read nothing passes about nothing.
    /// </summary>
    public static Func<string, string?>? ReadFromCheckout()
    {
        if (LocateCMake() is null)
            return null;

        return relative =>
            SanitizerSource.LocateRelative(relative) is { } found ? File.ReadAllText(found) : null;
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
        => MissingFrom(RequiredUnconditionally(cmake), manifestJson);

    /// <summary>
    /// PP434: the same question asked of the whole reachable graph, which is what the gate runs.
    ///
    /// The single-file <see cref="Missing(string, string)"/> stays as the unit - it is what the
    /// synthetic cases exercise - and this is what a runner's configure would actually hit.
    /// </summary>
    public static IReadOnlyList<string> MissingAcrossGraph(
        Func<string, string?> readRelative, string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(readRelative);

        return MissingFrom(RequiredAcrossGraph(readRelative), manifestJson);
    }

    // The port-name mapping and the host-tool exclusion, applied to whichever set was gathered.
    private static IReadOnlyList<string> MissingFrom(
        IReadOnlySet<string> required, string manifestJson)
    {
        IReadOnlySet<string> declared = Declared(manifestJson);

        return [.. required
            .Where(package => !HostTools.Contains(package))
            .Select(package => PortFor.TryGetValue(package, out string? port) ? port : package)
            .Where(port => !declared.Contains(port))
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)];
    }

    // find_package(NAME ...
    [GeneratedRegex(@"^find_package\(\s*([A-Za-z0-9_]+)")]
    private static partial Regex FindPackageRegex();

    // add_subdirectory(name) and add_subdirectory(name EXCLUDE_FROM_ALL) - the hyphen matters, as
    // third-party is the one the root adds at column zero.
    [GeneratedRegex(@"^add_subdirectory\(\s*([A-Za-z0-9_\-.]+)")]
    private static partial Regex AddSubdirectoryRegex();

    // pkg_check_modules(VAR REQUIRED name>=version ...) - the MODULE is what vcpkg would install,
    // not the variable cmake stores it in, so the name after REQUIRED is the one that matters.
    [GeneratedRegex(@"^pkg_check_modules\(\s*[A-Za-z0-9_]+\s+REQUIRED\s+([A-Za-z0-9_\-]+)")]
    private static partial Regex PkgCheckRegex();
}
