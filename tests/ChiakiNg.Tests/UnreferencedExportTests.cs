using System.Text.RegularExpressions;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP290: the exported C functions nothing in the tree refers to.
///
/// This port deletes C, so what is deletable is planning information - and PP288 found its one
/// example by accident, while tracing who called the FEC decode. A sweep finds the rest, and a
/// sweep that runs once is a list that is wrong by the next commit.
///
/// Both directions are news, which is why the set is asserted exactly rather than as a ceiling. A
/// name leaving it means something started using that function, and a name arriving means a caller
/// went away and left an export behind - which is the shape PP288 was.
///
/// Bare names, not calls
/// ---------------------
/// The first version of this looked for "name(" and reported twenty. Seven of those were false:
/// chiaki_ffmpeg_decoder_video_sample_cb and the audio receiver's init and fini are assigned as
/// callbacks and referenced without ever being called at the point of reference, so deleting them
/// on that evidence would have broken the build at best. A bare identifier match finds them.
/// </summary>
public partial class UnreferencedExportTests(ITestOutputHelper output)
{
    /// <summary>Where the exports are declared.</summary>
    public const string HeadersRelativePath = @"lib\include\chiaki";

    /// <summary>Where a reference to one could be. app\ is included: the shim is not the only seam.</summary>
    public static IReadOnlyList<string> SearchTrees { get; } = ["lib", "gui", "shim", "test", "app"];

    /// <summary>
    /// The thirteen, as of PP290. Kept in the tree rather than in a commit message, because the
    /// value of the list is that the next person can see it without re-deriving it.
    ///
    /// They are NOT deleted here. They span keyboard input, the ambassador key derivation, the
    /// discovery one-shot and four threading primitives, and whether each goes depends on what the
    /// port intends to do with the feature behind it - which is a decision per name, not a sweep.
    /// </summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        "chiaki_bool_pred_cond_broadcast",
        "chiaki_bool_pred_cond_wait",
        "chiaki_discovery_thread_start_oneshot",
        "chiaki_mutex_trylock",
        "chiaki_packet_stats_reset",
        "chiaki_rpcrypt_ambassador_from_aeropause",
        "chiaki_session_keyboard_accept",
        "chiaki_session_keyboard_reject",
        "chiaki_session_keyboard_set_text",
        "chiaki_thread_set_affinity_cb",
        "chiaki_thread_timedjoin",
        "stream_connection_send_toggle_mute_direct_message",
    ];

    /// <summary>Every name declared CHIAKI_EXPORT in the public headers.</summary>
    public static IReadOnlyList<string> Exported(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var names = new SortedSet<string>(StringComparer.Ordinal);
        string headers = Path.Combine(root, HeadersRelativePath);
        if (!Directory.Exists(headers))
            return [];

        foreach (string file in Directory.EnumerateFiles(headers, "*.h", SearchOption.AllDirectories))
        {
            foreach (Match match in ExportRegex().Matches(File.ReadAllText(file)))
                names.Add(match.Groups["name"].Value);
        }

        return [.. names];
    }

    /// <summary>Those with no reference outside their own declaration or definition.</summary>
    public static IReadOnlyList<string> Unreferenced(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Read every candidate file once. Doing it per name would be 318 sweeps of the tree.
        var sources = new List<string>();
        foreach (string tree in SearchTrees)
        {
            string directory = Path.Combine(root, tree);
            if (!Directory.Exists(directory))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}third-party{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (file.EndsWith(".c", StringComparison.Ordinal) || file.EndsWith(".h", StringComparison.Ordinal)
                    || file.EndsWith(".cpp", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
                {
                    sources.Add(File.ReadAllText(file));
                }
            }
        }

        var dead = new List<string>();
        foreach (string name in Exported(root))
        {
            bool referenced = false;
            foreach (string text in sources)
            {
                foreach (Match match in Regex.Matches(text, @"\b" + Regex.Escape(name) + @"\b"))
                {
                    // The line that exports it is the declaration or the definition, not a use.
                    int start = text.LastIndexOf('\n', match.Index) + 1;
                    int end = text.IndexOf('\n', match.Index);
                    string line = end < 0 ? text[start..] : text[start..end];
                    if (line.Contains("CHIAKI_EXPORT", StringComparison.Ordinal))
                        continue;

                    referenced = true;
                    break;
                }

                if (referenced)
                    break;
            }

            if (!referenced)
                dead.Add(name);
        }

        return dead;
    }

    /// <summary>THE GUARD. The set is exactly the one PP290 recorded.</summary>
    [Fact]
    public void TheUnreferencedExportsAreTheOnesOnRecord()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        IReadOnlyList<string> exported = Exported(root);

        // A sweep that found nothing to sweep would agree with any list at all.
        Assert.True(exported.Count > 200, $"only {exported.Count} exports found - the scan is not working");

        IReadOnlyList<string> dead = Unreferenced(root);
        output.WriteLine($"{exported.Count} exports, {dead.Count} referenced nowhere");

        string[] appeared = [.. dead.Where(n => !Known.Contains(n, StringComparer.Ordinal)).Order(StringComparer.Ordinal)];
        string[] gone = [.. Known.Where(n => !dead.Contains(n, StringComparer.Ordinal)).Order(StringComparer.Ordinal)];

        Assert.True(
            appeared.Length == 0,
            "these exports lost their last caller and are now dead C nobody has decided about:\n  "
                + string.Join("\n  ", appeared));

        Assert.True(
            gone.Length == 0,
            "these are referenced again, or were deleted - either way the list in this file is "
                + "stale and should shrink:\n  " + string.Join("\n  ", gone));
    }

    // The declaration form the headers use: CHIAKI_EXPORT, a return type that may carry stars, then
    // the name and its open bracket.
    [GeneratedRegex(@"CHIAKI_EXPORT\s+[\w \*]+?\**\s*(?<name>\w+)\s*\(")]
    private static partial Regex ExportRegex();
}
