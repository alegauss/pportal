using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP368: nothing that owns a thread is released with a bare free, and nothing is copied into an
/// allocation nobody checked.
///
/// A ChiakiGKCrypt owns a key-buffer THREAD, a cond, a mutex and an aligned buffer.
/// chiaki_gkcrypt_fini stops the thread, JOINS it, and then releases the rest; chiaki_gkcrypt_free
/// is that plus the struct. One error path in streamconnection.c used a bare free instead - so the
/// thread went on running with its struct already freed. A use-after-free by a live thread, not a
/// leak, and reachable when the second of two gkcrypt allocations fails.
///
/// THE CHECK IS THE PAIRING. A type with its own fini has a matching free, and a bare free of such a
/// type is the defect wherever it appears - which is what notices the next one rather than this one.
///
/// The second half is smaller and the same shape: a malloc whose result is memcpy'd into without
/// being tested. The early-streaminfo save did that, and the one moment it runs is a console
/// answering faster than the client changes state.
/// </summary>
public static partial class OwnedResourceFrees
{
    /// <summary>The files this checks.</summary>
    public static IReadOnlyList<string> Files { get; } =
        [@"lib\src\streamconnection.c", @"lib\src\ctrl.c", @"lib\src\session.c"];

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Types whose release does more than free memory, so a bare free of one loses the rest.
    ///
    /// Named by the field a bare free would name. Each has a chiaki_*_free that finis first.
    /// </summary>
    public static IReadOnlyList<string> OwnThreadsOrLocks { get; } =
        ["gkcrypt_local", "gkcrypt_remote"];

    /// <summary>
    /// Every bare free of something that owns more than memory.
    /// </summary>
    /// <returns>The free's text, so a failure names what it found.</returns>
    public static IReadOnlyList<string> BareFreesOfOwnedResources(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (string field in OwnThreadsOrLocks)
        {
            foreach (Match bare in Regex.Matches(
                         source, @"[^_\w]free\s*\([^)]*" + Regex.Escape(field) + @"[^)]*\)"))
            {
                // chiaki_gkcrypt_free ends in the same four letters; the class above excludes it by
                // requiring a non-identifier character before "free".
                found.Add(bare.Value.Trim());
            }
        }

        return found;
    }

    /// <summary>
    /// Every allocation copied into without being checked.
    ///
    /// A malloc assigned to something, and a memcpy into that same thing, with no test of it in
    /// between. The shape a reader skips because the two lines look like one operation.
    /// </summary>
    public static IReadOnlyList<string> UncheckedAllocationsCopiedInto(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match allocation in Allocation().Matches(source))
        {
            string target = allocation.Groups["target"].Value;

            int after = allocation.Index + allocation.Length;
            int copy = source.IndexOf($"memcpy({target},", after, StringComparison.Ordinal);
            if (copy < 0)
                continue;

            // A test of the target between the two means it was checked.
            string between = source[after..copy];
            if (!between.Contains($"!{target}", StringComparison.Ordinal)
                && !between.Contains($"{target} ==", StringComparison.Ordinal)
                && !between.Contains($"{target})", StringComparison.Ordinal))
            {
                found.Add(allocation.Value.Trim());
            }
        }

        return found;
    }

    // An assignment from malloc. The target is captured so the copy can be matched to it.
    [GeneratedRegex(@"(?<target>[\w>.\-]+)\s*=\s*malloc\([^;]*\);")]
    private static partial Regex Allocation();
}
