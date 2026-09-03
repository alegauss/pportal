using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: the key position, which is the counter every encrypted byte of a session is keyed by.
///
/// The wire carries 32 bits of it and the cipher needs 64. Expanding one into the other is the
/// whole of this: remember the high half, increment it when the low half wraps, so a packet at
/// 0x1337 arriving after one at 0xffff0000 is 0x1_00001337 and not four billion bytes backwards.
///
/// Getting it wrong does not throw. It keys the stream at the wrong offset, so every packet after
/// the first wrap decrypts to noise and the session dies on a MAC failure - four gigabytes in,
/// which is far enough from the start that nothing points at a counter.
/// </summary>
public sealed class KeyState : IDisposable
{
    private IntPtr _handle;

    public KeyState()
    {
        _handle = KeyStateCreate();
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_key_state_create returned null.");
    }

    internal IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(KeyState));

    /// <summary>
    /// The 64-bit position a 32-bit one on the wire means.
    /// </summary>
    /// <param name="commit">
    /// Whether this request advances the state. A parse that may still turn out to be garbage asks
    /// without committing, so a corrupt packet cannot drag the counter forward with it.
    /// </param>
    public ulong RequestPos(uint low, bool commit = true) => KeyStateRequestPos(Handle, low, commit);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        KeyStateFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_key_state_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr KeyStateCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_key_state_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void KeyStateFree(IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_key_state_request_pos",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong KeyStateRequestPos(
        IntPtr state, uint low, [MarshalAs(UnmanagedType.I1)] bool commit);
}

/// <summary>One audio or video packet's header, with the payload named by where it sits.</summary>
/// <param name="IsHaptics">
/// PP295: the third way streamconnection.c routes a packet, which the mirror could not express.
/// The C sets it on the V12 AUDIO LAYOUT ONLY - <c>if(v12 &amp;&amp; !packet->is_video)
/// packet->is_haptics = *av == 0x02;</c> - and the shim's parse this mirror is built from is v9's,
/// which never sets it. So it is false for every packet the port parses today, and that is the C's
/// answer rather than a default standing in for one. Last and defaulted, so the two sites that
/// build this positionally stay as they are.
/// </param>
public readonly record struct AvPacket(
    bool IsVideo,
    ushort PacketIndex,
    ushort FrameIndex,
    ushort UnitIndex,
    ushort UnitsInFrameTotal,
    ushort UnitsInFrameFec,
    byte Codec,
    byte AdaptiveStreamIndex,
    ulong KeyPos,
    int DataOffset,
    int DataSize,
    bool IsHaptics = false);

/// <summary>
/// PP23: takion's AV packet header, which is what every frame of picture and sound arrives inside.
///
/// The payload comes back as an OFFSET into the caller's buffer rather than as a pointer. That is
/// the same ownership rule as the discovery reply, taken one step further: the buffer is already
/// the caller's, so naming a position in it costs no lifetime at all.
/// </summary>
public static class Takion
{
    /// <summary>
    /// Parses a v9 AV packet header, or returns null with the error the parse gave.
    ///
    /// A span rather than an array, because this runs once per packet and PP113's budget is zero
    /// bytes there. Note it is a <c>Span</c> and not a <c>ReadOnlySpan</c>: libchiaki parses the
    /// datagram IN PLACE, so the caller's bytes are modified and the type says so.
    /// </summary>
    public static unsafe AvPacket? ParseV9(KeyState keyState, Span<byte> buffer, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(keyState);

        fixed (byte* p = buffer)
        {
            int e = TakionV9ParsePtr(keyState.Handle, (IntPtr)p, buffer.Length,
                out bool video, out ushort pi, out ushort fi, out ushort ui,
                out ushort ut, out ushort uf, out byte c, out byte a,
                out ulong kp, out int off, out int sz);

            error = (ChiakiError)e;
            return error == ChiakiError.Success
                ? new AvPacket(video, pi, fi, ui, ut, uf, c, a, kp, off, sz)
                : null;
        }
    }

