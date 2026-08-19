using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: the handshake's key agreement, driven from managed code against a recorded exchange.
///
/// test/gkcrypt.c holds one complete ECDH: a local key pair, the signature it produces under a
/// handshake key, the console's public key and signature, and the 32-byte secret the two derived.
/// It is the only place in this tree where a real console's half of a key agreement is written
/// down, and it is what makes the derivation checkable without one on the network.
///
/// This is where PP26's warning lands hardest. A wrong byte here does not throw: it produces a key
/// that fails to open a session, with nothing to say which of eight steps was wrong. The recorded
/// exchange turns that into one failing assertion.
/// </summary>
public sealed class Ecdh : IDisposable
{
    /// <summary>CHIAKI_ECDH_SECRET_SIZE, read from the shim rather than assumed.</summary>
    public static int SecretSize => EcdhSecretSize();

    private IntPtr _handle;

    public Ecdh()
    {
        _handle = EcdhCreate();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_ecdh_init failed.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(Ecdh));

    /// <summary>
    /// Installs a recorded key pair. Without it the pair is generated, and a generated pair
    /// derives a secret nothing has a recorded answer for.
    /// </summary>
    public void SetLocalKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
        => Require(EcdhSetLocalKey(Handle, privateKey.ToArray(), privateKey.Length,
            publicKey.ToArray(), publicKey.Length), nameof(SetLocalKey));

    /// <summary>The local public key and its signature under the handshake key.</summary>
    public (byte[] PublicKey, byte[] Signature) LocalPublicKey(ReadOnlySpan<byte> handshakeKey)
    {
        var key = new byte[128];
        var sig = new byte[64];
        int keyLen = key.Length;
        int sigLen = sig.Length;

        Require(EcdhLocalPubKey(Handle, handshakeKey.ToArray(), key, ref keyLen, sig, ref sigLen),
            nameof(LocalPublicKey));

        return (key[..keyLen], sig[..sigLen]);
    }

    /// <summary>
    /// The shared secret. The remote signature is checked as part of it, which is why this takes
    /// the handshake key too - the agreement and the authentication are one step here.
    /// </summary>
    public byte[] DeriveSecret(
        ReadOnlySpan<byte> remotePublicKey, ReadOnlySpan<byte> handshakeKey, ReadOnlySpan<byte> remoteSignature)
    {
        var secret = new byte[128];
        Require(EcdhDeriveSecret(Handle, secret, remotePublicKey.ToArray(), remotePublicKey.Length,
            handshakeKey.ToArray(), remoteSignature.ToArray(), remoteSignature.Length), nameof(DeriveSecret));

        return secret[..SecretSize];
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        EcdhFree(_handle);
        _handle = IntPtr.Zero;
    }

    private static void Require(int err, string what)
    {
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"{what} failed: {(ChiakiError)err}.");
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_secret_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EcdhSecretSize();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr EcdhCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void EcdhFree(IntPtr ecdh);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_set_local_key",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EcdhSetLocalKey(
        IntPtr ecdh, byte[] privateKey, int privateKeySize, byte[] publicKey, int publicKeySize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_local_pub_key",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EcdhLocalPubKey(
        IntPtr ecdh, byte[] handshakeKey, byte[] keyOut, ref int keyOutSize, byte[] sigOut, ref int sigOutSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ecdh_derive_secret",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EcdhDeriveSecret(
        IntPtr ecdh, byte[] secretOut, byte[] remoteKey, int remoteKeySize,
        byte[] handshakeKey, byte[] remoteSig, int remoteSigSize);
}

/// <summary>
/// PP23: the session key stream, which every takion packet is XORed against.
///
/// The recorded case gives a handshake key and an ECDH secret and says what the stream at position
/// 0x30 has to be. That position matters: the stream is a function of where in the session you
/// are, so a rewrite that got the derivation right and the position wrong would produce a stream
/// that is correct for a packet nobody sent.
/// </summary>
public sealed class GkCrypt : IDisposable
{
    private IntPtr _handle;

    /// <param name="keyBufChunks">
    /// Zero means no precomputed window, which is what the recorded case uses: every stream is
    /// generated on demand rather than read out of a buffer.
    /// </param>
    public GkCrypt(int keyBufChunks, byte index, ReadOnlySpan<byte> handshakeKey, ReadOnlySpan<byte> ecdhSecret)
    {
        _handle = GkCryptCreate(IntPtr.Zero, keyBufChunks, index, handshakeKey.ToArray(), ecdhSecret.ToArray());
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_gkcrypt_init failed.");
    }

    /// <summary>The key stream at a position, generated rather than looked up.</summary>
    public byte[] KeyStream(ulong keyPos, int length)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        var buf = new byte[length];
        int err = GkCryptGenKeyStream(_handle, keyPos, buf, length);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_gkcrypt_gen_key_stream failed: {(ChiakiError)err}.");

        return buf;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        GkCryptFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GkCryptCreate(
        IntPtr log, int keyBufChunks, byte index, byte[] handshakeKey, byte[] ecdhSecret);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void GkCryptFree(IntPtr gkcrypt);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_gen_key_stream",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GkCryptGenKeyStream(IntPtr gkcrypt, ulong keyPos, byte[] buf, int bufSize);
}
