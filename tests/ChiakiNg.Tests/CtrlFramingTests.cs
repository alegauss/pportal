using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP341, under PP294: the control message header, and the length that did not fit its own sum.
/// </summary>
public class CtrlFramingTests
{
    /// <summary>
    /// The header is four bytes of length, two of type, two zero - all big-endian.
    ///
    /// The trailing pair is written rather than left, which is what makes a reused buffer safe.
    /// </summary>
    [Fact]
    public void TheHeaderIsLengthThenTypeThenTwoZeroes()
    {
        byte[] header = CtrlFraming.Header(0x8004, 0x1234);

        Assert.Equal(8, header.Length);
        Assert.Equal<byte[]>([0x00, 0x00, 0x12, 0x34], header[..4]);
        Assert.Equal<byte[]>([0x80, 0x04], header[4..6]);
        Assert.Equal<byte[]>([0x00, 0x00], header[6..]);
    }

    /// <summary>A message with no payload announces zero and keeps its type.</summary>
    [Fact]
    public void AMessageWithNoPayloadStillCarriesItsType()
    {
        byte[] header = CtrlFraming.Header(0x01fe, 0);

        Assert.Equal(0u, CtrlFraming.PayloadSizeOf(header));
        Assert.Equal(0x01fe, CtrlFraming.TypeOf(header));
    }

    /// <summary>And the two readers are the writer's inverse.</summary>
    [Theory]
    [InlineData((ushort)0x33, 16)]
    [InlineData((ushort)0xfe, 0)]
    [InlineData((ushort)0x8004, 4)]
    [InlineData((ushort)0x910, 4)]
    public void TheHeaderReadsBackAsWhatWasWritten(ushort type, int size)
    {
        byte[] header = CtrlFraming.Header(type, size);

        Assert.Equal(type, CtrlFraming.TypeOf(header));
        Assert.Equal((uint)size, CtrlFraming.PayloadSizeOf(header));
    }

    /// <summary>
    /// NO BUFFER IN ctrl.c IS SIZED BY A LENGTH NARROWER THAN THE SUM THAT FILLED IT.
    ///
    /// The rudp send declared its combined buffer as uint8_t from a size_t sum, so a payload of 248
    /// wrapped the length to zero while the copies used the real lengths - eight bytes of header
    /// past a buffer of nothing, then the payload after it. Reachable through
    /// chiaki_session_set_login_pin, which bounds its size_t nowhere.
    ///
    /// Checked as a shape, so the next one written that way is found without anybody remembering
    /// this one.
    /// </summary>
    [Fact]
    public void NoArrayIsSizedByALengthNarrowerThanItsSum()
    {
        string? path = CtrlFraming.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> narrow =
            CtrlFraming.ArraysSizedByANarrowLength(File.ReadAllText(path));

        Assert.True(
            narrow.Count == 0,
            "these lengths truncate the sum that filled them:\n  " + string.Join("\n  ", narrow));
    }

    /// <summary>
    /// And the reader finds one where there is one, so the check above means something.
    ///
    /// Written against the declaration as it was: a check that cannot fail says nothing.
    /// </summary>
    [Fact]
    public void TheReaderFindsANarrowLength()
    {
        const string asItWas = """
            	uint8_t buf_size = 8 + payload_size;
            	uint8_t buf[buf_size];
            	memcpy(buf, header, 8);
            """;

        string found = Assert.Single(CtrlFraming.ArraysSizedByANarrowLength(asItWas));

        Assert.Contains("uint8_t buf_size", found, StringComparison.Ordinal);
    }

    /// <summary>And ignores the same declaration once it is wide enough.</summary>
    [Fact]
    public void TheReaderIgnoresAWideLength()
    {
        const string fixedUp = """
            	size_t buf_size = 8 + payload_size;
            	uint8_t buf[buf_size];
            """;

        Assert.Empty(CtrlFraming.ArraysSizedByANarrowLength(fixedUp));
    }
}
