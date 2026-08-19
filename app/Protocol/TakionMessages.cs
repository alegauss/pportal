using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What nanopb read out of a takion message, for the fields that are scalars.</summary>
public readonly record struct DecodedTakionMessage(
    int Type, bool HasBang, uint ServerVersion, uint Token,
    bool EncryptedKeyAccepted, bool VersionAccepted);

/// <summary>
/// PP25: the wire format, regenerated rather than translated - and the check that both generators
/// agree.
///
/// lib/protobuf/takion.proto is one file that becomes C through nanopb and C# through protoc, both
/// at build time. That is what makes the messages the cheapest part of this core to port: nobody
/// transcribes a field, and a field added to the .proto reaches both halves or neither.
///
/// What it leaves open is whether the two generators put the same bytes on the wire, and this is
/// where that is answered. The managed side encodes a message and nanopb - which is what the
/// console's protocol is actually spoken with today - is asked what it reads back.
/// </summary>
public static class TakionMessages
{
    /// <summary>Decodes with nanopb. Null when it refuses the bytes.</summary>
    public static DecodedTakionMessage? DecodeWithNanopb(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        return TakionMessageDecode(encoded, encoded.Length, out int type, out bool hasBang,
                out uint serverVersion, out uint token, out bool keyOk, out bool versionOk)
            ? new DecodedTakionMessage(type, hasBang, serverVersion, token, keyOk, versionOk)
            : null;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_message_decode",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TakionMessageDecode(
        byte[] buf, int size, out int type,
        [MarshalAs(UnmanagedType.I1)] out bool hasBang,
        out uint serverVersion, out uint token,
        [MarshalAs(UnmanagedType.I1)] out bool encryptedKeyAccepted,
        [MarshalAs(UnmanagedType.I1)] out bool versionAccepted);
}
