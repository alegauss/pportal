using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP445, under PP29: the PS5 registration payload, derived and placed managed-side and held against
/// the C's own bytes.
///
/// PP444 could place the sixteen aeropause bytes and not derive them: the shim exported eleven rpcrypt
/// entry points and chiaki_rpcrypt_aeropause was not among them. So the half that could be checked
/// end to end was the PS4-pre10 one, and the half this project's console uses had no oracle.
/// </summary>
public class RegistPayloadPs5Tests(ITestOutputHelper output)
{
    private static byte[] Ambassador() =>
        [.. Enumerable.Range(0, 16).Select(i => (byte)(0x11 * (i + 1)))];

    private static byte[] AccountId() => [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>
    /// THE WHOLE THING. The managed head, built from a managed-derived aeropause, is the C's head
    /// byte for byte - and the payload's length is the one the layout computes.
    ///
    /// This is what PP444 declared it could not do. Nothing here reads the C's output to get the
    /// aeropause: it is derived from the ambassador and the two offsets the fill gives.
    /// </summary>
    [Fact]
    public void TheManagedPs5HeadIsTheCs()
    {
        byte[] ambassador = Ambassador();
        byte[] account = AccountId();
        const uint Pin = 4321;

        byte[] fromC;
        try
        {
            fromC = RpCrypt.RegistRequestPayload(ChiakiTarget.Ps5_1, ambassador, null, account, Pin);
        }
        catch (DllNotFoundException)
        {
            return; // no shim beside the runner
        }

        output.WriteLine($"the C returned {fromC.Length} bytes");

        // The offsets come off the head this side builds, exactly as regist.c reads them off its own.
        byte[] fill = new byte[RegistRequestPayload.InnerHeaderOffset];
        fill.AsSpan().Fill(RegistRequestPayload.Fill);

        int key0 = RegistRequestPayload.Key0Offset(fill);
        int key1 = RegistRequestPayload.Key1Offset(fill);
        output.WriteLine($"key offsets from the fill: {key0} and {key1}");

        // key0 is read and NOT passed: it feeds bright, which the aeropause never reads. Asserted
        // below rather than left as a comment.
        Assert.Equal(1, key0);

        byte[] aeropause = RpCrypt.Aeropause(ChiakiTarget.Ps5_1, ambassador, key1);
        byte[] head = RegistRequestPayload.Head(ChiakiTarget.Ps5_1, aeropause);

        Assert.Equal(fromC.Take(RegistRequestPayload.InnerHeaderOffset).ToArray(), head);

        // And the length, which is the inner header's - the account-id form.
        string inner = RegistRequestPayload.InnerHeader(
            ChiakiTarget.Ps5_1, null, Convert.ToBase64String(account));

        Assert.Equal(RegistRequestPayload.InnerHeaderOffset + inner.Length, fromC.Length);
    }

    /// <summary>
    /// And the aeropause is not zeroes, which is what makes the comparison above mean something.
    ///
    /// PP271: a derivation that returned an empty buffer would match a head whose two regions were
    /// never written, and both would be 'A'.
    /// </summary>
    [Fact]
    public void TheDerivedAeropauseIsRealBytes()
    {
        byte[] aeropause;
        try
        {
            aeropause = RpCrypt.Aeropause(ChiakiTarget.Ps5_1, Ambassador(), 8);
        }
        catch (DllNotFoundException)
        {
            return;
        }

        output.WriteLine("aeropause: " + Convert.ToHexString(aeropause));

        Assert.Equal(16, aeropause.Length);
        Assert.Contains(aeropause, b => b != 0);
        Assert.Contains(aeropause, b => b != RegistRequestPayload.Fill);
    }

    /// <summary>
    /// THE OFFSET CHANGES THE ANSWER, which is PP444's claim about the fill made checkable: if
    /// key_1_off did nothing, reproducing 0x41 would not matter and "can be random" would be a
    /// remark about padding.
    ///
    /// And it is the ONLY input besides the ambassador. The first version of this test asserted that
    /// key_0_off and the pin changed it too, because regist.c reaches the derivation through
    /// init_regist, which takes both. It failed - init_regist copies the ambassador through untouched
    /// and spends both on `bright`, which the aeropause never reads. That failure is why the export
    /// takes neither.
    /// </summary>
    [Fact]
    public void TheOffsetChangesTheDerivationAndIsTheOnlyThingThatDoes()
    {
        byte[] ambassador = Ambassador();

        byte[] baseline;
        try
        {
            baseline = RpCrypt.Aeropause(ChiakiTarget.Ps5_1, ambassador, 8);
        }
        catch (DllNotFoundException)
        {
            return;
        }

        Assert.NotEqual(baseline, RpCrypt.Aeropause(ChiakiTarget.Ps5_1, ambassador, 9));

        // The ambassador is the other input, and it is not ignored either.
        byte[] other = [.. ambassador.Select(b => (byte)(b ^ 0xff))];
        Assert.NotEqual(baseline, RpCrypt.Aeropause(ChiakiTarget.Ps5_1, other, 8));

        // A PS4 from 10 reads a different table, so the same inputs give a different answer.
        Assert.NotEqual(baseline, RpCrypt.Aeropause(ChiakiTarget.Ps4_10, ambassador, 8));
    }

    /// <summary>
    /// The offset is bounded HERE, because the C bounds the target and not the offset.
    ///
    /// chiaki_rpcrypt_aeropause indexes keys_1[i * 0x20 + key_1_off] with i to 15 over 512 bytes, so
    /// 0x20 reads one past the end. regist.c can only pass buf[0] >> 3, which is 0..31 - the path
    /// exists only because this entry point takes an int32, and it is closed before it is opened.
    /// </summary>
    [Fact]
    public void TheOffsetIsBoundedBeforeItReachesTheC()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RpCrypt.Aeropause(ChiakiTarget.Ps5_1, Ambassador(), -1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RpCrypt.Aeropause(ChiakiTarget.Ps5_1, Ambassador(), RpCrypt.KeyOffsetLimit));

        // And the last legal one is legal, so the bound is not off by one.
        try
        {
            Assert.Equal(16, RpCrypt.Aeropause(
                ChiakiTarget.Ps5_1, Ambassador(), RpCrypt.KeyOffsetLimit - 1).Length);
        }
        catch (DllNotFoundException)
        {
            // The two refusals above are managed-side and stand without the shim.
        }
    }

