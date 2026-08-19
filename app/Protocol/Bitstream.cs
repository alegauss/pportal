using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What kind of slice arrived. The values are libchiaki's enum order.</summary>
public enum BitstreamSliceType { Unknown = 0, I = 1, P = 2 }

/// <summary>
/// PP23: the bitstream parser, which is what tells the client what kind of frame just arrived.
///
/// It reads H.264 and H.265 slice headers far enough to answer two questions - is this an I frame,
/// and which frame does it reference - and everything the video path does about loss rests on
/// those. Whether a gap needs an IDR request, whether a frame can be decoded at all, whether a
/// reference can be rewritten to skip a missing one: all of it is these two numbers, so a parser
/// that is subtly wrong shows up as stutter that gets blamed on the network.
///
/// test/bitstream.c records real headers and slices for both codecs, one of them a regression case
/// carrying an upstream issue number. That is the closest this module has to a specification, and
/// it is what the assertions cite.
/// </summary>
public sealed class Bitstream : IDisposable
{
    private IntPtr _handle;

    /// <param name="codec">ChiakiCodec: H264, H265 or H265Hdr.</param>
    public Bitstream(ChiakiNg.Session.ChiakiCodec codec)
    {
        _handle = BitstreamCreate((int)codec);
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_bitstream_create returned null.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(Bitstream));

    /// <summary>
    /// Parses the parameter sets a stream opens with. Everything after depends on it: a slice
    /// cannot be read without the SPS that says how wide its fields are.
    /// </summary>
    public bool ReadHeader(byte[] data) => BitstreamHeader(Handle, data, data.Length);

    /// <summary>The slice type and the frame it references, or null when the slice is not read.</summary>
    public (BitstreamSliceType Type, uint ReferenceFrame)? ReadSlice(byte[] data)
        => BitstreamSlice(Handle, data, data.Length, out int type, out uint reference)
            ? ((BitstreamSliceType)type, reference)
            : null;

    /// <summary>
    /// Rewrites a slice's reference frame IN PLACE - the array passed in is modified. That is how
    /// a frame whose reference was lost is made decodable against one that survived.
    /// </summary>
    public bool SetReferenceFrame(byte[] data, uint referenceFrame)
        => SetReferenceFrame(data, data.Length, referenceFrame);

    /// <summary>
    /// The same, with the slice occupying only the first <paramref name="length"/> bytes.
    ///
    /// PP35: this exists because the array is the only arena managed code has. test/bitstream.c
    /// asserts that rewriting a TRUNCATED slice writes nothing outside it, and does so by placing
    /// the slice inside a larger buffer of sentinel bytes - which needs a length that is shorter
    /// than the buffer. Real callers pass the whole array and use the overload above.
    /// </summary>
    public bool SetReferenceFrame(byte[] data, int length, uint referenceFrame)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, data.Length);

        return BitstreamSetReferenceFrame(Handle, data, length, referenceFrame);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        BitstreamFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_bitstream_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BitstreamCreate(int codec);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_bitstream_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BitstreamFree(IntPtr bitstream);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_bitstream_header",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool BitstreamHeader(IntPtr bitstream, byte[] data, int size);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_bitstream_slice",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool BitstreamSlice(
        IntPtr bitstream, byte[] data, int size, out int sliceType, out uint referenceFrame);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_bitstream_slice_set_reference_frame",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool BitstreamSetReferenceFrame(
        IntPtr bitstream, byte[] data, int size, uint referenceFrame);
}
