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

    /// <summary>
    /// A bang encoded by nanopb, for the managed generator to read.
    ///
    /// The other direction, and the one that reaches the string and bytes fields: nanopb does not
    /// store those, it asks a callback to write them as the field goes past. Null when it refuses.
    /// </summary>
    public static byte[]? EncodeBangWithNanopb(
        uint serverVersion, uint token, bool encryptedKeyAccepted, bool versionAccepted,
        string sessionKey, byte[] ecdhPubKey, byte[] ecdhSig)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentNullException.ThrowIfNull(ecdhPubKey);
        ArgumentNullException.ThrowIfNull(ecdhSig);

        var buf = new byte[1024];
        int size = buf.Length;
        return TakionMessageEncodeBang(serverVersion, token, encryptedKeyAccepted, versionAccepted,
            sessionKey, ecdhPubKey, ecdhPubKey.Length, ecdhSig, ecdhSig.Length, buf, ref size)
            ? buf[..size]
            : null;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_message_encode_bang",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TakionMessageEncodeBang(
        uint serverVersion, uint token,
        [MarshalAs(UnmanagedType.I1)] bool encryptedKeyAccepted,
        [MarshalAs(UnmanagedType.I1)] bool versionAccepted,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionKey,
        byte[] ecdhPubKey, int ecdhPubKeySize,
        byte[] ecdhSig, int ecdhSigSize,
        byte[] buf, ref int bufSize);

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