    /// <summary>
    /// Every value regist.c can produce is inside the bound, which is why the C never needed one.
    ///
    /// buf[0] >> 3 over all 256 bytes: 0 to 31.
    /// </summary>
    [Fact]
    public void EveryOffsetTheCCanProduceIsInRange()
    {
        for (int b = 0; b <= 0xff; b++)
        {
            int offset = RegistRequestPayload.Key1Offset([(byte)b]);

            Assert.InRange(offset, 0, RpCrypt.KeyOffsetLimit - 1);
        }
    }

    /// <summary>An account id of the wrong size is refused before it reaches the C, which reads eight.</summary>
    [Fact]
    public void AnAccountIdOfTheWrongSizeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RpCrypt.RegistRequestPayload(
            ChiakiTarget.Ps5_1, Ambassador(), null, new byte[7], 1));

        Assert.Throws<ArgumentException>(() => RpCrypt.RegistRequestPayload(
            ChiakiTarget.Ps5_1, Ambassador(), null, new byte[9], 1));
    }

    /// <summary>
    /// A PS5 asked with an online id and no account id is refused BY THE C, which is the reason the
    /// four-argument overload could only reach the pre-10 path.
    /// </summary>
    [Fact]
    public void APs5WithNoAccountIdIsRefusedByTheC()
    {
        try
        {
            Assert.Throws<InvalidOperationException>(() => RpCrypt.RegistRequestPayload(
                ChiakiTarget.Ps5_1, Ambassador(), "someone", 4321));
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    /// <summary>
    /// PP437: the new export is imported, so it cannot land unused - asserted here as well as by the
    /// seam census, because that census is what would otherwise be the only reader.
    /// </summary>
    [Fact]
    public void TheNewExportIsPartOfTheSeam()
    {
        if (NativeSeam.ReadHeaders() is not { } headers)
            return;
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.Contains("chiaki_shim_rpcrypt_aeropause", NativeSeam.Exported(headers));
        Assert.Contains(
            "chiaki_shim_rpcrypt_aeropause",
            NativeSeam.Imported(NativeSeam.ManagedSources(root)));
    }
}
