using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP379: no call in senkusha.c has its answer discarded, and the disconnect says when it failed.
///
/// The run ended at a `disconnect:` label that called senkusha_send_disconnect and looked at
/// nothing. Every other call in the file has its answer read, which is what made this an omission
/// rather than a policy.
///
/// PP370 SETTLED WHAT TO DO WITH IT, one file over. A teardown cannot retry and should not change
/// what the run returns - the disconnect is the last act of a function already leaving. But it can
/// say so, and with no log nothing at the later failure points back here.
///
/// AND THE SENKUSHA CASE IS THE ONE WHERE IT MATTERS MORE. Senkusha runs BEFORE the stream
/// connection, on the same console, so a disconnect that never left holds the port against the
/// attempt that immediately follows it - inside the same session rather than a later one. What the
/// user sees is the console refusing a client that is already talking to it.
///
/// THE RULE IS OVER EVERY ANSWERING CALL, not the one that was wrong. Third member of the family
/// after PP370's ack and PP363's heartbeat, and like both of those it requires the result to be
/// READ rather than that failing ends anything - which is the only form of the rule the heartbeat
/// and this disconnect can both satisfy.
/// </summary>
public static class SenkushaSendResults
{
    /// <summary>Where the calls live.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every static in the file that answers a ChiakiErrorCode.
    ///
    /// Broader than PP370's list, which is sends alone, because here the three run helpers answer
    /// too and a discarded run result is the same hazard wearing a different name. Named rather
    /// than derived, so an eleventh added without appearing here is one the rule does not cover -
    /// which <see cref="EveryAnsweringStaticIsListed"/> is what catches.
    /// </summary>
    public static IReadOnlyList<string> CallsThatAnswer { get; } =
    [
        "senkusha_set_version",
        "senkusha_send_big",
        "senkusha_send_disconnect",
        "senkusha_send_echo_command",
        "senkusha_send_mtu_command",
        "senkusha_send_client_mtu_command",
        "senkusha_send_data_wait_for_ack",
        "senkusha_run_rtt_test",
        "senkusha_run_mtu_in_test",
        "senkusha_run_mtu_out_test",
    ];

    /// <summary>
    /// Every call to one of them whose result goes nowhere.
    ///
    /// Through PP370's reader rather than a second copy of it: the shape of a discard is a fact
    /// about C, and only the list is a fact about this file.
    /// </summary>
    public static IReadOnlyList<string> DiscardedResults(string source)
        => StreamSendResults.DiscardedCalls(source, CallsThatAnswer);

    /// <summary>
    /// Every static in the file that answers, read out of the file rather than out of the list
    /// above - so the list can be checked against what is actually there.
    /// </summary>
    public static IReadOnlyList<string> AnsweringStaticsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (string line in source.Split('\n'))
        {
            const string Prefix = "static ChiakiErrorCode ";
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            int open = line.IndexOf('(', Prefix.Length);
            if (open < 0)
                continue;

            string name = line[Prefix.Length..open].Trim();
            if (name.Length > 0 && !found.Contains(name, StringComparer.Ordinal))
                found.Add(name);
        }

        return found;
    }

    /// <summary>
    /// Whether the disconnect at the teardown label reads its answer and logs it.
    ///
    /// Both, in order, and neither alone. Reading it without logging leaves the same silence; the
    /// log without reading it does not compile.
    /// </summary>
    public static bool TheDisconnectIsReadAndLogged(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int label = source.IndexOf("disconnect:", StringComparison.Ordinal);
        if (label < 0)
            return false;

        int read = source.IndexOf("= senkusha_send_disconnect(", label, StringComparison.Ordinal);
        int tested = source.IndexOf(
            "if(disconnect_err != CHIAKI_ERR_SUCCESS)", read < 0 ? label : read, StringComparison.Ordinal);
        int logged = source.IndexOf("CHIAKI_LOGE", tested < 0 ? label : tested, StringComparison.Ordinal);

        return read > label && tested > read && logged > tested;
    }

    /// <summary>
    /// Whether the run still returns what it had already decided, rather than the disconnect's code.
    ///
    /// The half that says this is a report and not a behaviour change: `err` is what leaves, and a
    /// teardown that overwrote it would turn a successful senkusha run into a failed one because
    /// its goodbye did not send.
    /// </summary>
    public static bool TheRunStillReturnsWhatItDecided(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int read = source.IndexOf("= senkusha_send_disconnect(", StringComparison.Ordinal);
        if (read < 0)
            return false;

        int returned = source.IndexOf("return err;", read, StringComparison.Ordinal);
        if (returned < 0)
            return false;

        // Nothing between them assigns err from the disconnect.
        return !source[read..returned].Contains("err = disconnect_err", StringComparison.Ordinal);
    }
}
