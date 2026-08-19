using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP35: test/gkcrypt.c's recorded GMACs, which nothing in this port checked before.
///
/// The GMAC is what takion compares a received packet's tail against, and PP105 is why the gap
/// mattered: takion checks no MAC at all until crypt is available, and checks this one on
/// everything afterwards. A GMAC the port computed differently would reject every packet the
/// console sends and be reported as a stream that will not start - no error naming the MAC.
///
/// Vectors read out of the C at run time by <see cref="CryptoVectors"/>, never copied. The
/// expected values in this file are the ones the C names, fetched by name.
/// </summary>
public class GmacVectorTests
{
    private static string? File => SanitizerSource.LocateRelative(@"test\gkcrypt.c");

    /// <summary>
    /// The derivation, which is a pure function on both sides: an index, a base key and an IV.
    ///
    /// This is the one that moves when a key refresh crosses an index boundary, so getting it
    /// right is what makes a long session keep authenticating rather than stop partway.
    /// </summary>
    [Fact]
    public void TheRecordedKeyDerivationMatches()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = CryptoVectors.InFunction(File, "test_gen_gmac_key");
        Assert.Equal(3, v.Count);

        byte[] key = GkGmac.GenKey(1, v["key_initial"], v["iv"]);
        Assert.Equal(v["key_result"], key);
    }

    /// <summary>
    /// A wrong index must not produce the recorded key. Without this the test above passes for an
    /// implementation that ignores the index entirely - which is exactly the implementation a
    /// rewrite produces when it treats the index as bookkeeping rather than as input.
    /// </summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(2UL)]
    [InlineData(0x1_0000_0001UL)]
    public void AnotherIndexDoesNotProduceTheRecordedKey(ulong index)
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = CryptoVectors.InFunction(File, "test_gen_gmac_key");
        Assert.NotEqual(v["key_result"], GkGmac.GenKey(index, v["key_initial"], v["iv"]));
    }

    /// <summary>
    /// The GMAC itself, over a real packet, at the recorded low key position.
    /// </summary>
    [Fact]
    public void TheRecordedGmacMatchesAtTheLowPosition()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = CryptoVectors.InFunction(File, "test_gmac");
        using var gmac = new GkGmac(v["gkcrypt_key"], v["gkcrypt_iv"]);

        Assert.Equal(4, GkGmac.Size);
        Assert.Equal(v["gmac_expected"], gmac.Compute(0x69a0, v["buf"]));
    }

    /// <summary>
    /// And at the high one, which is the same buffer under the same key and a different answer.
    ///
    /// This is the case that says the key position is an input. A rewrite that truncated it to 32
    /// bits, or ignored it, passes the low case and fails here - and the high position is the one
    /// a session reaches after running for a while, so the failure would arrive mid-stream.
    /// </summary>
    [Fact]
    public void TheRecordedGmacMatchesAtTheHighPosition()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = CryptoVectors.InFunction(File, "test_gmac");
        using var gmac = new GkGmac(v["gkcrypt_key"], v["gkcrypt_iv"]);

        const ulong high = (1UL << 32) + 0x420;
        Assert.Equal(v["gmac_expected_high"], gmac.Compute(high, v["buf"]));
        Assert.NotEqual(v["gmac_expected"], v["gmac_expected_high"]);
    }

    /// <summary>
    /// A single flipped byte anywhere in the packet changes the MAC. This is the property takion
    /// relies on, and asserting it here means the port cannot pass by returning a constant.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(200)]
    public void AByteChangedAnywhereChangesTheGmac(int offset)
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = CryptoVectors.InFunction(File, "test_gmac");
        byte[] tampered = (byte[])v["buf"].Clone();
        tampered[offset] ^= 0xff;

        using var gmac = new GkGmac(v["gkcrypt_key"], v["gkcrypt_iv"]);
        Assert.NotEqual(v["gmac_expected"], gmac.Compute(0x69a0, tampered));
    }
}
