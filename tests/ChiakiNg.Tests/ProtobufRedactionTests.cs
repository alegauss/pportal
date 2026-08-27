using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP423: blanking a protobuf's fields, so the BANG can be recorded without its keys.
///
/// PP325 replaced a line-shaped redaction rule with a field-shaped one for HTTP heads. This is the
/// same move one encoding over, and the BANG is the message that earns it: nine fields, three
/// secret, and the two the client acts on were being hidden with them.
/// </summary>
public class ProtobufRedactionTests
{
    /// <summary>
    /// A BANG as senkusha's arrives, which is the shape with no key in it.
    ///
    /// 08 01                  type = BANG
    /// 1a 0a                  field 3, 10 bytes - bang_payload
    ///   08 09                  server_version 9
    ///   10 00                  token 0
    ///   18 01                  encrypted_key_accepted true
    ///   20 01                  version_accepted true
    ///   2a 00                  session_key ""
    /// </summary>
    private static byte[] BangWithoutKeys() =>
        [0x08, 0x01, 0x1a, 0x0a, 0x08, 0x09, 0x10, 0x00, 0x18, 0x01, 0x20, 0x01, 0x2a, 0x00];

    /// <summary>
    /// And one carrying them, as the stream's does: session_key, ecdh_pub_key and ecdh_sig.
    ///
    /// The keys are short here because their length is not what is being tested - that they are
    /// zeroed and the flags are not is.
    /// </summary>
    private static byte[] BangWithKeys() =>
    [
        0x08, 0x01,                                     // type = BANG
        0x1a, 0x12,                                     // field 3, 18 bytes
            0x08, 0x09,                                 //   server_version 9
            0x18, 0x01,                                 //   encrypted_key_accepted true
            0x20, 0x01,                                 //   version_accepted true
            0x2a, 0x04, 0xde, 0xad, 0xbe, 0xef,         //   session_key    (field 5)
            0x42, 0x04, 0xca, 0xfe, 0xba, 0xbe,         //   ecdh_pub_key   (field 8)
    ];

    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. The keys go and the verdict stays.
    /// </summary>
    [Fact]
    public void TheKeysAreZeroedAndTheFlagsSurvive()
    {
        byte[]? blanked = ProtobufRedaction.Blank(
            BangWithKeys(), MessageSecrets.BangPayloadField, MessageSecrets.BangSecretFields);

        Assert.NotNull(blanked);

        // Same length: the structure is kept, so a reader can still decode it.
        Assert.Equal(BangWithKeys().Length, blanked.Length);

        // The two flags the client acts on, untouched.
        Assert.Equal<byte[]>([0x18, 0x01, 0x20, 0x01], blanked[6..10]);

        // And server_version.
        Assert.Equal<byte[]>([0x08, 0x09], blanked[4..6]);

        // session_key and ecdh_pub_key: tags and lengths kept, values zeroed.
        Assert.Equal<byte[]>([0x2a, 0x04, 0x00, 0x00, 0x00, 0x00], blanked[10..16]);
        Assert.Equal<byte[]>([0x42, 0x04, 0x00, 0x00, 0x00, 0x00], blanked[16..22]);
    }

    /// <summary>
    /// A field that is not there is not an error: ecdh_pub_key and ecdh_sig are optional, and an
    /// absent key is nothing to hide.
    /// </summary>
    [Fact]
    public void AnAbsentSecretFieldIsNotAnError()
    {
        byte[]? blanked = ProtobufRedaction.Blank(
            BangWithoutKeys(), MessageSecrets.BangPayloadField, MessageSecrets.BangSecretFields);

        Assert.NotNull(blanked);

        // Only session_key was there, and it was already empty - so nothing moved at all.
        Assert.Equal<byte[]>(BangWithoutKeys(), blanked);
    }

