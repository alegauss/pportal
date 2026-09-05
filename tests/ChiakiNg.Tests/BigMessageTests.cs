using System.Text;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP727: stream_connection_send_big's payload - the message that asks a console for a stream.
///
/// The last of the four subsystems PP712's census owed PP707's host, so these hold that criterion
/// too: the list of what the run has no counterpart for is empty after this, and
/// StreamRunHostConsumersTests asserts it.
///
/// THE ASSERTION THIS FILE EXISTS FOR is <see cref="ObfuscatingIsNotTheSameAsEncrypting"/>. The C
/// hides the launch spec by encrypting a zero buffer and XORing the result, which reads as an
/// encryption written the long way round and is a different cipher mode - CFB for one block and
/// OFB thereafter. Nothing about getting it wrong fails loudly: the console simply never answers.
/// </summary>
public class BigMessageTests(ITestOutputHelper output)
{
    private static readonly byte[] Nonce =
        [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
         0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10];

    private static readonly byte[] Morning =
        [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
         0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x00];

    private static readonly byte[] HandshakeKey =
        [0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
         0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf];

    private static readonly byte[] PubKey = [.. Enumerable.Range(0, 65).Select(one => (byte)one)];

    private static readonly byte[] Sig = [.. Enumerable.Range(0, 32).Select(one => (byte)(0x80 + one))];

    private const string SessionKey = "sessionId4321";

    private static LaunchSpecFields Fields()
        => new(1920, 1080, 60, 15000, 1454, 12, ChiakiTarget.Ps5Unknown, ChiakiCodec.H265Hdr);

    private static RpCrypt Crypt() => new(ChiakiTarget.Ps5Unknown, Nonce, Morning);

    private static string? Read()
    {
        string? path = BigMessageSource.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>BIG is payload type ZERO, which is the value a port most easily loses.</summary>
    [Fact]
    public void TheBigsPayloadTypeIsZero()
        => Assert.Equal(0, BigMessage.BigType);

    /// <summary>The terminator is inside the message, and it is exactly one byte.</summary>
    [Fact]
    public void TheSpecCarriesItsTerminator()
    {
        byte[] bytes = BigMessage.SpecBytes("{}");

        Assert.Equal(3, bytes.Length);
        Assert.Equal((byte)0, bytes[^1]);
        Assert.Equal("{}", Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1));
    }

    /// <summary>
    /// THE FINDING, AS AN ASSERTION: the key stream is not what encrypting the spec gives.
    ///
    /// rpcrypt is AES-128-CFB128, so a block's key stream is the encryption of the previous
    /// CIPHERTEXT block. Over a zero plaintext that ciphertext IS the key stream, which makes the
    /// C's zero-buffer encrypt an OFB generator. The first block agrees with a direct encrypt
    /// because both are the plaintext XOR E(iv); every block after it differs, and a port that
    /// simplified the three lines into one would be wrong from byte sixteen with no error anywhere.
    /// </summary>
    [Fact]
    public void ObfuscatingIsNotTheSameAsEncrypting()
    {
        using RpCrypt crypt = Crypt();

        byte[] plain = [.. Enumerable.Range(0, 64).Select(one => (byte)(one + 1))];

        byte[] hidden = BigMessage.Obfuscate(BigMessage.KeyStream(crypt, plain.Length), plain);
        byte[] encrypted = crypt.Encrypt(BigMessage.LaunchSpecCounter, plain);

        output.WriteLine($"xor  {Convert.ToHexString(hidden)[..32]}...");
        output.WriteLine($"cfb  {Convert.ToHexString(encrypted)[..32]}...");

        // One block of agreement, because both are the plaintext XOR E(iv).
        Assert.Equal(hidden[..16], encrypted[..16]);

        // And no agreement after it: this is where a simplified port would already be wrong.
        Assert.NotEqual(hidden[16..], encrypted[16..]);
    }

    /// <summary>The XOR is its own inverse, so the console can undo exactly what this did.</summary>
    [Fact]
    public void TheObfuscationUndoesItself()
    {
        using RpCrypt crypt = Crypt();

        byte[] plain = BigMessage.SpecBytes("{\"a\":1}");
        byte[] keyStream = BigMessage.KeyStream(crypt, plain.Length);

        Assert.Equal(plain, BigMessage.Obfuscate(keyStream, BigMessage.Obfuscate(keyStream, plain)));
    }

    /// <summary>And the key stream is as long as what it hides, which is checked rather than assumed.</summary>
    [Fact]
    public void AKeyStreamOfTheWrongLengthIsRefused()
        => Assert.Throws<ArgumentException>(() => BigMessage.Obfuscate(new byte[4], new byte[5]));

    /// <summary>
    /// The encoded spec is the base64 of the obfuscated JSON, terminator and all.
    ///
    /// Read back the whole way round: decode, un-XOR with the same key stream, and the bytes are
    /// PP726's spec followed by its NUL.
    /// </summary>
    [Fact]
    public void TheEncodedSpecIsTheObfuscatedJsonAndItsTerminator()
    {
        using RpCrypt crypt = Crypt();

        string? encoded = BigMessage.EncodedLaunchSpec(crypt, Fields(), HandshakeKey);
        Assert.NotNull(encoded);

        byte[] hidden = Convert.FromBase64String(encoded);
        byte[] back = BigMessage.Obfuscate(BigMessage.KeyStream(crypt, hidden.Length), hidden);

        string? json = LaunchSpec.Format(Fields(), HandshakeKey);
        Assert.NotNull(json);

        output.WriteLine($"{json.Length} of JSON, {hidden.Length} obfuscated, {encoded.Length} encoded");

        Assert.Equal(json.Length + 1, back.Length);
        Assert.Equal(json, Encoding.UTF8.GetString(back, 0, back.Length - 1));
        Assert.Equal((byte)0, back[^1]);
    }

