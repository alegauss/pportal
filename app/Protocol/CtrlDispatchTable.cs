using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one arriving ctrl message does to the remote crypt counter.</summary>
/// <param name="DecryptAt">The counter value the payload is decrypted at.</param>
/// <param name="Next">The counter afterwards.</param>
/// <param name="Spent">Whether a value was consumed at all.</param>
public readonly record struct CtrlReceiveSpend(ushort DecryptAt, ushort Next, bool Spent);

/// <summary>
/// PP466, under PP294: which of ctrl.c's message types ARRIVE, and what receiving one costs.
///
/// PP440 censused all 22 types and which class answers each, and said what that column was for: "a
/// file this size lands one recorded half at a time, and a half cannot be picked without knowing which
/// parts are already done". This is one of those halves - the direction.
///
/// TEN OF THE TWENTY-TWO ARRIVE. `ctrl_message_received`'s switch has ten cases and a default; the
/// other twelve types are ones this side SENDS, and a handler for one of them would be code no console
/// can reach. The census does not say which is which, so a reader picking a part could not tell a
/// missing handler from a type that never comes - and both look like a gap.
///
/// A PAYLOADLESS MESSAGE SPENDS NO COUNTER, WHICH IS PP448'S RULE FROM THE OTHER SIDE. The decrypt
/// sits behind `if(payload_size > 0)` and takes `crypt_counter_remote++`, so a message that arrives
/// with nothing in it advances no counter. PP448 measured exactly this on the send side and called a
/// managed loop that incremented per message rather than per encrypted payload "drift by exactly the
/// number of bare messages it sent". The receive side has the same trap and nothing had stated it.
///
/// AND A FAILED DECRYPT STOPS BEFORE THE TAP. The order is decrypt, log, tap, switch - so a payload
/// that will not decrypt is neither recorded nor dispatched, and the counter it consumed is already
/// gone. That is the one path where a message costs a counter value and produces nothing at all.
/// </summary>
public static class CtrlDispatchTable
{
    /// <summary>
    /// The ten types `ctrl_message_received` has a case for, in the order the switch lists them.
    ///
    /// Order matters only as documentation of the C; a switch is not sequential. It is kept so the
    /// source check can compare position for position rather than as a set, which is what catches a
    /// case being moved into or out of the block.
    /// </summary>
    public static IReadOnlyList<string> Received { get; } =
    [
        "SESSION_ID",
        "HEARTBEAT_REQ",
        "LOGIN_PIN_REQ",
        "LOGIN",
        "KEYBOARD_OPEN",
        "KEYBOARD_TEXT_CHANGE_RES",
        "KEYBOARD_CLOSE_REMOTE",
        "DISPLAYA",
        "DISPLAYB",
        "SWITCH_TO_STREAM_CONNECTION",
    ];

    /// <summary>
    /// Every censused type the switch has no case for - the ones this side sends.
    ///
    /// Derived from <see cref="CtrlMessageCensus.Rows"/> rather than listed, so a type added to the
    /// census lands in exactly one of the two lists and cannot be forgotten by both.
    /// </summary>
    public static IReadOnlyList<string> SendOnly { get; } =
        [.. CtrlMessageCensus.Rows
            .Select(r => r.CName)
            .Where(name => !Received.Contains(name, StringComparer.Ordinal))];

    /// <summary>Whether a type arrives at this end.</summary>
    public static bool Arrives(string cName)
    {
        ArgumentNullException.ThrowIfNull(cName);
        return Received.Contains(cName, StringComparer.Ordinal);
    }

    /// <summary>
    /// What receiving a message does to the remote counter.
    ///
    /// The mirror of <see cref="CtrlSendCounter.Spend"/>, and deliberately the same shape: a payload of
    /// nothing consumes nothing, and the value decrypted at is the value consumed. There is no
    /// equivalent of the PIN reply's step back on this side - that quirk is the send's alone.
    /// </summary>
    public static CtrlReceiveSpend Receive(ushort counter, int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);

        if (payloadSize == 0)
            return new CtrlReceiveSpend(counter, counter, Spent: false);

        return new CtrlReceiveSpend(counter, (ushort)(counter + 1), Spent: true);
    }

    /// <summary>ctrl.c, where the dispatch lives.</summary>
    public static string? Locate() => CtrlMessageCensus.LocateCtrl();

    /// <summary>`ctrl_message_received`'s body, which is the switch and what precedes it.</summary>
    public static string? DispatchBody(string source)
        => CFunction.Body(
            source, "static void ctrl_message_received(ChiakiCtrl *ctrl, uint16_t msg_type");

    /// <summary>
    /// Every type the switch has a case for, read out of the body in order.
    ///
    /// Read rather than trusted, so <see cref="Received"/> and the file are two statements about one
    /// switch. A case added or removed shows up as a different list rather than as a passing test.
    /// </summary>
    public static IReadOnlyList<string> CasesIn(string dispatchBody)
    {
        ArgumentNullException.ThrowIfNull(dispatchBody);

        var found = new List<(int At, string Name)>();

        foreach (CtrlMessageRow row in CtrlMessageCensus.Rows)
        {
            int at = dispatchBody.IndexOf(
                $"case CTRL_MESSAGE_TYPE_{row.CName}:", StringComparison.Ordinal);

            if (at >= 0)
                found.Add((at, row.CName));
        }

        return [.. found.OrderBy(f => f.At).Select(f => f.Name)];
    }

    /// <summary>
    /// Whether the remote counter is still advanced only for a message with a payload.
    ///
    /// Both halves: the post-increment is inside the guard, and the guard tests the size. A decrypt
    /// moved outside it would advance the counter for every bare message and drift against a console
    /// that counts the way PP448 measured.
    /// </summary>
    public static bool TheCounterStillMovesOnlyForAPayload(string dispatchBody)
    {
        ArgumentNullException.ThrowIfNull(dispatchBody);

        int guard = dispatchBody.IndexOf("if(payload_size > 0)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int decrypt = dispatchBody.IndexOf(
            "chiaki_rpcrypt_decrypt(&ctrl->session->rpcrypt, ctrl->crypt_counter_remote++",
            StringComparison.Ordinal);

        return decrypt > guard;
    }

    /// <summary>
    /// Whether a failed decrypt still returns before the tap and the switch.
    ///
    /// The counter it consumed is gone either way, which is what makes the order worth asserting: a
    /// tap moved above the decrypt would record ciphertext as if it were a message.
    /// </summary>
    public static bool AFailedDecryptStillStopsBeforeTheTap(string dispatchBody)
    {
        ArgumentNullException.ThrowIfNull(dispatchBody);

        int fails = dispatchBody.IndexOf(
            "Failed to decrypt payload for Ctrl Message type", StringComparison.Ordinal);
        int tap = dispatchBody.IndexOf("chiaki_message_tap_emit(", StringComparison.Ordinal);
        int switched = dispatchBody.IndexOf("switch(msg_type)", StringComparison.Ordinal);

        if (fails < 0 || tap < fails || switched < tap)
            return false;

        // The return between the log and the tap is what stops it.
        return dispatchBody[fails..tap].Contains("return;", StringComparison.Ordinal);
    }
}
