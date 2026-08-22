using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>
/// PP22: the roots <c>scripts\package-windows.sh</c> walks the installer's payload from.
///
/// The packaging script cannot read <see cref="ChiakiNative.NativeLibraries"/> - it is a shell
/// script and that is a C# dictionary - so the three DLL names exist twice. Duplication was the
/// choice, the way it was for PP88's regex patterns; going unchecked was not. This reads the
/// script's own seed array back so the selftest can say the two lists are the same list.
///
/// What it protects is narrow and expensive: the resolver's table is what the host loads at
/// runtime, and the script's array is what an installer lays down. A name added to one and not the
/// other produces an installer that passes every test in this repository and then fails on a user's
/// first launch with a DllNotFoundException naming a file nobody shipped.
///
/// Like every reader here it only works in a checkout, which is honest rather than convenient - an
/// installed copy has no scripts\ beside it. A file it cannot parse yields an empty list, which
/// fails the comparison rather than passing it: PP272's rule, that a drift check answers no to an
/// empty file instead of finding nothing to disagree with.
/// </summary>
public static partial class PayloadScript
{
    /// <summary>Where the packaging script lives, relative to the repository root.</summary>
    public const string RelativePath = @"scripts\package-windows.sh";

    /// <summary>The script, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The names in the script's <c>payload_libraries=(...)</c> array, in the order it lists them.
    /// </summary>
    public static IReadOnlyList<string> LibrariesIn(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        Match seed = SeedRegex().Match(File.ReadAllText(filePath));
        return seed.Success
            ? seed.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            : [];
    }

    // One line, unquoted, which is how the script spells it. A form this does not match answers
    // with no libraries at all and fails the comparison - the direction a check like this has to
    // break in, because the alternative is a silent pass over a script it stopped understanding.
    [GeneratedRegex(@"payload_libraries=\(([^)]*)\)")]
    private static partial Regex SeedRegex();
}
