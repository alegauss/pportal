using System.Security.Cryptography;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP817: what CNG will actually do, asked of CNG rather than of a wrapper over it.
///
/// PP298 measured AesGcm - NonceByteSizes 12 to 12, TagByteSizes 12 to 16 - and concluded the
/// runtime would not lend this one. The measurement is right and it is about a TYPE's contract,
/// not about the algorithm: chiaki's sixteen-byte IV and four-byte tag are both ordinary GCM, and
/// OpenSSL reaches them by asking. So Ghash rested on a sentence about a different API, and these
/// are the two readings that replace the sentence with an answer.
/// </summary>
public class CngGcmTests(ITestOutputHelper output)
{
    /// <summary>The C's own two departures: a block-sized IV and four bytes kept.</summary>
    private const int ChiakiIvBytes = 16;
    private const int ChiakiTagBytes = 4;

    /// <summary>READING ONE: what tag lengths does this machine's AES-GCM provider report.</summary>
    [Fact]
    public void TheProviderReportsItsTagLengths()
    {
        CngTagLengths? lengths = CngGcm.TagLengths();

        output.WriteLine($"BCRYPT_AUTH_TAG_LENGTH: {lengths?.ToString() ?? "refused"}");

        Assert.NotNull(lengths);

        // Sane rather than exact: the numbers are the provider's and this port does not get to
        // decide them. What is asserted is that they are BYTES and bracket a GCM tag - which is
        // also what catches the interop reading a bits-valued struct by mistake.
        Assert.InRange(lengths.Value.Minimum, 1, Ghash.BlockSize);
        Assert.Equal(Ghash.BlockSize, lengths.Value.Maximum);

        output.WriteLine(
            $"four bytes directly? {lengths.Value.Allows(ChiakiTagBytes)}");
    }

    /// <summary>
    /// READING TWO: does CNG accept a SIXTEEN-byte nonce, which is the one AesGcm refuses.
    ///
    /// And the answer is only worth anything if the bytes are right, so it is not asked on its own:
    /// the tag CNG produces is compared with the port's, which PP298 holds to chiaki's, which is
    /// OpenSSL's. A provider that accepted the nonce and computed something else would pass a test
    /// that only checked for a non-null answer.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(512)]
    public void ASixteenByteNonceAgreesWithThePortsOwnTag(int dataLength)
    {
        var key = new byte[16];
        var nonce = new byte[ChiakiIvBytes];
        var data = new byte[dataLength];

        new Random(dataLength + 1).NextBytes(key);
        new Random(dataLength + 2).NextBytes(nonce);
        new Random(dataLength).NextBytes(data);

        byte[]? fromCng = CngGcm.Tag(key, nonce, data, Ghash.BlockSize);

        Assert.NotNull(fromCng);

        byte[] fromPort = Ghash.Tag(key, nonce, data, Ghash.BlockSize);

        output.WriteLine($"cng {Convert.ToHexString(fromCng)}\nport {Convert.ToHexString(fromPort)}");

        Assert.Equal(fromPort, fromCng);
    }

    /// <summary>
    /// AND THE TWELVE-BYTE NONCE TOO, which is the case AesGcm does cover - so it has a third
    /// oracle and the interop is held against the runtime as well as against the port.
    /// </summary>
    [Fact]
    public void ATwelveByteNonceAgreesWithTheRuntimeAsWell()
    {
        var key = new byte[16];
        var nonce = new byte[12];
        var data = new byte[64];

        new Random(7).NextBytes(key);
        new Random(8).NextBytes(nonce);
        new Random(9).NextBytes(data);

        var fromRuntime = new byte[Ghash.BlockSize];
        using (var gcm = new AesGcm(key, Ghash.BlockSize))
            gcm.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, fromRuntime, data);

        byte[]? fromCng = CngGcm.Tag(key, nonce, data, Ghash.BlockSize);

        Assert.NotNull(fromCng);
        Assert.Equal(fromRuntime, fromCng);
    }

    /// <summary>
    /// THE FOUR BYTES chiaki KEEPS, which is the second departure and needs no provider support.
    ///
    /// A GCM tag is a PREFIX: four bytes of a sixteen-byte tag are the same four bytes whether the
    /// provider was asked for four or for sixteen. So a minimum above four is not a refusal, and
    /// EVP_CTRL_GCM_GET_TAG with four is doing the same cut underneath.
    /// </summary>
    [Fact]
    public void FourBytesAreThePrefixOfSixteen()
    {
        var key = new byte[16];
        var nonce = new byte[ChiakiIvBytes];
        var data = new byte[48];

        new Random(11).NextBytes(key);
        new Random(12).NextBytes(nonce);
        new Random(13).NextBytes(data);

        byte[]? four = CngGcm.Tag(key, nonce, data, ChiakiTagBytes);
        byte[]? sixteen = CngGcm.Tag(key, nonce, data, Ghash.BlockSize);

        Assert.NotNull(four);
        Assert.NotNull(sixteen);

        Assert.Equal(sixteen.AsSpan(0, ChiakiTagBytes).ToArray(), four);
        Assert.Equal(Ghash.Tag(key, nonce, data, ChiakiTagBytes), four);
    }

    /// <summary>
    /// AND THE LIVE PATH TAKES IT: the crypt's own GMAC is CNG's, and still the C's bytes.
    ///
    /// This is what the reading was for. ManagedGkCrypt.Gmac was the one production caller of the
    /// hand-written GHASH; it asks CNG first now and keeps the GHASH behind it. Both halves are
    /// asserted, because "the tag is right" and "CNG is what produced it" are different claims and
    /// a fallback that quietly answered everything would satisfy the first.
    /// </summary>
    [Fact]
    public void TheCryptsOwnGmacGoesThroughCngAndKeepsTheCsBytes()
    {
        ManagedGkCrypt crypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x3d, 16)],
            [.. Enumerable.Repeat((byte)0x91, 32)]);

        const ulong keyPos = 0x2000;
        byte[] packet = [.. Enumerable.Repeat((byte)0x5e, 96)];

        var tag = new byte[ChiakiTagBytes];
        crypt.Gmac(keyPos, packet, tag);

        Assert.True(
            ManagedGkCrypt.LastGmacUsedCng,
            "the crypt fell back to the hand-written GHASH, so CNG refused something");

        // The oracle, untouched: PP298 holds this one against chiaki's own GMAC.
        byte[] expected = Ghash.Tag(
            crypt.GmacKeyFor(keyPos), crypt.GmacIvFor(keyPos), packet, ChiakiTagBytes);

        output.WriteLine($"{Convert.ToHexString(tag)} against {Convert.ToHexString(expected)}");

        Assert.Equal(expected, tag);
    }

    /// <summary>
    /// A NONCE CNG WILL NOT TAKE ANSWERS null RATHER THAN THROWING, which is what the fallback is.
    ///
    /// The provider reports a maximum tag length and this asks past it. A refusal has to be a value
    /// the caller can act on: an exception here would have made "this machine is narrower" look
    /// like a defect in the interop, which is exactly the confusion that made the first run of
    /// these readings report a refusal that was not one.
    /// </summary>
    [Fact]
    public void APastTheMaximumTagLengthIsRefusedRatherThanThrown()
    {
        CngTagLengths? lengths = CngGcm.TagLengths();
        Assert.NotNull(lengths);

        Assert.Null(
            CngGcm.Tag(new byte[16], new byte[ChiakiIvBytes], [], lengths.Value.Maximum + 1));
    }
}
