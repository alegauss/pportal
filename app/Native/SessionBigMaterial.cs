using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>What senkusha measured, which is what a launch spec describes the link with.</summary>
/// <param name="MtuIn">The inbound MTU the console agreed to.</param>
/// <param name="MtuOut">The outbound one, which is measured separately and need not match.</param>
/// <param name="RoundTripMicroseconds">The round trip, in microseconds.</param>
public readonly record struct SessionTransport(uint MtuIn, uint MtuOut, ulong RoundTripMicroseconds);

/// <summary>PP777: what the session's own rpcrypt was built from, which is what hides the spec.</summary>
/// <param name="Target">session-&gt;target, which the crypt's schedule differs by.</param>
/// <param name="Nonce">The nonce ctrl's handshake decoded, sixteen bytes.</param>
/// <param name="Morning">The registration's key, the same size.</param>
public readonly record struct SessionAuthMaterial(ChiakiTarget Target, byte[] Nonce, byte[] Morning);

/// <summary>PP777: the stream shape the connect info asked for, which the spec announces.</summary>
/// <param name="Width">The picture's width.</param>
/// <param name="Height">And its height.</param>
/// <param name="MaxFps">The frame rate ceiling.</param>
/// <param name="BitrateKbps">What the spec spends as bw_kbps_sent.</param>
/// <param name="Codec">Which codec the preset chose.</param>
public readonly record struct SessionVideoProfile(
    uint Width, uint Height, uint MaxFps, uint BitrateKbps, ChiakiCodec Codec);

/// <summary>The ecdh half of a BIG: a public key and the signature over the handshake key.</summary>
/// <param name="PublicKey">Copied out, because the pair it came from is freed a step later.</param>
/// <param name="Signature">The same, over the session's own handshake key.</param>
public readonly record struct SessionEcdhMaterial(byte[] PublicKey, byte[] Signature);

/// <summary>
/// PP766: the three things a managed BIG needs out of a live session.
///
/// PP765 measured the eleven parts a run host takes and found ten of them compose from work that
/// shipped. The eleventh is the BIG - the message that STARTS a stream, which every test has been
/// handing the host a heartbeat in place of - and BigMessage.Encode wants five arguments, three of
/// which belong to the C.
///
/// THE SESSION ID IS THE BIG'S SESSION KEY, which is worth saying because the names disagree.
/// streamconnection.c fills the payload's session_key field from session-&gt;session_id, and a port
/// that went looking for something called a session key would find the regist key or the handshake
/// key and send the wrong one.
///
/// AND THE ECDH IS COPIED. session.c creates the pair on the line before the run and frees it on
/// the line after, so a reader handing back a pointer would hand back something freed a step later.
/// Both sizes cross in and out - the caller offers room and is told what was written - which is the
/// same shape chiaki_ecdh_get_local_pub_key has and the same two stack arrays the C's own send_big
/// gives it.
/// </summary>
public static class SessionBigMaterial
{
    /// <summary>CHIAKI_SESSION_ID_SIZE_MAX, which the C zero-terminates inside.</summary>
    public const int SessionIdBytes = 80;

    /// <summary>What the C's own send_big offers the key, so a shorter buffer is this side's bug.</summary>
    public const int PublicKeyBytes = 128;

    /// <summary>And the signature, which is a SHA-256 HMAC.</summary>
    public const int SignatureBytes = 32;

    /// <summary>CHIAKI_HANDSHAKE_KEY_SIZE, which the launch spec carries base64'd.</summary>
    public const int HandshakeKeyBytes = 0x10;

    /// <summary>
    /// The handshake key, which is the fourth thing and was not in this task's first reading.
    ///
    /// It signs the ecdh material AND is base64'd into the launch spec's JSON, so the managed side
    /// wants it in its own right rather than only inside a signature. Null before a session has
    /// one, which is every moment before the stream phase generates it.
    /// </summary>
    public static byte[]? HandshakeKeyOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var key = new byte[HandshakeKeyBytes];
        if (!SessionHandshakeKey(session.Handle, key, key.Length))
            return null;

