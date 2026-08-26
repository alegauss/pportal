using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP355: everything an outside caller allocates into the ctrl channel is freed at teardown.
///
/// Two things arrive that way. A login PIN is malloc'd by chiaki_ctrl_set_login_pin and handed over;
/// a queued message is malloc'd by chiaki_ctrl_send_message, twice - the node and its payload. Both
/// are the ctrl channel's to free from the moment the call returns.
///
/// ONE OF THEM WAS FREED AT TEARDOWN AND THE OTHER WAS NOT. ctrl_message_queue_free had exactly one
/// caller: the drain inside the thread's cancelled branch. On the path everybody takes that is
/// enough - a stop pokes the notify pipe, the loop wakes, and its order is queue-then-PIN-then-stop,
/// so the drain empties the queue before the stop is read. The queue was empty at teardown because
/// the loop emptied it, not because anything at teardown looked.
///
/// Every other exit from that loop skips the drain: an overflow, a failed select, a recv error, a
/// failed rudp receive, a short rudp message, a rudp finish message. Anything queued when one of
/// those happened leaked - small, once per session, and reachable by queueing anything at the moment
/// the socket errors. goto-bed from the power menu during a network drop is the shape of it.
///
/// THE CHECK IS THE SYMMETRY, not the two names. fini already freed the PIN, so ownership at
/// teardown had been thought about and one of two was missed - which is the shape a third would take.
/// </summary>
public static class CtrlOwnership
{
    /// <summary>Where the lifecycle lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The fini's body, or null.</summary>
    public static string? FiniBody(string filePath)
        => CFunction.BodyIn(filePath, "chiaki_ctrl_fini");

    /// <summary>
    /// What an outside caller allocates into ctrl, by the field it lands in.
    ///
    /// Named as a list because that is what the check is: each of these must be released at
    /// teardown, and a field added here without a release below turns the assertion red.
    /// </summary>
    public static IReadOnlyList<string> HandedOverByCallers { get; } = ["login_pin", "msg_queue"];

    /// <summary>
    /// Which of them the fini does not release.
    ///
    /// A release is a free of the field, or - for the queue, which is a list - a loop that walks it
    /// into the free that owns a node.
    /// </summary>
    public static IReadOnlyList<string> NotReleasedAtTeardown(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        var missed = new List<string>();

        foreach (string field in HandedOverByCallers)
        {
            bool freedDirectly = finiBody.Contains($"free(ctrl->{field})", StringComparison.Ordinal);

            bool walked = finiBody.Contains($"while(ctrl->{field})", StringComparison.Ordinal)
                && finiBody.Contains("ctrl_message_queue_free(", StringComparison.Ordinal);

            if (!freedDirectly && !walked)
                missed.Add(field);
        }

        return missed;
    }

    /// <summary>
    /// Whether the drain in the loop is still there too.
    ///
    /// The teardown free is a backstop, not a replacement: the loop drains because a stop should
    /// SEND what was queued rather than discard it, and freeing at teardown must not become the
    /// reason nobody noticed the drain went away.
    /// </summary>
    public static bool TheLoopStillDrainsRatherThanDiscards(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int drain = threadBody.IndexOf("while(ctrl->msg_queue)", StringComparison.Ordinal);
        if (drain < 0)
            return false;

        // The drain sends before it frees; the teardown only frees.
        int send = threadBody.IndexOf("ctrl_message_send(ctrl, msg->type", drain, StringComparison.Ordinal);
        int free = threadBody.IndexOf("ctrl_message_queue_free(msg)", drain, StringComparison.Ordinal);

        return send > drain && free > send;
    }
}
