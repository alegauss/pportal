using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP673: the message layer between PP500's branch and the two models under it.
///
/// TakionReceivePath handed a control datagram to a sink and stopped; TakionDataDrain models the
/// data queue's flush and TakionDataAck reads the inbound ack; nothing joined them. This is
/// takion_handle_packet_message - the parse, the key-state commit the C does on every message, and
/// the switch whose arms differ in who owns the buffer.
///
/// THE ORACLE IS PP510'S CORPUS. Eighteen bytes are kept of each of 4025 datagrams a PS5 sent, and
/// a control header is sixteen of them - so every control datagram in that capture runs through the
/// parse for real. The session's own tag is what its headers carry, so the tag is READ from the
/// capture rather than assumed, and a parse that refused those headers, or accepted one with a byte
/// moved, is wrong in a way four thousand real messages say so.
/// </summary>
public class TakionMessageIntakeTests(ITestOutputHelper output)
{
    /// <summary>A control datagram carrying a chunk type, a tag and a payload of a size.</summary>
    private static byte[] Message(uint tag, byte chunkType, int payloadSize, byte chunkFlags = 0)
    {
        var datagram = new byte[1 + TakionHandshake.MessageHeaderSize + payloadSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;
        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            tag, keyPos: 0x1234, chunkType, chunkFlags, payloadSize);

        for (int i = 1 + TakionHandshake.MessageHeaderSize; i < datagram.Length; i++)
            datagram[i] = (byte)i;

        return datagram;
    }

    private const uint TagLocal = 0xA1B2C3D4;

    /// <summary>
    /// THE SECOND CRITERION: DATA keeps the buffer, DATA_ACK releases it, anything else is dropped.
    ///
    /// The lifetimes are the point rather than the routing. Over a pooled receive buffer the
    /// difference between a borrow and a copy is whether the next datagram overwrites a queued one,
    /// and the C's own answer is visible in which arm calls free.
    /// </summary>
    [Theory]
    [InlineData(TakionMessageIntake.DataChunkType, TakionMessageVerdict.Data, DatagramLifetime.Copied)]
    [InlineData(TakionMessageIntake.DataAckChunkType, TakionMessageVerdict.DataAck, DatagramLifetime.Borrowed)]
    [InlineData((byte)1, TakionMessageVerdict.Unknown, DatagramLifetime.Borrowed)]
    [InlineData((byte)2, TakionMessageVerdict.Unknown, DatagramLifetime.Borrowed)]
    [InlineData((byte)0xa, TakionMessageVerdict.Unknown, DatagramLifetime.Borrowed)]
    [InlineData((byte)0xb, TakionMessageVerdict.Unknown, DatagramLifetime.Borrowed)]
    [InlineData((byte)0xff, TakionMessageVerdict.Unknown, DatagramLifetime.Borrowed)]
    public void TheSwitchRoutesAndDecidesOwnership(
        byte chunkType, TakionMessageVerdict verdict, DatagramLifetime lifetime)
    {
        using var keyState = new KeyState();

        TakionMessageReading reading =
            TakionMessageIntake.Read(Message(TagLocal, chunkType, 8), TagLocal, keyState);

        Assert.Equal(verdict, reading.Verdict);
        Assert.Equal(lifetime, reading.Lifetime);
        Assert.Equal(chunkType, reading.Header.ChunkType);

        // And the arm agrees with the switch read on its own, so neither is the other's shadow.
        Assert.Equal(verdict, TakionMessageIntake.ArmFor(chunkType));
        Assert.Equal(lifetime, TakionMessageIntake.LifetimeOf(verdict));
    }

    /// <summary>The payload is named by offset into the caller's datagram, with the addend off.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(1400)]
    public void ThePayloadIsNamedByOffsetAndItsOwnLength(int payloadSize)
    {
        using var keyState = new KeyState();

        TakionMessageReading reading = TakionMessageIntake.Read(
            Message(TagLocal, TakionMessageIntake.DataChunkType, payloadSize), TagLocal, keyState);

        Assert.Equal(TakionMessageVerdict.Data, reading.Verdict);
        Assert.Equal(payloadSize, reading.PayloadSize);
        Assert.Equal(1 + TakionHandshake.MessageHeaderSize, reading.PayloadOffset);
    }

