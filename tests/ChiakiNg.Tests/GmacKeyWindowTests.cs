using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: which GMAC key window a packet falls in, and the boundary that belongs to the window below.
/// </summary>
public class GmacKeyWindowTests
{
    /// <summary>
    /// THE BOUNDARY. An exact multiple of the refresh position is the window BELOW it.
    ///
    /// A port dividing directly is right for 44999 positions out of every 45000 and wrong for the
    /// one on the line - a packet that fails authentication roughly once per window, on a stream
    /// that is otherwise fine.
    /// </summary>
    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 0UL)]
    [InlineData(44999UL, 0UL)]
    [InlineData(45000UL, 0UL)]      // the boundary, and the one that catches a plain divide
    [InlineData(45001UL, 1UL)]
    [InlineData(90000UL, 1UL)]
    [InlineData(90001UL, 2UL)]
    public void TheBoundaryBelongsToTheWindowBelow(ulong keyPos, ulong expected)
        => Assert.Equal(expected, GmacKeyWindow.IndexFor(keyPos));

    /// <summary>
    /// And position zero does not underflow, which is what the guard is for.
    ///
    /// Without it, 0 - 1 is 0xffff_ffff_ffff_ffff and the first packet of a session asks for a key
    /// window near the top of the range.
    /// </summary>
    [Fact]
    public void ZeroDoesNotUnderflow()
        => Assert.Equal(0UL, GmacKeyWindow.IndexFor(0));

    /// <summary>Ahead refreshes, behind is temporary, level uses what is held.</summary>
    [Theory]
    [InlineData(45001UL, 0UL, GmacKeyAction.Refresh)]
    [InlineData(1UL, 0UL, GmacKeyAction.Current)]
    [InlineData(1UL, 3UL, GmacKeyAction.Temporary)]
    [InlineData(90001UL, 2UL, GmacKeyAction.Current)]
    public void TheActionFollowsTheWindow(ulong keyPos, ulong current, GmacKeyAction expected)
        => Assert.Equal(expected, GmacKeyWindow.Choose(keyPos, current).Action);

    /// <summary>
    /// The GMAC's IV advances per BLOCK, not per window.
    ///
    /// Two different divisors in the same function - key_pos/0x10 for the IV and key_pos/45000 for
    /// the key - and using one for both would give every packet in a window the same IV.
    /// </summary>
    [Fact]
    public void TheIvAdvancesPerBlockNotPerWindow()
    {
        byte[] iv = new byte[16];

        Assert.Equal(GmacKeyWindow.IvFor(iv, 0), GmacKeyWindow.IvFor(iv, 15));
        Assert.NotEqual(GmacKeyWindow.IvFor(iv, 0), GmacKeyWindow.IvFor(iv, 16));

        // ...and it is the upward-carrying counter, so block 1 moves byte 0.
        Assert.Equal(1, GmacKeyWindow.IvFor(iv, 16)[0]);
    }

    /// <summary>THE DRIFT CHECK. The boundary and the two refresh paths are still the C's.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\gkcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\gkcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(GmacKeyWindow.TheBoundaryIsStillBelow(core),
            "the key index no longer subtracts one, so the boundary moved a window");
        Assert.True(GmacKeyWindow.AnOlderWindowIsStillTemporary(core),
            "an out-of-order packet from an older window no longer gets a temporary key");
    }
}
