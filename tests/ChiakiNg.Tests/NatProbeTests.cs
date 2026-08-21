using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the eighty-eight byte packets the candidate race is made of.
/// </summary>
public class NatProbeTests
{
    private const ushort LocalSid = 0x1234;
    private const ushort ConsoleSid = 0x5678;

    private static readonly byte[] RequestId = [0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    private static byte[] Id(byte first)
        => [.. Enumerable.Range(0, NatProbe.HashedIdLength).Select(i => (byte)(first + i))];

    private static byte[] Request()
        => NatProbe.BuildRequest(Id(0x10), Id(0x40), LocalSid, ConsoleSid, RequestId);

    private static byte[] Response(string address = "203.0.113.9", ushort port = 9295)
    {
        byte[]? response = NatProbe.BuildResponse(
            Request(), Id(0x10), Id(0x40), LocalSid, ConsoleSid, address, port);

        Assert.NotNull(response);
        return response;
    }

    /// <summary>A request is the type, two ids, two session ids and five random bytes.</summary>
    [Fact]
    public void ARequestIsEightyEightBytesInThatOrder()
    {
        byte[] request = Request();

        Assert.Equal(88, request.Length);
        Assert.Equal(CandidateRace.RequestType, BinaryPrimitives.ReadUInt32BigEndian(request));
        Assert.Equal(Id(0x10), request[0x04..0x18]);
        Assert.Equal(Id(0x40), request[0x24..0x38]);
        Assert.Equal(LocalSid, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0x44)));
        Assert.Equal(ConsoleSid, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0x46)));
        Assert.Equal(RequestId, NatProbe.RequestIdOf(request));
    }

    /// <summary>
    /// TWO FIFTHS OF IT IS NEVER WRITTEN. Thirty-five of the eighty-eight bytes are the zeros the
    /// buffer was cleared to - twelve after each twenty-byte id sitting in a thirty-two byte slot,
    /// three before the request id, and eight at the end. Fifty-three bytes carry anything.
    ///
    /// A port that packed the fields would produce a shorter packet no console would answer, and
    /// would look tidier doing it.
    /// </summary>
    [Fact]
    public void ThirtyFiveOfTheEightyEightBytesAreNeverWritten()
    {
        byte[] request = Request();

        int padding = 0;
        foreach ((int at, int count) in NatProbe.Padding)
        {
            Assert.All(request[at..(at + count)], b => Assert.Equal(0, b));
            padding += count;
        }

        Assert.Equal(35, padding);
        Assert.Equal(NatProbe.Length - 35, 4 + (2 * NatProbe.HashedIdLength) + 2 + 2 + NatProbe.RequestIdLength);
        Assert.Equal(12, NatProbe.HashedIdSlot - NatProbe.HashedIdLength);
    }

    /// <summary>A response is the same shape, with the other type at the front.</summary>
    [Fact]
    public void AResponseIsTheSameShapeWithTheOtherType()
    {
        byte[] response = Response();

        Assert.Equal(88, response.Length);
        Assert.Equal(CandidateRace.ResponseType, BinaryPrimitives.ReadUInt32BigEndian(response));
        Assert.Equal(Id(0x10), response[0x04..0x18]);
    }

    /// <summary>
    /// THE MATCHING IS FIVE BYTES, ECHOED VERBATIM. The response copies them across without reading
    /// them - they are the whole of what PP197 checks a reply against, and the core's own comment
    /// beside them asks what they are.
    /// </summary>
    [Fact]
    public void TheFiveMatchingBytesAreEchoedWithoutBeingRead()
    {
        byte[] request = NatProbe.BuildRequest(
            Id(0x10), Id(0x40), LocalSid, ConsoleSid, [0xFF, 0x00, 0xFF, 0x00, 0xFF]);

        byte[]? response = NatProbe.BuildResponse(
            request, Id(0x10), Id(0x40), LocalSid, ConsoleSid, "203.0.113.9", 9295);

        Assert.NotNull(response);
        Assert.Equal(NatProbe.RequestIdOf(request), NatProbe.RequestIdOf(response));
    }

    /// <summary>
    /// THE TAIL HIDES THE CONSOLE'S ADDRESS BEHIND THE SESSION IDS - sid_local, sid_console,
    /// sid_local, XORed with four address bytes and two port bytes.
    ///
    /// The local id appears TWICE, masking the address's first half and the port, with the same key
    /// in the same packet - so anyone who can guess one can read the other.
    /// </summary>
    [Fact]
    public void TheTailIsTheAddressAndPortBehindTheSessionIds()
    {
        byte[] response = Response("203.0.113.9", 9295);

        // Unmask by writing the same three values again and XORing back.
        var expected = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), LocalSid);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), ConsoleSid);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), LocalSid);

        var recovered = new byte[6];
        for (int i = 0; i < 6; i++)
            recovered[i] = (byte)(response[0x50 + i] ^ expected[i]);

        Assert.Equal<byte[]>([203, 0, 113, 9], recovered[..4]);
        Assert.Equal(9295, BinaryPrimitives.ReadUInt16BigEndian(recovered.AsSpan(4)));

        // The same key really is used twice.
        Assert.Equal(
            BinaryPrimitives.ReadUInt16BigEndian(expected.AsSpan(0)),
            BinaryPrimitives.ReadUInt16BigEndian(expected.AsSpan(4)));
    }

    /// <summary>
    /// AND THE TAIL CANNOT CARRY AN IPv6 ADDRESS. The XOR takes four bytes whatever the family, so
    /// a sixteen-byte address is masked by its first four and the other twelve never leave.
    /// </summary>
    [Fact]
    public void OnlyFourBytesOfAnAddressEverLeave()
    {
        byte[] four = Response("203.0.113.9", 9295);
        byte[] sixteen = Response("2001:db8::1", 9295);

        Assert.Equal(4, NatProbe.MaskedAddressLength);

        // Two addresses agreeing on their first four bytes produce the same tail, whatever follows.
        byte[] a = Response("2001:db8::1", 9295);
        byte[] b = Response("2001:db8::9999", 9295);

        Assert.Equal(a[0x50..0x56], b[0x50..0x56]);
        Assert.NotEqual(four[0x50..0x56], sixteen[0x50..0x56]);
    }

    /// <summary>
    /// THE FAMILY IS CHOSEN BY LOOKING FOR A DOT, not by parsing - so a v4-mapped v6 literal goes
    /// to the v4 parser and is refused, though it is a perfectly good address.
    /// </summary>
    [Fact]
    public void AMappedLiteralIsRefusedBecauseItHasADotInIt()
    {
        Assert.Null(NatProbe.ParseAddress("::ffff:203.0.113.9"));

        // The same address written without a dot is accepted.
        Assert.NotNull(NatProbe.ParseAddress("::ffff:cb00:7109"));
    }

    /// <summary>Ordinary addresses of both families parse.</summary>
    [Theory]
    [InlineData("203.0.113.9", 4)]
    [InlineData("10.0.0.4", 4)]
    [InlineData("2001:db8::1", 16)]
    [InlineData("::1", 16)]
    public void OrdinaryAddressesParseToTheirBytes(string text, int length)
        => Assert.Equal(length, NatProbe.ParseAddress(text)?.Length);

    /// <summary>And rubbish is refused rather than throwing.</summary>
    [Theory]
    [InlineData("not an address")]
    [InlineData("999.1.1.1")]
    [InlineData("")]
    public void RubbishIsRefused(string text)
        => Assert.Null(NatProbe.ParseAddress(text));

    /// <summary>A response to an address that will not parse is not sent at all.</summary>
    [Fact]
    public void AnUnparseableCandidateGetsNoResponse()
        => Assert.Null(NatProbe.BuildResponse(
            Request(), Id(0x10), Id(0x40), LocalSid, ConsoleSid, "not an address", 1));

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheProbesRulesAreStillTheQtCores()
    {
        string? path = NatProbeSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(NatProbeSource.TheRequestIsStillThoseSixWrites(core), "six writes, in order");
        Assert.True(NatProbeSource.TheIdsAreStillShortOfTheirSlots(core), "twenty in thirty-two");
        Assert.True(NatProbeSource.TheResponseStillEchoesTheFiveBytes(core), "five bytes echoed");
        Assert.True(NatProbeSource.TheTailIsStillMaskedBySessionIds(core), "masked by the ids");
        Assert.True(NatProbeSource.TheFamilyIsStillChosenByADot(core), "a dot decides the family");
    }
}