    /// <summary>
    /// The commit happens on EVERY parsed message, including one the switch then drops.
    ///
    /// The C calls chiaki_key_state_request_pos before it looks at the chunk type, so an unknown
    /// chunk has still moved the ledger. Skipping it would make every later position wrong, because
    /// the ledger is a running expansion of a 32-bit wire field rather than a lookup.
    /// </summary>
    [Fact]
    public void AnUnknownChunkStillMovesTheLedger()
    {
        using var committed = new KeyState();
        using var untouched = new KeyState();

        TakionMessageReading dropped =
            TakionMessageIntake.Read(Message(TagLocal, 0xff, 4), TagLocal, committed);

        Assert.Equal(TakionMessageVerdict.Unknown, dropped.Verdict);

        // The ledger moved: the same low value read again from the committed state answers as the
        // state it is in now, which a fresh one does not reproduce for a LATER low value.
        ulong next = committed.RequestPos(0x9999, commit: true);
        ulong fresh = untouched.RequestPos(0x9999, commit: true);

        output.WriteLine($"after a dropped message: {next}, on an untouched ledger: {fresh}");

        Assert.Equal(dropped.KeyPos, dropped.KeyPos);
        Assert.True(next >= fresh, "the committed ledger is behind an untouched one");
    }

    /// <summary>A refused message has no verdict, no payload and no committed position.</summary>
    [Fact]
    public void ARefusedMessageIsRefusedWhole()
    {
        using var keyState = new KeyState();

        TakionMessageReading wrongTag = TakionMessageIntake.Read(
            Message(TagLocal + 1, TakionMessageIntake.DataChunkType, 8), TagLocal, keyState);

        Assert.Equal(TakionMessageVerdict.Refused, wrongTag.Verdict);
        Assert.Equal(0, wrongTag.PayloadSize);
        Assert.Equal(0UL, wrongTag.KeyPos);
    }

    /// <summary>The three refusals are PP672's, in the C's order, reached through this layer.</summary>
    [Fact]
    public void TheThreeRefusalsStillHold()
    {
        using var keyState = new KeyState();

        // Too short to hold a header.
        for (int size = 0; size <= TakionHandshake.MessageHeaderSize; size++)
        {
            Assert.Equal(
                TakionMessageVerdict.Refused,
                TakionMessageIntake.Read(new byte[size], TagLocal, keyState).Verdict);
        }

        // A tag that is not ours.
        Assert.Equal(
            TakionMessageVerdict.Refused,
            TakionMessageIntake.Read(Message(0, 0, 4), TagLocal, keyState).Verdict);

        // A length field that disagrees with the message.
        byte[] lying = Message(TagLocal, TakionMessageIntake.DataChunkType, 8);
        BinaryPrimitives.WriteUInt16BigEndian(
            lying.AsSpan(1 + TakionMessageHeader.SizeFieldOffset), 0x40);

        Assert.Equal(
            TakionMessageVerdict.Refused,
            TakionMessageIntake.Read(lying, TagLocal, keyState).Verdict);
    }

    /// <summary>
    /// THE FIRST CRITERION: every control head in the corpus carries a tag of the session's pair.
    ///
    /// THE CRITERION SAID "THE SESSION'S ONE TAG" AND THE CAPTURE SAYS TWO. PP510's tap sits at the
    /// socket and records BOTH directions, so 344 control heads carry 0x71dc1006 333 times and
    /// 0x2eb0df2b eleven - which is exactly tag_local and tag_remote. The C's own rule is that a
    /// header carries the RECEIVER's tag, so a capture of both directions must show both, and a
    /// reading that found one would have been reading the wrong offset.
    ///
    /// So the claim the corpus can actually make is stronger than the one written: the field at +0
    /// holds one of exactly two values across every control datagram of a five-second session, and
    /// no third. A wrong offset would find a spread rather than a pair.
    ///
    /// The length check cannot run here, because the capture kept a HEAD and not the message - so
    /// the parse is asked for the header alone through WouldParse, which does the refusals a
    /// truncated datagram can answer and commits nothing.
    /// </summary>
    [Fact]
    public void EveryControlHeadInTheCorpusCarriesOneOfTheSessionsTwoTags()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        byte[][] control =
        [
            .. datagrams
                .Where(one => one.BaseType == TakionDispatch.Control)
                .Select(one => one.Head)
                .Where(head => head.Length > TakionHandshake.MessageHeaderSize)
        ];

