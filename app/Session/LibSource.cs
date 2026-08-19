using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP6: the check that keeps the shim's one undeclared import honest.
///
/// chiaki_discovery_srch_response_parse is CHIAKI_EXPORT in lib/src/discovery.c and appears in no
/// header. The shim declares it itself, because the alternative - a second reply parser on the
/// managed side - is the one piece of discovery that would need a console present to disprove.
/// The price is a signature no compiler compares against its definition, and a wrong one there is
/// not a build error but a corrupted stack at the first reply.
///
/// So it is compared here instead, the same way <see cref="SanitizerSource"/> compares patterns:
/// read the definition out of lib/, normalise the whitespace, and hold the two texts equal. lib/
/// is the half of this project that is not being ported, and nothing here writes to it.
/// </summary>
public static partial class LibSource
{
    /// <summary>Where the definition lives, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\discovery.c";

    /// <summary>Where the shim's declaration lives.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The shim source, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// The parameter list of a function as <paramref name="filePath"/> spells it, whitespace
    /// squeezed to single spaces, or null where the file does not define it.
    ///
    /// Only the parameters are compared. The return type and the CHIAKI_EXPORT in front of it
    /// differ between a definition and a declaration by design, and it is the parameters that
    /// corrupt a stack when they disagree.
    /// </summary>
    public static string? SignatureOf(string filePath, string function)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(function);

        Match m = Regex.Match(
            File.ReadAllText(filePath),
            Regex.Escape(function) + @"\s*\(([^)]*)\)",
            RegexOptions.Singleline);

        return m.Success ? WhitespaceRegex().Replace(m.Groups[1].Value, " ").Trim() : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
