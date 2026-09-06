using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which side of the tree a consumer of takion's three files is on.</summary>
public enum TakionConsumerKind
{
    /// <summary>lib/src: C that this port is replacing, and whose own departure is a task.</summary>
    Library,

    /// <summary>test/: the C suite, which exercises the modules directly.</summary>
    Suite,

    /// <summary>shim/: this port's own oracle, which is the group nobody had counted.</summary>
    Shim,
}

/// <summary>One translation unit that stops linking when the three files go.</summary>
/// <param name="File">Its path, as this tree spells one.</param>
/// <param name="Kind">Which of the three groups it belongs to.</param>
/// <param name="Symbols">The symbols it names, distinct and sorted.</param>
/// <param name="Why">What that consumer is, and what answering for it would mean.</param>
public readonly record struct TakionConsumer(
    string File, TakionConsumerKind Kind, IReadOnlyList<string> Symbols, string Why);

/// <summary>
/// PP780, under PP27: what the linker says takion's deletion costs.
///
/// PP27's fourth criterion is an end state - takion.c, takionsendbuffer.c and reorderqueue.c leave
/// the build - and PP565's rule is that a deletion is MEASURED rather than reasoned about. PP638 did
/// it for the frame path by taking four files out of lib's source list and asking the build, and
/// found session.c: a consumer three readings had missed. PP702 counted senkusha's five by reading.
///
/// NOBODY HAD ASKED THE LINKER ABOUT TAKION. Taking the three out leaves twenty-four distinct
/// symbols undefined across eight translation units, and they fall into three groups that want three
/// different answers - which is why the rows carry a kind rather than only a count.
///
/// THE SHIM IS THE LARGEST CONSUMER, with eighteen of the twenty-four: twice what lib's four files
/// hold between them. That is the finding. Those exports exist so a managed reorder queue, send
/// buffer, MAC gate and AV parser can be compared against the C being replaced, so deleting takion.c
/// deletes what proves the replacement right. PP563 found the same shape one module over - PP33's
/// third consumer was the shim - and PP33's answer is the one this points at: RECORD what the C
/// answers before removing it, which is what --record-json-oracle is.
///
/// AND A READING OVERCOUNTS. audioreceiver.c calls three takion functions and appears nowhere in the
/// link, because all three are static inline in takion.h. Eight symbols are like that in all, and a
/// reading counted five of them onto rows the deletion does not have to answer for. Only the linker
/// knows which, which is the whole argument for asking it rather than grepping.
/// </summary>
public static class TakionConsumers
{
    /// <summary>The three files PP27's fourth criterion says leave the build.</summary>
    public static IReadOnlyList<string> Leaving { get; } =
    [
        @"lib\src\takion.c",
        @"lib\src\takionsendbuffer.c",
        @"lib\src\reorderqueue.c",
    ];

    /// <summary>Every consumer the link named, with the symbols each one holds.</summary>
    public static IReadOnlyList<TakionConsumer> Consumers { get; } =
    [
        new(
            @"lib\src\senkusha.c",
            TakionConsumerKind.Library,
            [
                "chiaki_takion_close",
                "chiaki_takion_connect",
                "chiaki_takion_send_message_data",
                "chiaki_takion_send_raw",
                "chiaki_takion_v7_av_packet_format_header",
            ],
            "PP702's five. Senkusha runs BEFORE the stream phase, so it does not go with it."),
        new(
            @"lib\src\streamconnection.c",
            TakionConsumerKind.Library,
            [
                "chiaki_takion_close",
                "chiaki_takion_connect",
                "chiaki_takion_send_message_data",
                "chiaki_takion_send_message_data_cont",
            ],
            "Goes when the stream connection does, which PP638 measured and PP763 put back."),
        new(
            @"lib\src\feedbacksender.c",
            TakionConsumerKind.Library,
            ["chiaki_takion_send_feedback_history", "chiaki_takion_send_feedback_state"],
            "The stream connection's own subsystem: it leaves with the file that starts it."),
        new(
            @"lib\src\congestioncontrol.c",
            TakionConsumerKind.Library,
            ["chiaki_takion_send_congestion"],
            "The same."),
        new(
            @"test\takion.c",
            TakionConsumerKind.Suite,
            [
                "chiaki_takion_format_congestion",
                "chiaki_takion_packet_mac",
                "chiaki_takion_send_buffer_ack",
                "chiaki_takion_send_buffer_fini",
                "chiaki_takion_send_buffer_init",
                "chiaki_takion_send_buffer_push",
                "chiaki_takion_v9_av_packet_parse",
            ],
            "The C suite exercising the module: it leaves with it, and the case floor drops."),
        new(
            @"test\reorderqueue.c",
            TakionConsumerKind.Suite,
            [
                "chiaki_reorder_queue_drop",
                "chiaki_reorder_queue_fini",
                "chiaki_reorder_queue_init_16",
                "chiaki_reorder_queue_peek",
                "chiaki_reorder_queue_pull",
                "chiaki_reorder_queue_push",
            ],
            "The same, for the queue - and it is the file PP107's two deferred defects sit under."),
        new(
            @"test\allocbudget.c",
            TakionConsumerKind.Suite,
            ["chiaki_takion_v9_av_packet_parse"],
            "PP44's budget measured on the C, which is the number the managed one is held to."),
        new(
            @"shim\chiaki_shim.c",
            TakionConsumerKind.Shim,
            [
                "chiaki_reorder_queue_drop",
                "chiaki_reorder_queue_fini",
                "chiaki_reorder_queue_init_16",
                "chiaki_reorder_queue_init_32",
                "chiaki_reorder_queue_peek",
                "chiaki_reorder_queue_pull",
                "chiaki_reorder_queue_push",
                "chiaki_takion_close",
                "chiaki_takion_connect",
                "chiaki_takion_format_congestion",
                "chiaki_takion_packet_mac",
                "chiaki_takion_send_buffer_ack",
                "chiaki_takion_send_buffer_fini",
                "chiaki_takion_send_buffer_init",
                "chiaki_takion_send_buffer_push",
                "chiaki_takion_v7_av_packet_format_header",
                "chiaki_takion_v7_av_packet_parse",
                "chiaki_takion_v9_av_packet_parse",
            ],
            "THE ORACLE. Eighteen, twice what lib's four hold between them - and what proves the port."),
    ];

