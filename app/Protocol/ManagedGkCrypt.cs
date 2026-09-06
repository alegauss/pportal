using System.Buffers;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One step stream_connection_init_crypt takes, in the order it takes them.</summary>
public enum GkCryptStep
{
    /// <summary>The local crypt, at index 2 - what this client sends under.</summary>
    Local,

    /// <summary>The remote crypt, at index 3 - what the console sends under.</summary>
    Remote,

    /// <summary>Both handed to the takion, as a pair and in one call.</summary>
    SetOnTakion,
}

/// <summary>
/// PP731, under PP729: one gk crypt - the object the derivation, the key stream and the GMAC
/// window had no owner for.
///
/// PP415 derives the key and IV, PP416 makes the key stream, PP418 decides which GMAC window a
/// packet belongs to. All three are functions, and a session needs a thing that HOLDS the material
/// they work from and remembers which window it is on - because the window is state and a packet
/// arriving out of order has to be judged against it.
///
/// THE INDEX IS WHAT MAKES TWO OF THESE DIFFERENT. One secret and one handshake key derive both
/// streams; the only thing separating what this client encrypts with from what it decrypts with is
/// the byte fed into the derivation. A port that built one crypt and used it both ways would
/// produce a stream the console cannot read and could not read the console's - and both halves fail
/// silently, as noise.
///
/// NO KEY BUFFER, AND THAT IS A DEPARTURE WORTH NAMING. The C precomputes key stream into an
/// aligned ring behind a thread, which is why chiaki_gkcrypt_free exists and why PP368 found a bare
/// free using one after it was gone. This computes on demand instead: the same bytes, no thread,
/// and nothing to release. The C's buffer is a cache, so the OUTPUT is unchanged - which is what
/// makes it a departure that costs nothing rather than a different protocol.
/// </summary>
public sealed class ManagedGkCrypt
{
    private readonly byte[] keyBase;
    private readonly byte[] iv;
    private byte[] gmacCurrent;

    private ManagedGkCrypt(byte index, byte[] keyBase, byte[] iv, byte[] gmacBase)
    {
        Index = index;
        this.keyBase = keyBase;
        this.iv = iv;
        GmacKeyBase = gmacBase;
        gmacCurrent = [.. gmacBase];
    }

    /// <summary>Which stream this is, as the derivation's own byte.</summary>
    public byte Index { get; }

    /// <summary>The window this crypt is currently holding a key for. Zero at the start.</summary>
    public ulong GmacIndexCurrent { get; private set; }

    /// <summary>The key window zero was derived with, kept because a refresh is derived from it.</summary>
    public IReadOnlyList<byte> GmacKeyBase { get; }

    /// <summary>The key the session is holding now, which a refresh replaces.</summary>
    public IReadOnlyList<byte> GmacKeyCurrent => gmacCurrent;

    /// <summary>The stream's key material, for a caller that needs to derive beside it.</summary>
    public IReadOnlyList<byte> KeyBase => keyBase;

    /// <inheritdoc cref="KeyBase"/>
    public IReadOnlyList<byte> Iv => iv;

    /// <summary>
    /// chiaki_gkcrypt_init, without the key buffer: derive the material and the first GMAC key.
    /// </summary>
    public static ManagedGkCrypt Derive(
        byte index, ReadOnlySpan<byte> handshakeKey, ReadOnlySpan<byte> ecdhSecret)
    {
        (byte[] keyBase, byte[] iv) = GkDerivation.KeyAndIv(index, handshakeKey, ecdhSecret);

        // Window zero, which the C derives at init and copies into the current key.
        return new ManagedGkCrypt(index, keyBase, iv, GkDerivation.GmacKey(0, keyBase, iv));
    }

    /// <summary>The key stream at a position, which is what a packet is XORed against.</summary>
    public byte[] KeyStream(ulong keyPos, int length)
        => GkKeyStream.Generate(keyBase, iv, keyPos, length);

    /// <summary>
    /// PP737: the same, into a caller's span - which is what a packet path should be calling.
    ///
    /// The overload above allocates the stream by signature, once per packet. This one allocates
    /// nothing but the Aes the generator makes, which is the remainder PP737 measured and the one
    /// piece that cannot go without giving this object something to release.
    /// </summary>
    public void KeyStream(ulong keyPos, Span<byte> into)
        => GkKeyStream.Generate(keyBase, iv, keyPos, into);

