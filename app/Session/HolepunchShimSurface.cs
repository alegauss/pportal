namespace ChiakiNg.Session;

/// <summary>
/// PP33: what the linker says is left, asked rather than counted from memory.
///
/// §PP33's fourth criterion is that libchiaki builds with neither curl nor json-c, and it names the
/// method: take holepunch.c out of lib's sources and read what fails, because "every reference the
/// exes fail on is a holepunch symbol, not curl or json-c" is a claim a build makes and prose does
/// not. PP565 measured the compiling half. This is the linking half.
///
/// THE ANSWER, taken by commenting src/remote/holepunch.c out of lib/CMakeLists.txt and building:
/// EVERYTHING COMPILES. lib compiles and its archive is built. ONE TARGET FAILS - chiaki-shim.dll -
/// and it fails on TEN undefined references, every one of them a holepunch symbol. Not one curl
/// call, not one json-c call, and nothing in test/ or in the exes.
///
/// So the four callers PP563 and PP564 named are down to one, and it is the port's own seam rather
/// than upstream's code: the wrappers PP481 put in the shim so the managed side could drive the C
/// instead of replacing it. What blocks the deletion is a thing this port wrote to hold the oracle.
///
/// AND THERE ARE TEN OF THEM, NOT NINE. Three places in this tree say nine - it was nine when PP481
/// wrote them, and PP556 added <c>set_recorded</c> as the tenth when the prepare became an instance
/// call that records the socket the ctrl punch produced. The count was never re-derived, so a number
/// from one commit outlived the commit after it. Counted here rather than typed, for that reason.
/// </summary>
public static class HolepunchShimSurface
{
    /// <summary>The shim, which is the one target the deletion now fails.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>Where holepunch.c is listed, and what the probe comments out.</summary>
    public const string LibCMakeRelativePath = @"lib\CMakeLists.txt";

    /// <summary>The line that carries it there.</summary>
    public const string SourceEntry = "src/remote/holepunch.c";

    /// <summary>
    /// The ten symbols the linker named, in the order it named them.
    ///
    /// Recorded because a build's output is not in the tree and this is what it said on 2026-09-03.
    /// Every one is holepunch's: the point of the criterion is what is ABSENT from this list, and
    /// what is absent is curl and json-c.
    /// </summary>
    public static IReadOnlyList<string> UndefinedReferences { get; } =
    [
        "chiaki_holepunch_generate_client_device_uid",
        "chiaki_holepunch_session_set_recorded",
        "chiaki_get_regist_info",
        "chiaki_get_ps_selected_addr",
        "chiaki_get_ps_ctrl_port",
        "chiaki_holepunch_session_init",
        "chiaki_get_holepunch_sock",
        "holepunch_session_create_offer",
        "chiaki_holepunch_session_punch_hole",
        "chiaki_holepunch_session_fini",
    ];

    /// <summary>
    /// The prefixes a curl or json-c reference would carry.
    ///
    /// The criterion is about what the failure set does NOT contain, and a check for absence needs
    /// to know what it is looking for. These are the two libraries PP33 is about.
    /// </summary>
    public static IReadOnlyList<string> DeletedLibraryPrefixes { get; } = ["curl_", "json_object", "json_tokener"];

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// The exported wrappers that call one of those symbols, by name and ordered.
    ///
    /// Derived from the shim rather than listed, which is the whole point: the nine in this tree's
    /// prose is a number somebody typed when it was true, and it stopped being true one commit
    /// later without anything noticing.
    /// </summary>
    public static IReadOnlyList<string> Wrappers(string shim)
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

                current = NameIn(line);
                calls = false;
                continue;
            }

            if (current is not null && UndefinedReferences.Any(
                symbol => line.Contains(symbol + "(", StringComparison.Ordinal)))
            {
                calls = true;
            }
        }

        if (current is not null && calls)
            found.Add(current);

        return found;
    }

    /// <summary>
    /// The exported name on a CHIAKI_SHIM_API line, or null where the declaration wraps past it.
    ///
    /// A wrapped declaration is not a failure to find one: the next line carries the name, and the
    /// caller only needs SOMETHING to attribute the calls below to.
    /// </summary>
    public static string? NameIn(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        int at = line.IndexOf("chiaki_shim_", StringComparison.Ordinal);
        if (at < 0)
            return "(wrapped declaration)";

        int end = at;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
            end++;

        return line[at..end];
    }

    /// <summary>Whether a symbol belongs to one of the libraries PP33 deletes.</summary>
    public static bool IsFromADeletedLibrary(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return DeletedLibraryPrefixes.Any(p => symbol.StartsWith(p, StringComparison.Ordinal));
    }
}
