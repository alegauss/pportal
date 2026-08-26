using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which GMAC key a packet is authenticated under, and how it has to be obtained.</summary>
/// <param name="Index">The refresh window the packet belongs to.</param>
/// <param name="Action">Whether the current key serves, or one has to be made.</param>
public readonly record struct GmacKeyChoice(ulong Index, GmacKeyAction Action);

/// <summary>What a packet's key index means for the key a session is holding.</summary>
public enum GmacKeyAction
{
    /// <summary>The packet is in the window the session is already on.</summary>
    Current,

    /// <summary>It is ahead: the session advances, and keeps the new key.</summary>
    Refresh,

    /// <summary>It is behind: a key for that window is made, used once and dropped.</summary>
    Temporary,
}

/// <summary>
/// PP26: which GMAC key window a key position falls in.
///
/// A session refreshes its GMAC key every 45000 bytes of key position, and packets do not arrive in
/// order - so a packet can belong to the window the session is on, one ahead of it, or one behind.
/// Ahead advances the session and keeps the key; behind derives a key for that window, uses it and
/// throws it away, because rolling backwards would make every packet after it fail.
///
/// The window boundary belongs to the window BELOW it
/// ---------------------------------------------------
/// <c>(key_pos &gt; 0 ? key_pos - 1 : 0) / 45000</c>. Key position 45000 is index 0, not 1 - the
/// subtraction moves every exact multiple down a window, and the guard is there so position 0 does
/// not underflow into the top of the range.
///
/// A port that divided directly would be right for 44999 positions out of every 45000 and wrong for
/// the one on the boundary, which is a packet that fails authentication about once per window on a
/// stream that is otherwise fine.
/// </summary>
public static class GmacKeyWindow
{
    /// <summary>CHIAKI_GKCRYPT_GMAC_KEY_REFRESH_KEY_POS.</summary>
    public const ulong RefreshKeyPos = 45000;

    /// <summary>The window a key position falls in, boundary included.</summary>
    public static ulong IndexFor(ulong keyPos) => (keyPos > 0 ? keyPos - 1 : 0) / RefreshKeyPos;

    /// <summary>
    /// What to do about a packet at <paramref name="keyPos"/> given the window the session is on.
    /// </summary>
    public static GmacKeyChoice Choose(ulong keyPos, ulong currentIndex)
    {
        ulong index = IndexFor(keyPos);

        // Compared in this order because the C does: ahead first, behind second, and equal falls
        // through to using what is already held.
        GmacKeyAction action =
            index > currentIndex ? GmacKeyAction.Refresh
            : index < currentIndex ? GmacKeyAction.Temporary
            : GmacKeyAction.Current;

        return new GmacKeyChoice(index, action);
    }

    /// <summary>The IV a packet's GMAC is computed under: the session IV advanced by its block.</summary>
    /// <remarks>
    /// Divided by the BLOCK size and not by the refresh position - this is the same advance the key
    /// stream uses, and it moves per block where the key moves per window.
    /// </remarks>
    public static byte[] IvFor(ReadOnlySpan<byte> sessionIv, ulong keyPos)
        => GkKeyStream.CounterAdd(sessionIv, keyPos / GkKeyStream.BlockSize);

    /// <summary>PP26: whether the C still moves the boundary down a window.</summary>
    public static bool TheBoundaryIsStillBelow(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "(key_pos > 0 ? key_pos - 1 : 0) / CHIAKI_GKCRYPT_GMAC_KEY_REFRESH_KEY_POS",
            StringComparison.Ordinal);
    }

    /// <summary>And whether a packet from an older window still gets a temporary key.</summary>
    public static bool AnOlderWindowIsStillTemporary(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CCall.Happens(core, "chiaki_gkcrypt_gen_tmp_gmac_key(gkcrypt, key_index, gmac_key_tmp)")
            && CCall.Happens(core, "chiaki_gkcrypt_gen_new_gmac_key(gkcrypt, key_index)");
    }
}
