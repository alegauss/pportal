using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one ctrl send does to the local crypt counter.</summary>
/// <param name="EncryptAt">The counter value the payload is encrypted at.</param>
/// <param name="Next">The counter afterwards.</param>
/// <param name="Spent">Whether a value was consumed at all.</param>
public readonly record struct CtrlSendSpend(ulong EncryptAt, ushort Next, bool Spent);

/// <summary>
/// PP448: the counter ctrl_message_send encrypts at, which for one type on one path is not the one it
/// consumed.
///
/// PP356 modelled what the CONNECT spends - three to six values before a single ctrl message goes
/// out. This is the other half: what each SEND then picks. Over rudp a LOGIN_PIN_REP consumes
/// crypt_counter_local++ like everything else and encrypts at that value MINUS ONE, one behind every
/// other message. Off rudp, and for every other type, the value consumed is the value used.
///
/// A PAYLOADLESS MESSAGE SPENDS NOTHING. The encryption sits behind `if(payload)`, so a send with no
/// payload never reaches the counter - it frames a header and goes. Modelled because a managed loop
/// that incremented per message rather than per encrypted payload would drift by exactly the number
/// of bare messages it sent.
///
/// THE UNDERFLOW IS FAITHFUL, NOT FIXED. local_counter is uint16_t and chiaki_rpcrypt_encrypt takes
/// uint64_t, so at counter zero the C computes -1 as an int and converts it: 0xFFFFFFFFFFFFFFFF, not
/// 0xFFFF. Unreachable, because the connect zeroes the counter and then spends at least three - and
/// asserted here as the arithmetic the C performs rather than the arithmetic it looks like.
/// </summary>
public static class CtrlSendCounter
{
    /// <summary>The one type the quirk applies to.</summary>
    public const ushort LoginPinRep = 0x8004;

    /// <summary>
    /// What a send does to the counter.
    /// </summary>
    /// <param name="counter">The counter before the send.</param>
    /// <param name="type">The ctrl message type.</param>
    /// <param name="rudp">Whether the session is over rudp.</param>
    /// <param name="hasPayload">Whether a payload pointer is present - a null one skips encryption.</param>
    public static CtrlSendSpend Spend(ushort counter, ushort type, bool rudp, bool hasPayload)
    {
        if (!hasPayload)
            return new CtrlSendSpend(counter, counter, Spent: false);

        ushort next = (ushort)(counter + 1);

        // The quirk, as the C spells it: uint16_t local_counter = crypt_counter_local++, then
        // encrypt at local_counter - 1. The subtraction happens in int and lands in a uint64_t.
        ulong at = rudp && type == LoginPinRep
            ? (ulong)(long)((int)counter - 1)
            : counter;

        return new CtrlSendSpend(at, next, Spent: true);
    }

    /// <summary>ctrl.c, where the send lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the quirk is still written the way this models it.
    ///
    /// Read from code and not from prose: this file's own summary spells the condition out.
    /// </summary>
    public static bool TheQuirkIsStillThere(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return code.Contains("rudp && type == CTRL_MESSAGE_TYPE_LOGIN_PIN_REP", StringComparison.Ordinal)
            && code.Contains("local_counter - 1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the encryption is still behind a payload test, which is what makes a bare message
    /// free.
    /// </summary>
    public static bool EncryptionIsStillBehindAPayloadTest(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        int send = code.IndexOf(
            "static ChiakiErrorCode ctrl_message_send(ChiakiCtrl *ctrl, uint16_t type",
            StringComparison.Ordinal);

        if (send < 0)
            return false;

        // The second declaration is the definition; take the body after it.
        int body = code.IndexOf(
            "static ChiakiErrorCode ctrl_message_send(ChiakiCtrl *ctrl, uint16_t type",
            send + 1,
            StringComparison.Ordinal);

        string tail = code[(body < 0 ? send : body)..];

        int guard = tail.IndexOf("if(payload)", StringComparison.Ordinal);
        int encrypt = tail.IndexOf("chiaki_rpcrypt_encrypt", StringComparison.Ordinal);

        return guard >= 0 && encrypt > guard;
    }
}