    /// <summary>Every symbol the deletion leaves undefined, distinct and sorted.</summary>
    public static IReadOnlyList<string> Symbols { get; } =
        [.. Consumers.SelectMany(one => one.Symbols).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>How many of them the shim alone holds, which is the row worth reading twice.</summary>
    public static int ShimSymbolCount =>
        Consumers.Where(one => one.Kind == TakionConsumerKind.Shim).Sum(one => one.Symbols.Count);

    /// <summary>One of the listed files, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// The symbols a C source calls out of the three files, distinct and sorted.
    ///
    /// Comments stripped first, so a paragraph naming a call is not one - PP735's trap, and this
    /// census's own docstrings spell most of the twenty-four.
    ///
    /// DELIBERATELY WIDER THAN THE LIST. Anything in either namespace matches, so a call the rows do
    /// not know about is news rather than silence: the failure this exists to catch is a symbol
    /// ARRIVING, which a pattern built from the rows could never see.
    /// </summary>
    public static IReadOnlyList<string> CallsIn(string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in CCall.Code(cSource).Split('\n'))
        {
            // A declaration is not a call, which is how takion.h's own prototypes would read.
            if (line.Contains("extern ", StringComparison.Ordinal)
                || line.Contains("CHIAKI_EXPORT", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in TakionOrQueueCall.Matches(line))
                found.Add(match.Groups["symbol"].Value);
        }

        return [.. found.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The symbols this census claims a file holds that its source no longer calls.
    ///
    /// Only that direction here: a file may legitimately call an INLINE takion function the link
    /// never sees - audioreceiver.c calls three - so an extra call in the source is not a row this
    /// census is missing. What a stale row means is a consumer that stopped being one.
    /// </summary>
    public static IReadOnlyList<string> StaleIn(TakionConsumer consumer, string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        IReadOnlyList<string> called = CallsIn(cSource);

        return [.. consumer.Symbols.Where(one => !called.Contains(one))];
    }

    /// <summary>
    /// PP780: the three the header defines inline, which the link never asks anybody for.
    ///
    /// audioreceiver.c calls all three and appears in no link error, so a census built by reading
    /// would have listed a consumer the deletion does not have to answer for.
    /// </summary>
    public static IReadOnlyList<string> InlineInTheHeader { get; } =
    [
        "chiaki_reorder_queue_count",
        "chiaki_reorder_queue_set_drop_cb",
        "chiaki_reorder_queue_set_drop_strategy",
        "chiaki_reorder_queue_size",
        "chiaki_takion_av_packet_audio_fec_units_count",
        "chiaki_takion_av_packet_audio_source_units_count",
        "chiaki_takion_av_packet_audio_unit_size",
        "chiaki_takion_set_crypt",
    ];

    private static readonly Regex TakionOrQueueCall = new(
        @"\b(?<symbol>chiaki_(?:takion|reorder_queue)_\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
