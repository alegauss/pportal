namespace ChiakiNg.Session;

/// <summary>
/// PP435: executables for a platform this port does not ship to, found where its source lives.
///
/// scripts/holepunch/ carried two: `holepunch` at 7,660,992 bytes and `refresh-token` at 7,488,349,
/// both unstripped Linux x86-64 wanting /lib64/ld-linux-x86-64.so.2. Nothing in the tree named
/// either. They arrived with upstream's "Added holepunch go scripts" and nothing looked again, and
/// they were the only two tracked ELF files in the repository.
///
/// WINDOWS-ONLY IS A BINDING NON-GOAL, which is what makes this a rule and not a preference. An ELF
/// binary in a source directory here cannot run on the platform the thing ships to, so there is no
/// configuration in which it is the answer to something.
///
/// A .gitignore stops those two returning by name. This is the general case: a THIRD one, built by
/// some other script into some other directory, would be caught here rather than in a year.
///
/// third-party/ IS NOT WALKED, deliberately. Vendored source ships fixtures and prebuilt helpers for
/// platforms its own upstream supports, and curl alone carries test data this rule has no business
/// judging. What this checks is the source this port is responsible for.
///
/// AND NEITHER IS A SUBMODULE, for the same reason and by a rule rather than a name. test/munit is
/// one, declared in .gitmodules and sitting inside a directory this walks, so "not third-party/" was
/// not the whole of the exclusion - the boundary is a nested checkout, which announces itself with a
/// .git entry. munit found it: its gitlink is a FILE and not a directory, so the first version of
/// this walked straight in and read munit's tree as if this port owned it.
/// </summary>
public static class ForeignBinaries
{
    /// <summary>
    /// The directories this port's own source lives in.
    ///
    /// Named rather than "everything below the root": build/, spike/ and gate/ hold real build
    /// output, and a rule that walked them would be a rule about a .gitignore instead.
    /// </summary>
    public static IReadOnlyList<string> SourceRelativeDirectories { get; } =
        ["app", "lib", "shim", "gui", "scripts", "test", "tests", "cmake"];

    /// <summary>
    /// Directory names skipped wherever they appear.
    ///
    /// bin and obj are the .NET build's, and tests/ and app/ both have them - so this walk would
    /// otherwise read a few thousand files to answer a question about none of them.
    /// </summary>
    public static IReadOnlySet<string> SkippedDirectoryNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs", "build",
        };

    /// <summary>The ELF magic: 0x7F then "ELF", which is the whole of the test.</summary>
    public static ReadOnlySpan<byte> ElfMagic => [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    /// <summary>
    /// Whether a file's opening bytes are an ELF header.
    ///
    /// Fewer than four bytes is not one - which is the empty-input case, and it has to answer false
    /// rather than throw: this walks a tree it does not control and an empty file is ordinary.
    /// </summary>
    public static bool IsElf(ReadOnlySpan<byte> opening)
        => opening.Length >= 4 && opening[..4].SequenceEqual(ElfMagic);

    /// <summary>
    /// Every file under the named directories, skipping the build output.
    ///
    /// Returned as full paths, and a directory that is not in this checkout is skipped rather than
    /// reported - gui/ is absent from a source tree configured without the Qt client.
    /// </summary>
    public static IReadOnlyList<string> Walk(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        var found = new List<string>();

        foreach (string relative in SourceRelativeDirectories)
        {
            string start = Path.Combine(root, relative);
            if (Directory.Exists(start))
                WalkInto(start, found);
        }

        return found;
    }

    /// <summary>
    /// The files under this port's source that are built for another platform.
    ///
    /// Paths are returned relative to the root, because the absolute one names the machine that ran
    /// the check and the point is the file.
    /// </summary>
    public static IReadOnlyList<string> Foreign(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        var foreign = new List<string>();
        Span<byte> opening = stackalloc byte[4];

        foreach (string path in Walk(root))
        {
            int read;
            try
            {
                using FileStream file = File.OpenRead(path);
                read = file.Read(opening);
            }
            catch (IOException)
            {
                // A file another process holds open is not evidence either way, and a gate that
                // failed on one would be a gate that fails at random.
                continue;
            }

            if (IsElf(opening[..read]))
                foreign.Add(Path.GetRelativePath(root, path));
        }

        return foreign;
    }

    /// <summary>
    /// Whether a directory is the top of a checkout other than this one.
    ///
    /// Either shape counts: a submodule's .git is a FILE holding a gitdir: line, and a plain nested
    /// clone's is a directory. test/munit is the first kind, which is why a check for a directory
    /// alone would have walked into it.
    /// </summary>
    public static bool IsNestedCheckout(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string marker = Path.Combine(directory, ".git");
        return File.Exists(marker) || Directory.Exists(marker);
    }

    private static void WalkInto(string directory, List<string> found)
    {
        found.AddRange(
            Directory.EnumerateFiles(directory)
                .Where(file => !string.Equals(
                    Path.GetFileName(file), ".git", StringComparison.OrdinalIgnoreCase)));

        IEnumerable<string> descend = Directory.EnumerateDirectories(directory)
            .Where(sub => !SkippedDirectoryNames.Contains(Path.GetFileName(sub)))
            .Where(sub => !IsNestedCheckout(sub));

        foreach (string sub in descend)
            WalkInto(sub, found);
    }
}
