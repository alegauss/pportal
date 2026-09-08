using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>What a provider says it will accept, in BYTES.</summary>
/// <param name="Minimum">The shortest tag it will produce.</param>
/// <param name="Maximum">The longest.</param>
/// <param name="Increment">The step between legal lengths; zero where only one is legal.</param>
public readonly record struct CngTagLengths(int Minimum, int Maximum, int Increment)
{
    /// <summary>Whether a length is one this provider offers.</summary>
    public bool Allows(int length)
        => length >= Minimum
            && length <= Maximum
            && (Increment == 0 ? length == Minimum : (length - Minimum) % Increment == 0);
}

/// <summary>
/// PP817: AES-GCM as the PLATFORM offers it, because PP298 asked a wrapper and not the platform.
///
/// PP298's measurement is right and its conclusion was about the wrong thing. AesGcm.NonceByteSizes
/// is 12 to 12 and TagByteSizes 12 to 16 - measured, not assumed - and chiaki passes a sixteen-byte
/// IV and keeps four bytes of tag. Both are ordinary GCM. Neither is in that TYPE's contract, and
/// AesGcm is a wrapper whose contract is narrower than the algorithm underneath it.
///
/// THE ALGORITHM UNDERNEATH IT IS CNG, and gkcrypt.c reaches the same two departures through
/// OpenSSL by simply asking: EVP_CTRL_GCM_SET_IVLEN with the block size, EVP_CTRL_GCM_GET_TAG with
/// four. BCryptEncrypt takes a nonce as a buffer AND a length rather than a fixed-size array, and
/// BCRYPT_AUTH_TAG_LENGTH reports the range a provider supports rather than a constant a wrapper
/// picked. So the two questions have answers, and this is where they are read.
///
/// NOTHING HERE REPLACES <see cref="ChiakiNg.Protocol.Ghash"/> ON ITS OWN. A tag this produces is
/// worth having only where it equals the one the port already computes, which is held to chiaki's,
/// which is OpenSSL's. The oracle costs nothing because it already exists.
///
/// EVERY ENTRY POINT ANSWERS null RATHER THAN THROWING where CNG refuses. A refusal is the finding
/// on a machine whose provider is narrower, not an error - and a probe that threw would make "this
/// provider will not do it" indistinguishable from a mistake in the interop.
/// </summary>
public static class CngGcm
{
    private const string Bcrypt = "bcrypt.dll";

    private const string AesAlgorithm = "AES";
    private const string ChainingModeProperty = "ChainingMode";
    private const string ChainMode = "ChainingModeGCM";
    private const string AuthTagLengthProperty = "AuthTagLength";
    private const string ObjectLengthProperty = "ObjectLength";

    private const int StatusSuccess = 0;

    /// <summary>BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO_VERSION.</summary>
    private const int AuthModeInfoVersion = 1;

    /// <summary>
    /// The tag lengths this machine's AES-GCM provider reports, or null where it will not say.
    ///
    /// BCRYPT_AUTH_TAG_LENGTH fills a BCRYPT_KEY_LENGTHS_STRUCT, and for this property the three
    /// numbers are BYTES - unlike BCRYPT_KEY_LENGTHS itself, where they are bits. That difference
    /// is the one thing about this call worth writing down.
    ///
    /// THE CHAINING MODE HAS TO BE SET FIRST, and skipping that is what a first attempt does. An
    /// AES provider opens in CBC, which has no authentication tag, so the property is simply not
    /// there and the call fails - which reads exactly like the refusal this task exists to look
    /// for. Asking a provider that has not been told it is doing GCM answers nothing about GCM.
    /// </summary>
    public static CngTagLengths? TagLengths()
    {
        IntPtr algorithm = IntPtr.Zero;

        try
        {
            if (BCryptOpenAlgorithmProvider(out algorithm, AesAlgorithm, null, 0) != StatusSuccess)
                return null;

            if (BCryptSetProperty(algorithm, ChainingModeProperty, ChainMode, (ChainMode.Length + 1) * 2, 0)
                != StatusSuccess)
            {
                return null;
            }

            var lengths = new byte[12];

            if (BCryptGetProperty(algorithm, AuthTagLengthProperty, lengths, lengths.Length, out int written, 0)
                    != StatusSuccess
                || written < lengths.Length)
            {
                return null;
            }

            return new CngTagLengths(
                BitConverter.ToInt32(lengths, 0),
                BitConverter.ToInt32(lengths, 4),
                BitConverter.ToInt32(lengths, 8));
        }
        finally
        {
            if (algorithm != IntPtr.Zero)
                BCryptCloseAlgorithmProvider(algorithm, 0);
        }
    }

    /// <summary>
    /// The GCM tag over additional data only - a GMAC - with a nonce of ANY length.
    /// </summary>
    /// <param name="key">The AES key.</param>
    /// <param name="iv">The nonce. Sixteen bytes is the case that PP298 said could not be asked for.</param>
    /// <param name="additionalData">The authenticated message; there is no ciphertext.</param>
    /// <param name="tagLength">How many bytes to keep - four for chiaki, if the provider allows it.</param>
    /// <returns>The tag, or null where this provider refused any step of it.</returns>
    /// <remarks>
    /// TRUNCATION IS THE CALLER'S WHERE CNG WILL NOT DO IT. The C asks OpenSSL for four bytes
    /// outright; a provider whose minimum is longer can still produce the same four, because GCM's
    /// tag is a prefix - a shorter tag IS the leading bytes of a longer one. So this asks for the
    /// shortest length the provider allows at or above what was wanted, and cuts. That is what
    /// GET_TAG with four does underneath, and it is why a narrow minimum is not a refusal.
    /// </remarks>
    public static byte[]? Tag(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> additionalData, int tagLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagLength);

