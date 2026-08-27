using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP442, under PP294: the ctrl participant's payloads, held against the C they were copied from.
///
/// PP441 found that four of the seven are for types no console was watched exchanging, so for those
/// four this is the only oracle there can be. The senkusha participant has had a capture check since
/// PP421; this one had nothing.
/// </summary>
public class CtrlPayloadSourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE. Every payload still matches the array in ctrl.c it was read off.
    /// </summary>
    [Fact]
    public void EveryPayloadStillMatchesItsArray()
    {
        if (CtrlPayloadSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        // PP271: a reader that found no arrays would report nothing and mean nothing. Every one of
        // the five is read before any verdict is taken from the sweep.
        foreach ((ushort type, string name) in CtrlPayloadSource.ArrayFor)
        {
            byte[] managed = CtrlExchangeParticipant.Payloads[type];
            byte[]? declared = CtrlPayloadSource.Declared(source, name, managed.Length);

            Assert.True(
                declared is not null,
                $"uint8_t {name}[{managed.Length}] was not found in ctrl.c - the reader is not working");

            output.WriteLine($"0x{type:x} {name}: {CtrlPayloadSource.Render(declared!)}");
        }

        IReadOnlyList<string> apart = CtrlPayloadSource.Disagreements(source);

        Assert.True(
            apart.Count == 0,
            "the participant would send bytes ctrl.c no longer declares:\n  "
                + string.Join("\n  ", apart));
    }

    /// <summary>
    /// PP383's case, asserted on the real file: fifteen initialisers for a sixteen-byte array, so
    /// the last byte is an implicit zero.
    /// </summary>
    [Fact]
    public void FifteenInitialisersFillSixteenBytes()
    {
        if (CtrlPayloadSource.Locate() is not { } path)
            return;

        byte[]? declared = CtrlPayloadSource.Declared(File.ReadAllText(path), "connect", 16);

        Assert.NotNull(declared);
        Assert.Equal(16, declared.Length);

        // The fifteenth initialiser is 0x00 and so is the implicit sixteenth, which is why a reader
        // comparing counts would have called this a disagreement.
        Assert.Equal(0x00, declared[15]);
        Assert.Equal(0xa0, declared[0]);
    }

    /// <summary>
    /// ctrl.c has two arrays called connect - two bytes at 901 and sixteen at 1074 - so the size is
    /// part of what identifies one.
    /// </summary>
    [Fact]
    public void TheSizeTellsTwoArraysOfTheSameNameApart()
    {
        const string Source = """
            	uint8_t connect[2] = {0x00, 0x00};
            		const uint8_t connect[0x10] = { 0xa0, 0xab, 0x51, 0xbd, 0xd1, 0x7e, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00 };
            """;

        byte[]? two = CtrlPayloadSource.Declared(Source, "connect", 2);
        byte[]? sixteen = CtrlPayloadSource.Declared(Source, "connect", 16);

        Assert.Equal([0x00, 0x00], two);
        Assert.NotNull(sixteen);
        Assert.Equal(0xa0, sixteen[0]);
        Assert.Equal(16, sixteen.Length);
    }

    /// <summary>
    /// And two things called enable: the scalar at 1081 is not the array at 1067.
    ///
    /// This is why the subscript is part of the match rather than the name alone - a reader that took
    /// the first `uint8_t enable` would have read a scalar as a three-byte payload.
    /// </summary>
    [Fact]
    public void AScalarOfTheSameNameIsNotAnArray()
    {
        const string Source = """
            		uint8_t enable = 1;
            		const uint8_t enable[3] = { 0x00, 0x40, 0x00 };
            """;

        Assert.Equal([0x00, 0x40, 0x00], CtrlPayloadSource.Declared(Source, "enable", 3));

        // And the scalar is not readable as a one-byte array, because it is not one.
        Assert.Null(CtrlPayloadSource.Declared(Source, "enable", 1));
    }

    /// <summary>A decimal initialiser is read as a byte: the toggle is {0, 1, 1, 89}.</summary>
    [Fact]
    public void DecimalInitialisersAreRead()
    {
        Assert.Equal(
            [0x00, 0x01, 0x01, 0x59],
            CtrlPayloadSource.Declared("\tuint8_t toggle[0x4] = {0, 1, 1, 89};\n", "toggle", 4));
    }

    /// <summary>A value that moved is reported, with both spellings so the diff is legible.</summary>
    [Fact]
    public void AChangedValueIsReported()
    {
        // The real 0x40 becomes 0x41: one byte, in a payload no recording covers.
        const string Source = """
            	uint8_t toggle[0x4] = {0, 1, 1, 89};
            	uint8_t display[0x4] = { 0x00, 0x00, 0x00, 0x00 };
            		const uint8_t enable[3] = { 0x00, 0x41, 0x00 };
            		uint8_t signature[0x10] = { 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x05, 0xAE, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            		const uint8_t connect[0x10] = { 0xa0, 0xab, 0x51, 0xbd, 0xd1, 0x7e, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00 };
            """;

        string apart = Assert.Single(CtrlPayloadSource.Disagreements(Source));

        Assert.Contains("00-41-00", apart, StringComparison.Ordinal);
        Assert.Contains("00-40-00", apart, StringComparison.Ordinal);
    }

    /// <summary>An array that is gone is reported as gone, not as a disagreement about bytes.</summary>
    [Fact]
    public void AMissingArrayIsReportedAsMissing()
    {
        IReadOnlyList<string> apart = CtrlPayloadSource.Disagreements(
            "\tuint8_t toggle[0x4] = {0, 1, 1, 89};\n");

        Assert.Contains(apart, line => line.Contains("declares no uint8_t display", StringComparison.Ordinal));
        Assert.DoesNotContain(apart, line => line.Contains("toggle", StringComparison.Ordinal));
    }

    /// <summary>PP400: a declaration inside a comment is not one.</summary>
    [Fact]
    public void ACommentedDeclarationIsNotRead()
    {
        Assert.Null(CtrlPayloadSource.Declared(
            "\t// uint8_t toggle[0x4] = {0, 1, 1, 89};\n", "toggle", 4));

        Assert.Null(CtrlPayloadSource.Declared(
            "/* uint8_t display[0x4] = { 0x01, 0x02, 0x03, 0x04 }; */\n", "display", 4));
    }

    /// <summary>PP272: and an empty file declares nothing, for any name or size.</summary>
    [Fact]
    public void AnEmptyFileDeclaresNothing()
    {
        Assert.Null(CtrlPayloadSource.Declared("", "toggle", 4));
        Assert.Null(CtrlPayloadSource.Declared("", "connect", 16));

        // Every named array then reads as missing, which is the honest answer and not silence.
        Assert.Equal(CtrlPayloadSource.ArrayFor.Count, CtrlPayloadSource.Disagreements("").Count);
    }

    /// <summary>A size of zero or less is not a question this can answer.</summary>
    [Fact]
    public void ANonPositiveSizeIsNoAnswer()
    {
        Assert.Null(CtrlPayloadSource.Declared("\tuint8_t toggle[0x4] = {0, 1, 1, 89};\n", "toggle", 0));
        Assert.Null(CtrlPayloadSource.Declared("\tuint8_t toggle[0x4] = {0, 1, 1, 89};\n", "toggle", -1));
    }

    /// <summary>
    /// The four PP441 found unwitnessed are the ones this is for, named so the reason survives.
    /// </summary>
    [Theory]
    [InlineData((ushort)0x13)]
    [InlineData((ushort)0xd)]
    [InlineData((ushort)0x11)]
    public void TheUnwitnessedOnesAreCovered(ushort type)
    {
        Assert.Contains(type, CtrlPayloadSource.ArrayFor);
    }
}
