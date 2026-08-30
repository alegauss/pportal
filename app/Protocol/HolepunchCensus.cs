using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP534: which of holepunch.c's functions anything under app/ names, counted rather than read.
///
/// §PP533 says what is left of the file in a sentence - "the candidate and STUN work, the
/// notification queue and the state machine" - and that sentence was written by reading six
/// thousand lines, which is how a scope becomes a guess nobody rechecks. Counting says something
/// different: the candidate and STUN work is among the part already named, and what is not named
/// is ten functions.
///
/// WHAT "NAMED" MEANS: app/ quotes the C symbol somewhere. That is the join
/// UnreferencedExportTests uses, for its reason - a name used as a callback or quoted in a comment
/// is a reference a call-shaped match misses.
///
/// PP536: IT ERRS IN BOTH DIRECTIONS, and PP534 wrote down only one of them. A managed file
/// mentioning a C function in prose reads as named, which over-reports. And a counterpart that
/// does not quote the C name reads as unlooked-at, which under-reports - eight of the ten PP534
/// found have one: http_create_session is SessionCalls.CreateAsync, the UPnP discover is
/// GatewayDiscovery, and so on down the list HolepunchCensusTests now carries.
///
/// So this is not a bound on coverage in either direction. It is a cheap sweep that produces a
/// SHORT LIST worth reading by hand, and the reading is what the test beside it records. Used that
/// way it is useful; read as "nothing has looked at these" it is wrong, which is how PP534 read it.
/// </summary>
public static partial class HolepunchCensus
{
    /// <summary>The file, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>Where a managed counterpart would be.</summary>
    public const string ManagedRelativePath = "app";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Where the managed side is, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativePath);

    /// <summary>Build output, which is not the managed side saying anything.</summary>
    public static IReadOnlyList<string> ExcludedDirectories { get; } = ["bin", "obj"];

    /// <summary>
    /// This file, which the sweep must not read.
    ///
    /// PP536 walked into it: correcting the doc comment above to say that http_create_session is
    /// answered by SessionCalls put that C name into app/, and the next run reported it as quoted.
    /// A check cannot be its own evidence, and this is the second place that bites - the annotated
    /// list lives in tests/ for the same reason.
    /// </summary>
    public static IReadOnlyList<string> ExcludedFiles { get; } = ["HolepunchCensus.cs"];

    /// <summary>
    /// Every function holepunch.c DEFINES, by its definition rather than its declaration.
    ///
    /// The brace on its own line is what tells the two apart: this file declares its statics near
    /// the top and defines them far below, and counting both would report every static twice while
    /// counting a name nothing implements as implemented.
    /// </summary>
    public static IReadOnlyList<string> DefinedFunctions(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return [.. DefinitionRegex()
            .Matches(source.ReplaceLineEndings("\n"))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The split: which of them app/ names, and which it does not.
    /// </summary>
    /// <param name="source">holepunch.c.</param>
    /// <param name="managedDirectory">Where app/ is.</param>
    public static (IReadOnlyList<string> Named, IReadOnlyList<string> Unnamed) Split(
        string source, string managedDirectory)
    {
        ArgumentNullException.ThrowIfNull(managedDirectory);

        string managed = ReadManaged(managedDirectory);
        var named = new List<string>();
        var unnamed = new List<string>();

        foreach (string function in DefinedFunctions(source))
        {
            if (Regex.IsMatch(managed, $@"\b{Regex.Escape(function)}\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
                named.Add(function);
            else
                unnamed.Add(function);
        }

        return (named, unnamed);
    }

    /// <summary>Every managed source, as one string, with build output left out.</summary>
    private static string ReadManaged(string directory)
    {
        var text = new System.Text.StringBuilder();

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, file);
            bool output = relative
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));

            bool itself = ExcludedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);

            if (!output && !itself)
                text.Append(File.ReadAllText(file)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A definition: an optional storage class, a return type, a name, a parameter list, and a
    /// brace on the next line. Anchored at column zero, which is where this file puts them.
    /// </summary>
    [GeneratedRegex(
        @"(?m)^(?:static\s+|CHIAKI_EXPORT\s+)?[A-Za-z_][A-Za-z0-9_ \*]*?\**\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\([^;]*?\)\s*\n\{",
        RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex DefinitionRegex();
}
