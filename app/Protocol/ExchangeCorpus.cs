using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Why an entry is not in a corpus, or that it is.</summary>
public enum CorpusVerdict
{
    /// <summary>Kept: a replay can expect it.</summary>
    Kept,

    /// <summary>
    /// Dropped: it recurs with the clock or with what the link did, so its count is a property of
    /// the run rather than of the protocol.
    /// </summary>
    Recurring,
}

/// <summary>What one selection kept and what it left behind.</summary>
/// <param name="Kept">The entries a replay can be held against.</param>
/// <param name="DroppedByType">
/// How many of each recurring type were dropped, keyed "channel/type". Reported rather than
/// discarded: a corpus that quietly kept 8 of 677 reads as complete coverage.
/// </param>
public readonly record struct CorpusSelection(
    IReadOnlyList<ExchangeEntry> Kept, IReadOnlyDictionary<string, int> DroppedByType);

/// <summary>
/// PP420: which entries of a recording belong in a corpus.
///
/// PP297 established what a good capture looks like and left it as one curated file - 13 ctrl
/// entries and 2 session ones - with the rule in its prose. PP396's first capture of the stream
/// channel is 677 entries, of which 566 are CORRUPTFRAME, 67 HEARTBEAT and 11 CONNECTIONQUALITY.
/// The eight that a replay can expect are BIG, BANG, STREAMINFO, its ACK and DISCONNECT.
///
/// THE DISCRIMINATOR IS WHETHER THE COUNT IS THE PROTOCOL'S. A handshake message occurs a bounded
/// number of times because the protocol says so. A heartbeat occurs as often as the session was
/// open; a corrupt-frame report as often as the link misbehaved. Those counts describe the run, so
/// comparing a later run against them compares two networks.
///
/// PP395 MADE THIS ARGUMENT ONE LEVEL DOWN and it is quoted here because it is the same one: it
/// refused to tap fragments because "a recording of fragments would only replay against a run that
/// negotiated the same MTU, which is the opposite of an oracle". A heartbeat is that objection with
/// a timer in place of an MTU.
///
/// THE RECORDER IS NOT CHANGED. It keeps recording everything the taps emit, because a raw capture
/// is a diagnostic and its bulk is the point - 566 corrupt-frame reports is a fact worth seeing.
/// This is the other job: what a corpus keeps. One file doing both is why the rule stayed unwritten.
///
/// LISTED BY TYPE RATHER THAN INFERRED FROM COUNTS. A threshold - "more than N occurrences is
/// steady state" - would classify a short capture's heartbeat as protocol and a long one's BIG as
/// noise, and would answer differently for the same message on two runs. The list is a fact about
/// the protocol and can be read.
/// </summary>
public static class ExchangeCorpus
{
    /// <summary>
    /// The stream channel's recurring payload types, by name, with the clock or link that drives
    /// each.
    ///
    /// Taken from lib/protobuf/takion.proto's PayloadType. Not every recurring type is here - only
    /// the ones a session can actually produce - and one that turns up later is added when a capture
    /// shows it rather than guessed at now.
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> StreamRecurring { get; } =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            // Sent on a timer, both ways.
            ["HEARTBEAT"] = 3,

            // One per lost packet the receiver noticed.
            ["PACKETLOSS"] = 4,

            // One per frame the decoder could not use. 566 of them in a twelve-second capture.
            ["CORRUPTFRAME"] = 5,

            // A periodic report from the console.
            ["CONNECTIONQUALITY"] = 16,

            // And the client's own periodic reports.
            ["CLIENTMETRIC"] = 17,
            ["PERIODICTIMESTAMP"] = 27,

            // Sent when the client wants a fresh keyframe, which is a consequence of loss.
            ["IDRREQUEST"] = 25,
        };

    /// <summary>
    /// And senkusha's, which is nothing.
    ///
    /// Senkusha measures a link and then stops: its exchange is a request, a BIG and BANG, a bounded
    /// run of MTU and echo commands, and a disconnect. Stated as an empty set rather than left out,
    /// the way PP397 stated its redaction.
    /// </summary>
    public static IReadOnlySet<ushort> SenkushaRecurring { get; } = new HashSet<ushort>();

    /// <summary>
    /// Whether an entry on this channel is one a replay can expect.
    /// </summary>
    /// <param name="channel">Which conversation it crossed.</param>
    /// <param name="type">A ctrl message type, or a protobuf payload type on the other two.</param>
    public static CorpusVerdict Judge(string channel, ushort type)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel == ChiakiMessageTap.StreamChannel)
        {
            return StreamRecurring.Values.Contains(type)
                ? CorpusVerdict.Recurring
                : CorpusVerdict.Kept;
        }

        if (channel == ChiakiMessageTap.SenkushaChannel)
        {
            return SenkushaRecurring.Contains(type)
                ? CorpusVerdict.Recurring
                : CorpusVerdict.Kept;
        }

        // ctrl and session are both bounded per session already - PP297's capture is 13 and 2
        // entries of them - so nothing there is dropped. A ctrl heartbeat exists, and PP342
        // established that answering it is the property being asserted rather than how often it
        // arrived: two of them in a capture is the pair, not a sample.
        return CorpusVerdict.Kept;
    }

    /// <summary>
    /// The corpus a recording contributes, and the counts of what it left behind.
    /// </summary>
    public static CorpusSelection Select(ExchangeRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var kept = new List<ExchangeEntry>();
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ExchangeEntry entry in recording.Entries)
        {
            if (Judge(entry.Channel, TypeIn(entry)) == CorpusVerdict.Kept)
            {
                kept.Add(entry);
                continue;
            }

            string key = $"{entry.Channel}/{TypeIn(entry):x4}";
            dropped[key] = dropped.TryGetValue(key, out int already) ? already + 1 : 1;
        }

        return new CorpusSelection(kept, dropped);
    }

    /// <summary>
    /// The type an entry's payload leads with, or <see cref="ChiakiMessageTap.UnknownType"/>.
    ///
    /// The rendered form is four hex digits then a space then the payload, which is PP326's shape.
    /// An entry that is not in that shape - a session HTTP head - has no type, and UnknownType is
    /// never in a recurring list, so it is kept.
    /// </summary>
    public static ushort TypeIn(ExchangeEntry entry)
    {
        // No null guard: ExchangeEntry is a record STRUCT, so there is nothing to be null.
        return entry.Payload?.Length >= 4
            && ushort.TryParse(
                entry.Payload.AsSpan(0, 4),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out ushort type)
            ? type
            : ChiakiMessageTap.UnknownType;
    }
}
