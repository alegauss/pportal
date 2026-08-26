using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: three helpers that turn identifiers, each wrong in its own direction.
/// </summary>
public class HolepunchIdentifiersTests
{
    /// <summary>A sequence that repeats, standing in for a reseeded rand().</summary>
    private static Func<int, int> Draws(params int[] values)
    {
        int at = 0;
        return n => values[at++ % values.Length] % n;
    }

    /// <summary>An ordinary device identifier goes round.</summary>
    [Fact]
    public void ADeviceIdentifierRoundTrips()
    {
        byte[] bytes = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
        string hex = HolepunchIdentifiers.BytesToHex(bytes);

        Assert.Equal(HolepunchIdentifiers.DeviceUidTextLength, hex.Length);
        Assert.Equal(bytes, HolepunchIdentifiers.HexToBytes(hex, HolepunchIdentifiers.DeviceUidLength));
    }

    /// <summary>It is lowercase, which is what the URLs and the payloads all say.</summary>
    [Fact]
    public void TheHexIsLowercase()
        => Assert.Equal("00ff1a", HolepunchIdentifiers.BytesToHex([0x00, 0xFF, 0x1A]));

    /// <summary>
    /// THE CORE CLAMPS A LONG STRING AND REPORTS SUCCESS - a hundred and twenty-eight characters
    /// become the first thirty-two bytes and nothing says so. This refuses it.
    /// </summary>
    [Fact]
    public void AnOverlongIdentifierIsRefusedRatherThanClamped()
    {
        string tooLong = new('a', HolepunchIdentifiers.DeviceUidTextLength * 2);

        Assert.Null(HolepunchIdentifiers.HexToBytes(tooLong, HolepunchIdentifiers.DeviceUidLength));
    }

    /// <summary>
    /// AND A SHORT ONE IS ACCEPTED, leaving the bytes it did not reach exactly as it found them -
    /// which in the core is a stack local nobody zeroed. This refuses that too, because a device id
    /// ending in stack contents does not name a device.
    /// </summary>
    [Fact]
    public void AShortIdentifierIsRefusedRatherThanLeavingATail()
        => Assert.Null(HolepunchIdentifiers.HexToBytes("abcd", HolepunchIdentifiers.DeviceUidLength));

    /// <summary>
    /// An ODD length reads its last field off the end of the digits, so the core turns "abcde" into
    /// three bytes whose last is a single nibble. Refused here.
    /// </summary>
    [Fact]
    public void AnOddLengthIsRefused()
    {
        Assert.Null(HolepunchIdentifiers.HexToBytes("abcde", 3));

        // And the even neighbour it would have been mistaken for does decode.
        Assert.Equal<byte[]>([0xab, 0xcd, 0x0e], HolepunchIdentifiers.HexToBytes("abcd0e", 3)!);
    }

    /// <summary>Something that is not hex at all is refused rather than throwing.</summary>
    [Theory]
    [InlineData("zzzz")]
    [InlineData("    ")]
    [InlineData("")]
    public void RubbishIsRefused(string text)
        => Assert.Null(HolepunchIdentifiers.HexToBytes(text, 2));

    /// <summary>
    /// THE ENCODER GUARDS THE WRONG WAY. It tests len against max_len * 2 before writing
    /// len * 2 + 1 characters, so its bound permits four times the room it has.
    ///
    /// It is safe today for one reason: the single call site passes thirty-two bytes into
    /// sixty-five characters, which fits exactly.
    /// </summary>
    [Fact]
    public void TheEncodersBoundPermitsFourTimesItsBuffer()
    {
        // The one call site, which fits with nothing to spare.
        Assert.False(HolepunchIdentifiers.TheCoresEncoderWouldOverrun(
            HolepunchIdentifiers.DeviceUidLength, HolepunchIdentifiers.DeviceUidBuffer));

        Assert.Equal(
            HolepunchIdentifiers.DeviceUidBuffer,
            (HolepunchIdentifiers.DeviceUidLength * 2) + 1);

        // One byte more and it would run over, while the guard would still wave it through.
        Assert.True(HolepunchIdentifiers.TheCoresEncoderWouldOverrun(
            HolepunchIdentifiers.DeviceUidLength + 1, HolepunchIdentifiers.DeviceUidBuffer));

        // The guard only starts refusing at four times the buffer's worth.
        Assert.False(HolepunchIdentifiers.TheCoresEncoderWouldOverrun(
            (HolepunchIdentifiers.DeviceUidBuffer * 2) + 1, HolepunchIdentifiers.DeviceUidBuffer));
    }

