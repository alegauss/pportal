using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the registration keys, against the C, at every column and over the PIN's whole width.
/// </summary>
public class RpCryptRegistManagedTests(ITestOutputHelper output)
{
    private static readonly byte[] Ambassador =
        [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
         0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x00];

    /// <summary>
    /// PINs that reach every byte of the four the XOR touches.
    ///
    /// Zero leaves the key alone, which is the case that would pass for a port XORing at the wrong
    /// offset. The rest each set a different byte, so an offset that is out by one shows as the
    /// wrong byte moving rather than as no difference.
    /// </summary>
    public static TheoryData<uint> Pins() => [0, 1, 0xff, 0x100, 0xff00, 0xff0000, 0xff000000, 0x12345678, uint.MaxValue];

    /// <summary>THE COMPARISON, pre-10: fixed key, PIN in the first four bytes.</summary>
    [Theory]
    [MemberData(nameof(Pins))]
    public void ThePre10RegistKeyIsTheCs(uint pin)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] fromC = RpCrypt.RegistBrightPs4Pre10(Ambassador, pin);
        byte[] managed = RpCryptRegist.BrightPs4Pre10(pin);

        Assert.True(fromC.SequenceEqual(managed),
            $"pin {pin:x8}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
    }

    /// <summary>And from firmware 10: a column of the table, PIN in the last four bytes.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_10)]
    [InlineData(ChiakiTarget.Ps5_1)]
    public void TheRegistKeyIsTheCsAtEveryColumn(ChiakiTarget target)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        ReadOnlySpan<byte> keys0 = RpCryptRegist.Keys0For(target);

        // Every column, not a sample: the strided read is right for column zero however it is
        // written, so zero alone proves nothing.
        for (int column = 0; column < RpCryptRegist.Columns; column++)
        {
            const uint Pin = 0x12345678;

            using var native = RpCrypt.ForRegistration(target, Ambassador, column, Pin);
            byte[] fromC = native.Bright();
            byte[] managed = RpCryptRegist.Bright(target, keys0, column, Pin);

            Assert.True(fromC.SequenceEqual(managed),
                $"{target} column {column}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
        }

        output.WriteLine($"{target}: {RpCryptRegist.Columns} columns agree");
    }

    /// <summary>
    /// The PIN lands at different ends on the two paths, which the comparisons prove only against
    /// the C.
    ///
    /// Asserted directly too, because "both agree with the C" stops being informative the day
    /// somebody changes both together.
    /// </summary>
    [Fact]
    public void ThePinLandsAtOppositeEnds()
    {
        const uint Pin = 0xaabbccdd;

        byte[] pre10 = RpCryptRegist.BrightPs4Pre10(Pin);
        byte[] plain = RpCryptRegist.Ps4Pre10RegistKey.ToArray();

        // First four moved, last twelve did not.
        Assert.NotEqual(plain[0], pre10[0]);
        Assert.Equal(plain[15], pre10[15]);

        byte[] ten = RpCryptRegist.Bright(ChiakiTarget.Ps4_10, RpCryptTables.Ps4Keys0, 0, Pin);
        byte[] zero = RpCryptRegist.Bright(ChiakiTarget.Ps4_10, RpCryptTables.Ps4Keys0, 0, 0);

        // ...and the other way round from ten onwards.
        Assert.Equal(zero[0], ten[0]);
        Assert.NotEqual(zero[15], ten[15]);
    }

    /// <summary>
    /// A column really is a column: reading the table contiguously would give a different key for
    /// every offset but zero.
    /// </summary>
    [Fact]
    public void TheKeyIsAColumnAndNotARun()
    {
        byte[] column1 = RpCryptRegist.Bright(ChiakiTarget.Ps5_1, RpCryptTables.Ps5Keys0, 1, 0);
        byte[] contiguous = RpCryptTables.Ps5Keys0.Slice(1, RpCryptRegist.KeySize).ToArray();

        Assert.NotEqual(contiguous, column1);
    }

    /// <summary>An offset past the table, and a target below ten, are refused.</summary>
    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RpCryptRegist.Bright(ChiakiTarget.Ps5_1, RpCryptTables.Ps5Keys0, RpCryptRegist.Columns, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RpCryptRegist.Bright(ChiakiTarget.Ps4_9, RpCryptTables.Ps4Keys0, 0, 0));
    }

    /// <summary>THE DRIFT CHECK. The two offsets and the strided read are still the C's.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(RpCryptRegist.ThePinOffsetsAreStill(core),
            "the PIN no longer lands at bright[0] before firmware 10 and bright[0xc] from it");
        Assert.True(RpCryptRegist.TheKeyIsStillAColumn(core),
            "the registration key is no longer read as a column of keys_0");
    }
}
