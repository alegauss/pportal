namespace ChiakiNg.Session;

/// <summary>
/// PP32, the half that leaves with the client: where speexdsp is allowed to be.
///
/// §PP32's central correction was that speex is not in the library. audioreceiver.c is 363 lines
/// over Opus and lib/ references speex NOWHERE - both families, speex_preprocess_* for noise
/// suppression and speex_echo_* for cancellation, live in gui/ and both are on the microphone path,
/// in the client this port replaces. That was prose in a rationale file, and a ship deletes those.
///
/// THE BUILD HAD NOT NOTICED. PP632 retired the Qt client's build, so CHIAKI_ENABLE_GUI is off by
/// default and gui/ is never added - and the top-level CMakeLists went on probing for speexdsp on
/// every configure, printing "Speex DSP found echo cancelling and noise suppression enabled" for a
/// tree in which nothing links it. The dependency was gone from the binary and still present in the
/// build, which is the worst of the two states: the report somebody reads before asking whether it
/// is still needed said yes.
///
/// SO THE PROBE FOLLOWS THE CLIENT NOW, and this holds all three halves of that: speex is linked
/// only by gui/, probed only where gui/ is built, and absent from lib/ entirely. Building with
/// -DCHIAKI_ENABLE_GUI=ON restores every one of them unchanged, which is the difference between
/// removing a dependency from the default build and removing it from the client.
///
/// WHAT THIS DOES NOT SETTLE is the other half of PP32. The managed host captures no microphone at
/// all, so there is nothing yet for a noise or echo stage to run on - and what replaces speex, if
/// anything does, is a question about that host rather than a translation of this one.
/// </summary>
public static class SpeexBuildGate
{
    /// <summary>Where the probe is.</summary>
    public const string RootCMakeRelativePath = "CMakeLists.txt";

    /// <summary>The one file allowed to link it.</summary>
    public const string ClientCMakeRelativePath = @"gui\CMakeLists.txt";

    /// <summary>The library, which references speex nowhere.</summary>
    public const string LibRelativePath = "lib";

    /// <summary>The linked target, spelled as CMake spells it.</summary>
    public const string LinkedTarget = "PkgConfig::SpeexDSP";

    /// <summary>The option that has to gate the probe: the client's, not speex's own.</summary>
    public const string ClientOption = "CHIAKI_ENABLE_GUI";

    /// <summary>A file, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>lib/, or null outside a checkout.</summary>
    public static string? LocateLib() => SanitizerSource.LocateDirectory(LibRelativePath);

    /// <summary>
    /// Whether the probe is gated on the client being built.
    ///
    /// The condition and not the presence: a probe is fine, a probe that runs when nothing links its
    /// result is what this is about. Found by reading the line that opens the block, so a guard
    /// added further in - after the find_package has already run - does not satisfy it.
    /// </summary>
    public static bool TheProbeIsGatedOnTheClient(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Comments dropped, for the reason FilesLinkingSpeex records: the paragraph above this
        // probe explains the guard, and a reader of raw text would find its words rather than it.
        string cmake = Code(source);

        int probe = cmake.IndexOf("find_package(SpeexDSP", StringComparison.Ordinal);
        if (probe < 0)
            return true; // No probe at all is the strongest form of the same answer.

        int opens = cmake.LastIndexOf("if(", probe, StringComparison.Ordinal);
        if (opens < 0)
            return false;

        int closes = cmake.IndexOf(')', opens);
        return closes > opens
            && closes < probe
            && cmake[opens..closes].Contains(ClientOption, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every file under the repository that links speexdsp, relative to the root and ordered.
    ///
    /// CMake only, and build/ excluded: a generated cache naming a package it was configured with is
    /// a record of a configure rather than a place the dependency is declared.
    ///
    /// COMMENTS DROPPED FIRST, which the root CMakeLists taught this check on its first run: the
    /// paragraph explaining that speex is linked in exactly one place names the target, so a reader
    /// of raw text reported the file that says where it is linked as a second place it is linked.
    /// </summary>
    public static IReadOnlyList<string> FilesLinkingSpeex(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "CMakeLists.txt", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            if (Code(File.ReadAllText(path)).Contains(LinkedTarget, StringComparison.Ordinal))
                found.Add(Path.GetRelativePath(root, path));
        }

        return found;
    }

    /// <summary>
    /// A CMake file with its comments removed.
    ///
    /// Whole lines and trailing halves both: CMake has one comment marker and no strings that would
    /// contain it in this tree, so the naive cut is the right one and a cleverer one would be a
    /// second thing to be wrong.
    /// </summary>
    public static string Code(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        var kept = new List<string>();
        foreach (string line in cmake.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            int hash = line.IndexOf('#', StringComparison.Ordinal);
            kept.Add(hash < 0 ? line : line[..hash]);
        }

        return string.Join('\n', kept);
    }

    /// <summary>
    /// Whether the library mentions speex at all, which §PP32 said it does not.
    ///
    /// Recursively, because lib/src has a remote/ subtree a flat glob misses - the trap PP483
    /// recorded and the one a check like this walks into.
    /// </summary>
    public static IReadOnlyList<string> LibFilesMentioningSpeex()
    {
        if (LocateLib() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            if (File.ReadAllText(path).Contains("speex", StringComparison.OrdinalIgnoreCase))
                found.Add(Path.GetRelativePath(root, path));
        }

        return found;
    }
}
