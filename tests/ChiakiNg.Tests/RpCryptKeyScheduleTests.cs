using System.Text.RegularExpressions;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the PS4-10 and PS5 derivations, against the C, with the C's own tables fed in.
///
/// The four key tables are 3.5KB each and are not copied into the port - they are read out of
/// rpcrypt.c here, which makes this a test of the ALGORITHM against the C rather than of a
/// transcription. That is the right thing to check first: a wrong constant is a bug in a line
/// somebody wrote, and a wrong table byte is a bug in a paste nobody will read.
/// </summary>
public partial class RpCryptKeyScheduleTests(ITestOutputHelper output)
{
    /// <summary>Pulls one of the four tables out of the C source.</summary>
    private static byte[] Table(string core, string name)
    {
        Match declaration = Regex.Match(
            core, Regex.Escape($"static const uint8_t {name}[0x70 * 0x20] = {{") + "(.*?)};",
            RegexOptions.Singleline);

        Assert.True(declaration.Success, $"{name} is not in rpcrypt.c");

        byte[] bytes =
        [
            .. HexRegex().Matches(declaration.Groups[1].Value)
                .Select(m => Convert.ToByte(m.Value, 16)),
        ];

        Assert.Equal(RpCryptKeySchedule.TableSize, bytes.Length);
        return bytes;
    }

    private static string Core()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");
        return File.ReadAllText(impl);
    }

    public static TheoryData<ChiakiTarget> Targets() => [ChiakiTarget.Ps4_10, ChiakiTarget.Ps5_1];

    /// <summary>
    /// THE COMPARISON, over nonces that select different rows.
    ///
    /// The row comes from nonce[0] and nonce[7] shifted right three, so the nonces here are chosen
    /// to land on different rows for each - including one where the two bytes agree, which is the
    /// case a port that used nonce[0] twice would still pass.
    /// </summary>
    [Theory]
    [MemberData(nameof(Targets))]
    public void TheManagedScheduleIsTheCs(ChiakiTarget target)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        string core = Core();
        bool isPs5 = RpVersion.IsPs5(target);
        byte[] keysA = Table(core, isPs5 ? "keys_a_ps5" : "keys_a_ps4");
        byte[] keysB = Table(core, isPs5 ? "keys_b_ps5" : "keys_b_ps4");

        byte[][] nonces =
        [
            new byte[16],
            [.. Enumerable.Repeat((byte)0xff, 16)],
            [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 17))],

            // nonce[0] and nonce[7] in the same row - a port reusing nonce[0] passes this one.
            [0x08, 1, 2, 3, 4, 5, 6, 0x0f, 8, 9, 10, 11, 12, 13, 14, 15],

            // ...and in different rows, which is the one that catches it.
            [0x08, 1, 2, 3, 4, 5, 6, 0xf0, 8, 9, 10, 11, 12, 13, 14, 15],
        ];

        foreach (byte[] nonce in nonces)
        {
            byte[] morning = [.. nonce.Select(b => (byte)(b ^ 0x3c))];

            (byte[] nativeBright, byte[] nativeAmbassador) =
                RpCrypt.BrightAmbassador(target, nonce, morning);

            (byte[] bright, byte[] ambassador) =
                RpCryptKeySchedule.BrightAmbassador(target, keysA, keysB, nonce, morning);

            Assert.True(nativeAmbassador.SequenceEqual(ambassador),
                $"{target} nonce[0]={nonce[0]:x2} ambassador: C {Convert.ToHexString(nativeAmbassador)}, "
                    + $"managed {Convert.ToHexString(ambassador)}");
            Assert.True(nativeBright.SequenceEqual(bright),
                $"{target} nonce[7]={nonce[7]:x2} bright: C {Convert.ToHexString(nativeBright)}, "
                    + $"managed {Convert.ToHexString(bright)}");
        }

        output.WriteLine($"{target}: {nonces.Length} nonces agree");
    }

    /// <summary>
    /// The two selections really do use different nonce bytes - measured away from index 7.
    ///
    /// Written first as "change nonce[7], the ambassador stays the same", which is false and the
    /// port is right: every one of these loops reads nonce ELEMENTWISE as well as through the row,
    /// so ambassador[7] moves whatever the row does. What isolates the selection is the bytes that
    /// are NOT 7: changing nonce[7] across a row boundary must leave those alone in the ambassador,
    /// whose row comes from nonce[0], and disturb them in bright, whose row does not.
    /// </summary>
    [Fact]
    public void TheTwoRowsComeFromDifferentNonceBytes()
    {
        string core = Core();
        byte[] keysA = Table(core, "keys_a_ps5");
        byte[] keysB = Table(core, "keys_b_ps5");

        byte[] morning = new byte[16];
        byte[] first = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        byte[] second = [.. first];

        // 7 is row 0 and 0xf8 is row 31, so bright's key row changes and the ambassador's does not.
        second[7] = 0xf8;

        (byte[] brightA, byte[] ambassadorA) =
            RpCryptKeySchedule.BrightAmbassador(ChiakiTarget.Ps5_1, keysA, keysB, first, morning);
        (byte[] brightB, byte[] ambassadorB) =
            RpCryptKeySchedule.BrightAmbassador(ChiakiTarget.Ps5_1, keysA, keysB, second, morning);

        for (int i = 0; i < RpCryptKeySchedule.KeySize; i++)
        {
            if (i == 7)
                continue;

            Assert.True(
                ambassadorA[i] == ambassadorB[i],
                $"ambassador[{i}] moved when only nonce[7] changed, so its row is not from nonce[0]");
        }

        int disturbed = Enumerable.Range(0, RpCryptKeySchedule.KeySize)
            .Count(i => i != 7 && brightA[i] != brightB[i]);

        Assert.True(
            disturbed > 0,
            "bright was unchanged away from index 7, so its row is not from nonce[7]");
    }

    /// <summary>The row is the byte shifted right three, so eight nonces share each row.</summary>
    [Fact]
    public void EightNonceValuesSelectEachRow()
    {
        Assert.Equal(0, RpCryptKeySchedule.RowFor(0));
        Assert.Equal(0, RpCryptKeySchedule.RowFor(7));
        Assert.Equal(1, RpCryptKeySchedule.RowFor(8));
        Assert.Equal(RpCryptKeySchedule.Rows - 1, RpCryptKeySchedule.RowFor(0xff));
    }

    /// <summary>A target below PS4 10 belongs to the other path and is refused here.</summary>
    [Fact]
    public void BelowTenIsRefused()
    {
        string core = Core();
        byte[] table = Table(core, "keys_a_ps4");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RpCryptKeySchedule.BrightAmbassador(
                ChiakiTarget.Ps4_9, table, table, new byte[16], new byte[16]));
    }

    /// <summary>THE DRIFT CHECK. The selections, the constants and the odd XOR order are still the C's.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string core = Core();

        Assert.True(RpCryptKeySchedule.TheRowSelectionIsStill(core),
            "the key rows are no longer selected from nonce[0] and nonce[7]");
        Assert.True(RpCryptKeySchedule.TheConstantsAreStill(core),
            "the four constants are no longer 0x2d, 0x36, 0x18 and 0x21");
        Assert.True(RpCryptKeySchedule.ThePs4BrightStillXorsFirst(core),
            "the PS4 bright no longer XORs the key before the arithmetic, so it is now like the others");
    }

    [GeneratedRegex(@"0x[0-9a-fA-F]{2}")]
    private static partial Regex HexRegex();
}
