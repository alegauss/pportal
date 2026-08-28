using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP495, under PP27: the key position ledger, whose whole content is "this arithmetic has not been
/// improved".
///
/// Three of its shapes read as mistakes and none of them may be corrected - the console runs the
/// same sums. So most of these assertions exist to fail if somebody tidies the C, which is the only
/// direction this code can move in.
/// </summary>
public class TakionKeyPositionTests
{
    /// <summary>
    /// THE ROUNDING IS NOT ROUNDING, and the two answers are named side by side so it is provable
    /// rather than remembered.
    ///
    /// Adding the remainder gives 24 for 20 where rounding up gives 32. Only exact multiples agree,
    /// which is exactly the set a careless test would have used.
    /// </summary>
    [Theory]
    [InlineData(20UL, 24UL, 32UL)]
    [InlineData(1UL, 2UL, 16UL)]
    [InlineData(15UL, 30UL, 16UL)]
    [InlineData(17UL, 18UL, 32UL)]
    [InlineData(31UL, 46UL, 32UL)]
    public void AddingTheRemainderIsNotRoundingUp(ulong size, ulong sized, ulong rounded)
    {
        Assert.Equal(sized, TakionKeyPosition.Sized(size));
        Assert.Equal(rounded, TakionKeyPosition.RoundedUp(size));
        Assert.NotEqual(TakionKeyPosition.Sized(size), TakionKeyPosition.RoundedUp(size));
    }

    /// <summary>And on an exact multiple the two agree, which is why the trap is easy to miss.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(16UL)]
    [InlineData(1024UL)]
    public void OnAnExactBlockTheTwoAgree(ulong size)
        => Assert.Equal(TakionKeyPosition.RoundedUp(size), TakionKeyPosition.Sized(size));

    /// <summary>The position handed out is where the packet begins, and the ledger moves past it.</summary>
    [Fact]
    public void ThePositionIsTakenBeforeTheLedgerMoves()
    {
        KeyPositionGrant grant = TakionKeyPosition.Advance(current: 100, dataSize: 20);

        Assert.Equal(ChiakiError.Success, grant.Error);
        Assert.Equal(100UL, grant.KeyPosition);
        Assert.Equal(124UL, grant.Next);
    }

    /// <summary>
    /// With no cipher every packet claims position zero and the ledger does not move.
    ///
    /// Not an error path: it is the state the whole handshake is sent in, so a model treating it as
    /// one would refuse to connect.
    /// </summary>
    [Fact]
    public void WithNoCipherThePositionIsZeroAndNothingMoves()
    {
        KeyPositionGrant grant =
            TakionKeyPosition.Advance(current: 4096, dataSize: 900, cipherPresent: false);

        Assert.Equal(ChiakiError.Success, grant.Error);
        Assert.Equal(0UL, grant.KeyPosition);
        Assert.Equal(4096UL, grant.Next);
    }

    /// <summary>The ledger refuses to wrap, and leaves itself where it was when it does.</summary>
    [Fact]
    public void TheLedgerRefusesToWrap()
    {
        ulong current = ulong.MaxValue - 8;
        KeyPositionGrant grant = TakionKeyPosition.Advance(current, dataSize: 32);

        Assert.Equal(ChiakiError.Overflow, grant.Error);
        Assert.Equal(current, grant.Next);
    }

    /// <summary>A position that fits exactly is granted rather than refused.</summary>
    [Fact]
    public void AnExactFitIsGranted()
    {
        ulong size = 32;
        ulong current = ulong.MaxValue - TakionKeyPosition.Sized(size);

        Assert.Equal(ChiakiError.Success, TakionKeyPosition.Advance(current, size).Error);
    }

    /// <summary>
    /// The two data sends stamp a position sized for their payload while putting 26 and 25 more
    /// bytes on the wire.
    ///
    /// Two different discrepancies, because the continuation omits the data-type byte. Neither is
    /// reconciled anywhere and neither may be.
    /// </summary>
    [Fact]
    public void TheTwoDataSendsUnderstateTheirPacketByDifferentAmounts()
    {
        Assert.Equal(26, TakionKeyPosition.DataSendPacketOverhead);
        Assert.Equal(25, TakionKeyPosition.ContinuationPacketOverhead);
        Assert.NotEqual(
            TakionKeyPosition.DataSendPacketOverhead, TakionKeyPosition.ContinuationPacketOverhead);
    }

