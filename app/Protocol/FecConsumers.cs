using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP574: who calls the FEC decode, which PP30's line counts as one.
///
/// PP289 shipped saying "frameprocessor.c is the only caller of the FEC decode" and it was true
/// then. PP30's line still says it. There are three: frameprocessor.c, the C suite's own fec.c, and
/// this port's shim.
///
/// THE SHIM IS THE ONE THAT KEEPS BEING MISSED, and it is not specific to this module. It wraps 130
/// chiaki_ entry points across every module the port has - session, takion, rpcrypt, discovery,
/// video, frame, holepunch, fec. So the shim is a consumer of everything any deletion task plans to
/// remove, and a line saying "N callers" is short by one unless it counted the port's own seam.
/// PP563 found that for holepunch and PP573 corrected PP33's line; this is the same fact one module
/// over, and the reason it is worth a model rather than another one-line fix.
///
/// HELD AS A LIST, NOT A PARSER. Telling a call from a declaration by reading C is the job
/// CFunction does for bodies and it is more than this needs: the three files are named, and each is
/// asserted to still call the export. A fourth caller arriving is what this cannot see, and is what
/// the counterpart check on the roadmap line is for.
/// </summary>
public static class FecConsumers
{
    /// <summary>The export PP30's deletion is measured by.</summary>
    public const string Export = "chiaki_fec_decode";

    /// <summary>Where it is called, in the order a reader meets them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        @"lib\src\frameprocessor.c",
        @"test\fec.c",
        @"shim\chiaki_shim.c",
    ];

    /// <summary>
    /// Where it is DEFINED, which is not a call and is why a plain sweep overcounts.
    ///
    /// fec.h declares it and fec.c defines it; both name the symbol and neither depends on it.
    /// </summary>
    public static IReadOnlyList<string> Declares { get; } =
        [@"lib\include\chiaki\fec.h", @"lib\src\fec.c"];

    /// <summary>Any of the files above, or null outside a checkout.</summary>
    public static string? Locate(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return SanitizerSource.LocateRelative(relativePath);
    }

    /// <summary>Whether a file still calls the export.</summary>
    public static bool Calls(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Contains(Export + "(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether PP30's line agrees with this list.
    ///
    /// The old claim is refused by name, the way PP573 refuses PP33's: a line that merely stopped
    /// mentioning callers would otherwise pass a check for the new phrasing being absent.
    /// </summary>
    public static bool TheRoadmapLineAgreesOnTheCount(string roadmapLine)
    {
        ArgumentNullException.ThrowIfNull(roadmapLine);

        if (roadmapLine.Contains("one caller left", StringComparison.OrdinalIgnoreCase))
            return false;

        return roadmapLine.Contains("three callers", StringComparison.OrdinalIgnoreCase);
    }
}
