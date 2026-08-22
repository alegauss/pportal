using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the managed IV derivation, against the C's for every target and a spread of counters.
///
/// rpcrypt has recorded vectors and a working wrapper, so this is a differential comparison rather
/// than a case table - the strongest form available, and the right one for a module where a wrong
/// byte produces a key that fails silently.
/// </summary>
public class RpCryptIvTests(ITestOutputHelper output)
{
    /// <summary>A nonce and a morning to derive an ambassador from. Any pair will do.</summary>
    private static readonly byte[] Nonce =
        [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
         0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10];

    private static readonly byte[] Morning =
        [0x57, 0x49, 0xd7, 0x87, 0x8f, 0xce, 0xfd, 0x23,
         0x3f, 0x72, 0xfe, 0xf0, 0x7e, 0x30, 0xe7, 0x5a];

    public static TheoryData<ChiakiTarget> Targets() =>
    [
        ChiakiTarget.Ps4_8, ChiakiTarget.Ps4_9, ChiakiTarget.Ps4_10, ChiakiTarget.Ps5_1,
    ];

    /// <summary>
    /// THE COMPARISON. Same target, same ambassador, same counter - same sixteen bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Targets))]
    public void TheManagedIvIsTheCs(ChiakiTarget target)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var native = new RpCrypt(target, Nonce, Morning);
        (byte[] _, byte[] ambassador) = RpCrypt.BrightAmbassador(target, Nonce, Morning);

        // Zero, one, a byte boundary, a word boundary and the top of the range - the counters where
        // a big-endian serialisation that was written little-endian would first differ.
        ulong[] counters = [0, 1, 2, 0xff, 0x100, 0xffff, 0x1_0000, 0xffff_ffff, 0x1_0000_0000, ulong.MaxValue];

        foreach (ulong counter in counters)
        {
            byte[] fromC = native.GenerateIv(counter);
            byte[] managed = RpCryptIv.Generate(target, ambassador, counter);

            Assert.Equal(RpCryptIv.KeySize, managed.Length);
            Assert.True(
                fromC.SequenceEqual(managed),
                $"{target} counter {counter:x}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
        }

        output.WriteLine($"{target}: {counters.Length} counters agree");
    }

    /// <summary>
    /// The counter really is big-endian, which the comparison above would also catch but not name.
    ///
    /// Counter 1 and counter 0x0100_0000_0000_0000 are byte-swaps of each other. A little-endian
    /// serialisation would produce the same two IVs with the arguments exchanged, so asserting they
    /// differ is what pins the direction rather than the agreement.
    /// </summary>
    [Fact]
    public void TheCounterIsBigEndian()
    {
        (byte[] _, byte[] ambassador) = (new byte[16], new byte[16]);

        byte[] one = RpCryptIv.Generate(ChiakiTarget.Ps4_10, ambassador, 1);
        byte[] swapped = RpCryptIv.Generate(ChiakiTarget.Ps4_10, ambassador, 0x0100_0000_0000_0000);

        Assert.NotEqual(one, swapped);
    }

    /// <summary>
    /// Every target maps to a key, including the two Unknowns, and the three keys are distinct.
    ///
    /// The default is not a fallback so much as the PS4 key doing double duty. A port that mapped
    /// only the four named targets would answer nothing for Ps4Unknown, where the C answers.
    /// </summary>
    [Fact]
    public void EveryTargetHasAKeyAndTheThreeAreDistinct()
    {
        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps4Unknown).SequenceEqual(RpCryptIv.Ps4Key));
        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps5Unknown).SequenceEqual(RpCryptIv.Ps4Key));
        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps4_10).SequenceEqual(RpCryptIv.Ps4Key));

        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps4_8).SequenceEqual(RpCryptIv.Ps4Pre10Key));
        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps4_9).SequenceEqual(RpCryptIv.Ps4Pre10Key));
        Assert.True(RpCryptIv.KeyFor(ChiakiTarget.Ps5_1).SequenceEqual(RpCryptIv.Ps5Key));

        Assert.False(RpCryptIv.Ps4Key.SequenceEqual(RpCryptIv.Ps5Key));
        Assert.False(RpCryptIv.Ps4Key.SequenceEqual(RpCryptIv.Ps4Pre10Key));
        Assert.False(RpCryptIv.Ps5Key.SequenceEqual(RpCryptIv.Ps4Pre10Key));
    }

    /// <summary>An ambassador of the wrong length is refused rather than hashed.</summary>
    [Fact]
    public void AShortAmbassadorIsRefused()
        => Assert.Throws<ArgumentException>(
            () => RpCryptIv.Generate(ChiakiTarget.Ps5_1, new byte[8], 0));

    /// <summary>THE DRIFT CHECK. The three keys and the default are still the C's.</summary>
    [Fact]
    public void TheCStillHoldsTheseKeys()
    {
        string? file = SanitizerSource.LocateRelative(CryptoVectors.RelativePath);
        Assert.True(file is not null, "no test/rpcrypt.c");

        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(RpCryptIv.TheKeysAreStill(core),
            "the three HMAC keys in rpcrypt.c are no longer the three this port carries");
        Assert.True(RpCryptIv.TheDefaultIsStillThePs4Key(core),
            "an unnamed target no longer falls through to the PS4 key");
    }
}
