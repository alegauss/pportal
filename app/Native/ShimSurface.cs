using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>
/// PP580: the names this assembly imports from the shim, against the names the shim exports.
///
/// A <c>[DllImport]</c> is resolved on first call, not at build. So an export renamed in
/// chiaki_shim.c, or an EntryPoint typed with one letter wrong, compiles clean, passes the ABI
/// check - the version is a number, not a symbol table - and throws EntryPointNotFoundException
/// wherever that call happens to sit. For most of the 231 that is mid-session.
///
/// THE SELFTEST COVERS A SUBSET AND SAYS SO. Its note is that "a DLL from an older build exports
/// every name this assembly imports and answers all of them", which is what the ABI guards. What
/// nothing guarded is a name the CURRENT shim does not export at all.
///
/// BOTH DIRECTIONS ARE NEWS, which is PP290's argument for its own set one seam over. A name
/// imported and not exported is a call that will throw; a name exported and not imported is shim C
/// nothing reaches, which this port deletes rather than keeps. They are equal today at 231 each.
///
/// DEFINITIONS, NOT DECLARATIONS. The header declares each export too, so a sweep of the header
/// would pass on a function that was declared and never written - which is the same failure with a
/// different message.
/// </summary>
public static partial class ShimSurface
{
    /// <summary>The shim's implementation, which is where an export exists or does not.</summary>
    public const string SourceRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The shim source, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(SourceRelativePath);

    /// <summary>Where the imports are declared.</summary>
    public const string ManagedRelativePath = "app";

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativePath);

    /// <summary>The prefix every shim export carries.</summary>
    public const string Prefix = "chiaki_shim_";

    /// <summary>The functions the shim defines, by name.</summary>
    public static IReadOnlySet<string> Exports(string shimSource)
    {
        ArgumentNullException.ThrowIfNull(shimSource);

        return Definition().Matches(shimSource)
            .Select(one => one.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The entry points the host declares, by name, across every file under app/.</summary>
    public static IReadOnlySet<string> Imports(string managedDirectory)
    {
        ArgumentNullException.ThrowIfNull(managedDirectory);

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(managedDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(@"\obj\", StringComparison.Ordinal)
                || path.Contains(@"\bin\", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in EntryPoint().Matches(File.ReadAllText(path)))
                found.Add(match.Groups["name"].Value);
        }

        return found;
    }

    /// <summary>A shim function's definition - the API macro, a return type, the name, a brace list.</summary>
    [GeneratedRegex(@"CHIAKI_SHIM_API[^;{]*?\b(?<name>chiaki_shim_\w+)\s*\([^;]*?\)\s*\{")]
    private static partial Regex Definition();

    /// <summary>An EntryPoint naming a shim export.</summary>
    [GeneratedRegex(@"EntryPoint\s*=\s*""(?<name>chiaki_shim_\w+)""")]
    private static partial Regex EntryPoint();
}
