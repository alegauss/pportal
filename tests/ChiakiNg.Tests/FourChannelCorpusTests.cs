using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP396: the capture that turns senkusha and the stream connection into an oracle.
///
/// PP23 named four modules with no test at all. PP391 and PP392 replayed ctrl and session against
/// PP297's capture; PP394 and PP395 built the two missing tap channels; PP397 keyed the redaction to
/// the channel. What was left was a run against a real console, and this is it.
///
/// SELECTED RATHER THAN RECORDED WHOLE. The run produced 677 entries, of which 566 were CORRUPTFRAME,
/// 67 HEARTBEAT and 11 CONNECTIONQUALITY. PP420 decides which of those a replay can expect, and
/// `--select-corpus` applies it: 33 entries across four channels, with the dropped counts printed
/// rather than quietly cut.
///
/// PP297'S CAPTURE IS KEPT, not replaced. It holds content assertions written against it - LOGIN,
/// SESSION_ID and its two heartbeats - and replacing a known-good oracle to add two channels would
/// have risked what already works for no gain.
/// </summary>
public class FourChannelCorpusTests
{
    /// <summary>Where the capture is kept.</summary>
    public const string RelativePath = @"tests\corpus\exchange-ps5-four-channels.txt";

    /// <summary>The recording, or null outside a checkout.</summary>
    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(RelativePath);
        return path is null ? null : ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>It is a recording, and the format reads it back.</summary>
    [Fact]
    public void TheCorpusIsARecording()
    {
        ExchangeRecording? recording = Corpus();
        Assert.NotNull(recording);
        Assert.NotEmpty(recording.Entries);
    }