    /// <summary>A spec that would not fit the C's buffer answers null rather than a shorter BIG.</summary>
    [Fact]
    public void ASpecThatWouldNotFitLeavesNoEncoding()
    {
        using RpCrypt crypt = Crypt();

        var impossible = new LaunchSpecFields(
            1920, 1080, 60, 15000, 1454, 12, ChiakiTarget.Ps5Unknown, ChiakiCodec.H265Hdr);

        // Nothing about the fields can overrun; the key is what the C's buffer runs out on, and
        // Format is where that is decided - this only has to pass the refusal on.
        Assert.NotNull(BigMessage.EncodedLaunchSpec(crypt, impossible, HandshakeKey));
        Assert.Throws<ArgumentException>(
            () => BigMessage.EncodedLaunchSpec(crypt, impossible, new byte[8]));
    }

    /// <summary>
    /// THE BYTES: what this builds is what the other generator writes, field for field.
    ///
    /// PP25's pair, used the way PP684 uses it - one .proto becomes nanopb for the C and
    /// Google.Protobuf for this project, and a message built by hand here is held against the one
    /// the generated writer produces from the same values.
    /// </summary>
    [Fact]
    public void TheEncodedBigIsWhatTheOtherGeneratorWrites()
    {
        byte[] mine = BigMessage.Encode(12, SessionKey, "c3BlYw==", PubKey, Sig);

        var theirs = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Big,
            BigPayload = new Tkproto.BigPayload
            {
                ClientVersion = 12,
                SessionKey = SessionKey,
                LaunchSpec = "c3BlYw==",
                EncryptedKey = ByteString.CopyFrom(BigMessage.ZeroEncryptedKey),
                EcdhPubKey = ByteString.CopyFrom(PubKey),
                EcdhSig = ByteString.CopyFrom(Sig),
            },
        };

        output.WriteLine($"{mine.Length} byte(s) here, {theirs.CalculateSize()} there");

        Assert.Equal(theirs.ToByteArray(), mine);
    }

    /// <summary>And it reads back with every field where the proto puts it.</summary>
    [Fact]
    public void TheEncodedBigReadsBackFieldForField()
    {
        using RpCrypt crypt = Crypt();

        string? spec = BigMessage.EncodedLaunchSpec(crypt, Fields(), HandshakeKey);
        Assert.NotNull(spec);

        var parsed = Tkproto.TakionMessage.Parser.ParseFrom(
            BigMessage.Encode(12, SessionKey, spec, PubKey, Sig));

        Assert.Equal(Tkproto.TakionMessage.Types.PayloadType.Big, parsed.Type);
        Assert.Equal(12u, parsed.BigPayload.ClientVersion);
        Assert.Equal(SessionKey, parsed.BigPayload.SessionKey);
        Assert.Equal(spec, parsed.BigPayload.LaunchSpec);
        Assert.Equal(PubKey, parsed.BigPayload.EcdhPubKey.ToByteArray());
        Assert.Equal(Sig, parsed.BigPayload.EcdhSig.ToByteArray());

        // Present, four bytes long, and all of them zero - which is none of "absent" or "empty".
        Assert.True(parsed.BigPayload.HasEncryptedKey);
        Assert.Equal(new byte[4], parsed.BigPayload.EncryptedKey.ToByteArray());
    }

    /// <summary>
    /// The message fragments, which is what PP376's loop then does with it.
    ///
    /// The join rather than a claim: a real BIG at the narrowest MTU this port has measured is more
    /// than one datagram, and every fragment but the last is a continuation.
    /// </summary>
    [Fact]
    public void ARealBigNeedsFragmentingAtTheNarrowestMeasuredMtu()
    {
        using RpCrypt crypt = Crypt();

        string? spec = BigMessage.EncodedLaunchSpec(crypt, Fields(), HandshakeKey);
        Assert.NotNull(spec);

        byte[] encoded = BigMessage.Encode(12, SessionKey, spec, PubKey, Sig);

        IReadOnlyList<BigFragment> plan = BigFragments.Plan(
            encoded.Length, BigFragments.NarrowestMeasuredMtu - BigFragments.NetworkOverhead);

        output.WriteLine($"{encoded.Length} bytes in {plan.Count} fragment(s)");

        Assert.True(plan.Count > 1, $"a {encoded.Length}-byte BIG fitted one narrow datagram");
        Assert.True(plan[0].IsFirst);
        Assert.True(plan[^1].EndsTheMessage);
        Assert.Equal(encoded.Length, plan.Sum(one => one.Size));
    }

    /// <summary>
    /// THE DRIFT CHECKS: the four decisions, still where this port read them.
    /// </summary>
    [Fact]
    public void TheCsSenderStillMakesTheDecisionsThisPortCopied()
    {
        if (Read() is not { } source)
            return;

        string? body = BigMessageSource.SendBody(source);
        Assert.NotNull(body);

        Assert.True(
            BigMessageSource.TheSpecIsStillHiddenByAKeyStreamAndNotEncrypted(body),
            "the spec is no longer hidden by zeroing, encrypting and XORing in that order");

        Assert.True(
            BigMessageSource.TheTerminatorIsStillCountedIn(body),
            "the trailing zero is no longer counted in before the encrypt");

        Assert.True(
            BigMessageSource.TheBigStillBypassesTheChokepoint(body),
            "the BIG now goes through stream_connection_send_data, which would give it a data type");

        Assert.True(
            BigMessageSource.TheEncryptedKeyIsStillFourZeroBytes(source),
            "the encrypted key is no longer four zero bytes written by its own callback");
    }
}
