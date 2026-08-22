using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the PS4-before-10 derivations, against the C for a spread of inputs.
/// </summary>
public class RpCryptPs4Pre10Tests(ITestOutputHelper output)
{
    /// <summary>
    /// Inputs chosen to reach the wraparound, which is where the byte arithmetic can differ.
    ///
    /// All-zero underflows on the first subtraction at every index; all-0xff overflows on the one
    /// addition; the ascending pattern makes the "minus i" term visible in the output; and the real
    /// rp_key is a value a console actually produced.
    /// </summary>
    public static TheoryData<string, byte[]> Inputs() => new()
    {
        { "zero", new byte[16] },
        { "ff", [.. Enumerable.Repeat((byte)0xff, 16)] },
        { "ascending", [.. Enumerable.Range(0, 16).Select(i => (byte)i)] },
        { "descending", [.. Enumerable.Range(0, 16).Select(i => (byte)(0xff - i))] },
        {
            "real rp_key",
            [0x57, 0x49, 0xd7, 0x87, 0x8f, 0xce, 0xfd, 0x23,
             0x3f, 0x72, 0xfe, 0xf0, 0x7e, 0x30, 0xe7, 0x5a]
        },
    };

    /// <summary>
    /// THE COMPARISON. Bright and ambassador, both, against the C.
    /// </summary>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void TheManagedBrightAndAmbassadorAreTheCs(string name, byte[] nonce)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        // Morning differs from nonce so a port that confused the two is caught: bright reads both.
        byte[] morning = [.. nonce.Select(b => (byte)(b ^ 0x5a))];

        (byte[] nativeBright, byte[] nativeAmbassador) =
            RpCrypt.BrightAmbassador(ChiakiTarget.Ps4_8, nonce, morning);

        (byte[] bright, byte[] ambassador) = RpCryptPs4Pre10.BrightAmbassador(nonce, morning);

        Assert.True(nativeAmbassador.SequenceEqual(ambassador),
            $"{name} ambassador: C {Convert.ToHexString(nativeAmbassador)}, managed {Convert.ToHexString(ambassador)}");
        Assert.True(nativeBright.SequenceEqual(bright),
            $"{name} bright: C {Convert.ToHexString(nativeBright)}, managed {Convert.ToHexString(bright)}");

        output.WriteLine($"{name}: ambassador {Convert.ToHexString(ambassador)}");
    }

    /// <summary>And the aeropause, derived from whatever ambassador the C produced.</summary>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void TheManagedAeropauseIsTheCs(string name, byte[] ambassador)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] fromC = RpCrypt.AeropausePs4Pre10(ambassador);
        byte[] managed = RpCryptPs4Pre10.Aeropause(ambassador);

        Assert.True(fromC.SequenceEqual(managed),
            $"{name}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");
    }

    /// <summary>
    /// Firmware 9 derives the same way as 8, and 10 does not.
    ///
    /// The switch sends PS4_8 and PS4_9 to this path and everything else to the AES one, so a port
    /// that applied these three loops to a PS4 10 would produce keys with the right shape and the
    /// wrong values.
    /// </summary>
    [Fact]
    public void OnlyFirmwareBelowTenTakesThisPath()
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] nonce = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 7))];
        byte[] morning = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 13))];

        (byte[] _, byte[] eight) = RpCrypt.BrightAmbassador(ChiakiTarget.Ps4_8, nonce, morning);
        (byte[] _, byte[] nine) = RpCrypt.BrightAmbassador(ChiakiTarget.Ps4_9, nonce, morning);
        (byte[] _, byte[] ten) = RpCrypt.BrightAmbassador(ChiakiTarget.Ps4_10, nonce, morning);

        Assert.Equal(eight, nine);
        Assert.Equal(eight, RpCryptPs4Pre10.BrightAmbassador(nonce, morning).Ambassador);

        // ...and 10 goes somewhere else entirely.
        Assert.NotEqual(eight, ten);
    }

    /// <summary>
    /// The three constants are different from each other, which the comparisons above prove only
    /// implicitly.
    ///
    /// Copying one loop and editing it is how this gets ported wrong, and the failure is silent -
    /// so the distinctness is asserted rather than left to be inferred from four agreeing vectors.
    /// </summary>
    [Fact]
    public void TheThreeLoopsAreNotTheSameLoop()
    {
        byte[] input = new byte[16];

        (byte[] bright, byte[] ambassador) = RpCryptPs4Pre10.BrightAmbassador(input, input);
        byte[] aeropause = RpCryptPs4Pre10.Aeropause(input);

        Assert.NotEqual(ambassador, aeropause);
        Assert.NotEqual(ambassador, bright);
        Assert.NotEqual(bright, aeropause);
    }

    /// <summary>A key of the wrong length is refused rather than read past.</summary>
    [Fact]
    public void AShortKeyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RpCryptPs4Pre10.Aeropause(new byte[8]));
        Assert.Throws<ArgumentException>(() => RpCryptPs4Pre10.BrightAmbassador(new byte[16], new byte[4]));
    }

    /// <summary>THE DRIFT CHECK. The constants and the tables are still the C's.</summary>
    [Fact]
    public void TheCStillUsesTheseConstants()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(RpCryptPs4Pre10.TheConstantsAreStill(core),
            "the three derivation constants are no longer 0x27 minus, 0x34 plus and 0x29 minus");
        Assert.True(RpCryptPs4Pre10.TheTablesAreStill(core),
            "echo_a or echo_b is no longer the table this port carries");
    }
}
