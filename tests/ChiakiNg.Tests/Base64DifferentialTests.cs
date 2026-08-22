using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP269: reading it, then running it.
///
/// PP261 said what the encoder does when it runs out of room, by reading it. These call it.
///
/// <see cref="ATruncatedEncodeLeavesNoTerminator"/> is the one that turns that reading into a
/// measurement, and <see cref="ThePortsArithmeticAgreesWithTheEncoder"/> makes the encoder the thing
/// that answers how much room a conversion needs.
/// </summary>
public class Base64DifferentialTests
{
    /// <summary>A filler nothing produces, so anything left is what the encoder did not touch.</summary>
    private const byte Untouched = 0xAA;

    private static byte[] Filled(int size)
    {
        byte[] buffer = new byte[size];
        Array.Fill(buffer, Untouched);
        return buffer;
    }

    /// <summary>
    /// THE MEASUREMENT. A conversion with one byte too few comes back with an error and no
    /// terminator anywhere in it - which is what PP261 read and could not run.
    /// </summary>
    [Fact]
    public void ATruncatedEncodeLeavesNoTerminator()
    {
        byte[] source = new byte[16];
        Array.Fill(source, (byte)0x41);

        // One short of what sixteen bytes need.
        byte[] destination = Filled(24);

        int error = NativeBase64.Encode(source, destination);

        Assert.Equal(NativeBase64.BufferTooSmall, error);
        Assert.False(
            NativeBase64.IsTerminated(destination),
            "the encoder terminated a truncated buffer, so PP261's reading no longer holds");

        // And it did write into it, so what a percent-s would run past is real rather than the
        // filler.
        Assert.Contains(destination, b => b != Untouched);
    }

    /// <summary>A conversion that fits terminates, which is the other half of the same question.</summary>
    [Fact]
    public void AConversionThatFitsTerminates()
    {
        byte[] source = new byte[16];
        byte[] destination = Filled(25);

        Assert.Equal(NativeBase64.Success, NativeBase64.Encode(source, destination));
        Assert.True(NativeBase64.IsTerminated(destination));
    }

    /// <summary>
    /// PP261's arithmetic, answered by the encoder rather than by the port. Each of its buffers is
    /// exactly enough and one byte less is not.
    /// </summary>
    [Theory]
    [InlineData(16, 25)]
    [InlineData(20, 29)]
    public void ThePortsArithmeticAgreesWithTheEncoder(int sourceBytes, int exactly)
    {
        byte[] source = new byte[sourceBytes];

        Assert.Equal(exactly, RequestPrinter.EncodedLength(sourceBytes) + 1);

        Assert.Equal(NativeBase64.Success, NativeBase64.Encode(source, Filled(exactly)));
        Assert.Equal(NativeBase64.BufferTooSmall, NativeBase64.Encode(source, Filled(exactly - 1)));
    }

    /// <summary>
    /// And the two buffers PP261 named are the ones the printer actually uses, so the branch it
    /// called unreachable is unreachable against the real encoder too.
    /// </summary>
    [Fact]
    public void TheBranchIsUnreachableAgainstTheRealEncoder()
    {
        foreach (PrintBuffer buffer in RequestPrinter.Buffers.Where(b => b.Name != "mac_addr"))
        {
            Assert.Equal(
                NativeBase64.Success,
                NativeBase64.Encode(new byte[buffer.SourceBytes], Filled(buffer.Size)));

            Assert.False(RequestPrinter.CanFail(buffer));
        }
    }

    /// <summary>
    /// The encoder's output is base64 - compared against the runtime's, which is a second
    /// implementation and the whole point of a differential.
    /// </summary>
    [Fact]
    public void ItProducesWhatTheRuntimeProduces()
    {
        byte[] source = Encoding.ASCII.GetBytes("chiaki-ng port");
        byte[] destination = Filled(RequestPrinter.EncodedLength(source.Length) + 1);

        Assert.Equal(NativeBase64.Success, NativeBase64.Encode(source, destination));

        int end = Array.IndexOf(destination, (byte)0);
        string produced = Encoding.ASCII.GetString(destination, 0, end);

        Assert.Equal(Convert.ToBase64String(source), produced);
    }

    /// <summary>An empty destination is refused rather than written to.</summary>
    [Fact]
    public void AnEmptyDestinationIsRefused()
    {
        byte[] destination = Filled(1);

        // One byte holds the terminator and nothing else, so anything at all is too small.
        Assert.Equal(NativeBase64.BufferTooSmall, NativeBase64.Encode(new byte[1], destination));
    }

    /// <summary>
    /// The error code this file compares against is a POSITION in an enum, so it is counted from
    /// the header rather than trusted - which is what stops a member inserted above it turning
    /// every comparison here into a different question that still passes.
    /// </summary>
    [Fact]
    public void TheErrorCodeIsWhereTheHeaderPutsIt()
    {
        string? header = NativeBase64Source.Locate();
        if (header is null)
            return;

        Assert.True(
            NativeBase64Source.TheErrorCodeIsStillTwelve(File.ReadAllText(header)),
            "the enum moved, so the value this file compares against is no longer that error");
    }
}
