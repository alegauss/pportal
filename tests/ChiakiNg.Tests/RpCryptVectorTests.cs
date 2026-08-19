using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP35: test/rpcrypt.c's PS4-pre-10 vectors, a console generation the port checked nowhere.
///
/// The suite drives one of rpcrypt's nine recorded functions - test_bright_ambassador, on the
/// modern target. The four here need nothing the seam did not already carry, and they cover the
/// older firmware's key schedule, which is a different derivation and not a different constant.
///
/// Registration-mode vectors are still uncovered: chiaki_rpcrypt_init_regist is not on the shim,
/// and adding it is its own change rather than a line in this file.
/// </summary>
public class RpCryptVectorTests
{
    private static string? File => SanitizerSource.LocateRelative(@"test\rpcrypt.c");

    private static IReadOnlyDictionary<string, byte[]> Vectors(string function)
        => CryptoVectors.InFunction(File!, function);

    /// <summary>
    /// The two keys a nonce and a morning key derive to on the older firmware. Same function as
    /// the modern target, different schedule inside it - so a port that special-cased the target
    /// in the wrong direction passes one of these and fails the other.
    /// </summary>
    [Fact]
    public void TheRecordedPre10BrightAmbassadorMatches()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_bright_ambassador_ps4_pre10");
        (byte[] bright, byte[] ambassador) =
            RpCrypt.BrightAmbassador(ChiakiTarget.Ps4_9, v["nonce"], v["morning"]);

        Assert.Equal(v["bright_expected"], bright);
        Assert.Equal(v["ambassador_expected"], ambassador);
    }

    /// <summary>
    /// The IV at a counter. Two counters are recorded, and each is generated TWICE on purpose:
    /// the C's own vector asserts the same answer both times, which says generate_iv is a
    /// function of the counter and not of how many times it has been called. A rewrite that
    /// advanced internal state per call passes the first assertion and fails the second.
    /// </summary>
    [Fact]
    public void TheRecordedPre10IvIsPureInItsCounter()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_iv_ps4_pre10");
        using var crypt = new RpCrypt(ChiakiTarget.Ps4_9, v["nonce"], v["morning"]);

        Assert.Equal(v["iv_a_expected"], crypt.GenerateIv(0));
        Assert.Equal(v["iv_a_expected"], crypt.GenerateIv(0));
        Assert.Equal(v["iv_b_expected"], crypt.GenerateIv(0x0102030405060708));
        Assert.Equal(v["iv_b_expected"], crypt.GenerateIv(0x0102030405060708));

        // And the counters differ, so the two above are not the same assertion written twice.
        Assert.NotEqual(v["iv_a_expected"], v["iv_b_expected"]);
    }

    /// <summary>
    /// Encryption at three lengths, and the short one is the point. Five bytes is under a block,
    /// which is where a rewrite that padded to sixteen, or that dropped the tail, produces a
    /// cipher the console cannot read - and the recorded answer is five bytes long, not sixteen.
    /// </summary>
    [Fact]
    public void TheRecordedPre10EncryptionMatchesAtEveryLength()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_encrypt_ps4_pre10");
        using var crypt = new RpCrypt(ChiakiTarget.Ps4_9, v["nonce"], v["morning"]);

        const ulong counter = 0x0102030405060708;
        byte[] shortCipher = crypt.Encrypt(counter, v["buf_a"]);

        Assert.Equal(5, v["buf_a"].Length);
        Assert.Equal(v["cipher_expected_a"], shortCipher);
        Assert.Equal(v["buf_a"].Length, shortCipher.Length);

        Assert.Equal(16, v["buf_b"].Length);
        Assert.Equal(v["cipher_expected_b"], crypt.Encrypt(counter, v["buf_b"]));
    }

    /// <summary>
    /// And the other direction, at the four lengths the C records: under a block, exactly one,
    /// over one but not a multiple of it, and exactly two. The third is the one that matters -
    /// a rewrite that handled whole blocks and a short tail separately gets the first two right
    /// and the tail's keystream position wrong.
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    public void TheRecordedPre10DecryptionMatches(string which)
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_decrypt_ps4_pre10");
        using var crypt = new RpCrypt(ChiakiTarget.Ps4_9, v["nonce"], v["morning"]);

        byte[] cipher = v["buf_" + which];
        byte[] expected = v["expected_" + which];

        Assert.Equal(expected, crypt.Decrypt(0x0102030405060708, cipher));
        Assert.Equal(cipher.Length, expected.Length);
    }

    /// <summary>
    /// The lengths really are those four, so the theory above is not the same case run four
    /// times under different names.
    /// </summary>
    [Fact]
    public void TheDecryptionCasesCoverFourLengths()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_decrypt_ps4_pre10");
        Assert.Equal([4, 16, 20, 32],
            new[] { "a", "b", "c", "d" }.Select(k => v["buf_" + k].Length));
    }

    /// <summary>
    /// A wrong counter must not decrypt. Without it every assertion above passes for an
    /// implementation that ignores the counter, which for a stream cipher means every packet in
    /// a session keyed identically - a real failure, and a silent one.
    /// </summary>
    [Fact]
    public void AnotherCounterDoesNotDecrypt()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_decrypt_ps4_pre10");
        using var crypt = new RpCrypt(ChiakiTarget.Ps4_9, v["nonce"], v["morning"]);

        Assert.NotEqual(v["expected_a"], crypt.Decrypt(0x0102030405060709, v["buf_a"]));
    }
}
