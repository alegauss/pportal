using System.Text;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP444, under PP29: the PIN exchange's payload layout, held against regist.c and against the C's
/// own output.
///
/// The crypto stays in C and is named: the PS5 path needs the general chiaki_rpcrypt_aeropause and
/// the shim exports no such thing. What is ported is the layout, which is where a silent defect
/// lives - a wrong offset produces a payload of the right length that no console accepts.
/// </summary>
public class RegistRequestPayloadTests(ITestOutputHelper output)
{
    private static string? Regist()
    {
        string? path = RegistRequestPayloadSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE DIFFERENTIAL. The C's real payload for a pre-10 target, against what this says its shape
    /// is: 0x1e0 of 'A' except the sixteen aeropause bytes at 0x11c, then the inner header.
    ///
    /// The inner header itself is encrypted in the C output, so its BYTES cannot be compared - its
    /// LENGTH can, and that is what the layout decides.
    /// </summary>
    [Fact]
    public void TheCsOwnPayloadHasTheShapeThisDescribes()
    {
        byte[] ambassador = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x30 + i))];
        const string OnlineId = "someone";

        byte[] payload;
        try
        {
            payload = RpCrypt.RegistRequestPayload(ChiakiTarget.Ps4_9, ambassador, OnlineId, 12345);
        }
        catch (DllNotFoundException)
        {
            return; // no shim beside the test runner
        }

        output.WriteLine($"the C returned {payload.Length} bytes");

        // The head is exactly the fill except where the aeropause lands.
        Assert.True(payload.Length > RegistRequestPayload.InnerHeaderOffset);

        for (int i = 0; i < RegistRequestPayload.InnerHeaderOffset; i++)
        {
            bool inAeropause = i >= RegistRequestPayload.Pre10AeropauseOffset
                && i < RegistRequestPayload.Pre10AeropauseOffset + RegistRequestPayload.AeropauseSize;

            if (!inAeropause)
            {
                Assert.True(
                    payload[i] == RegistRequestPayload.Fill,
                    $"byte 0x{i:x} is 0x{payload[i]:x2} and the fill is 0x{RegistRequestPayload.Fill:x2}");
            }
        }

        // And the inner header's length is the one this lays out.
        string inner = RegistRequestPayload.InnerHeader(ChiakiTarget.Ps4_9, OnlineId, null);
        Assert.Equal(
            RegistRequestPayload.InnerHeaderOffset + Encoding.ASCII.GetByteCount(inner),
            payload.Length);
    }

    /// <summary>
    /// And the aeropause region is NOT the fill, which is what makes the loop above mean something.
    ///
    /// PP271: a region that happened to be 'A' would let the exclusion above pass over nothing.
    /// </summary>
    [Fact]
    public void TheAeropauseRegionIsNotJustFill()
    {
        byte[] ambassador = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x30 + i))];

        byte[] payload;
        try
        {
            payload = RpCrypt.RegistRequestPayload(ChiakiTarget.Ps4_9, ambassador, "someone", 12345);
        }
        catch (DllNotFoundException)
        {
            return;
        }

        byte[] region = payload
            .Skip(RegistRequestPayload.Pre10AeropauseOffset)
            .Take(RegistRequestPayload.AeropauseSize)
            .ToArray();

        Assert.Contains(region, b => b != RegistRequestPayload.Fill);
    }

    /// <summary>
    /// The head this builds for a pre-10 target reproduces the C's, given the C's own aeropause.
    ///
    /// Taken out of the C's output rather than computed, because the computation is the part that is
    /// still C - which is exactly what this test is honest about.
    /// </summary>
    [Fact]
    public void TheHeadMatchesTheCsForPre10()
    {
        byte[] ambassador = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x50 + i))];

        byte[] payload;
        try
        {
            payload = RpCrypt.RegistRequestPayload(ChiakiTarget.Ps4_9, ambassador, "player", 999);
        }
        catch (DllNotFoundException)
        {
            return;
        }

        byte[] aeropause = payload
            .Skip(RegistRequestPayload.Pre10AeropauseOffset)
            .Take(RegistRequestPayload.AeropauseSize)
            .ToArray();

        byte[] head = RegistRequestPayload.Head(ChiakiTarget.Ps4_9, aeropause);

        Assert.Equal(
            payload.Take(RegistRequestPayload.InnerHeaderOffset).ToArray(),
            head);
    }

    /// <summary>
    /// The two targets write it to different places, which a single rule would have got wrong.
    /// </summary>
    [Fact]
    public void ThePathsPutTheAeropauseInDifferentPlaces()
    {
        byte[] aeropause = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xf0 - i))];

        byte[] pre10 = RegistRequestPayload.Head(ChiakiTarget.Ps4_9, aeropause);
        byte[] ps5 = RegistRequestPayload.Head(ChiakiTarget.Ps5_1, aeropause);

        // Contiguous at 0x11c on the old path.
        Assert.Equal(
            aeropause,
            pre10.Skip(RegistRequestPayload.Pre10AeropauseOffset).Take(16).ToArray());

        // Split on the new one, HIGH half at the LOWER offset.
        Assert.Equal(
            aeropause.Skip(8).Take(8).ToArray(),
            ps5.Skip(RegistRequestPayload.AeropauseHighOffset).Take(8).ToArray());

        Assert.Equal(
            aeropause.Take(8).ToArray(),
            ps5.Skip(RegistRequestPayload.AeropauseLowOffset).Take(8).ToArray());

        // And 0x11c is untouched on the new path, which is what tells the two apart.
        Assert.Equal(
            RegistRequestPayload.Fill,
            ps5[RegistRequestPayload.Pre10AeropauseOffset]);
    }

    /// <summary>
    /// The key offsets are derived from the fill and are 1 and 8 because 'A' is 0x41.
    /// </summary>
    [Fact]
    public void TheKeyOffsetsComeOutOfTheFill()
    {
        byte[] head = RegistRequestPayload.Head(ChiakiTarget.Ps5_1, new byte[16]);

        Assert.Equal(0x41 & 0x1F, RegistRequestPayload.Key0Offset(head));
        Assert.Equal(0x41 >> 3, RegistRequestPayload.Key1Offset(head));

        // A different fill would give different offsets, which is why 0x41 is not cosmetic.
        byte[] other = new byte[RegistRequestPayload.InnerHeaderOffset];
        other.AsSpan().Fill(0xff);

        Assert.NotEqual(RegistRequestPayload.Key0Offset(head), RegistRequestPayload.Key0Offset(other));
    }

    /// <summary>
    /// A PS5 discards an online id, because regist.c sets it to NULL before it chooses a form.
    /// </summary>
    [Fact]
    public void APs5DiscardsTheOnlineId()
    {
        string inner = RegistRequestPayload.InnerHeader(ChiakiTarget.Ps5_1, "someone", "YWJj");

        Assert.Contains("Np-AccountId: YWJj", inner, StringComparison.Ordinal);
        Assert.DoesNotContain("Np-Online-Id", inner, StringComparison.Ordinal);
        Assert.Contains(RegistRequestPayload.ClientType, inner, StringComparison.Ordinal);

        // A pre-10 target with an online id takes the other form, with a literal Windows.
        string old = RegistRequestPayload.InnerHeader(ChiakiTarget.Ps4_9, "someone", null);
        Assert.Contains("Client-Type: Windows", old, StringComparison.Ordinal);
        Assert.Contains("Np-Online-Id: someone", old, StringComparison.Ordinal);
    }

    /// <summary>Neither id is a refusal, not an empty header - regist.c returns INVALID_DATA.</summary>
    [Fact]
    public void NeitherIdIsARefusal()
        => Assert.Throws<ArgumentException>(
            () => RegistRequestPayload.InnerHeader(ChiakiTarget.Ps5_1, null, null));

    /// <summary>
    /// A cipher that changes the length is refused: regist.c encrypts IN PLACE, so the payload's
    /// length is the plaintext's and anything else is a port that has diverged.
    /// </summary>
    [Fact]
    public void ACipherThatResizesIsRefused()
        => Assert.Throws<InvalidOperationException>(() => RegistRequestPayload.Format(
            ChiakiTarget.Ps5_1, new byte[16], null, "YWJj", plain => [.. plain, 0x00]));

    /// <summary>Format lays the head and the sealed header end to end, at 0x1e0.</summary>
    [Fact]
    public void FormatPutsTheSealedHeaderAtTheOffset()
    {
        byte[] payload = RegistRequestPayload.Format(
            ChiakiTarget.Ps5_1, new byte[16], null, "YWJj", plain => [.. plain.Select(b => (byte)~b)]);

        string inner = RegistRequestPayload.InnerHeader(ChiakiTarget.Ps5_1, null, "YWJj");

        Assert.Equal(RegistRequestPayload.InnerHeaderOffset + inner.Length, payload.Length);
        Assert.Equal(RegistRequestPayload.Fill, payload[0]);

        // The sealed bytes are the cipher's, not the plaintext's.
        Assert.Equal(
            (byte)~Encoding.ASCII.GetBytes(inner)[0],
            payload[RegistRequestPayload.InnerHeaderOffset]);
    }

    /// <summary>Every constant above is still what regist.c says, read out of the file.</summary>
    [Fact]
    public void TheLayoutRulesAreStillTheCs()
    {
        if (Regist() is not { } text)
            return;

        Assert.True(RegistRequestPayloadSource.StillFillsTheHeadWithA(text));
        Assert.Equal(
            RegistRequestPayload.InnerHeaderOffset,
            RegistRequestPayloadSource.InnerHeaderOffsetIn(text));
        Assert.True(RegistRequestPayloadSource.KeyOffsetsAreStillDerivedFromTheFill(text));
        Assert.True(RegistRequestPayloadSource.AeropauseStillSplitsTheSameWay(text));
        Assert.True(RegistRequestPayloadSource.Pre10StillWritesContiguouslyAt011c(text));
    }

    /// <summary>PP272: and every source predicate answers false for an empty file.</summary>
    [Fact]
    public void AnEmptyFileSaysNothing()
    {
        Assert.False(RegistRequestPayloadSource.StillFillsTheHeadWithA(""));
        Assert.Null(RegistRequestPayloadSource.InnerHeaderOffsetIn(""));
        Assert.False(RegistRequestPayloadSource.KeyOffsetsAreStillDerivedFromTheFill(""));
        Assert.False(RegistRequestPayloadSource.AeropauseStillSplitsTheSameWay(""));
        Assert.False(RegistRequestPayloadSource.Pre10StillWritesContiguouslyAt011c(""));
    }

    /// <summary>PP400: and a commented memset is not the fill.</summary>
    [Fact]
    public void ACommentedFillIsNotTheFill()
        => Assert.False(RegistRequestPayloadSource.StillFillsTheHeadWithA(
            "\t// memset(buf, 'A', inner_header_off); // can be random\n"));
}
