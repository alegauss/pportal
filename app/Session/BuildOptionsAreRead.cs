using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One build option, and how many times the build mentions it.</summary>
/// <param name="Name">The cache variable's name.</param>
/// <param name="Description">What it promises, as the declaration words it.</param>
/// <param name="Mentions">Every reference including the declaration.</param>
public readonly record struct BuildOption(string Name, string Description, int Mentions);

/// <summary>
/// PP430: an option the build declares and never reads is a promise it does not keep.
///
/// CHIAKI_ENABLE_RUDP was declared as "Enable Remote Play over Internet" and appeared once in the
/// whole build - its own declaration. Setting it OFF changed nothing: holepunch.c, rudp.c and
/// rudpsendbuffer.c sit in the unconditional source list, and curl and json-c link
/// unconditionally. Somebody turning the feature off got the feature, both libraries fetched, built
/// and shipped, and no message saying so.
///
/// THE COUNT IS WHAT MAKES IT CHECKABLE. Every other option in CMakeLists.txt is referenced between
/// three and fourteen times; that one was referenced once. A declaration with no reader is the whole
/// defect, and it is a number rather than a judgement.
///
/// IT ALSO DISSOLVED PP313. That line held that building the remote path off would satisfy PP33's
/// fourth criterion without the managed session ever landing - a "second door" worth deciding about
/// before somebody found it in a hurry. The door was not there: the option did nothing. PP313 is
/// retired against PP430 rather than decided.
///
/// THE RULE IS OVER DECLARATIONS, NOT OVER FEATURES. It cannot know whether an option's gate is
/// CORRECT - only that something reads it. That is the floor, and the floor is what was missing.
/// </summary>
public static partial class BuildOptionsAreRead
{
    /// <summary>The build files an option can be declared or read in.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        "CMakeLists.txt",
        @"lib\CMakeLists.txt",
        @"gui\CMakeLists.txt",
        @"test\CMakeLists.txt",
    ];

    /// <summary>
    /// The fewest mentions an option that is actually read can have.
    ///
    /// Two: the declaration, and at least one place that reads it. One means nothing does.
    /// </summary>
    public const int Floor = 2;

    /// <summary>Every option the top-level build declares, with how often the build mentions it.</summary>
    public static IReadOnlyList<BuildOption> All(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string top = Path.Combine(root, "CMakeLists.txt");
        if (!File.Exists(top))
            return [];

        string declarations = File.ReadAllText(top);

        // Every build file's text at once: an option declared at the top is usually read in a
        // subdirectory's file, which is the normal shape rather than an exception.
        string everywhere = string.Concat(
            Files
                .Select(one => Path.Combine(root, one.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .Select(File.ReadAllText));

        var options = new List<BuildOption>();

        foreach (Match declared in DeclarationRegex().Matches(declarations))
        {
            string name = declared.Groups["name"].Value;

            options.Add(new BuildOption(
                name,
                declared.Groups["desc"].Value,
                Mentions(everywhere, name)));
        }

        return options;
    }

    /// <summary>The options nothing reads, which should be none.</summary>
    public static IReadOnlyList<BuildOption> Unread(string root)
        => [.. All(root).Where(one => one.Mentions < Floor)];

    /// <summary>How many times a name appears, on an identifier boundary.</summary>
    public static int Mentions(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Regex.Matches(
            text, $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])").Count;
    }

    // option(NAME "desc" ...) and tri_option(NAME "desc" ...) - the two the build uses. Commented
    // declarations are not options: PP400's rule, and this file's own removal left a comment naming
    // the one it took out.
    [GeneratedRegex(@"^[ \t]*(?:tri_)?option\s*\(\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+""(?<desc>[^""]*)""",
        RegexOptions.Multiline)]
    private static partial Regex DeclarationRegex();
}