        if (TagLengths() is not { } lengths)
            return null;

        // The shortest legal length that is not shorter than what was asked for.
        int produce = tagLength;
        while (produce <= lengths.Maximum && !lengths.Allows(produce))
            produce++;

        if (produce > lengths.Maximum)
            return null;

        IntPtr algorithm = IntPtr.Zero;
        IntPtr handle = IntPtr.Zero;
        byte[]? keyObject = null;

        try
        {
            if (BCryptOpenAlgorithmProvider(out algorithm, AesAlgorithm, null, 0) != StatusSuccess)
                return null;

            if (BCryptSetProperty(algorithm, ChainingModeProperty, ChainMode, (ChainMode.Length + 1) * 2, 0)
                != StatusSuccess)
            {
                return null;
            }

            var objectLength = new byte[4];

            if (BCryptGetProperty(algorithm, ObjectLengthProperty, objectLength, 4, out _, 0) != StatusSuccess)
                return null;

            keyObject = new byte[BitConverter.ToInt32(objectLength, 0)];

            if (BCryptGenerateSymmetricKey(
                    algorithm, out handle, keyObject, keyObject.Length, key.ToArray(), key.Length, 0)
                != StatusSuccess)
            {
                return null;
            }

            var nonce = iv.ToArray();
            var authData = additionalData.ToArray();
            var tag = new byte[produce];

            var info = new AuthenticatedCipherModeInfo
            {
                Size = Marshal.SizeOf<AuthenticatedCipherModeInfo>(),
                Version = AuthModeInfoVersion,
            };

            // Pinned rather than marshalled: the struct holds raw pointers and CNG reads them
            // during the call, so the buffers have to stay where they were put.
            GCHandle pinnedNonce = GCHandle.Alloc(nonce, GCHandleType.Pinned);
            GCHandle pinnedAuth = GCHandle.Alloc(authData, GCHandleType.Pinned);
            GCHandle pinnedTag = GCHandle.Alloc(tag, GCHandleType.Pinned);

            try
            {
                info.Nonce = pinnedNonce.AddrOfPinnedObject();
                info.NonceSize = nonce.Length;
                info.AuthData = authData.Length == 0 ? IntPtr.Zero : pinnedAuth.AddrOfPinnedObject();
                info.AuthDataSize = authData.Length;
                info.Tag = pinnedTag.AddrOfPinnedObject();
                info.TagSize = tag.Length;

                // No plaintext at all, which is what makes this a GMAC rather than an encryption.
                int status = BCryptEncrypt(
                    handle, IntPtr.Zero, 0, ref info, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, 0);

                if (status != StatusSuccess)
                    return null;
            }
            finally
            {
                pinnedNonce.Free();
                pinnedAuth.Free();
                pinnedTag.Free();
            }

            return tag.Length == tagLength ? tag : tag[..tagLength];
        }
        finally
        {
            if (handle != IntPtr.Zero)
                BCryptDestroyKey(handle);

            if (algorithm != IntPtr.Zero)
                BCryptCloseAlgorithmProvider(algorithm, 0);
        }
    }

    /// <summary>BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AuthenticatedCipherModeInfo
    {
        public int Size;
        public int Version;
        public IntPtr Nonce;
        public int NonceSize;
        public IntPtr AuthData;
        public int AuthDataSize;
        public IntPtr Tag;
        public int TagSize;
        public IntPtr MacContext;
        public int MacContextSize;
        public int AadSize;
        public long DataSize;
        public int Flags;
    }

    [DllImport(Bcrypt, CharSet = CharSet.Unicode)]
    private static extern int BCryptOpenAlgorithmProvider(
        out IntPtr algorithm, string algorithmId, string? implementation, int flags);

    [DllImport(Bcrypt)]
    private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);

    [DllImport(Bcrypt, CharSet = CharSet.Unicode)]
    private static extern int BCryptGetProperty(
        IntPtr handle, string property, byte[] output, int outputSize, out int written, int flags);

    [DllImport(Bcrypt, CharSet = CharSet.Unicode)]
    private static extern int BCryptSetProperty(
        IntPtr handle, string property, string input, int inputSize, int flags);

    [DllImport(Bcrypt)]
    private static extern int BCryptGenerateSymmetricKey(
        IntPtr algorithm, out IntPtr key, byte[] keyObject, int keyObjectSize,
        byte[] secret, int secretSize, int flags);

    [DllImport(Bcrypt)]
    private static extern int BCryptDestroyKey(IntPtr key);

    [DllImport(Bcrypt)]
    private static extern int BCryptEncrypt(
        IntPtr key, IntPtr input, int inputSize, ref AuthenticatedCipherModeInfo modeInfo,
        IntPtr iv, int ivSize, IntPtr output, int outputSize, out int written, int flags);
}
