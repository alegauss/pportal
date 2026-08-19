using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP105: what stands between a forged bang and a keyed session, read out of lib/.
///
/// PP105 established that chiaki_ecdh_derive_secret ignores the console's signature over its
/// public key - asserted behaviourally against test/gkcrypt.c's recorded exchange, where a
/// flipped byte in the signature still returns the same secret. What it could not establish was
/// REACHABILITY: whether a missing check is also an exploitable one, which is the difference
/// between a note and a report.
///
/// It is answerable from the source, and this is the answer, as five facts each of which is a
/// step an attacker does or does not have to take. Nothing here attacks anything: they are
/// predicates over lib/'s own text, and they exist so the conclusion drawn from them stops being
/// true out loud if the code moves underneath it.
///
/// The conclusion, which the assertions in SelfTest carry one by one:
///
/// The bang is accepted with no cryptographic authentication at all. Its signature is unread,
/// and the MAC that covers every other packet is not checked either - takion_handle_packet_mac
/// returns SUCCESS outright while gkcrypt_remote is NULL, which is by definition the state until
/// the bang itself establishes the secret. So the two checks that could refuse a forged bang are
/// the one that is missing and the one that cannot exist yet.
///
/// What remains is not cryptography. The socket is connect()ed on both transports, so the kernel
/// drops datagrams that do not come from the console's address; and takion_parse_message refuses
/// a message whose tag is not the local one, which is 32 random bits. Off-path that is three
/// things at once - spoof the source address, guess the tag, and land inside the window where
/// the state is still STATE_EXPECT_BANG. On-path it is none of them: the tag travels in the
/// clear in the INIT message, the address is observed, and the window is the race.
///
/// So: on-path, a forged bang substitutes an attacker's ECDH public key, the client derives a
/// shared secret with the attacker, stream_connection_init_crypt keys gkcrypt from it, and every
/// MAC from then on verifies correctly - against the wrong peer. The unread signature is exactly
/// the check that would refuse it, because the handshake key it is computed under reaches the
/// console inside an rpcrypt-encrypted launch spec that only the registered console can read.
///
/// Which makes this a missing check with a consequence, not an omission. Whether that is
/// reported upstream is not this port's call to make.
/// </summary>
public static partial class BangReachability
{
    /// <summary>Where the unchecked MAC and the tag check live.</summary>
    public const string TakionRelativePath = @"lib\src\takion.c";

    /// <summary>Where the bang is accepted and the secret derived from it.</summary>
    public const string StreamConnectionRelativePath = @"lib\src\streamconnection.c";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateTakion() => SanitizerSource.LocateRelative(TakionRelativePath);

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? LocateStreamConnection()
        => SanitizerSource.LocateRelative(StreamConnectionRelativePath);

    /// <summary>
    /// Fact 1. The MAC check passes everything while the session is unkeyed.
    ///
    /// The first two lines of takion_handle_packet_mac are the whole of it: no gkcrypt_remote, no
    /// check, success. This is not a defect - there is no key to check against yet - but it is
    /// what makes the bang's own signature the only authentication available at that moment, and
    /// therefore what turns PP105 from a redundant check into the only one.
    /// </summary>
    public static bool MacPassesWhileUnkeyed(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return UnkeyedMacRegex().IsMatch(takionText);
    }

    /// <summary>
    /// Fact 2. The bang is accepted before crypt is initialised, in that order.
    ///
    /// stream_connection_takion_data_expect_bang derives the secret and only then inits crypt, so
    /// there is no ordering in which the bang could have been covered by a MAC.
    /// </summary>
    public static bool SecretIsDerivedBeforeCryptInit(string streamConnectionText)
    {
        ArgumentNullException.ThrowIfNull(streamConnectionText);
        int derive = streamConnectionText.IndexOf("chiaki_ecdh_derive_secret", StringComparison.Ordinal);
        int init = streamConnectionText.IndexOf("stream_connection_init_crypt", StringComparison.Ordinal);
        if (derive < 0 || init < 0)
            return false;

        // The declaration of init_crypt precedes both, so the call after the derive is what counts.
        int callAfter = streamConnectionText.IndexOf(
            "stream_connection_init_crypt", derive, StringComparison.Ordinal);
        return callAfter > derive;
    }

    /// <summary>
    /// Fact 3. The tag is checked, and is 32 random bits - the off-path attacker's real cost.
    ///
    /// Both halves matter. A tag that stopped being checked would drop the cost to zero; a tag
    /// that stopped being random would drop it further.
    /// </summary>
    public static bool TagIsCheckedAndRandom(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("if(msg->tag != takion->tag_local)", StringComparison.Ordinal)
            && TagRandomRegex().IsMatch(takionText);
    }

    /// <summary>
    /// Fact 4. And it travels in the clear, which is why that cost is off-path only.
    ///
    /// takion_send_message_init puts tag_local in the INIT message, before any key exists. An
    /// attacker who can read one packet of the handshake has it.
    /// </summary>
    public static bool TagIsSentInTheClear(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("init_payload.tag = takion->tag_local", StringComparison.Ordinal);
    }

    /// <summary>
    /// Fact 5. The socket is connected, so the kernel filters by source address.
    ///
    /// This is what makes the attack on-path rather than blind, and it is the only guard here
    /// that is not cryptographic. Asserted per file because the two transports reach it
    /// separately - the local one connects in takion.c, the PSN one in the holepunch before
    /// handing the socket over.
    /// </summary>
    public static bool SocketIsConnected(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ConnectRegex().IsMatch(text);
    }

    [GeneratedRegex(
        @"takion_handle_packet_mac\([^)]*\)\s*\r?\n\{\s*\r?\n\s*if\(!takion->gkcrypt_remote\)\s*\r?\n\s*return CHIAKI_ERR_SUCCESS;")]
    private static partial Regex UnkeyedMacRegex();

    [GeneratedRegex(@"takion->tag_local\s*=\s*chiaki_random_32\(\)")]
    private static partial Regex TagRandomRegex();

    [GeneratedRegex(@"\bconnect\(\s*(takion->sock|selected_sock)\s*,")]
    private static partial Regex ConnectRegex();
}