    /// <summary>
    /// THE POINT OF PP396. All four of PP23's modules are in one recording.
    ///
    /// PP297's capture had two of them. senkusha and the stream connection had no tap channel at all
    /// until PP394 and PP395, so no recording could hold them.
    /// </summary>
    [Fact]
    public void AllFourChannelsAreInIt()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        IReadOnlySet<string> channels =
            recording.Entries.Select(e => e.Channel).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("session", channels);
        Assert.Contains("ctrl", channels);
        Assert.Contains("senkusha", channels);
        Assert.Contains("stream", channels);
    }

    /// <summary>
    /// Senkusha's whole exchange: the protocol request and its ack, a BIG and BANG, the MTU and echo
    /// commands, and a disconnect.
    ///
    /// This is what senkusha DOES - it measures a link and stops - and the recording holding all of
    /// it end to end is what makes it replayable rather than sampled.
    /// </summary>
    [Theory]
    [InlineData("001f")]  // TAKIONPROTOCOLREQUEST
    [InlineData("0020")]  // TAKIONPROTOCOLREQUESTACK
    [InlineData("0000")]  // BIG
    [InlineData("0001")]  // BANG
    [InlineData("000c")]  // SENKUSHA - the MTU and echo commands
    [InlineData("0008")]  // DISCONNECT
    public void SenkushasExchangeIsWholeInIt(string type)
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.Contains(
            recording.Entries.Where(e => e.Channel == "senkusha"),
            e => e.Payload.StartsWith(type + " ", StringComparison.Ordinal));
    }

    /// <summary>And the stream connection's handshake, which is the eight of its 651 that replay.</summary>
    [Theory]
    [InlineData("0000")]  // BIG
    [InlineData("0001")]  // BANG
    [InlineData("000d")]  // STREAMINFO
    [InlineData("000e")]  // STREAMINFOACK
    [InlineData("0008")]  // DISCONNECT
    public void TheStreamsHandshakeIsInIt(string type)
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.Contains(
            recording.Entries.Where(e => e.Channel == "stream"),
            e => e.Payload.StartsWith(type + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// PP420'S RULE HELD. Nothing that recurs with the clock is in it.
    ///
    /// Asserted as the absence of the three types the run was full of, so a later capture written
    /// past the selection would be caught rather than merely be larger.
    /// </summary>
    [Theory]
    [InlineData("0005")]  // CORRUPTFRAME, 566 of them in the run
    [InlineData("0003")]  // HEARTBEAT, 67
    [InlineData("0010")]  // CONNECTIONQUALITY, 11
    public void NothingRecurringSurvivedTheSelection(string type)
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.DoesNotContain(
            recording.Entries.Where(e => e.Channel == "stream"),
            e => e.Payload.StartsWith(type + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the selection is idempotent: running the rule over the corpus keeps all of it.
    ///
    /// Which is the property that makes it a corpus rather than one pass of a filter.
    /// </summary>
    [Fact]
    public void TheRuleKeepsTheWholeCorpus()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        CorpusSelection selection = ExchangeCorpus.Select(recording);

        Assert.Equal(recording.Entries.Count, selection.Kept.Count);
        Assert.Empty(selection.DroppedByType);
    }

    /// <summary>
    /// NOTHING IN IT IS A SECRET, which is the assertion this file exists for.
    ///
    /// The four rules that had to hold, each against what the console actually sent:
    ///
    ///   PP325 took RP-Registkey out of the session request and RP-Nonce out of the answer, and
    ///   PP88's rule took the console's address out of the Host header.
    ///
    ///   PP326 took the payloads of ctrl's LOGIN and SESSION_ID.
    ///
    ///   PP397 took the stream's BIG, because it carries the session id - which PP418 now holds
    ///   against streamconnection.c.
    ///
    ///   PP423 took the stream's BANG by FIELD rather than whole, so its three key-bearing fields
    ///   are zeroed and the console's verdict on the handshake stays readable. That one is asserted
    ///   by <see cref="TheBangsKeysAreZeroedAndItsVerdictIsNot"/>, because "carries the marker" is
    ///   no longer what it means.
    /// </summary>
    [Fact]
    public void NothingInTheCorpusIsASecret()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        AssertRedacted(recording, "session", null);
        AssertRedacted(recording, "ctrl", "0005 ");
        AssertRedacted(recording, "ctrl", "0033 ");
        AssertRedacted(recording, "stream", "0000 ");
    }

    /// <summary>
    /// PP423: the BANG's keys are zeroed and its verdict is not.
    ///
    /// A stronger assertion than the marker it replaces: it reads the bytes rather than checking for
    /// a placeholder. session_key, ecdh_pub_key and ecdh_sig keep their tags and lengths and carry
    /// nothing; server_version and the two accepted-flags are there to be replayed against.
    /// </summary>
    [Fact]
    public void TheBangsKeysAreZeroedAndItsVerdictIsNot()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        ExchangeEntry bang = Assert.Single(
            recording.Entries,
            e => e.Channel == "stream" && e.Payload.StartsWith("0001 ", StringComparison.Ordinal));

        // Not the marker: the payload is bytes, and the rule is about which of them are zero.
        Assert.DoesNotContain("<redacted", bang.Payload, StringComparison.Ordinal);

        byte[] payload = Bytes(bang.Payload);

        foreach (int field in MessageSecrets.BangSecretFields)
        {
            Assert.True(
                ProtobufRedaction.TryFindField(
                    payload, 0, payload.Length, MessageSecrets.BangPayloadField,
                    out int nestedAt, out int nestedLength),
                "the BANG's bang_payload cannot be found, so nothing here checks anything");

            if (!ProtobufRedaction.TryFindField(
                    payload, nestedAt, nestedAt + nestedLength, field, out int at, out int length))
            {
                // Optional, and an absent key is nothing to hide.
                continue;
            }

            Assert.All(
                payload[at..(at + length)],
                b => Assert.Equal(0, b));
        }

        // And the verdict survived: encrypted_key_accepted and version_accepted, both true.
        Assert.Contains("18-01-20-01", bang.Payload, StringComparison.Ordinal);
    }

    /// <summary>A rendered payload's bytes, past the four hex digits of its type.</summary>
    private static byte[] Bytes(string payload)
        => [.. payload[5..]
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => Convert.ToByte(pair, 16))];

    /// <summary>
    /// AND SENKUSHA'S BIG AND BANG ARE IN THE CLEAR, WHICH IS THE ONE THAT NEEDED MEASURING.
    ///
    /// PP418 read senkusha.c and holds that its BIG sets session_key, launch_spec and encrypted_key
    /// to the empty string. Its BANG cannot be held that way: senkusha.c only checks that one
    /// arrived and never reads a field of it, so the C says nothing about what the console put there.
    ///
    /// The bound is what can be asserted. The stream's BANG carries a 128-byte ECDH public key and a
    /// 32-byte signature; rendered as dash-separated hex that is over four hundred characters.
    /// Senkusha's is a small acknowledgement, and this is the assertion that it stays one - so a
    /// console firmware that started answering senkusha with a key would turn this red rather than
    /// publish it.
    /// </summary>
    [Fact]
    public void SenkushasBigAndBangAreTooSmallToCarryAKey()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        // The stream's ECDH pair is 128 + 32 bytes; even one of them dwarfs this.
        const int TooBigToBeAnAcknowledgement = 128;

        foreach (string type in (string[])["0000 ", "0001 "])
        {
            ExchangeEntry entry = Assert.Single(
                recording.Entries,
                e => e.Channel == "senkusha"
                    && e.Payload.StartsWith(type, StringComparison.Ordinal));

            Assert.DoesNotContain("<redacted", entry.Payload, StringComparison.Ordinal);
            Assert.True(
                entry.Payload.Length < TooBigToBeAnAcknowledgement,
                $"senkusha's {type.Trim()} is {entry.Payload.Length} characters, which is large "
                    + "enough to be carrying something this corpus should not publish");
        }
    }

    /// <summary>The offsets are ordered and start at zero, so a replay can order them.</summary>
    [Fact]
    public void TheOffsetsAreOrderedAndStartAtZero()
    {
        ExchangeRecording? recording = Corpus();
        if (recording is null)
            return;

        Assert.Equal(0, recording.Entries[0].AtMicroseconds);

        for (var at = 1; at < recording.Entries.Count; at++)
        {
            Assert.True(
                recording.Entries[at].AtMicroseconds >= recording.Entries[at - 1].AtMicroseconds,
                $"entry {at} is earlier than the one before it");
        }
    }

    /// <summary>Every entry on a channel matching a prefix carries the redaction marker.</summary>
    private static void AssertRedacted(
        ExchangeRecording recording, string channel, string? prefix)
    {
        IReadOnlyList<ExchangeEntry> matching =
            [.. recording.Entries.Where(
                e => e.Channel == channel
                    && (prefix is null || e.Payload.StartsWith(prefix, StringComparison.Ordinal)))];

        Assert.NotEmpty(matching);
        Assert.All(
            matching,
            e => Assert.Contains("<redacted", e.Payload, StringComparison.Ordinal));
    }
}
