using System.Text.RegularExpressions;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the carried key tables, byte for byte against the ones in rpcrypt.c.
///
/// <see cref="RpCryptTables"/> is generated from that file, so this is the check that keeps the
/// generation honest - and the one that matters most, because a single wrong byte in 14336 produces
/// keys that are wrong only for the nonces selecting that row. Roughly one session in thirty-two,
/// failing with no pattern anybody could describe.
/// </summary>
public partial class RpCryptTablesTests(ITestOutputHelper output)
{
    private static string Core()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\rpcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\rpcrypt.c - this file is describing nothing");
        return File.ReadAllText(impl);
    }

    private static byte[] FromC(string core, string name, string dimension)
    {
        Match declaration = Regex.Match(
            core, Regex.Escape($"static const uint8_t {name}[{dimension}] = {{") + "(.*?)};",
            RegexOptions.Singleline);

        Assert.True(declaration.Success, $"{name} is not in rpcrypt.c");

        return
        [
            .. HexRegex().Matches(declaration.Groups[1].Value).Select(m => Convert.ToByte(m.Value, 16)),
        ];
    }

    public static TheoryData<string, string, int> Names() => new()
    {
        { "keys_a_ps4", "0x70 * 0x20", 3584 },
        { "keys_a_ps5", "0x70 * 0x20", 3584 },
        { "keys_b_ps4", "0x70 * 0x20", 3584 },
        { "keys_b_ps5", "0x70 * 0x20", 3584 },
        { "ps4_keys_0", "512", 512 },
        { "ps5_keys_0", "512", 512 },
    };

    private static ReadOnlySpan<byte> Carried(string name) => name switch
    {
        "keys_a_ps4" => RpCryptTables.KeysAPs4,
        "keys_a_ps5" => RpCryptTables.KeysAPs5,
        "keys_b_ps4" => RpCryptTables.KeysBPs4,
        "keys_b_ps5" => RpCryptTables.KeysBPs5,
        "ps4_keys_0" => RpCryptTables.Ps4Keys0,
        "ps5_keys_0" => RpCryptTables.Ps5Keys0,
        _ => default,
    };

    /// <summary>THE ASSERTION. Every byte, and the index of the first that is not.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheCarriedTableIsTheCs(string name, string dimension, int size)
    {
        byte[] fromC = FromC(Core(), name, dimension);
        ReadOnlySpan<byte> carried = Carried(name);

        Assert.Equal(size, fromC.Length);
        Assert.Equal(fromC.Length, carried.Length);

        for (int i = 0; i < fromC.Length; i++)
        {
            // Named rather than SequenceEqual: "they differ" is useless for 3584 bytes, and the row
            // is what a reader needs - row i/0x70 is the one whose nonces would have failed.
            Assert.True(
                fromC[i] == carried[i],
                $"{name}[{i}] (row {i / RpCryptKeySchedule.RowStride}): C {fromC[i]:x2}, carried {carried[i]:x2}");
        }

        output.WriteLine($"{name}: {fromC.Length} bytes identical");
    }

    /// <summary>
    /// The four are different from each other, which the comparisons above would not notice.
    ///
    /// A generator that wrote the same table four times would agree with the C on whichever one it
    /// happened to read and be caught only by this.
    /// </summary>
    [Fact]
    public void TheFourTablesAreFour()
    {
        byte[][] tables =
        [
            RpCryptTables.KeysAPs4.ToArray(), RpCryptTables.KeysAPs5.ToArray(),
            RpCryptTables.KeysBPs4.ToArray(), RpCryptTables.KeysBPs5.ToArray(),
        ];

        for (int i = 0; i < tables.Length; i++)
        {
            for (int j = i + 1; j < tables.Length; j++)
                Assert.False(tables[i].SequenceEqual(tables[j]), $"tables {i} and {j} are the same table");
        }
    }

    /// <summary>
    /// And the whole derivation works off the carried tables, not just off the C's.
    ///
    /// This is what PP26 was actually for: the managed side producing the console's keys with
    /// nothing read out of lib/ at run time.
    /// </summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_10)]
    [InlineData(ChiakiTarget.Ps5_1)]
    public void TheDerivationWorksOffTheCarriedTables(ChiakiTarget target)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        bool isPs5 = RpVersion.IsPs5(target);
        ReadOnlySpan<byte> keysA = isPs5 ? RpCryptTables.KeysAPs5 : RpCryptTables.KeysAPs4;
        ReadOnlySpan<byte> keysB = isPs5 ? RpCryptTables.KeysBPs5 : RpCryptTables.KeysBPs4;

        byte[] nonce = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 19))];
        byte[] morning = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 29))];

        (byte[] nativeBright, byte[] nativeAmbassador) = RpCrypt.BrightAmbassador(target, nonce, morning);
        (byte[] bright, byte[] ambassador) =
            RpCryptKeySchedule.BrightAmbassador(target, keysA, keysB, nonce, morning);

        Assert.Equal(nativeAmbassador, ambassador);
        Assert.Equal(nativeBright, bright);
    }

    [GeneratedRegex(@"0x[0-9a-fA-F]{2}")]
    private static partial Regex HexRegex();
}
