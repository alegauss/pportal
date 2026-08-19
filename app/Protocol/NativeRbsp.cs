using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: vl_rbsp itself, reachable so <see cref="VlRbsp"/> can be held against it.
///
/// It is header-only C - every function is `static inline` in lib/src/vl_rbsp.h - so nothing links
/// it, nothing exports it, and until the shim included the header there was no way to call it at
/// all. Which also means nothing would have noticed a managed version disagreeing.
///
/// The payload is copied into an allocation the handle owns, at a chosen address alignment: the
/// number of bits valid after init depends on it, and whether the OUTPUT does is what the tests are
/// for.
/// </summary>
public sealed class NativeRbsp : IDisposable
{
    private IntPtr handle;

    /// <param name="data">The NAL payload.</param>
    /// <param name="numBits">The end-of-NAL search limit; uint.MaxValue for none.</param>
    /// <param name="alignment">The low two bits of the address to place the payload at, 0-3.</param>
    public NativeRbsp(byte[] data, uint numBits = uint.MaxValue, int alignment = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        handle = RbspCreate(data, data.Length, numBits, alignment);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_shim_rbsp_create failed.");
    }

    /// <summary>The alignment the payload actually landed on, for the test that varies it.</summary>
    public int Alignment => RbspAlignment(Check());

    /// <summary>vl_rbsp_overrun.</summary>
    public bool Overrun => RbspOverrun(Check());

    /// <summary>vl_vlc_valid_bits of the reader's own buffer.</summary>
    public uint ValidBits => RbspValidBits(Check());

    /// <summary>vl_vlc_bits_left of the reader's own buffer.</summary>
    public uint BitsLeft => RbspBitsLeft(Check());

    /// <summary>vl_rbsp_u.</summary>
    public uint U(uint n) => RbspU(Check(), n);

    /// <summary>vl_rbsp_ue.</summary>
    public uint Ue() => RbspUe(Check());

    /// <summary>vl_rbsp_se.</summary>
    public int Se() => RbspSe(Check());

    /// <summary>vl_rbsp_has_bits.</summary>
    public bool HasBits(uint n) => RbspHasBits(Check(), n);

    /// <summary>vl_rbsp_more_data.</summary>
    public bool MoreData() => RbspMoreData(Check());

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        RbspFree(handle);
        handle = IntPtr.Zero;
    }

    private IntPtr Check()
        => handle != IntPtr.Zero ? handle : throw new ObjectDisposedException(nameof(NativeRbsp));

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr RbspCreate(byte[] data, int size, uint numBits, int alignment);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void RbspFree(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_alignment",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RbspAlignment(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_u",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint RbspU(IntPtr rbsp, uint n);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_ue",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint RbspUe(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_se",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RbspSe(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_overrun",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool RbspOverrun(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_has_bits",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool RbspHasBits(IntPtr rbsp, uint n);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_more_data",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool RbspMoreData(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_valid_bits",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint RbspValidBits(IntPtr rbsp);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rbsp_bits_left",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint RbspBitsLeft(IntPtr rbsp);
}
