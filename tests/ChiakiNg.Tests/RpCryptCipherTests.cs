using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the managed cipher against the C's, at lengths that are and are not whole blocks.
/// </summary>
public class RpCryptCipherTests(ITestOutputHelper output)
{
    private static readonly byte[] Nonce =
        [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
         0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x00];

    private static readonly byte[] Morning =
        [0x57, 0x49, 0xd7, 0x87, 0x8f, 0xce, 0xfd, 0x23,
         0x3f, 0x72, 0xfe, 0xf0, 0x7e, 0x30, 0xe7, 0x5a];

    /// <summary>
    /// Lengths chosen around the block boundary, which is where a stream mode written as a block
    /// mode first differs: one byte, one short of a block, exactly one, one over, and several.
    /// </summary>
    public static TheoryData<int> Lengths() => [1, 15, 16, 17, 31, 32, 33, 64, 100];

    /// <summary>THE COMPARISON. Same key, same IV, same bytes out.</summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void TheManagedCipherIsTheCs(int length)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new RpCrypt(ChiakiTarget.Ps5_1, Nonce, Morning);
        byte[] bright = native.Bright();
        byte[] iv = native.GenerateIv(0);

        var plain = new byte[length];
        new Random(length).NextBytes(plain);

        byte[] fromC = native.Encrypt(0, plain);
        byte[] managed = RpCryptCipher.Encrypt(bright, iv, plain);

        Assert.True(fromC.SequenceEqual(managed),
            $"{length} bytes: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");

        output.WriteLine($"{length} bytes agree");
    }

    /// <summary>And decrypting agrees too, which is a different walk with the same feedback.</summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void TheManagedDecryptIsTheCs(int length)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new RpCrypt(ChiakiTarget.Ps5_1, Nonce, Morning);
        byte[] bright = native.Bright();
        byte[] iv = native.GenerateIv(0);

        var cipher = new byte[length];
        new Random(length * 3).NextBytes(cipher);

        byte[] fromC = native.Decrypt(0, cipher);
        byte[] managed = RpCryptCipher.Decrypt(bright, iv, cipher);

        Assert.True(fromC.SequenceEqual(managed),
            $"{length} bytes: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
    }

    /// <summary>
    /// The counter reaches the cipher through the IV, so different counters encrypt differently.
    ///
    /// A port that derived the IV once and reused it would pass every single-counter comparison
    /// above and reuse a keystream across the session, which is the failure CFB is least forgiving
    /// of - two payloads under one keystream XOR to their own plaintexts.
    /// </summary>
    [Fact]
    public void DifferentCountersProduceDifferentCiphertext()
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new RpCrypt(ChiakiTarget.Ps5_1, Nonce, Morning);
        byte[] bright = native.Bright();
        byte[] plain = [.. Enumerable.Repeat((byte)0x41, 48)];

        byte[] first = RpCryptCipher.Encrypt(bright, native.GenerateIv(0), plain);
        byte[] second = RpCryptCipher.Encrypt(bright, native.GenerateIv(1), plain);

        Assert.NotEqual(first, second);
        Assert.Equal(native.Encrypt(1, plain), second);
    }

    /// <summary>Encrypt then decrypt is the identity, at every length.</summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void ItRoundTrips(int length)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new RpCrypt(ChiakiTarget.Ps5_1, Nonce, Morning);
        byte[] bright = native.Bright();
        byte[] iv = native.GenerateIv(7);

        var plain = new byte[length];
        new Random(length + 500).NextBytes(plain);

        byte[] cipher = RpCryptCipher.Encrypt(bright, iv, plain);
        Assert.Equal(plain, RpCryptCipher.Decrypt(bright, iv, cipher));
    }

    /// <summary>Nothing in, nothing out - and not an exception.</summary>
    [Fact]
    public void AnEmptyPayloadIsEmpty()
        => Assert.Empty(RpCryptCipher.Encrypt(new byte[16], new byte[16], []));

    /// <summary>A key or IV of the wrong length is refused rather than read past.</summary>
    [Fact]
    public void AShortKeyOrIvIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RpCryptCipher.Encrypt(new byte[8], new byte[16], [1]));
        Assert.Throws<ArgumentException>(() => RpCryptCipher.Encrypt(new byte[16], new byte[8], [1]));
    }

    /// <summary>THE DRIFT CHECK. The C still uses CFB128 keyed on bright.</summary>
    [Fact]
    public void TheCStillUsesThisCipher()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(RpCryptCipher.TheModeIsStillCfb128(core),
            "rpcrypt no longer uses AES-128 CFB128 on both its crypto backends");
        Assert.True(RpCryptCipher.TheKeyIsStillBright(core),
            "the cipher is no longer keyed on bright");
    }
}
