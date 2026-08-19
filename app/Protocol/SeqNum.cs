using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: RFC 1982 serial number comparison, which is the arithmetic the whole transport rests on.
///
/// Sequence numbers wrap. 0xffff is followed by 0, so a packet numbered 1 is NEWER than one
/// numbered 0xfff5 even though the integer is smaller. Every reorder decision, every duplicate
/// check and every "have I seen this already" in takion is one of these two comparisons.
///
/// That is why it is worth reaching across the seam for two functions that fit on a line. A
/// rewrite spells them <c>a &lt; b</c> and is right for 65535 of every 65536 packets - the counter
/// turns over once, and the queue starts discarding everything as stale while the picture freezes.
/// At a stream's packet rate that is minutes, not weeks, and the symptom points at the network.
/// </summary>
public static class SeqNum
{
    /// <summary>Whether <paramref name="a"/> is older than <paramref name="b"/>, wrap included.</summary>
    public static bool Lt(ushort a, ushort b) => SeqNum16Lt(a, b);

    /// <summary>Whether <paramref name="a"/> is newer than <paramref name="b"/>, wrap included.</summary>
    public static bool Gt(ushort a, ushort b) => SeqNum16Gt(a, b);

    public static bool Lt(uint a, uint b) => SeqNum32Lt(a, b);

    public static bool Gt(uint a, uint b) => SeqNum32Gt(a, b);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_seq_num_16_lt",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SeqNum16Lt(ushort a, ushort b);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_seq_num_16_gt",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SeqNum16Gt(ushort a, ushort b);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_seq_num_32_lt",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SeqNum32Lt(uint a, uint b);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_seq_num_32_gt",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SeqNum32Gt(uint a, uint b);
}