    /// <summary>All four sizing shapes are used, so the enum has no member nothing reaches.</summary>
    [Fact]
    public void EverySizingShapeHasACallSite()
    {
        Assert.Equal(6, TakionKeyPosition.CallSites.Count);
        Assert.Equal(
            Enum.GetValues<KeyPositionSizing>().Order(),
            TakionKeyPosition.CallSites.Values.Distinct().Order());
    }

    /// <summary>
    /// THE DRIFT CHECK: the C's arithmetic is still exactly this, in all four places it can move.
    ///
    /// Every one of these is an assertion that nobody improved the line. The remainder is the one
    /// that matters most: repaired, it desynchronises the cipher on the first encrypted packet, and
    /// the symptom is a stream of noise rather than an error.
    /// </summary>
    [Fact]
    public void TheCsArithmeticIsStillTheProtocolsAndNotTheTidyOne()
    {
        if (TakionKeyPositionSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string advance = Assert.IsType<string>(TakionKeyPositionSource.AdvanceBody(source));

        Assert.True(TakionKeyPositionSource.TheRemainderIsAdded(advance));
        Assert.True(TakionKeyPositionSource.ThePositionIsTakenBeforeTheAdvance(advance));
        Assert.True(TakionKeyPositionSource.WithNoCipherThePositionIsZero(advance));
        Assert.True(TakionKeyPositionSource.TheOverflowGuardIsSizeMax(advance));
    }

    /// <summary>
    /// And every call site still passes the size shape recorded for it.
    ///
    /// Six sites, four meanings. A caller that started passing its packet size instead of its
    /// payload size would be an improvement in every reading except the console's.
    ///
    /// This assertion earned its keep on the commit that added it: the map first named the two
    /// public feedback sends, and the two sites that actually pass payload-plus-a-block are the
    /// feedback HELPER and the mic packet. Six line numbers read correctly and attributed wrongly.
    /// </summary>
    [Fact]
    public void EveryCallSiteStillPassesItsOwnShape()
    {
        if (TakionKeyPositionSource.Locate() is not { } path)
            return;

        Assert.True(TakionKeyPositionSource.EveryCallSitePassesItsRecordedShape(File.ReadAllText(path)));
    }

    /// <summary>
    /// The two re-entrant sites hold the cipher's mutex across the call that takes it again, and
    /// that mutex is created recursive where takion's other one is not.
    ///
    /// The pair is what works. Made plain, the first feedback packet deadlocks; released early, the
    /// ledger could move between the position taken and the bytes encrypted with it.
    /// </summary>
    [Fact]
    public void TheReentrantSitesRelyOnARecursiveMutex()
    {
        if (TakionKeyPositionSource.Locate() is not { } path)
            return;

        Assert.True(TakionKeyPositionSource.TheReentrantSitesHoldARecursiveMutex(File.ReadAllText(path)));
    }

    /// <summary>The block size is the header's, not a number typed into this port.</summary>
    [Fact]
    public void TheBlockSizeIsTheHeaders()
    {
        if (TakionKeyPositionSource.LocateCrypt() is not { } path)
            return;

        Assert.Equal(
            TakionKeyPosition.BlockSize, TakionKeyPositionSource.BlockSizeIn(File.ReadAllText(path)));
    }

    /// <summary>
    /// The overflow guard's correctness depends on this target's size_t being 64 bits.
    ///
    /// Asserted through the runtime rather than argued: on a 32-bit build the C's SIZE_MAX test
    /// would trip long before the ledger could wrap, and this port would be modelling the wrong
    /// bound. The Windows-only non-goal is what makes that unreachable.
    /// </summary>
    [Fact]
    public void ThisTargetsPointerWidthIsWhatMakesTheGuardRight()
        => Assert.Equal(8, IntPtr.Size);
}
