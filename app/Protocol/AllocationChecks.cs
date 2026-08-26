using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One allocation in the C, and what it was assigned to.</summary>
/// <param name="File">Which file it is in.</param>
/// <param name="Line">The line, 1-based, so a failure is clickable.</param>
/// <param name="Target">What receives the pointer - a name, or a dereference like <c>*out</c>.</param>
/// <param name="Call">malloc, calloc, realloc or strdup.</param>
public readonly record struct Allocation(string File, int Line, string Target, string Call)
{
    /// <summary>Said the way a failure should read.</summary>
    public override string ToString() => $"{File}:{Line}  {Target} = {Call}(...)";
}

/// <summary>
/// PP398: every allocation in lib/src is tested before what it produced is used.
///
/// PP345 found one instance of this and fixed it one layer up: chiaki_ctrl_set_login_pin returned
/// void, so a failed malloc reached the person as a wrong PIN. The shape is a failure arriving as
/// something else, and it is worth a rule rather than a line because nothing about an unchecked
/// allocation looks wrong - the code reads as though it succeeded, which it usually did.
///
/// THE ONE THIS FOUND was get_websocket_fqdn. It ended with
/// <c>*fqdn = strdup(json_object_get_string(fqdn_json));</c> and fell into its cleanup, where `err`
/// was already CHIAKI_ERR_SUCCESS - so a failed allocation returned success with the out-parameter
/// left NULL. The caller stores it in session->ws_fqdn, and what consumes it is
/// <c>snprintf("wss://%s/np/pushNotification", session-&gt;ws_fqdn)</c>. A remote play session then
/// fails to open a websocket to a host built from a null pointer, and the report says the network
/// is broken.
///
/// BOTH SPELLINGS OF THE CHECK COUNT. holepunch.c writes <c>if(!(*out))</c> where ctrl.c writes
/// <c>if(!buf)</c>, and a reader that knew only one of them called three correct sites defects -
/// which is how the first version of this sweep read, before the code was looked at.
/// </summary>
public static partial class AllocationChecks
{
    /// <summary>Where the library's C lives.</summary>
    public const string SourceRelativePath = @"lib\src";

    /// <summary>The directory, or null outside a checkout. See PP382's note on the locator.</summary>
    public static string? LocateSources() => SanitizerSource.LocateDirectory(SourceRelativePath);

    [GeneratedRegex(
        @"(?m)^[ \t]*(?:[\w\*\s]*?[\s\*])?(?<target>\*?\(?\*?[\w\->\.\[\]]+\)?)\s*=\s*(?<call>malloc|calloc|realloc|strdup)\s*\(")]
    private static partial Regex AllocationSite();

    /// <summary>Every allocation assigned to something, in one translation unit.</summary>
    public static IReadOnlyList<Allocation> AllocationsIn(string file, string source)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<Allocation>();

        foreach (Match match in AllocationSite().Matches(source))
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            if (source[lineStart..match.Index].Contains("//", StringComparison.Ordinal))
                continue;

            found.Add(new Allocation(
                file,
                source.Take(match.Index).Count(c => c == '\n') + 1,
                match.Groups["target"].Value.Trim(),
                match.Groups["call"].Value));
        }

        return found;
    }

    /// <summary>
    /// The ones whose result is not tested in the lines that follow.
    ///
    /// A test is any mention of the target inside a condition within the next few lines - the two
    /// spellings this tree uses are <c>if(!buf)</c> and <c>if(!(*out))</c>, and asking for a
    /// mention rather than a spelling covers both and whatever a third file writes.
    /// </summary>
    /// <param name="source">The translation unit.</param>
    /// <param name="allocations">What <see cref="AllocationsIn"/> found in it.</param>
    /// <param name="within">
    /// How many lines after the allocation the check may be. Six, because streamconnection.c puts
    /// a comment between the strdup and the guard - PP371's, explaining why the guard is there -
    /// and a window of four called that site a defect when it is the opposite.
    /// </param>
    public static IReadOnlyList<Allocation> Unchecked(
        string source, IReadOnlyList<Allocation> allocations, int within = 6)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentOutOfRangeException.ThrowIfNegative(within);

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var missing = new List<Allocation>();

        foreach (Allocation allocation in allocations)
        {
            if (IsExcused(allocation, lines))
                continue;

            // The bare name, without the dereference or the parentheses around it.
            string name = allocation.Target.Trim('*', '(', ')', '&');
            if (name.Length == 0)
                continue;

            var tested = false;
            for (int at = allocation.Line; at < Math.Min(allocation.Line + within, lines.Length); at++)
            {
                string line = lines[at];
                if (!line.Contains("if", StringComparison.Ordinal))
                    continue;

                if (line.Contains(name, StringComparison.Ordinal))
                {
                    tested = true;
                    break;
                }
            }

            if (!tested)
                missing.Add(allocation);
        }

        return missing;
    }

    /// <summary>
    /// Whether NULL is a correct answer at this site, so testing for it buys nothing.
    ///
    /// TWO SHAPES, AND BOTH ARE ABOUT WHAT USES THE POINTER rather than about tolerating a gap.
    ///
    /// A ZERO-SIZE ALLOCATION may return NULL or a unique pointer and both are conforming. The
    /// eleven in holepunch.c are <c>.data = malloc(0)</c> in an HttpResponseData, and their only
    /// consumer is <c>realloc(response_data-&gt;data, ...)</c>, which treats NULL as malloc. So NULL
    /// is not a failure there; it is one of the two right answers.
    ///
    /// THE DISCOVERY NAME is the other. Its macro writes <c>strdup</c> in one arm and a literal
    /// NULL in the other, so the field's readers already handle NULL - a failed strdup produces a
    /// host with no name, which is exactly what the else branch produces on purpose.
    /// </summary>
    public static bool IsExcused(Allocation allocation, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (allocation.Line - 1 >= lines.Count || allocation.Line < 1)
            return false;

        string line = lines[allocation.Line - 1];

        // A zero-size allocation, consumed by a realloc that accepts NULL.
        if (line.Contains("malloc(0)", StringComparison.Ordinal)
            || line.Contains("calloc(0,", StringComparison.Ordinal))
        {
            return true;
        }

        // The discovery host's name, where the sibling arm assigns NULL deliberately.
        return string.Equals(allocation.File, "discoveryservice.c", StringComparison.Ordinal)
            && allocation.Target.EndsWith("name", StringComparison.Ordinal);
    }
}