    /// <summary>
    /// The GMAC key a packet at <paramref name="keyPos"/> is authenticated under.
    ///
    /// THE WINDOW IS STATE AND ONLY ONE DIRECTION MOVES IT. A packet ahead of the window refreshes
    /// the key and the session keeps it; one behind gets a key derived for its own window, used and
    /// dropped. Rolling backwards would make every packet after it fail, which is the failure PP418
    /// wrote the choice down to prevent - and it is reproduced here rather than restated, because
    /// this is the object that would have to do the rolling.
    /// </summary>
    public byte[] GmacKeyFor(ulong keyPos)
    {
        GmacKeyChoice choice = GmacKeyWindow.Choose(keyPos, GmacIndexCurrent);

        switch (choice.Action)
        {
            case GmacKeyAction.Refresh:
                gmacCurrent = GkDerivation.GmacKey(choice.Index, keyBase, iv);
                GmacIndexCurrent = choice.Index;
                return [.. gmacCurrent];

            case GmacKeyAction.Temporary:
                // Made for that window, handed back, and NOT kept.
                return GkDerivation.GmacKey(choice.Index, keyBase, iv);

            default:
                return [.. gmacCurrent];
        }
    }

    /// <summary>The IV this packet's GMAC is computed under, which advances per block.</summary>
    public byte[] GmacIvFor(ulong keyPos) => GmacKeyWindow.IvFor(iv, keyPos);

    /// <summary>
    /// PP750: chiaki_gkcrypt_encrypt - the key stream at a position, XORed over a payload in place.
    ///
    /// The stream cipher IS the XOR, so encrypt and decrypt are one operation and the C has one
    /// function for both. Every part of this existed and nothing performed it, which is why nothing
    /// in the port had ever encrypted a packet.
    ///
    /// THE POSITION NEED NOT BE ALIGNED, and that is the piece a first attempt gets wrong: the C
    /// rounds down to the block before it and reads from the padding in.
    /// <see cref="GkKeyStream.Apply"/> is where that arithmetic lives, so this and PP667's decrypt
    /// share one copy of it.
    /// </summary>
    public void Encrypt(ulong keyPos, Span<byte> payload)
        => GkKeyStream.Apply(keyBase, iv, keyPos, payload);

    /// <summary>
    /// PP750: chiaki_gkcrypt_gmac - the four-byte tag over a whole packet, written where it goes.
    ///
    /// TAKEN OVER THE PACKET INCLUDING ITS OWN MAC FIELD, which the caller has already zeroed. That
    /// is the C's order and it is the reason the field is written after the tag rather than before:
    /// a MAC computed over its own value could not be checked by anyone.
    /// </summary>
    /// <param name="keyPos">The packet's position - NOT the one its payload was encrypted at.</param>
    /// <param name="packet">The whole datagram, with the MAC field zeroed.</param>
    /// <param name="into">Where the tag goes, which is <see cref="TakionFeedbackSends.GmacSize"/> bytes.</param>
    public void Gmac(ulong keyPos, ReadOnlySpan<byte> packet, Span<byte> into)
    {
        if (into.Length != TakionFeedbackSends.GmacSize)
        {
            throw new ArgumentException(
                $"a gmac is {TakionFeedbackSends.GmacSize} bytes", nameof(into));
        }

        Ghash.Tag(GmacKeyFor(keyPos), GmacIvFor(keyPos), packet, TakionFeedbackSends.GmacSize)
            .CopyTo(into);
    }
}

/// <summary>
/// PP731: stream_connection_init_crypt - the two crypts a bang starts, and the order it starts them.
///
/// PP729 decides a bang and hands the keying to a seam. This is what the seam is for: a local crypt
/// at index 2, a remote one at index 3, both from the handshake key and the ECDH secret the bang
/// produced, and then set on the takion as a PAIR.
///
/// THE INDICES ARE NOT A DETAIL. They are what makes one side's key stream the other's, so a port
/// that swapped them would encrypt with the key the console decrypts with and the traffic would be
/// noise in both directions - with nothing failing, because both ends would be doing exactly what
/// they were told.
///
/// THE ORDER IS CARRIED AS A VALUE, which is PP731's own reason. Where the remote fails to build,
/// the C releases the local one with chiaki_gkcrypt_free and not free - PP368's finding, because a
/// gk crypt owns a key-buffer thread that fini stops and joins first, and a bare free left that
/// thread running over a freed struct. A managed port has no such bug to reproduce, and that is
/// exactly why the order is a list something reads rather than a comment nobody does.
/// </summary>
public sealed class ManagedGkCryptPair
{
    /// <summary>The index the C builds the local crypt at.</summary>
    public const byte LocalIndex = 2;

    /// <summary>And the remote, which is the next one.</summary>
    public const byte RemoteIndex = 3;

    private ManagedGkCryptPair(ManagedGkCrypt local, ManagedGkCrypt remote)
    {
        Local = local;
        Remote = remote;
    }

    /// <summary>What this client sends under.</summary>
    public ManagedGkCrypt Local { get; }

