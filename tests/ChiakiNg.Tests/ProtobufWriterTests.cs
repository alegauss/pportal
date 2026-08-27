using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP425: writing a protobuf from named fields, so a participant builds its messages.
///
/// PP421's and PP424's payloads were transcribed out of the corpus, which made half of each replay a
/// tautology. PP422 is the evidence it mattered: the one payload that was built rather than copied
/// is the one that found a defect.
/// </summary>
public class ProtobufWriterTests
{
    /// <summary>A varint field, tag then value, as the wire format has it.</summary>
    [Theory]
    [InlineData(1, 0u, new byte[] { 0x08, 0x00 })]
    [InlineData(1, 9u, new byte[] { 0x08, 0x09 })]
    [InlineData(1, 31u, new byte[] { 0x08, 0x1f })]
    [InlineData(2, 1u, new byte[] { 0x10, 0x01 })]
    [InlineData(3, 2u, new byte[] { 0x18, 0x02 })]
    [InlineData(1, 150u, new byte[] { 0x08, 0x96, 0x01 })]
    public void AVarintFieldIsATagAndAValue(int field, ulong value, byte[] expected)
    {
        Assert.Equal(expected, ProtobufWriter.Varint(field, value));
    }

    /// <summary>A field number above fifteen takes two bytes of tag, which is where 0xfa 0x01 comes from.</summary>
    [Fact]
    public void ALargeFieldNumberTakesATwoByteTag()
    {
        // Field 31, wire type 2: (31 << 3) | 2 = 250 = 0xfa, then the continuation.
        Assert.Equal<byte[]>([0xfa, 0x01, 0x00], ProtobufWriter.Bytes(31, []));

        // And field 22, which is the controller connection's.
        Assert.Equal<byte[]>([0xb2, 0x01, 0x00], ProtobufWriter.Bytes(22, []));
    }

    /// <summary>
    /// PRESENT AND EMPTY IS NOT ABSENT, which is the distinction senkusha's BIG rests on.
    ///
    /// Its three credential fields are set to the empty string rather than left out, and PP418 holds
    /// that against senkusha.c. A writer that skipped an empty value would produce a different
    /// message and the replay would go red - correctly.
    /// </summary>
    [Fact]
    public void AnEmptyValueStillWritesItsTagAndZeroLength()
    {
        Assert.Equal<byte[]>([0x12, 0x00], ProtobufWriter.Bytes(2, []));
        Assert.Equal<byte[]>([0x1a, 0x00], ProtobufWriter.Bytes(3, []));
        Assert.Equal<byte[]>([0x22, 0x00], ProtobufWriter.Bytes(4, []));
    }

    /// <summary>A nested message declares the length of what it holds.</summary>
    [Fact]
    public void ANestedMessageDeclaresItsLength()
    {
        byte[] nested = ProtobufWriter.Message(
            3,
            ProtobufWriter.Varint(1, 9),
            ProtobufWriter.Bool(3, true));

        Assert.Equal<byte[]>([0x1a, 0x04, 0x08, 0x09, 0x18, 0x01], nested);
    }

    /// <summary>A bool is a varint of one or zero.</summary>
    [Fact]
    public void ABoolIsAVarint()
    {
        Assert.Equal<byte[]>([0x18, 0x01], ProtobufWriter.Bool(3, true));
        Assert.Equal<byte[]>([0x18, 0x00], ProtobufWriter.Bool(3, false));
    }

    /// <summary>
    /// THE TWO DIRECTIONS AGREE. What the writer writes, the reader finds.
    ///
    /// ProtobufRedaction walks a message to blank fields and this one builds one. Checking them
    /// against each other is what stops a bug in either from being invisible to the other.
    /// </summary>
    [Fact]
    public void WhatTheWriterWritesTheReaderFinds()
    {
        byte[] message = ProtobufWriter.Concat(
            ProtobufWriter.Varint(1, 1),
            ProtobufWriter.Message(
                3,
                ProtobufWriter.Varint(1, 12),
                ProtobufWriter.Bool(3, true),
                ProtobufWriter.Bool(4, true),
                ProtobufWriter.Bytes(8, [0xaa, 0xbb, 0xcc])));

        Assert.True(ProtobufRedaction.TryFindField(
            message, 0, message.Length, 3, out int at, out int length));

        Assert.True(ProtobufRedaction.TryFindField(
            message, at, at + length, 8, out int keyAt, out int keyLength));

        Assert.Equal(3, keyLength);
        Assert.Equal<byte[]>([0xaa, 0xbb, 0xcc], message[keyAt..(keyAt + keyLength)]);

        // And blanking it leaves the flags where they were.
        byte[]? blanked = ProtobufRedaction.Blank(message, 3, new HashSet<int> { 8 });
        Assert.NotNull(blanked);
        Assert.Equal<byte[]>([0x00, 0x00, 0x00], blanked[keyAt..(keyAt + keyLength)]);
        Assert.Equal<byte[]>([0x18, 0x01, 0x20, 0x01], blanked[(at + 2)..(at + 6)]);
    }

    /// <summary>A field number must be positive, because zero is not a field.</summary>
    [Fact]
    public void FieldZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtobufWriter.Varint(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtobufWriter.Bytes(0, []));
    }

    /// <summary>
    /// AND THE DERIVED PAYLOADS ARE WHAT THE CONSOLE RECEIVED.
    ///
    /// The point of PP425, asserted directly rather than only through the replay: these bytes were
    /// built from field numbers read out of takion.proto and values read out of the C, and they are
    /// the bytes in PP396's capture. Before this they were the capture's bytes, copied.
    /// </summary>
    [Fact]
    public void TheDerivedPayloadsMatchTheCapture()
    {
        // senkusha's version request: 08 1f fa 01 02 08 09
        Assert.Equal<byte[]>(
            [0x08, 0x1f, 0xfa, 0x01, 0x02, 0x08, 0x09],
            SenkushaExchangeParticipant.Payloads[
                (ushort)SenkushaMessage.TakionProtocolRequest]);

        // and its BIG: 08 00 12 08 08 09 12 00 1a 00 22 00
        Assert.Equal<byte[]>(
            [0x08, 0x00, 0x12, 0x08, 0x08, 0x09, 0x12, 0x00, 0x1a, 0x00, 0x22, 0x00],
            SenkushaExchangeParticipant.Payloads[(ushort)SenkushaMessage.Big]);

        // the stream's ack: 08 0e
        Assert.Equal<byte[]>([0x08, 0x0e], StreamExchangeParticipant.StreamInfoAck());

        // its controller connection: 08 15 b2 01 04 10 01 18 02
        Assert.Equal<byte[]>(
            [0x08, 0x15, 0xb2, 0x01, 0x04, 0x10, 0x01, 0x18, 0x02],
            StreamExchangeParticipant.ControllerConnection(dualSense: false));

        // and the microphone's STREAMINFO, header and all.
        Assert.Equal<byte[]>(
            [
                0x08, 0x0d, 0x7a, 0x10, 0x12, 0x0e,
                0x10, 0x01, 0x00, 0x00, 0xbb, 0x80, 0x00, 0x00, 0x01, 0xe0, 0x00, 0x00, 0x00, 0x01,
            ],
            StreamExchangeParticipant.MicrophoneStreamInfo());
    }
}
