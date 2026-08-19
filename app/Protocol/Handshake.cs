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

    /// <summary>
    /// PP123: CHIAKI_GKCRYPT_BLOCK_SIZE, which a caller adds to a packet's key position before
    /// decrypting its payload. Asked rather than written down a second time.
    /// </summary>
    public static int BlockSize => GkCryptBlockSize();

    /// <summary>
    /// Decrypts a payload IN PLACE at a key position.
    ///
    /// In place because that is what the C does and what the receive path wants: the payload is
    /// already a span of the buffer the packet arrived in, and copying it to decrypt would be a
    /// copy per packet on the one path PP113 measured at zero.
    ///
    /// The position is the packet's plus <see cref="BlockSize"/>. Getting it wrong does not fail -
    /// it produces plausible garbage, which the decoder then reports as a corrupt frame, and the
    /// fault reads as the network's.
    /// </summary>
    public void Decrypt(ulong keyPos, byte[] buf, int length)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, buf.Length);

        int err = GkCryptDecrypt(_handle, keyPos, buf, length);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_gkcrypt_decrypt failed: {(ChiakiError)err}.");
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

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_decrypt",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GkCryptDecrypt(IntPtr gkcrypt, ulong keyPos, byte[] buf, int bufSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_block_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GkCryptBlockSize();
}

/// <summary>
/// PP35: the GMAC that authenticates every takion packet.
///
/// gkcrypt's other half, and the port had none of it. It matters more than a missing test usually
/// does because of what PP105 established: takion checks no MAC at all until crypt is available,
/// and checks this one on everything afterwards. A GMAC computed differently from the C is a
/// session that rejects every packet the console sends, reported as a stream that will not start.
///
/// Separate from <see cref="GkCrypt"/> because it is a different object, not a different method.
/// test/gkcrypt.c's recorded GMACs are taken against a gkcrypt built by hand - zeroed, with only
/// the current GMAC key and the IV written in and no key buffer - which chiaki_gkcrypt_init cannot
/// produce, since it derives both from a handshake key and an ECDH secret. Folding that onto
/// GkCrypt would give one class two constructors with incompatible lifetimes and one Dispose that
/// has to guess which it was.
/// </summary>
public sealed class GkGmac : IDisposable
{
    private IntPtr _handle;

    /// <summary>CHIAKI_GKCRYPT_GMAC_SIZE, asked rather than copied.</summary>
    public static int Size => GmacSize();

    /// <summary>
    /// The GMAC key for an index, derived from a base key and an IV. A pure function on the C
    /// side too - no gkcrypt is involved, which is why this is static here as well.
    /// </summary>
    public static byte[] GenKey(ulong index, ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv)
    {
        var key = new byte[16];
        GenGmacKey(index, keyBase.ToArray(), iv.ToArray(), key);
        return key;
    }

    public GkGmac(ReadOnlySpan<byte> currentGmacKey, ReadOnlySpan<byte> iv)
    {
        _handle = CreateForGmac(currentGmacKey.ToArray(), iv.ToArray());
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_shim_gkcrypt_create_for_gmac failed.");
    }

    /// <summary>
    /// The four bytes takion compares a received packet's tail against.
    ///
    /// keyPos is part of the input and not a bookkeeping detail: the same buffer under the same
    /// key answers differently at a different position, which is what the recorded high and low
    /// cases exist to pin.
    /// </summary>
    public byte[] Compute(ulong keyPos, ReadOnlySpan<byte> buf)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        var mac = new byte[Size];
        int err = Gmac(_handle, keyPos, buf.ToArray(), buf.Length, mac);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_gkcrypt_gmac failed: {(ChiakiError)err}.");

        return mac;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        // The free that matches the constructor. Passing this handle to chiaki_shim_gkcrypt_free
        // would run chiaki_gkcrypt_fini over a struct chiaki_gkcrypt_init never built.
        FreeForGmac(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_gen_gmac_key",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void GenGmacKey(ulong index, byte[] keyBase, byte[] iv, byte[] keyOut);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_create_for_gmac",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CreateForGmac(byte[] currentGmacKey, byte[] iv);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_free_for_gmac",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void FreeForGmac(IntPtr gkcrypt);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_gmac",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int Gmac(IntPtr gkcrypt, ulong keyPos, byte[] buf, int bufSize, byte[] gmacOut);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_gkcrypt_gmac_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GmacSize();
}
