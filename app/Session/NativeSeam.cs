using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP437: the entry points the host imports, against the functions the shim exports.
///
/// The two shim headers declare 242 functions and the host names 241 of them in about 290 DllImport
/// attributes across 31 files. Nothing held the two sets against each other.
///
/// THE DIRECTION THAT CRASHES IS THE UNCHECKED ONE. An EntryPoint is a STRING, and the compiler has
/// no opinion about it. A name the shim does not export compiles, links, and throws
/// EntryPointNotFoundException the first time that particular call is made - which for most of this
/// seam is inside a live session and not at startup. PP75's selftest drives the native seam, but it
/// drives a subset: an entry point the selftest never touches is proven by nothing.
///
/// IT IS CONSISTENT TODAY AND THAT WAS MEASURED, which is the point - the first reading of the
/// sources reported six of them missing, and all six are defined in chiaki_render_dcomp.cpp as
/// `extern "C"`. A column-zero pattern whose character class excluded the quote could not match one.
/// The reader was wrong, not the seam, and this reads the HEADERS for exactly that reason: a header
/// declaration is the contract, and the definition's spelling is the compiler's business.
///
/// COMMENTS ARE STRIPPED FIRST. chiaki_render.h documents PP131's experiment at length and names
/// chiaki_render_share_to_d3d9_format inside that prose, and PP400's rule is that an absence claim
/// reads code and not comments.
/// </summary>
public static partial class NativeSeam
{
    /// <summary>The contract: what the shim says it exports.</summary>
    public static IReadOnlyList<string> HeaderRelativePaths { get; } =
        [@"shim\chiaki_shim.h", @"shim\chiaki_render.h"];

    /// <summary>Where the host's imports live.</summary>
    public const string ManagedRelativeDirectory = "app";

    /// <summary>
    /// Exports the host imports by design and does not.
    ///
    /// chiaki_render_share_to_d3d9 is PP131's narrow entry: a five-line wrapper over
    /// chiaki_render_share_to_d3d9_format with BGRA8 filled in, and the host imports the wider one
    /// HDR needed. This shim has exactly ONE consumer in this repository, so a wrapper kept for
    /// source compatibility is compatibility with nobody - it stays because PP131's header prose is
    /// the record of that experiment, and it is named here so the set allowed to differ is a
    /// decision rather than this check's blind spot.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExportsNothingImports { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chiaki_render_share_to_d3d9"] =
                "PP131's narrow entry, superseded by chiaki_render_share_to_d3d9_format, which the "
                    + "host imports; kept because the header prose around it records the experiment",
        };

    /// <summary>The two headers' code, comments stripped, or null outside a checkout.</summary>
    public static string? ReadHeaders()
    {
        string?[] found = [.. HeaderRelativePaths.Select(SanitizerSource.LocateRelative)];

        if (found.Any(path => path is null))
            return null;

        return string.Concat(found.Select(path => CCall.Code(File.ReadAllText(path!))));
    }

    /// <summary>
    /// Every .cs file under app/, skipping the build output.
    ///
    /// bin/ and obj/ hold generated interop and a copy of the sources, and a reader that counted
    /// those would find every import twice and some that no longer exist.
    /// </summary>
    public static IReadOnlyList<string> ManagedSources(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        string managed = Path.Combine(root, ManagedRelativeDirectory);
        if (!Directory.Exists(managed))
            return [];

        return
        [
            .. Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutput(root, path))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>The entry points the host names, deduplicated - two overloads can share one.</summary>
    public static IReadOnlySet<string> Imported(IEnumerable<string> managedSources)
    {
        ArgumentNullException.ThrowIfNull(managedSources);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string path in managedSources)
        {
            foreach (Match found in EntryPointRegex().Matches(File.ReadAllText(path)))
                names.Add(found.Groups["name"].Value);
        }

        return names;
    }

    /// <summary>The same read from text, which is what the synthetic cases exercise.</summary>
    public static IReadOnlySet<string> ImportedFrom(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SortedSet<string>(
            EntryPointRegex().Matches(source).Select(m => m.Groups["name"].Value),
            StringComparer.Ordinal);
    }

    /// <summary>Every function the headers declare.</summary>
    public static IReadOnlySet<string> Exported(string headerCode)
    {
        ArgumentNullException.ThrowIfNull(headerCode);

        return new SortedSet<string>(
            DeclarationRegex().Matches(headerCode).Select(m => m.Groups["name"].Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// THE ONE THAT CRASHES: names the host imports and no header declares.
    ///
    /// Empty is the only acceptable answer, and an entry here is a call that throws when it is first
    /// reached rather than when it is compiled.
    /// </summary>
    public static IReadOnlyList<string> Undefined(
        IReadOnlySet<string> imported, IReadOnlySet<string> exported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(exported);

        return [.. imported.Where(name => !exported.Contains(name)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The other way: exports nothing imports, minus the ones named with a reason.
    ///
    /// Not a crash and not nothing - a shim whose only consumer is this repository exports surface
    /// somebody maintains. An entry appearing here is a decision to make, not a defect.
    /// </summary>
    public static IReadOnlyList<string> UnimportedWithoutReason(
        IReadOnlySet<string> imported, IReadOnlySet<string> exported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(exported);

        return
        [
            .. exported
                .Where(name => !imported.Contains(name))
                .Where(name => !ExportsNothingImports.ContainsKey(name))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool IsUnderBuildOutput(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);

        return relative.Split(Path.DirectorySeparatorChar)
            .Any(part => ForeignBinaries.SkippedDirectoryNames.Contains(part));
    }

    // EntryPoint = "chiaki_shim_takion_packet_mac" - the shim's names only. A kernel32 import is not
    // this seam's business, and there are three of those.
    [GeneratedRegex(@"EntryPoint\s*=\s*""(?<name>chiaki_(?:shim|render)_[a-z0-9_]+)""")]
    private static partial Regex EntryPointRegex();

    // ... chiaki_shim_foo( - a declaration, in a header whose comments are already gone.
    [GeneratedRegex(@"\b(?<name>chiaki_(?:shim|render)_[a-z0-9_]+)\s*\(")]
    private static partial Regex DeclarationRegex();
}
