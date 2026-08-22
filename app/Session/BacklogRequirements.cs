using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP312: what a line waits on that is not another line, held against what the project declared.
///
/// Deps could not express it and were right not to. PP23's deps are satisfied and PP297's are
/// empty, so both are ready in the only sense a dep graph means it - and neither can be started,
/// because starting them needs a console reaching the stream. `brief` answered PP23 on four
/// consecutive sessions for that reason.
///
/// roadkeep's answer is `[requirements]`, and it is two files that have to agree with no reader in
/// common: roadkeep.toml declares the names, docs/ROADMAP.md spells them on the lines that wait.
/// A line requiring something undeclared is a typo that reads as a real blocker; a declared
/// requirement nothing uses is a blocker that was lifted and never removed, which is worse - it
/// says the project is still waiting for a thing it has.
/// </summary>
public static partial class BacklogRequirements
{
    /// <summary>Where the requirement names are declared.</summary>
    public const string ConfigRelativePath = "roadkeep.toml";

    /// <summary>And where lines say they wait on one.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The config, or null outside a checkout.</summary>
    public static string? LocateConfig() => SanitizerSource.LocateRelative(ConfigRelativePath);

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? LocateRoadmap() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>
    /// The names <c>[requirements] declared</c> holds.
    ///
    /// Read from the array rather than from the whole file, because every one of them is also
    /// written in the prose above it explaining why it is there - and a comment is not a
    /// declaration.
    /// </summary>
    public static IReadOnlySet<string> Declared(string config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        Match array = DeclaredArrayRegex().Match(config);
        if (!array.Success)
            return names;

        foreach (Match name in QuotedRegex().Matches(array.Groups["items"].Value))
            names.Add(name.Groups["name"].Value);

        return names;
    }

    /// <summary>Every requirement a roadmap line says it waits on.</summary>
    public static IReadOnlySet<string> Used(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match line in RequiresRegex().Matches(roadmap))
        {
            foreach (string name in line.Groups["names"].Value.Split(','))
            {
                string trimmed = name.Trim();
                if (trimmed.Length > 0)
                    names.Add(trimmed);
            }
        }

        return names;
    }

    // declared = [ "console", ... ]  - up to the closing bracket, comments and all.
    [GeneratedRegex(@"declared\s*=\s*\[(?<items>[^\]]*)\]", RegexOptions.Singleline)]
    private static partial Regex DeclaredArrayRegex();

    [GeneratedRegex("\"(?<name>[^\"]+)\"")]
    private static partial Regex QuotedRegex();

    // (requires: console) and (requires: a, b)
    [GeneratedRegex(@"\(requires:\s*(?<names>[^)]+)\)")]
    private static partial Regex RequiresRegex();
}
