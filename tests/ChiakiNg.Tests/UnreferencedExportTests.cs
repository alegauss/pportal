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
    /// PP735: whether a match sits inside a string literal, which is a MODEL and not a call.
    ///
    /// PP716 met this and worked around it by excluding one file by name - a hand-kept list of
    /// exactly the shape PP279, PP718, PP720, PP724 and PP733 each cost a task. The rule replaces
    /// it: a census names its symbols in quotes and a caller writes them as code.
    ///
    /// Measured before it was applied. It moves five exports onto the dead list - the packetstats
    /// reset PP716 excluded, two holepunch accessors and regist's fini and stop - and every one of
    /// them is named only inside a census row. Nothing tracked here is reached through a literal,
    /// because every P/Invoke entry point in app is a shim wrapper or a chiaki_render name and
    /// neither is declared in the headers this sweep reads.
    ///
    /// Verbatim strings are handled because this tree writes paths in them: there a backslash is
    /// an ordinary character and a doubled quote is an escape, which is the opposite of both rules
    /// in an ordinary literal.
    /// </summary>
    public static bool InsideALiteral(string line, int index)
    {
        ArgumentNullException.ThrowIfNull(line);

        var quoted = false;
        var verbatim = false;

        for (var at = 0; at < index && at < line.Length; at++)
        {
            char one = line[at];

            if (!quoted)
            {
                if (one == '\'')
                {
                    // A character literal, so that '"' cannot open a string.
                    int close = line.IndexOf('\'', at + 1);
                    if (close < 0 || close >= index)
                        return false;

                    at = close;
                    continue;
                }

                if (one == '"')
                {
                    quoted = true;
                    verbatim = at > 0 && line[at - 1] == '@';
                }

                continue;
            }

            if (!verbatim && one == '\\')
            {
                at++;
                continue;
            }

            if (one != '"')
                continue;

            if (verbatim && at + 1 < line.Length && line[at + 1] == '"')
            {
                at++;
                continue;
            }

            quoted = false;
            verbatim = false;
        }

        return quoted;
    }

    /// <summary>
    /// Sixteen, as of PP735 - twelve PP290 found and four the literal rule uncovered. Kept in the
    /// tree rather than in a commit message, because the
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

        // PP735: the four the literal rule added, each named only inside a census row and called by
        // nothing. They are C nobody has decided about, which is what this sweep exists to find -
        // and they were hidden because a model naming a symbol looked exactly like a caller.
        // chiaki_packet_stats_reset is a fifth and was already here: PP716 kept it by excluding one
        // file by name, which the rule now does without a list.
        "chiaki_holepunch_session_get_stun_allocation",
        "chiaki_holepunch_session_set_recorded",
        "chiaki_regist_fini",
        "chiaki_regist_stop",
    ];

    /// <summary>
    /// PP758: what loses its last caller when PP696 takes session.c's five calls out.
    ///
    /// ONE, WHICH IS THE MEASUREMENT AND NOT THE GUESS. The reading was that all five would go dead;
    /// the trial deletion says the other four keep a reference - the run and the stop through the
    /// header's own uses, the idr request through the shim - and only the init is left with nobody.
    /// A list of five here would have turned this guard red from the other direction, reporting four
    /// names as referenced again on the day the deletion landed.
    ///
    /// Held apart from <see cref="Known"/> rather than merged into it, because the two are different
    /// facts: those are dead today, and this is dead after a commit that has not happened.
    /// </summary>
    public static IReadOnlyList<string> DeadAfterTheFlip { get; } = ["chiaki_stream_connection_init"];

    /// <summary>The record for the shape the tree is in.</summary>
    public static IReadOnlyList<string> KnownIn(ConsumerShape session)
        => session == ConsumerShape.Silent ? [.. Known, .. DeadAfterTheFlip] : Known;

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

                    // PP735: a MODEL names its symbols in string literals and a caller writes them
                    // as code. Every P/Invoke entry point in app is a shim wrapper or a
                    // chiaki_render name, and neither is declared in the headers this sweep reads,
                    // so nothing tracked here is reached through a literal - which is what makes
                    // the rule safe. It replaces PP716's exclusion of one file by name.
                    if (InsideALiteral(line, match.Index - start))
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

        // PP758: the record grows by one when PP696 takes session.c's calls out, and that commit is
        // the one forbidden from editing a test file - so both records are here and the tree picks.
        IReadOnlyList<string> known = KnownIn(FramePathConsumers.SessionShape());
        output.WriteLine($"{exported.Count} exports, {dead.Count} referenced nowhere, {known.Count} on record");

        string[] appeared = [.. dead.Where(n => !known.Contains(n, StringComparer.Ordinal)).Order(StringComparer.Ordinal)];
        string[] gone = [.. known.Where(n => !dead.Contains(n, StringComparer.Ordinal)).Order(StringComparer.Ordinal)];

        Assert.True(
            appeared.Length == 0,
            "these exports lost their last caller and are now dead C nobody has decided about:\n  "
                + string.Join("\n  ", appeared));

        Assert.True(
            gone.Length == 0,
            "these are referenced again, or were deleted - either way the list in this file is "
                + "stale and should shrink:\n  " + string.Join("\n  ", gone));
    }

    /// <summary>
    /// PP735: the literal rule itself, over the shapes this tree actually writes.
    ///
    /// It decides whether a name is C nobody calls, so its own edges are worth asserting rather
    /// than trusting - a detector that answered true too often would retire live exports, and one
    /// that answered false too often would put the hand-kept list back.
    /// </summary>
    [Theory]
    // A plain call is code.
    [InlineData("\tchiaki_regist_stop(regist);", 1, false)]
    // A census row names it in quotes.
    [InlineData("        \"chiaki_regist_stop\",", 9, true)]
    // A verbatim path, where a backslash is an ordinary character.
    [InlineData("    const string P = @\"lib\\src\\regist.c\";", 27, true)]
    // A doubled quote inside a verbatim string does not close it.
    [InlineData("    var s = @\"a\"\"chiaki_regist_stop\"\"b\";", 20, true)]
    // An escape in an ordinary literal takes the next character with it.
    [InlineData("    var s = \"a\\\"chiaki_regist_stop\";", 20, true)]
    // Code that follows a closed literal is code again.
    [InlineData("    Log(\"x\"); chiaki_regist_stop(r);", 22, false)]
    // A character literal cannot open a string.
    [InlineData("    if (c == '\"') chiaki_regist_stop(r);", 22, false)]
    public void TheLiteralRuleReadsTheShapesThisTreeWrites(string line, int index, bool inside)
        => Assert.Equal(inside, UnreferencedExportTests.InsideALiteral(line, index));

    // The declaration form the headers use: CHIAKI_EXPORT, a return type that may carry stars, then
    // the name and its open bracket.
    [GeneratedRegex(@"CHIAKI_EXPORT\s+[\w \*]+?\**\s*(?<name>\w+)\s*\(")]
    private static partial Regex ExportRegex();
}
