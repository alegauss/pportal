using ChiakiNg.Native;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP773: the derivation a bang leads to, over the session's own ECDH pair.
///
/// <see cref="IBangKeying"/> has stood in front of OpenSSL since PP729 wrote the bang handler, and
/// SeamReach listed it as a seam on purpose - the port keeps that behind a boundary. What PP773
/// found is that a seam with nothing behind it is not a boundary: the arrivals now reach the
/// handler, and every console's bang was refused at the derive.
///
/// THE PAIR IS THE SESSION'S AND NOT A FRESH ONE, which is the whole reason this takes a session.
/// The BIG carried session-&gt;ecdh's public half and its signature, so the console's answer can only
/// be derived against the private half that signed it. chiaki_shim_ecdh_create makes a pair no
/// console has seen, and a secret derived from one is thirty-two bytes of nothing that key a session
/// the console cannot read - which does not fail, it produces a stream of garbage.
///
/// AND THE CRYPT IS MANAGED. PP731 already ports stream_connection_init_crypt: two crypts at indices
/// two and three, derived from the handshake key and the secret, local first because a remote that
/// fails leaves a local to release. So only the derivation crosses the boundary, and what it hands
/// back is thirty-two bytes rather than a pointer to something the C owns.
///
/// THE TAKION IS TOLD, which is chiaki_takion_set_crypt and the third step of PP731's order. Without
/// it the pair exists and nothing sends under it, and the console's first encrypted answer arrives
/// against a MAC gate that has no key to check it with.
/// </summary>
/// <param name="session">The C session, whose ecdh and handshake key this pairs. Not owned.</param>
/// <param name="takion">The takion the pair is set on, which is the C's third step.</param>
public sealed class SessionBangKeying(ChiakiSession session, ManagedTakion takion) : IBangKeying
{
    private readonly ChiakiSession session =
        session ?? throw new ArgumentNullException(nameof(session));

    private readonly ManagedTakion takion =
        takion ?? throw new ArgumentNullException(nameof(takion));

    private byte[]? secret;

    /// <summary>The pair, once a bang has produced one - and null before or after a refusal.</summary>
    public ManagedGkCryptPair? Crypt { get; private set; }

    /// <summary>
    /// chiaki_ecdh_derive_secret, over the console's public key and its signature.
    ///
    /// False is the C's own refusal and not an exception: a bang carrying a key this session cannot
    /// derive against is a message the handler answers with state_failed, which is a path
    /// <see cref="BangHandler"/> already has.
    /// </summary>
    public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig)
    {
        byte[]? derived = SessionBigMaterial.DeriveSecret(session, remotePubKey, remoteSig);

        secret = derived;
        return derived is not null;
    }

    /// <summary>
    /// stream_connection_init_crypt: both crypts from the secret, then the takion is told.
    ///
    /// Only reachable past a derive that succeeded, which is where the C calls it too - so the
    /// secret is never null here, and a null one is answered rather than dereferenced because this
    /// is a public method and the C's ordering is not this object's to enforce.
    /// </summary>
    public bool InitCrypt()
    {
        byte[]? material = secret;
        byte[]? handshakeKey = SessionBigMaterial.HandshakeKeyOf(session);

        if (material is null || handshakeKey is null)
            return false;

        ManagedGkCryptPair pair = ManagedGkCryptPair.Derive(handshakeKey, material);

        // The C's third step. Only the local crypt has a home on this takion today - what it sends
        // under - and the remote is kept on the pair for the MAC gate that reads it.
        Crypt = pair;
        takion.LocalCrypt = pair.Local;
        takion.CryptAvailable = true;

        // PP795: AND THE AV ARM, which is the same moment for the same reason. Its sink decrypts
        // each arriving packet against the remote crypt's key base and IV, so it could not have
        // been built when the takion was - and a run without it reaches the idle loop, receives
        // fourteen thousand datagrams and decodes none of them.
        //
        // Whoever holds the receivers installs it, because this object holds neither. A caller
        // that supplies no installer keys the session and leaves the picture where it was, which
        // is what every test of the keying does.
        InstallArm?.Invoke(pair);

        return true;
    }

    /// <summary>
    /// PP795: what to do with the pair once it exists, which is where the AV arm is joined.
    ///
    /// A callback rather than a receiver, because this object knows the session and the takion and
    /// deliberately not the video receiver: the run makes those and the composition root wires the
    /// two together. Absent leaves the crypt built and the arm uninstalled, which is every case
    /// that is not a live stream.
    /// </summary>
    public Action<ManagedGkCryptPair>? InstallArm { get; set; }
}