    /// <summary>
    /// A PAYLOAD IT CANNOT WALK IS REFUSED, and the caller must treat null as the marker.
    ///
    /// Blanking nothing and recording it would publish exactly the bytes the rule exists to hide.
    /// PP326's principle: with no field identified there is no basis to record it.
    /// </summary>
    [Theory]
    [InlineData(new byte[0])]                                  // nothing
    [InlineData(new byte[] { 0x08 })]                          // a tag with no value
    [InlineData(new byte[] { 0x08, 0x01 })]                    // a type and no bang_payload
    [InlineData(new byte[] { 0x08, 0x01, 0x1a, 0x40, 0x08 })]  // a length past the end
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff })]  // continuation bytes forever
    [InlineData(new byte[] { 0x08, 0x01, 0x1c, 0x02, 0x08, 0x09 })]  // wire type 4: a group
    public void APayloadItCannotWalkIsRefused(byte[] payload)
    {
        Assert.Null(ProtobufRedaction.Blank(
            payload, MessageSecrets.BangPayloadField, MessageSecrets.BangSecretFields));
    }

    /// <summary>Each wire type is measured by its own width, so blanking one does not corrupt the frame.</summary>
    [Fact]
    public void EachWireTypeIsMeasuredByItsOwnWidth()
    {
        // field 1 varint, field 2 length-delimited, field 3 fixed64, field 4 fixed32
        byte[] message =
        [
            0x08, 0x96, 0x01,                                            // 1: varint 150
            0x12, 0x02, 0xaa, 0xbb,                                      // 2: two bytes
            0x19, 1, 2, 3, 4, 5, 6, 7, 8,                                // 3: fixed64
            0x25, 9, 10, 11, 12,                                         // 4: fixed32
        ];

        Assert.True(ProtobufRedaction.TryFindField(message, 0, message.Length, 1, out int at, out int length));
        Assert.Equal((1, 2), (at, length));

        Assert.True(ProtobufRedaction.TryFindField(message, 0, message.Length, 2, out at, out length));
        Assert.Equal((5, 2), (at, length));

        Assert.True(ProtobufRedaction.TryFindField(message, 0, message.Length, 3, out at, out length));
        Assert.Equal((8, 8), (at, length));

        Assert.True(ProtobufRedaction.TryFindField(message, 0, message.Length, 4, out at, out length));
        Assert.Equal((17, 4), (at, length));

        // And a field that is not there is reported as absent rather than guessed at.
        Assert.False(ProtobufRedaction.TryFindField(message, 0, message.Length, 5, out _, out _));
    }

    /// <summary>
    /// THE BIG STAYS WHOLE-REDACTED, which is the other half of the decision.
    ///
    /// Five of its six fields are secret, so blanking by field would leave one varint and buy
    /// nothing for the machinery. Stated so the asymmetry reads as a choice rather than an omission.
    /// </summary>
    [Fact]
    public void TheBigIsStillRedactedWhole()
    {
        Assert.Equal(
            PayloadDisclosure.None,
            MessageSecrets.DisclosureFor(
                ChiakiMessageTap.StreamChannel, MessageSecrets.StreamSecret["BIG"]));

        Assert.Equal(
            PayloadDisclosure.FieldsBlanked,
            MessageSecrets.DisclosureFor(
                ChiakiMessageTap.StreamChannel, MessageSecrets.StreamSecret["BANG"]));
    }

    /// <summary>
    /// And every other channel and type is unchanged, so PP326's and PP397's decisions still stand.
    /// </summary>
    [Theory]
    [InlineData("ctrl", (ushort)0x33, PayloadDisclosure.None)]     // SESSION_ID
    [InlineData("ctrl", (ushort)0xfe, PayloadDisclosure.Whole)]    // heartbeat
    [InlineData("senkusha", (ushort)1, PayloadDisclosure.Whole)]   // senkusha's BANG carries no key
    [InlineData("senkusha", (ushort)0, PayloadDisclosure.Whole)]   // nor its BIG
    [InlineData("stream", (ushort)13, PayloadDisclosure.Whole)]    // STREAMINFO
    [InlineData("session", (ushort)0, PayloadDisclosure.Whole)]
    public void EveryOtherDecisionIsUnchanged(
        string channel, ushort type, PayloadDisclosure expected)
    {
        Assert.Equal(expected, MessageSecrets.DisclosureFor(channel, type));
    }

    /// <summary>An unknown type is still recorded by nobody, on any channel.</summary>
    [Fact]
    public void AnUnknownTypeIsStillNeverRecorded()
    {
        foreach (string channel in (string[])["ctrl", "stream", "senkusha", "session"])
        {
            Assert.Equal(
                PayloadDisclosure.None,
                MessageSecrets.DisclosureFor(channel, ChiakiMessageTap.UnknownType));
        }
    }
}
