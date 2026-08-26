using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP357: an assert is not a bound, because the shipped build compiles it out.
///
/// This project configures Release with -DNDEBUG, which is what CMake does by default and what the
/// portable tree is built with. So every <c>assert</c> in lib/src is nothing in the binary that
/// ships, and any invariant standing on one is unenforced exactly where it matters.
///
/// Two keyboard handlers relied on that. Each checked its header had arrived and then asserted the
/// relationship between the payload it received and the text length the header CLAIMED, before
/// mallocing that length and memcpying it out of a 512-byte buffer. A modest lie read half a
/// kilobyte past the end and handed it to a screen as the text the user was editing.
///
/// THE CHECK IS A SHAPE, not a list of the two that were wrong. What it looks for is an assert
/// standing between a size and a copy, in a file that never sees NDEBUG defined - because the next
/// invariant written that way is the same defect with a different name.
/// </summary>
public static partial class CtrlAssertBounds
{
    /// <summary>Where the handlers live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Where the build type is recorded, once a tree has been configured.</summary>
    public const string CacheRelativePath = @"build\CMakeCache.txt";

    /// <summary>That cache, or null where nothing has been configured yet.</summary>
    public static string? LocateCache() => SanitizerSource.LocateRelative(CacheRelativePath);

    /// <summary>
    /// Whether the configured build compiles asserts out.
    ///
    /// Read rather than assumed: the whole argument of this task is that the flags decide, and a
    /// tree configured Debug would make the asserts real and this check moot.
    /// </summary>
    public static bool AssertsAreCompiledOut(string cmakeCache)
    {
        ArgumentNullException.ThrowIfNull(cmakeCache);

        bool release = cmakeCache.Contains("CMAKE_BUILD_TYPE:STRING=Release", StringComparison.Ordinal);
        bool defined = cmakeCache.Contains("CMAKE_C_FLAGS_RELEASE:STRING=", StringComparison.Ordinal)
            && cmakeCache.Contains("-DNDEBUG", StringComparison.Ordinal);

        return release && defined;
    }

    /// <summary>
    /// Every assert that is the only thing between a copy and the length it copies.
    ///
    /// Found by shape: an <c>assert</c> mentioning a size, followed by a <c>memcpy</c> before any
    /// <c>if</c> that could have checked instead. That is the pattern the two keyboard handlers had.
    /// </summary>
    /// <returns>The assert text of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> AssertsStandingInForABound(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match assertion in SizeAssert().Matches(source))
        {
            int after = assertion.Index + assertion.Length;

            int copy = source.IndexOf("memcpy(", after, StringComparison.Ordinal);
            if (copy < 0)
                continue;

            // A real check between the two means the assert is not what the copy stands on.
            int guard = source.IndexOf("if(", after, StringComparison.Ordinal);
            if (guard < 0 || guard > copy)
                found.Add(assertion.Value.Trim());
        }

        return found;
    }

    // An assert about a size or a length - the shape an invariant about a buffer takes.
    [GeneratedRegex(@"assert\([^;]*(?:size|length|len)[^;]*\);", RegexOptions.IgnoreCase)]
    private static partial Regex SizeAssert();
}
