namespace ChiakiNg.Session;

/// <summary>
/// PP655's flip is 25 exports and not 9, which an attempt at it found rather than a reading.
///
/// PP653 took holepunch.c out of lib's sources and asked the linker what failed. It named ten
/// undefined references, all in the shim, all holepunch's - and PP655 sized the flip from that
/// answer. The answer was true and it was narrow, for a reason the method hides: json-c was still
/// LINKED at the time, so the shim's own json-c calls resolved and the linker had nothing to say
/// about them.
///
/// THE SHIM IS AN ORACLE FOR BOTH LIBRARIES, not one. Fifteen chiaki_shim_json_* wrappers call
/// json_object, json_tokener and json_pointer directly, so the managed replacement can be held
/// against the library it replaces - the same reason the nine holepunch wrappers exist, for PP33's
/// other half. An attempt at PP655's flip on 2026-09-03 gated the file, curl, json-c and the nine,
/// and the shim failed to link on json_object_object_get_ex.
///
/// SO PP655's FIRST STEP IS NOT FINISHED, and what is missing is a third set of callers nobody had
/// counted. PP656 asked the seam's shape, PP657 derived the census's allowance from it, PP658
/// converted the file that drives the C - and all three are about the holepunch nine. The json
/// fifteen and their managed consumers have had none of it.
///
/// The attempt was reverted rather than finished. That is PP623's own rule arriving in practice: a
/// deletion done in one transaction is red from its first edit until its last, and the point of an
/// order is that each commit lands green. What the attempt bought is this number.
/// </summary>
public static class DeletedLibraryOracles
{
    /// <summary>The shim, where both oracles are.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The header that is the contract PP437's census reads.</summary>
    public const string ShimHeaderRelativePath = @"shim\chiaki_shim.h";

    /// <summary>
    /// The prefix the json oracle's wrappers share.
    ///
    /// A prefix rather than fifteen names: they are one group with one managed counterpart, and a
    /// list would go stale the first time the counterpart wanted a sixteenth.
    /// </summary>
    public const string JsonWrapperPrefix = "chiaki_shim_json_";

    /// <summary>The json-c entry points those wrappers reach, by prefix.</summary>
    public static IReadOnlyList<string> JsonEntryPrefixes { get; } =
        ["json_object", "json_tokener", "json_pointer"];

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// The exported wrappers that call json-c, by name and ordered.
    ///
    /// Derived, for the reason PP653's ten were: the nine in this tree's prose had been nine for two
    /// commits longer than it was true, and a number typed once is a number that stops being right
    /// somewhere nobody is looking.
    /// </summary>
    public static IReadOnlyList<string> JsonWrappers(string shim)
    {
        ArgumentNullException.ThrowIfNull(shim);

        var found = new List<string>();
        string? current = null;
        bool calls = false;

        foreach (string line in CCall.Code(shim).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("CHIAKI_SHIM_API", StringComparison.Ordinal))
            {
                if (current is not null && calls)
                    found.Add(current);

                current = HolepunchShimSurface.NameIn(line);
                calls = false;
                continue;
            }

            if (current is not null && CallsJsonC(line))
                calls = true;
        }

        if (current is not null && calls)
            found.Add(current);

        return found;
    }

    /// <summary>
    /// Whether a line calls into json-c, as opposed to naming it.
    ///
    /// The prefix followed by anything and an opening parenthesis. A variable would have to be
    /// called json_object_something and then be called, which is not a shape this file has.
    /// </summary>
    public static bool CallsJsonC(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        foreach (string prefix in JsonEntryPrefixes)
        {
            int at = line.IndexOf(prefix, StringComparison.Ordinal);
            while (at >= 0)
            {
                int end = at;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;

                if (end < line.Length && line[end] == '(')
                    return true;

                at = line.IndexOf(prefix, at + 1, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary>
    /// Everything the flip removes from the shim: both oracles and the device id's wrapper.
    ///
    /// The number PP655 was written without. Derived from the file rather than stated, so the day a
    /// wrapper is added or removed this is the count and not a claim about one.
    /// </summary>
    public static int FlipSurface(string shim)
    {
        ArgumentNullException.ThrowIfNull(shim);

        return HolepunchShimSurface.Wrappers(shim).Count + JsonWrappers(shim).Count;
    }
}
