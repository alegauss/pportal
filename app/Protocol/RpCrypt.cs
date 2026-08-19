using System.Runtime.InteropServices;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: the registration crypto, driven from managed code so both implementations can be run on
/// one input.
///
/// There is no specification for this protocol, so the oracle for a rewrite is the C code it
/// replaces plus whatever real hardware already agreed to. For this module both exist:
/// test/rpcrypt.c holds nonces, morning keys and the exact bytes a console produced from them.
/// <see cref="CryptoVectors"/> reads those out of the C suite rather than copying them, so the two
/// sides cannot drift into agreeing with themselves - which is the whole failure PP82 named.
///
/// Today the "second implementation" this compares is the seam itself. When Block F lands a
/// managed key derivation, it runs against the same vectors from the same file and the harness
/// becomes the differential comparison PP23 asks for without changing shape.
/// </summary>
public sealed class RpCrypt : IDisposable
{
    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE, read from the shim rather than assumed.</summary>
    public static int KeySize => RpCryptKeySize();

    private IntPtr _handle;

    /// <summary>A crypt initialised for the auth exchange from a nonce and a morning key.</summary>
    public RpCrypt(ChiakiTarget target, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> morning)
    {
        RequireKeySize(nonce, nameof(nonce));
        RequireKeySize(morning, nameof(morning));

        _handle = RpCryptCreateAuth((int)target, nonce.ToArray(), morning.ToArray());
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_rpcrypt_create_auth returned null.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(RpCrypt));

    /// <summary>
    /// chiaki_rpcrypt_bright_ambassador: the two keys a nonce and a morning key derive to.
    ///
    /// The target is part of the derivation and not a label on it - a PS4 before firmware 10 and
    /// one after it produce different keys from the same two inputs, which is why the vectors come
    /// in pairs.
    /// </summary>
    public static (byte[] Bright, byte[] Ambassador) BrightAmbassador(
        ChiakiTarget target, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> morning)
    {
        RequireKeySize(nonce, nameof(nonce));
        RequireKeySize(morning, nameof(morning));

        var bright = new byte[KeySize];
        var ambassador = new byte[KeySize];
        if (!RpCryptBrightAmbassador((int)target, bright, ambassador, nonce.ToArray(), morning.ToArray()))
            throw new InvalidOperationException("chiaki_rpcrypt_bright_ambassador refused its arguments.");

        return (bright, ambassador);
    }

    /// <summary>The initialisation vector a counter's block is encrypted under.</summary>
    public byte[] GenerateIv(ulong counter)
    {
        var iv = new byte[KeySize];
        int err = RpCryptGenerateIv(Handle, counter, iv);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_rpcrypt_generate_iv failed: {(ChiakiError)err}.");

        return iv;
    }

    public byte[] Encrypt(ulong counter, ReadOnlySpan<byte> plain) => Crypt(counter, plain, encrypt: true);

    public byte[] Decrypt(ulong counter, ReadOnlySpan<byte> cipher) => Crypt(counter, cipher, encrypt: false);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        RpCryptFree(_handle);
        _handle = IntPtr.Zero;
    }

    private byte[] Crypt(ulong counter, ReadOnlySpan<byte> input, bool encrypt)
    {
        byte[] inBuf = input.ToArray();
        var outBuf = new byte[inBuf.Length];
        int err = encrypt
            ? RpCryptEncrypt(Handle, counter, inBuf, outBuf, inBuf.Length)
            : RpCryptDecrypt(Handle, counter, inBuf, outBuf, inBuf.Length);

        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"rpcrypt failed: {(ChiakiError)err}.");

        return outBuf;
    }

    private static void RequireKeySize(ReadOnlySpan<byte> value, string name)
    {
        if (value.Length != KeySize)
            throw new ArgumentException($"{name} is {KeySize} bytes, not {value.Length}.", name);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_key_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RpCryptKeySize();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_bright_ambassador",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool RpCryptBrightAmbassador(
        int target, byte[] bright, byte[] ambassador, byte[] nonce, byte[] morning);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_create_auth",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr RpCryptCreateAuth(int target, byte[] nonce, byte[] morning);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void RpCryptFree(IntPtr rpcrypt);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_generate_iv",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RpCryptGenerateIv(IntPtr rpcrypt, ulong counter, byte[] iv);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_encrypt",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RpCryptEncrypt(IntPtr rpcrypt, ulong counter, byte[] input, byte[] output, int size);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_rpcrypt_decrypt",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int RpCryptDecrypt(IntPtr rpcrypt, ulong counter, byte[] input, byte[] output, int size);
}
