using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the key, the IV and the GMAC keys, against the C.
/// </summary>
public class GkDerivationTests(ITestOutputHelper output)
{
    private static byte[] HandshakeKey(int seed) =>
        [.. Enumerable.Range(0, 16).Select(i => (byte)((i * seed) + 3))];

    private static byte[] Secret(int seed) =>
        [.. Enumerable.Range(0, 32).Select(i => (byte)((i * seed) + 11))];

    /// <summary>
    /// THE COMPARISON. The derived key and IV are the C's, for several stream indices.
    ///
    /// The index is in the hashed message, so it is the argument that must reach it: a port that
    /// dropped it would derive one pair for every stream and both directions of a session would
    /// share a key.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    public void TheDerivedKeyAndIvAreTheCs(byte index)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] handshakeKey = HandshakeKey(11);
        byte[] secret = Secret(7);

        using var native = new GkCrypt(2, index, handshakeKey, secret);
        (byte[] nativeKey, byte[] nativeIv) = native.KeyAndIv();

        (byte[] key, byte[] iv) = GkDerivation.KeyAndIv(index, handshakeKey, secret);

        Assert.True(nativeKey.SequenceEqual(key),
            $"index {index} key: C {Convert.ToHexString(nativeKey)}, managed {Convert.ToHexString(key)}");
        Assert.True(nativeIv.SequenceEqual(iv),
            $"index {index} iv: C {Convert.ToHexString(nativeIv)}, managed {Convert.ToHexString(iv)}");

        output.WriteLine($"index {index}: key and iv agree");
    }

    /// <summary>Different indices really do derive different keys from one secret.</summary>
    [Fact]
    public void TheIndexSeparatesTheTwoDirections()
    {
        byte[] handshakeKey = HandshakeKey(5);
        byte[] secret = Secret(13);

        (byte[] keyA, byte[] ivA) = GkDerivation.KeyAndIv(0, handshakeKey, secret);
        (byte[] keyB, byte[] ivB) = GkDerivation.KeyAndIv(1, handshakeKey, secret);

        Assert.NotEqual(keyA, keyB);
        Assert.NotEqual(ivA, ivB);
    }

    /// <summary>THE OTHER COMPARISON. The GMAC key at several refresh indices.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1000UL)]
    public void TheGmacKeyIsTheCs(ulong index)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] keyBase = HandshakeKey(3);
        byte[] iv = HandshakeKey(9);

        byte[] fromC = GkGmac.GenKey(index, keyBase, iv);
        byte[] managed = GkDerivation.GmacKey(index, keyBase, iv);

        Assert.True(fromC.SequenceEqual(managed),
            $"index {index}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
    }

    /// <summary>
    /// The fold is load-bearing: the key is not the first half of the hash.
    ///
    /// Taking hash[0..16] alone would compile, run, and authenticate nothing correctly. Asserting
    /// the key differs from that half is what pins the XOR rather than leaving it to the vectors.
    /// </summary>
    [Fact]
    public void TheGmacKeyIsFoldedRatherThanTruncated()
    {
        byte[] keyBase = HandshakeKey(3);
        byte[] iv = HandshakeKey(9);

        byte[] key = GkDerivation.GmacKey(0, keyBase, iv);

        Span<byte> message = stackalloc byte[32];
        keyBase.CopyTo(message);
        GkKeyStream.CounterAdd(iv, 0).CopyTo(message[16..]);

        byte[] hash = System.Security.Cryptography.SHA256.HashData(message.ToArray());
        Assert.NotEqual(hash[..16], key);
    }

    /// <summary>And the refresh index advances the IV, so successive windows differ.</summary>
    [Fact]
    public void EachRefreshWindowHasItsOwnKey()
    {
        byte[] keyBase = HandshakeKey(3);
        byte[] iv = HandshakeKey(9);

        Assert.NotEqual(GkDerivation.GmacKey(0, keyBase, iv), GkDerivation.GmacKey(1, keyBase, iv));
    }

    /// <summary>Wrong-sized inputs are refused rather than read past.</summary>
    [Fact]
    public void BadSizesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => GkDerivation.KeyAndIv(0, new byte[8], new byte[32]));
        Assert.Throws<ArgumentException>(() => GkDerivation.KeyAndIv(0, new byte[16], new byte[16]));
        Assert.Throws<ArgumentException>(() => GkDerivation.GmacKey(0, new byte[8], new byte[16]));
    }

    /// <summary>THE DRIFT CHECK. The framing and the fold are still the C's.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\gkcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\gkcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(GkDerivation.TheFramingIsStill(core),
            "the derivation message is no longer framed with 1, index, 0 ... 1, 0");
        Assert.True(GkDerivation.TheGmacKeyIsStillFolded(core),
            "the GMAC key is no longer the hash folded in half");
    }
}
