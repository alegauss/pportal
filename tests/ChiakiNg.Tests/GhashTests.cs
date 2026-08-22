using System.Security.Cryptography;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP298: GHASH against two oracles - the runtime's own GCM where it will answer, and chiaki's
/// where it will not.
///
/// Nothing here checks the implementation against itself. GCM's bit convention is easy to get
/// backwards in a way that is perfectly self-consistent - a round trip would pass, a tag would look
/// like a tag, and no other GCM in the world would agree with it.
/// </summary>
public class GhashTests(ITestOutputHelper output)
{
    /// <summary>
    /// The runtime's AesGcm, on the one shape it accepts: a 12-byte nonce and a 16-byte tag.
    ///
    /// This is the check that the field arithmetic and the tag construction are right, done against
    /// an implementation that has nothing to do with chiaki. If GHASH is wrong, it is wrong here
    /// first and for a reason that is about GCM rather than about this port.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(100)]
    public void TheTagMatchesTheRuntimesGcm(int dataLength)
    {
        byte[] key = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 7))];
        byte[] nonce = [.. Enumerable.Range(0, 12).Select(i => (byte)(i * 13))];
        var data = new byte[dataLength];
        new Random(dataLength).NextBytes(data);

        var expected = new byte[16];
        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, expected.AsSpan(), data.AsSpan());

        byte[] managed = Ghash.Tag(key, nonce, data, 16);

        Assert.True(expected.SequenceEqual(managed),
            $"{dataLength} bytes: runtime {Convert.ToHexString(expected)}, managed {Convert.ToHexString(managed)}");

        output.WriteLine($"{dataLength} bytes of AAD agree with AesGcm");
    }

    /// <summary>
    /// THE COMPARISON THE RUNTIME CANNOT MAKE. chiaki's GMAC: 16-byte IV, 4-byte tag.
    ///
    /// Both of those are outside AesGcm's contract, which is what PP298 is about. The key position
    /// stays inside window zero so the C uses the key it was handed rather than refreshing.
    /// </summary>
    [Theory]
    [InlineData(0UL, 32)]
    [InlineData(16UL, 32)]
    [InlineData(160UL, 64)]
    [InlineData(44944UL, 100)]
    public void TheGmacMatchesChiakis(ulong keyPos, int dataLength)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] gmacKey = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 11 + 5))];
        byte[] sessionIv = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 3 + 1))];

        var buf = new byte[dataLength];
        new Random(dataLength + (int)keyPos).NextBytes(buf);

        using var native = new GkGmac(gmacKey, sessionIv);
        byte[] fromC = native.Compute(keyPos, buf);

        // The IV the C computes for this position, which PP26 already ports.
        byte[] iv = GmacKeyWindow.IvFor(sessionIv, keyPos);
        byte[] managed = Ghash.Tag(gmacKey, iv, buf, GkGmac.Size);

        Assert.True(fromC.SequenceEqual(managed),
            $"pos {keyPos} len {dataLength}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");

        output.WriteLine($"pos {keyPos}, {dataLength} bytes: {Convert.ToHexString(managed)}");
    }

    /// <summary>
    /// J0 is the IV only when the IV is twelve bytes.
    ///
    /// Sixteen goes through GHASH instead, so the two paths must not agree - and a port that took
    /// the fast path for every length would produce a tag that is wrong and looks fine.
    /// </summary>
    [Fact]
    public void TheInitialCounterTakesTheSlowPathForSixteen()
    {
        byte[] subkey = [.. Enumerable.Range(0, 16).Select(i => (byte)(i + 1))];
        byte[] twelve = [.. Enumerable.Range(0, 12).Select(i => (byte)(i + 1))];
        byte[] sixteen = [.. Enumerable.Range(0, 16).Select(i => (byte)(i + 1))];

        byte[] fast = Ghash.InitialCounter(subkey, twelve);
        Assert.Equal(1, fast[15]);
        Assert.Equal(twelve, fast[..12]);

        byte[] slow = Ghash.InitialCounter(subkey, sixteen);
        Assert.NotEqual(sixteen, slow);
    }

    /// <summary>
    /// The field multiply is not commutative-by-accident nonsense: it has an identity.
    ///
    /// In GCM's convention the identity is 0x80 followed by zeros - the bit numbered 0, which is
    /// the TOP bit of the first byte. Getting the convention backwards makes the identity 0x01 at
    /// the other end, and this is the cheapest place that shows.
    /// </summary>
    [Fact]
    public void TheFieldIdentityIsAtTheTopOfTheFirstByte()
    {
        byte[] value = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 17 + 3))];

        byte[] one = new byte[16];
        one[0] = 0x80;

        Assert.Equal(value, Ghash.Multiply(value, one));
        Assert.Equal(value, Ghash.Multiply(one, value));

        // ...and the other end is not the identity.
        byte[] wrong = new byte[16];
        wrong[15] = 0x01;
        Assert.NotEqual(value, Ghash.Multiply(value, wrong));
    }

    /// <summary>Anything times zero is zero, which the identity test cannot show.</summary>
    [Fact]
    public void ZeroAbsorbs()
    {
        byte[] value = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 29 + 7))];
        Assert.All(Ghash.Multiply(value, new byte[16]), b => Assert.Equal(0, b));
    }

    /// <summary>A tag longer than a block, or of no length, is refused.</summary>
    [Fact]
    public void BadTagLengthsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ghash.Tag(new byte[16], new byte[16], [], 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ghash.Tag(new byte[16], new byte[16], [], 0));
    }
}
