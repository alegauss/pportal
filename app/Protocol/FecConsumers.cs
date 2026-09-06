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

    /// <summary>The header, which is the whole surface PP692 is about.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\fec.h";

    /// <summary>The constant it carries beside the export, and the only other thing in it.</summary>
    public const string WordSizeMacro = "CHIAKI_FEC_WORDSIZE";

    /// <summary>gf-complete's entry point, which is what PP30 actually deletes.</summary>
    public const string FieldInit = "galois_init_default_field";

    /// <summary>The file its one call site is in.</summary>
    public const string FieldInitRelativePath = @"lib\src\common.c";

    /// <summary>And the function, which is not a FEC function at all.</summary>
    public const string FieldInitFunction = "chiaki_lib_init";

    /// <summary>
    /// Every file that includes fec.h, with what it takes from it.
    ///
    /// PP692: THE COUNT ABOVE ANSWERS A DIFFERENT QUESTION FROM THE ONE PP30 ASKS. <see cref="All"/>
    /// is right about who calls the decode, and PP30 does not delete the decode - it deletes
    /// gf-complete and jerasure. Those come apart here: <c>common.c</c> includes this header for the
    /// word size alone, to size the field <see cref="FieldInit"/> installs, and it is the only
    /// caller of that function in the tree. So after fec.c and the shim's two wrappers leave,
    /// gf-complete is still linked - by a function every session calls before it does anything.
    ///
    /// PP697: AND THEY HAVE LEFT. PP696 took fec.c out of the build and put the shim's two behind
    /// an #ifdef, and this paragraph's prediction is now a description: gf-complete is still linked,
    /// still by chiaki_lib_init, and PP30 still has that to delete. The row below for fec.c is a row
    /// about a file the tree keeps as source, which is what the includer sweep reads.
    ///
    /// <c>audiosender.c</c> takes NEITHER, which makes its include dead. Recorded rather than
    /// removed: this port does not patch the vendored C, and a dead include is a fact about the
    /// deletion's surface either way.
    /// </summary>
    public static IReadOnlyList<FecIncluder> Includers { get; } =
    [
        new(@"lib\src\fec.c", FecHeaderUse.Decode),
        new(@"lib\src\frameprocessor.c", FecHeaderUse.Decode),
        new(@"shim\chiaki_shim.c", FecHeaderUse.Decode),
        new(@"test\fec.c", FecHeaderUse.Decode),
        new(@"lib\src\common.c", FecHeaderUse.WordSize),
        new(@"lib\src\audiosender.c", FecHeaderUse.Neither),
    ];

    /// <summary>The directories a sweep for an includer or a field-init caller reads.</summary>
    public static IReadOnlyList<string> SweptDirectories { get; } =
        [@"lib\src", @"lib\include\chiaki", "shim", "test"];

    /// <summary>Whether a source includes the header, by the two spellings C allows.</summary>
    public static bool IncludesTheHeader(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = Session.CCall.Code(source);

        return code.Contains("<chiaki/fec.h>", StringComparison.Ordinal)
            || code.Contains("\"chiaki/fec.h\"", StringComparison.Ordinal)
            || code.Contains("\"fec.h\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// What a source actually takes from the header, read from its text.
    ///
    /// The decode wins where both appear, because a file calling it is <see cref="All"/>'s subject
    /// whatever else it names. Comments are stripped first, so a mention in prose is not a use -
    /// which matters for fec.h itself, whose own comment names both.
    /// </summary>
    public static FecHeaderUse UseIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = Session.CCall.Code(source);

        if (code.Contains(Export + "(", StringComparison.Ordinal))
            return FecHeaderUse.Decode;

        return code.Contains(WordSizeMacro, StringComparison.Ordinal)
            ? FecHeaderUse.WordSize
            : FecHeaderUse.Neither;
    }

    /// <summary>Whether a source calls gf-complete's field init.</summary>
    public static bool InitialisesTheField(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Session.CCall.Code(source).Contains(FieldInit + "(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every swept file that satisfies a predicate on its text, as repository-relative paths.
    ///
    /// Read from the tree rather than from <see cref="Includers"/>, which is what lets a seventh
    /// includer or a second field-init caller fail a check instead of going unnoticed - the miss
    /// PP574 was written about, one question over.
    /// </summary>
    public static IReadOnlyList<string> Sweep(Func<string, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        if (Session.SanitizerSource.RepositoryRoot() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string directory in SweptDirectories)
        {
            string full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
                continue;

            foreach (string file in Directory.EnumerateFiles(full, "*.*", SearchOption.TopDirectoryOnly)
                .Where(one => one.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
                    || one.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                .OrderBy(one => one, StringComparer.OrdinalIgnoreCase))
            {
                if (holds(File.ReadAllText(file)))
                    found.Add(Path.GetRelativePath(root, file));
            }
        }

        return found;
    }
}

/// <summary>What a file takes from fec.h, which is how PP692 tells its includers apart.</summary>
public enum FecHeaderUse
{
    /// <summary>The export - so the file is one of <see cref="FecConsumers.All"/>, or defines it.</summary>
    Decode,

    /// <summary>The word size alone, which is gf-complete's field width and not a FEC call.</summary>
    WordSize,

    /// <summary>Neither, which makes the include dead.</summary>
    Neither,
}

/// <summary>One file that includes fec.h, and what it takes.</summary>
/// <param name="Path">The file, repository-relative and spelled as this platform spells a path.</param>
/// <param name="Uses">What of the header's two names it actually uses.</param>
public readonly record struct FecIncluder(string Path, FecHeaderUse Uses);