    /// <summary>The same, for a caller that already has an array.</summary>
    public static AvPacket? ParseV9(KeyState keyState, byte[] buffer, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(keyState);
        ArgumentNullException.ThrowIfNull(buffer);

        int err = TakionV9Parse(keyState.Handle, buffer, buffer.Length,
            out bool isVideo, out ushort packetIndex, out ushort frameIndex, out ushort unitIndex,
            out ushort unitsTotal, out ushort unitsFec, out byte codec, out byte adaptive,
            out ulong keyPos, out int dataOffset, out int dataSize);

        error = (ChiakiError)err;
        if (error != ChiakiError.Success)
            return null;

        return new AvPacket(isVideo, packetIndex, frameIndex, unitIndex, unitsTotal, unitsFec,
            codec, adaptive, keyPos, dataOffset, dataSize);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v9_av_packet_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TakionV9Parse(
        IntPtr keyState, byte[] buf, int bufSize,
        [MarshalAs(UnmanagedType.I1)] out bool isVideo,
        out ushort packetIndex, out ushort frameIndex, out ushort unitIndex,
        out ushort unitsInFrameTotal, out ushort unitsInFrameFec,
        out byte codec, out byte adaptiveStreamIndex, out ulong keyPos,
        out int dataOffset, out int dataSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v9_av_packet_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TakionV9ParsePtr(
        IntPtr keyState, IntPtr buf, int bufSize,
        [MarshalAs(UnmanagedType.I1)] out bool isVideo,
        out ushort packetIndex, out ushort frameIndex, out ushort unitIndex,
        out ushort unitsInFrameTotal, out ushort unitsInFrameFec,
        out byte codec, out byte adaptiveStreamIndex, out ulong keyPos,
        out int dataOffset, out int dataSize);

    /// <summary>
    /// PP124: CHIAKI_TAKION_CONGESTION_PACKET_SIZE - fifteen bytes, asked rather than written
    /// down here as well.
    /// </summary>
    public static int CongestionPacketSize => TakionCongestionPacketSize();

    /// <summary>
    /// The congestion report the client sends UPSTREAM: how many packets it received and how many
    /// it lost, which is what the console's bitrate control reacts to.
    ///
    /// The first thing this port sends rather than reads. A wrong byte here is not a stream that
    /// fails - it is one that quietly degrades, with nothing on either side reporting it.
    /// </summary>
    public static byte[] FormatCongestion(ushort word0, ushort received, ushort lost, ulong keyPos)
    {
        var buf = new byte[CongestionPacketSize];
        int err = TakionFormatCongestion(buf, buf.Length, word0, received, lost, keyPos);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_takion_format_congestion failed: {(ChiakiError)err}.");

        return buf;
    }

    /// <summary>
    /// Writes the packet's MAC INTO it, at a fixed offset, over whatever was there.
    ///
    /// Not beside it and not after it. A rewrite that appended the MAC produces a packet of the
    /// right length that the console silently ignores, which is the failure this exists to pin.
    /// </summary>
    public static void WritePacketMac(GkCrypt crypt, byte[] buf, ulong keyPos)
    {
        ArgumentNullException.ThrowIfNull(crypt);
        ArgumentNullException.ThrowIfNull(buf);

        int err = TakionPacketMac(crypt.Handle, buf, buf.Length, keyPos, null, null);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_takion_packet_mac failed: {(ChiakiError)err}.");
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_format_congestion",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TakionFormatCongestion(
        byte[] buf, int bufSize, ushort word0, ushort received, ushort lost, ulong keyPos);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_congestion_packet_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TakionCongestionPacketSize();

    /// <summary>
    /// PP517: the C's gate with NO cipher, which is the path PP497 called the rewrite.
    ///
    /// With a null crypt the C copies the MAC out where asked, zeroes that field, computes nothing
    /// and returns success. That is the whole of PP497's placement claim and it needs no key, so it
    /// is the half of chiaki_takion_packet_mac a differential can actually run.
    /// </summary>
    /// <param name="packet">Mutated in place, as the C mutates it.</param>
    /// <param name="macBefore">The four bytes that were in the field, or null where none were asked for.</param>
    /// <returns>What the C returned.</returns>
    public static ChiakiError PacketMacWithoutCrypt(byte[] packet, ulong keyPos, out byte[]? macBefore)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // The literal and not TakionPacketMac.GmacSize: inside this class that name is the
        // DllImport below, not PP497's model. A join asserts the two agree.
        var before = new byte[4];
        int err = TakionPacketMac(IntPtr.Zero, packet, packet.Length, keyPos, null, before);

        macBefore = err == (int)ChiakiError.Success ? before : null;
        return (ChiakiError)err;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_packet_mac",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TakionPacketMac(
        IntPtr gkcrypt, byte[] buf, int bufSize, ulong keyPos, byte[]? macOut, byte[]? macOldOut);

}