        // All-zero is what the field holds before chiaki_random_bytes_crypt fills it, and a key of
        // zeroes base64s to a perfectly well-formed string that no console agreed to.
        return Array.TrueForAll(key, one => one == 0) ? null : key;
    }

    /// <summary>
    /// The session id, which is what the BIG sends as its session key.
    ///
    /// Null where the session has none yet - before ctrl's handshake there is nothing to read, and
    /// that is an ordinary answer rather than a failure.
    /// </summary>
    public static string? IdOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var buffer = new byte[SessionIdBytes];
        if (!SessionId(session.Handle, buffer, buffer.Length))
            return null;

        int end = Array.IndexOf(buffer, (byte)0);
        return end <= 0 ? null : Encoding.UTF8.GetString(buffer, 0, end);
    }

    /// <summary>What senkusha measured, or null where it has not run.</summary>
    public static SessionTransport? TransportOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!SessionTransportRaw(session.Handle, out uint mtuIn, out uint mtuOut, out ulong rtt))
            return null;

        // Zero is what the fields hold before senkusha writes them, and a launch spec built from a
        // zero MTU describes a link that cannot carry a frame.
        return mtuIn == 0 || mtuOut == 0 ? null : new SessionTransport(mtuIn, mtuOut, rtt);
    }

    /// <summary>
    /// The public key and its signature, copied out of a pair that outlives neither.
    ///
    /// Null where the ecdh has not been created, which is every moment outside the stream phase.
    /// </summary>
    public static SessionEcdhMaterial? EcdhOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var key = new byte[PublicKeyBytes];
        var sig = new byte[SignatureBytes];
        int keySize = key.Length;
        int sigSize = sig.Length;

        if (!SessionEcdhMaterialRaw(session.Handle, key, ref keySize, sig, ref sigSize))
            return null;

        if (keySize <= 0 || sigSize <= 0)
            return null;

        return new SessionEcdhMaterial(key[..keySize], sig[..sigSize]);
    }

    /// <summary>
    /// PP773: the console's half of the same exchange, derived against the session's own pair.
    ///
    /// <see cref="EcdhOf"/> sends the local public key out in the BIG and this takes the answer back
    /// in, so the two are one exchange read from both ends. It must be the SESSION's ecdh: the
    /// private half that signed the outbound key is the only one that derives against the reply, and
    /// a freshly created pair produces thirty-two bytes that key a session no console can read.
    ///
    /// Null is the C's refusal - a key or a signature the pair will not accept - and is what
    /// <see cref="Protocol.BangHandler"/> already has a path for.
    /// </summary>
    public static byte[]? DeriveSecret(
        ChiakiSession session, ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (remotePubKey.IsEmpty || remoteSig.IsEmpty)
            return null;

        var secret = new byte[EcdhSecretBytes];

        return SessionDeriveSecret(
            session.Handle,
            remotePubKey.ToArray(), remotePubKey.Length,
            remoteSig.ToArray(), remoteSig.Length,
            secret, secret.Length)
            ? secret
            : null;
    }

    /// <summary>CHIAKI_ECDH_SECRET_SIZE, which the derivation writes and does not take a size for.</summary>
    public const int EcdhSecretBytes = 32;

    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE, which both halves of the auth crypt are.</summary>
    public const int RpCryptKeyBytes = 0x10;

    /// <summary>
    /// PP777: what the launch spec is HIDDEN under, which the composition root was inventing.
    ///
    /// The C encrypts the spec with session-&gt;rpcrypt, and that crypt is init_auth over the target,
    /// the nonce ctrl's handshake decoded and the morning the registration holds. A root that built
    /// one from sixteen zero bytes each way produced base64 the console cannot read - and a console
    /// answers that by ACKNOWLEDGING the message and never banging, which is the failure a live
    /// trial read as two DataAcks and no answer.
    ///
    /// All three or none, as <see cref="TransportOf"/> is: a caller with a target and no nonce would
    /// build a crypt wrong in a way nothing reports.
    /// </summary>
    public static SessionAuthMaterial? AuthOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var nonce = new byte[RpCryptKeyBytes];
        var morning = new byte[RpCryptKeyBytes];

        return SessionAuthMaterialRaw(session.Handle, out int target, nonce, nonce.Length, morning, morning.Length)
            ? new SessionAuthMaterial((ChiakiTarget)target, nonce, morning)
            : null;
    }

    /// <summary>
    /// PP777: and what the spec DESCRIBES, which the root was spelling as constants.
    ///
    /// Right for the preset this tree happens to ask for and wrong the moment a caller asks for
    /// another - and a console told a shape the stream will not have has been told something false
    /// about the session it is about to serve.
    /// </summary>
    public static SessionVideoProfile? ProfileOf(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!SessionVideoProfileRaw(
                session.Handle, out uint width, out uint height, out uint maxFps, out uint bitrate, out int codec))
        {
            return null;
        }

        // Zero is what the preset writes for a resolution it does not know, and a spec describing a
        // stream of no size is one the console refuses with nothing to say why.
        return width == 0 || height == 0
            ? null
            : new SessionVideoProfile(width, height, maxFps, bitrate, (ChiakiCodec)codec);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_auth_material",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionAuthMaterialRaw(
        IntPtr session, out int target, byte[] nonce, int nonceCapacity, byte[] morning, int morningCapacity);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_video_profile",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionVideoProfileRaw(
        IntPtr session, out uint width, out uint height, out uint maxFps, out uint bitrate, out int codec);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_derive_secret",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionDeriveSecret(
        IntPtr session,
        byte[] remoteKey, int remoteKeySize,
        byte[] remoteSig, int remoteSigSize,
        byte[] secret, int secretCapacity);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_id",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionId(IntPtr session, byte[] buffer, int capacity);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_transport",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionTransportRaw(
        IntPtr session, out uint mtuIn, out uint mtuOut, out ulong rttUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_handshake_key",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionHandshakeKey(IntPtr session, byte[] buffer, int capacity);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_session_ecdh_material",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SessionEcdhMaterialRaw(
        IntPtr session, byte[] publicKey, ref int publicKeySize, byte[] signature, ref int signatureSize);
}