    /// <summary>What the console sends under.</summary>
    public ManagedGkCrypt Remote { get; }

    /// <summary>The three steps, in the C's order.</summary>
    public static IReadOnlyList<GkCryptStep> BuildOrder { get; } =
        [GkCryptStep.Local, GkCryptStep.Remote, GkCryptStep.SetOnTakion];

    /// <summary>
    /// What has to be released where a step fails, in the order the C releases it.
    ///
    /// Only one rung has anything to undo: a remote that failed leaves a local one built. The
    /// local's own failure has built nothing, and there is no failure after the pair is set.
    /// </summary>
    public static IReadOnlyList<GkCryptStep> ReleaseAfter(GkCryptStep failed) => failed switch
    {
        GkCryptStep.Remote => [GkCryptStep.Local],
        _ => [],
    };

    /// <summary>Both crypts, at their indices, from the material a bang produced.</summary>
    public static ManagedGkCryptPair Derive(
        ReadOnlySpan<byte> handshakeKey, ReadOnlySpan<byte> ecdhSecret)
        => new(
            ManagedGkCrypt.Derive(LocalIndex, handshakeKey, ecdhSecret),
            ManagedGkCrypt.Derive(RemoteIndex, handshakeKey, ecdhSecret));
}

/// <summary>
/// PP731: the two indices and the release, read out of streamconnection.c rather than restated.
/// </summary>
public static class ManagedGkCryptSource
{
    /// <summary>Where the pair is built.</summary>
    public const string RelativePath = StreamDispatchSource.RelativePath;

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The builder's body, or null where it is gone.</summary>
    public static string? InitCryptBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode stream_connection_init_crypt(");

    /// <summary>
    /// The index each of the two is built at, local first, as the calls spell them.
    /// </summary>
    public static IReadOnlyList<int> IndicesIn(string initCryptBody)
    {
        ArgumentNullException.ThrowIfNull(initCryptBody);

        const string call = "chiaki_gkcrypt_new(";
        var found = new List<int>();

        for (int at = initCryptBody.IndexOf(call, StringComparison.Ordinal);
             at >= 0;
             at = initCryptBody.IndexOf(call, at + call.Length, StringComparison.Ordinal))
        {
            int close = initCryptBody.IndexOf(')', at);
            if (close < 0)
                break;

            // The index is the third argument, between the chunk count and the handshake key.
            string[] arguments = initCryptBody[(at + call.Length)..close].Split(',');
            if (arguments.Length > 2 && int.TryParse(arguments[2].Trim(), out int index))
                found.Add(index);
        }

        return found;
    }

    /// <summary>
    /// Whether a failed remote still releases the local one through the wrapper, not a bare free.
    ///
    /// PP368's finding, held where it happened: a gk crypt owns a thread, and chiaki_gkcrypt_free
    /// is what stops and joins it. This port has no thread to leak, so the check is here to say the
    /// C's order is still the one the release list above models.
    /// </summary>
    public static bool AFailedRemoteStillFreesTheLocalProperly(string initCryptBody)
    {
        ArgumentNullException.ThrowIfNull(initCryptBody);

        int remote = initCryptBody.IndexOf("gkcrypt_remote = chiaki_gkcrypt_new(", StringComparison.Ordinal);
        if (remote < 0)
            return false;

        string after = initCryptBody[remote..];
        const string wrapper = "chiaki_gkcrypt_";
        const string release = "free(stream_connection->gkcrypt_local";

        var found = 0;
        for (int at = after.IndexOf(release, StringComparison.Ordinal);
             at >= 0;
             at = after.IndexOf(release, at + release.Length, StringComparison.Ordinal))
        {
            found++;

            // A bare free is what PP368 found, and `chiaki_gkcrypt_free(...)` CONTAINS `free(...)` -
            // so the test is what precedes the call, not whether the shorter spelling occurs.
            if (at < wrapper.Length
                || !after.AsSpan(at - wrapper.Length, wrapper.Length).SequenceEqual(wrapper))
            {
                return false;
            }
        }

        return found > 0;
    }

    /// <summary>Whether both are still handed to the takion in one call, after both are built.</summary>
    public static bool ThePairIsStillSetTogether(string initCryptBody)
    {
        ArgumentNullException.ThrowIfNull(initCryptBody);

        int set = initCryptBody.IndexOf(
            "chiaki_takion_set_crypt(&stream_connection->takion, stream_connection->gkcrypt_local, stream_connection->gkcrypt_remote)",
            StringComparison.Ordinal);

        int remote = initCryptBody.IndexOf(
            "gkcrypt_remote = chiaki_gkcrypt_new(", StringComparison.Ordinal);

        return set > 0 && remote > 0 && set > remote;
    }
}
