using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: libchiaki's own serial-number comparison, reachable so the managed one can be held against
/// it - the role <see cref="NativeHttp"/> and <see cref="NativeJson"/> play for their modules.
///
/// These four are `static inline` in seqnum.h, so they export no symbol of their own and the shim's
/// wrappers are the only way to call them at all. That is worth knowing before trusting a managed
/// rewrite: nothing links against the C version, so nothing would notice if the two disagreed.
/// </summary>
public static class NativeSeqNum
{
    /// <summary>chiaki_seq_num_16_lt.</summary>
    public static bool Lt(ushort a, ushort b) => SeqNum16Lt(a, b);

    /// <summary>chiaki_seq_num_16_gt.</summary>
    public static bool Gt(ushort a, ushort b) => SeqNum16Gt(a, b);

    /// <summary>chiaki_seq_num_32_lt.</summary>
    public static bool Lt(uint a, uint b) => SeqNum32Lt(a, b);

    /// <summary>chiaki_seq_num_32_gt.</summary>
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