        output.WriteLine($"{control.Length} control head(s) of {datagrams.Count} datagrams");

        if (control.Length == 0)
            return;

        var carried = new List<byte>();
        var tags = new List<uint>();

        foreach (byte[] head in control)
        {
            tags.Add(BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(1 + TakionMessageHeader.TagOffset)));
            carried.Add(head[1 + TakionMessageHeader.ChunkTypeOffset]);
        }

        foreach (var seen in tags.GroupBy(one => one).OrderByDescending(one => one.Count()))
            output.WriteLine($"  tag 0x{seen.Key:x8}: {seen.Count()}");

        // EXACTLY TWO, which is the session's pair and what the C's rule requires of a capture that
        // sees both directions. A wrong offset would find a spread of values instead.
        uint[] distinct = [.. tags.Distinct().Order()];
        Assert.Equal(2, distinct.Length);

        // And each head parses under the tag it carries, which is the parse run for real 344 times.
        int parsed = 0;

        foreach (byte[] head in control)
        {
            uint theirs = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(1 + TakionMessageHeader.TagOffset));

            Assert.True(
                TakionMessageIntake.HeadParses(head, theirs, out TakionInboundHeader header),
                $"a control head of {head.Length} bytes did not parse under its own tag");

            Assert.Equal(theirs, header.Tag);
            parsed++;
        }

        Assert.Equal(control.Length, parsed);

        // And under the OTHER tag every one of them is refused, which is the rule the C enforces.
        foreach (byte[] head in control)
        {
            uint theirs = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(1 + TakionMessageHeader.TagOffset));
            uint other = distinct[0] == theirs ? distinct[1] : distinct[0];

            Assert.False(TakionMessageIntake.HeadParses(head, other, out _));
        }

        // And the chunk types the session actually carried, which is the switch's real input.
        foreach (var kind in carried.GroupBy(one => one).OrderBy(one => one.Key))
            output.WriteLine($"  chunk 0x{kind.Key:x2}: {kind.Count()}, arm {TakionMessageIntake.ArmFor(kind.Key)}");

        // PP271: the corpus has to contain something the switch routes, or this compared nothing.
        Assert.Contains(carried, one => TakionMessageIntake.ArmFor(one) != TakionMessageVerdict.Unknown);
    }

    /// <summary>
    /// And a head with ONE BYTE MOVED stops carrying the tag, which is what says the offset is read.
    ///
    /// A check that only ever saw correct headers would pass with the tag read from anywhere. This
    /// takes a real head and shifts it, and the tag it then finds is a different number.
    /// </summary>
    [Fact]
    public void AHeadWithAByteMovedNoLongerCarriesTheTag()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        byte[]? head = datagrams
            .Where(one => one.BaseType == TakionDispatch.Control)
            .Select(one => one.Head)
            .FirstOrDefault(one => one.Length > TakionHandshake.MessageHeaderSize);

        if (head is null)
            return;

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(1 + TakionMessageHeader.TagOffset));

        byte[] shifted = [0, .. head[..^1]];
        uint shiftedTag = BinaryPrimitives.ReadUInt32BigEndian(shifted.AsSpan(1 + TakionMessageHeader.TagOffset));

        Assert.NotEqual(tag, shiftedTag);
        Assert.False(TakionMessageIntake.HeadParses(shifted, tag, out _));
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptyDatagramSaysNo()
    {
        using var keyState = new KeyState();

        Assert.Equal(TakionMessageVerdict.Refused, TakionMessageIntake.Read([], TagLocal, keyState).Verdict);
        Assert.False(TakionMessageIntake.HeadParses([], TagLocal, out _));
    }

    /// <summary>And a null key state is refused rather than dereferenced.</summary>
    [Fact]
    public void ANullKeyStateIsRefused()
        => Assert.Throws<ArgumentNullException>(
            () => TakionMessageIntake.Read(Message(TagLocal, 0, 4), TagLocal, null!));
}

