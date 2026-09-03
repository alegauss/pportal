using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP31: where managed code stops, stated as a constraint rather than discovered at the end.
///
/// ffmpegdecoder.c is 376 lines and bitstream.c 450, and behind them is FFmpeg doing hardware
/// accelerated H.264 and HEVC decode. Nothing in .NET replaces that. A pure managed decoder is
/// possible in the sense that it can be written and impossible in the sense that it would not hold
/// the frame rate - and it would ignore a GPU that is already doing this work for free.
///
/// THE CLAIM TO CORRECT IS THE FRAMING, which is what §PP31 said it was for. The goal this port can
/// reach is one that is 100% WINDOWS and builds in Visual Studio. The goal it cannot reach is one
/// that is 100% MANAGED, and the decoder is the counter-example. Those two are easy to say in one
/// breath and only one of them is true.
///
/// WHICH NATIVE DECODER IS A SEPARATE QUESTION and this does not answer it. FFmpeg through P/Invoke
/// and Media Foundation with D3D11VA are both native, and choosing between them turns on things
/// nothing here has measured - what the software fallback costs, whether Media Foundation covers the
/// bitstream parser's cases, and whether the cuda and vulkan paths are worth keeping when PP71
/// measured cuda last of the three. The boundary binds either way, which is why it is worth stating
/// before the choice rather than after.
///
/// SO THIS IS A GUARD AND NOT AN ARGUMENT. The non-goal in docs/ROADMAP.md is the constraint; this
/// checks it is there and that no prose in the port has gone back to promising the other thing.
/// §PP107's finding applies here as it did to lib/: prose does not go red.
/// </summary>
public static partial class ManagedBoundaryRule
{
    /// <summary>Where the non-goals are.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The managed half, whose docstrings are the prose most likely to drift.</summary>
    public const string ManagedRelativePath = "app";

    /// <summary>
    /// This file, excluded from the scan below.
    ///
    /// It has to be, for LibRepairCensus's reason: the phrasings are declared here as literals, so
    /// a guard reading its own declaration would report the claim it exists to forbid.
    /// </summary>
    public const string RuleFileName = "ManagedBoundaryRule.cs";

    /// <summary>The non-goal's own head, which is how it is addressed.</summary>
    public const string NonGoalLead = "No managed video decoder";

    /// <summary>
    /// The two things the non-goal has to keep saying.
    ///
    /// Not the whole sentence: a constraint should be re-wordable without a test failing. These are
    /// the halves that carry it - what is refused, and what is offered instead of it.
    /// </summary>
    public static IReadOnlyList<string> NonGoalMustSay { get; } =
        ["100% Windows", "100% managed"];

    /// <summary>
    /// The ways the port has claimed, or could claim, the thing the non-goal refuses.
    ///
    /// More than one spelling, because the point is the claim and not the phrasing - and the
    /// original stood in §PP31 as "one that is 100% managed", which no search for "fully managed"
    /// would have found.
    /// </summary>
    public static IReadOnlyList<string> FalsePromises { get; } =
    [
        "100% managed",
        "fully managed",
        "entirely managed",
        "all managed",
        "no native code",
    ];

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? LocateRoadmap() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativePath);

    /// <summary>
    /// Comment prose with its markers dropped and its whitespace collapsed.
    ///
    /// LibRepairCensus's reason, and it was the right one: doc prose WRAPS, so a claim can read
    /// "100%" at the end of one line and "managed" at the start of the next behind a comment marker,
    /// and no literal search would find it.
    /// </summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WhitespaceRegex().Replace(CommentMarkerRegex().Replace(text, " "), " ");
    }

    /// <summary>The non-goal's paragraph, or null where the roadmap has none.</summary>
    public static string? NonGoalIn(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        string flat = Normalise(roadmap);

        int at = flat.IndexOf($"**{NonGoalLead}**", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int next = flat.IndexOf(" - **", at + NonGoalLead.Length, StringComparison.Ordinal);
        return next < 0 ? flat[at..] : flat[at..next];
    }

    /// <summary>Whether a text promises the thing the non-goal refuses.</summary>
    public static bool PromisesAManagedDecoder(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string flat = Normalise(text);
        return FalsePromises.Any(claim => flat.Contains(claim, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The files under app/ whose prose promises it, relative to app/ and ordered.
    ///
    /// Recursively, and this file excluded. bin/ and obj/ are skipped: a build output carrying a
    /// copy of a docstring is the same claim counted twice, and it is not a file anybody edits.
    /// </summary>
    public static IReadOnlyList<string> ManagedFilesPromisingIt()
    {
        if (LocateManaged() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !Path.GetFileName(p).Equals(RuleFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            if (PromisesAManagedDecoder(File.ReadAllText(path)))
                found.Add(Path.GetRelativePath(root, path));
        }

        return found;
    }

    [GeneratedRegex(@"^\s*(///|//|\*|#)\s?", RegexOptions.Multiline)]
    private static partial Regex CommentMarkerRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