    /// <summary>
    /// The UUID's shape: thirty-six characters, four dashes, a version and a variant.
    ///
    /// PP316: the dash count is read off <c>uuid</c>. What stood here was
    /// <c>Assert.Equal("--- -".Replace(" ", "").Length, 4)</c> - the length of a literal written in
    /// this file, against 4 - which passed without touching the UUID and would have passed against
    /// the empty string. The loop below is not the same claim: it checks the four declared
    /// positions carry a dash, and a FIFTH dash anywhere else passes it.
    /// </summary>
    [Fact]
    public void TheUuidHasTheShapeItShould()
    {
        string uuid = HolepunchIdentifiers.Uuid();

        Assert.Equal(36, uuid.Length);
        Assert.Equal(4, uuid.Count(c => c == '-'));

        foreach (int at in HolepunchIdentifiers.UuidDashes)
            Assert.Equal('-', uuid[at]);

        Assert.Equal('4', uuid[HolepunchIdentifiers.UuidVersionAt]);
        Assert.Contains(uuid[HolepunchIdentifiers.UuidVariantAt], "89ab");

        foreach (char c in uuid.Where(c => c != '-'))
            Assert.Contains(c, HolepunchIdentifiers.HexDigits);
    }

    /// <summary>And it is lowercase throughout, like every other identifier here.</summary>
    [Fact]
    public void TheUuidIsLowercase()
    {
        string uuid = HolepunchIdentifiers.Uuid();

        Assert.Equal(uuid.ToLowerInvariant(), uuid);
    }

    /// <summary>
    /// THE SESSION UUID IS NOT RANDOM IN THE CORE. rand() is reseeded from the wall clock on every
    /// call, so two sessions created within the same second get the SAME identifier.
    ///
    /// The collision is shown here rather than shipped: the same draws give the same UUID, which is
    /// exactly what a one-second clock seed guarantees.
    /// </summary>
    [Fact]
    public void TheSameDrawsGiveTheSameUuid()
    {
        int[] sequence = [3, 7, 11, 1, 15, 0, 9, 5];

        string first = HolepunchIdentifiers.Uuid(Draws(sequence));
        string second = HolepunchIdentifiers.Uuid(Draws(sequence));

        Assert.Equal(first, second);
        Assert.Equal(36, first.Length);
        Assert.Equal('4', first[HolepunchIdentifiers.UuidVersionAt]);
    }

    /// <summary>Where this port's own generator does not repeat itself.</summary>
    [Fact]
    public void ThisPortsUuidsDiffer()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 100; i++)
            Assert.True(seen.Add(HolepunchIdentifiers.Uuid()));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheHelpersRulesAreStillTheQtCores()
    {
        string? path = HolepunchIdentifiersSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(HolepunchIdentifiersSource.TheDecoderStillClamps(core), "clamped, not refused");
        Assert.True(HolepunchIdentifiersSource.TheDecoderStillReadsInPairs(core), "two at a time");
        Assert.True(
            HolepunchIdentifiersSource.TheDeviceIsStillAnUnzeroedLocal(core), "a stack local, unzeroed");
        // PP401: the decoder fills before it parses, so a short duid no longer carries stack
        // contents into an identifier. The clamp above stays as it is - that half is a policy with
        // a cost, and PP402 carries it.
        Assert.True(
            HolepunchIdentifiersSource.TheDecoderFillsItsDestination(core),
            "the decoder parses into a destination it has not filled");

        // PP399: inverted. PP33 recorded this guard as permitting four times its buffer and left
        // it, on the argument that the callers happened to fit. That is an argument about the
        // callers rather than about the guard, and nothing said the defect had to be reproduced -
        // so it was corrected, and this now watches for the old shape returning.
        Assert.False(
            HolepunchIdentifiersSource.TheEncoderStillGuardsTheWrongWay(core),
            "the encoder guards the wrong way again");
        // PP400: inverted, for PP399's reason. PP33 recorded that the session id came from
        // srand(time(NULL)) and rand(), noted the crypto generator sitting in the same file, and
        // left it. Nothing said the defect had to be reproduced, and two sessions a second apart
        // sharing the identifier the whole session is keyed by is not behaviour to carry across.
        Assert.False(
            HolepunchIdentifiersSource.TheUuidIsStillSeededFromTheClock(core),
            "the session id is drawn from the clock again");
        Assert.True(
            HolepunchIdentifiersSource.TheUuidComesFromTheCryptoGenerator(core),
            "the session id no longer comes from the crypto generator, a nibble per digit");
        Assert.True(
            HolepunchIdentifiersSource.AFailedDrawProducesNothing(core),
            "a generator that failed now yields an identifier rather than an empty string");
        Assert.True(
            HolepunchIdentifiersSource.ACryptoGeneratorIsStillInTheSameFile(core),
            "a crypto generator was available all along");
        Assert.True(HolepunchIdentifiersSource.TheUuidShapeIsStillTheSame(core), "the same shape");
    }
}
