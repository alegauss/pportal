using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP679: takion.c's v7 parse and its only header formatter, through the shim.
///
/// The oracle <see cref="AvPacketV7"/> is held to. Both are pure - no socket, no session, no key -
/// so the whole comparison runs in a unit test, which is what lets PP679 close without the console
/// PP27 waits on.
///
/// The parse takes no key state. The C declares a parameter for one and never reads it, so the
/// export passes NULL and this signature does not offer the caller a state to pass; a header
/// parsed here advances nothing.
/// </summary>
public static class NativeAvPacketV7
{
    /// <summary>chiaki_takion_v7_av_packet_parse, over bytes it does not modify.</summary>
    /// <returns>The header, or null with the C's error code.</returns>
    public static V7AvHeader? Parse(byte[] buffer, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        int err = V7Parse(buffer, buffer.Length,
            out bool isVideo, out bool usesNaluInfo, out ushort packetIndex, out ushort frameIndex,
            out ushort unitIndex, out ushort unitsTotal, out ushort unitsFec, out byte codec,
            out ushort wordAt0x18, out byte adaptive, out ulong keyPos,
            out int dataOffset, out int dataSize);

        error = (ChiakiError)err;
        if (error != ChiakiError.Success)
            return null;

        return new V7AvHeader(
            isVideo, usesNaluInfo, packetIndex, frameIndex, unitIndex, unitsTotal, unitsFec,
            codec, wordAt0x18, adaptive, (uint)keyPos, dataOffset, dataSize);
    }

    /// <summary>
    /// chiaki_takion_v7_av_packet_format_header, writing into the caller's buffer.
    /// </summary>
    /// <param name="buffer">Written in place, as senkusha.c's two probes have it written.</param>
    /// <param name="headerSize">
    /// Set even when the C refuses, because the C sets it before its bound check - and senkusha.c's
    /// MTU probe asserts on the size before it looks at the error.
    /// </param>
    public static ChiakiError FormatHeader(byte[] buffer, in V7AvHeader header, out int headerSize)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        int err = V7FormatHeader(buffer, buffer.Length, out headerSize,
            header.IsVideo, header.UsesNaluInfoStructs, header.PacketIndex, header.FrameIndex,
            header.UnitIndex, header.UnitsInFrameTotal, header.UnitsInFrameFec, header.Codec,
            header.WordAt0x18, header.AdaptiveStreamIndex, header.KeyPos);

        return (ChiakiError)err;
    }

    /// <summary>Whether this build carries the two wrappers at all.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return FormatHeader(new byte[AvPacketV7.HeaderBase], default, out _) == ChiakiError.Success;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v7_av_packet_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int V7Parse(
        byte[] buf, int bufSize,
        [MarshalAs(UnmanagedType.I1)] out bool isVideo,
        [MarshalAs(UnmanagedType.I1)] out bool usesNaluInfoStructs,
        out ushort packetIndex, out ushort frameIndex, out ushort unitIndex,
        out ushort unitsInFrameTotal, out ushort unitsInFrameFec,
        out byte codec, out ushort wordAt0x18, out byte adaptiveStreamIndex, out ulong keyPos,
        out int dataOffset, out int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v7_av_packet_format_header",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int V7FormatHeader(
        byte[] buf, int bufSize, out int headerSize,
        [MarshalAs(UnmanagedType.I1)] bool isVideo,
        [MarshalAs(UnmanagedType.I1)] bool usesNaluInfoStructs,
        ushort packetIndex, ushort frameIndex, ushort unitIndex,
        ushort unitsInFrameTotal, ushort unitsInFrameFec,
        byte codec, ushort wordAt0x18, byte adaptiveStreamIndex, ulong keyPos);
}
