namespace ChiakiNg.Protocol;

/// <summary>
/// PP812: the port's own emit site, because the C's stopped seeing the traffic.
///
/// PP511 made takion.c emit every arriving datagram on the message tap's fifth channel, and
/// <see cref="TakionDatagramTap"/> turns those into a capture. PP783 then moved the streaming
/// session onto <see cref="ManagedTakion"/>, and nothing arrives on a C takion any more - so the
/// hook fires for the ctrl channel and for nothing else. The file is still written, it is still
/// short, and nothing in it says the traffic went elsewhere.
///
/// THE POSITION IS THE C'S, not a convenient one. takion.c emits inside takion_handle_packet,
/// ABOVE the MAC gate, because the arrivals a timing run most wants are the ones a managed
/// transport might handle differently - a datagram rejected for its MAC is exactly that. The
/// managed dispatch is the same place: it is handed every datagram the receive loop took, before
/// the intake reads a header and before the AV branch.
///
/// AND IT SEES BOTH TAKIONS. The stream's and senkusha's are the same class, so one tap covers a
/// run's whole life - which is the half PP813 could not see, because its senkusha opens a takion
/// of its own and the C's tap was never on it.
///
/// STATIC, LIKE THE ONE IT STANDS IN FOR. <see cref="ChiakiMessageTap"/> is a process-wide C hook
/// and this is its counterpart; the phase that builds a takion is composed several layers below
/// the flag that asks for a capture, so there is nothing to hand it down through. Install replaces,
/// for the same reason the C's does: two taps is one tap and a silence.
/// </summary>
public static class ManagedDatagramTap
{
    private static Action<ReadOnlySpan<byte>>? installed;

    /// <summary>
    /// Whether anything is listening, which every dispatch asks before it spends a call.
    /// </summary>
    /// <remarks>
    /// The C guards its emit with chiaki_message_tap_active() for the reason PP44 records: the
    /// dispatch is the one path budgeted at zero allocations, and setting up a call for a callback
    /// that is null would be paid on every datagram of every session.
    /// </remarks>
    public static bool Active => installed is not null;

    /// <summary>How many datagrams have been offered since the last install.</summary>
    public static int Offered { get; private set; }

    /// <summary>
    /// Installs a tap, replacing whatever was there. Dispose to uninstall.
    /// </summary>
    /// <param name="onDatagram">
    /// Handed the dispatch's own span, so an implementation that keeps the bytes copies them - the
    /// same contract <see cref="ManagedTakion"/>'s dispatch has, and for the same reason.
    /// </param>
    public static IDisposable Install(Action<ReadOnlySpan<byte>> onDatagram)
    {
        ArgumentNullException.ThrowIfNull(onDatagram);

        installed = onDatagram;
        Offered = 0;

        return new Handle(onDatagram);
    }

    /// <summary>One arriving datagram, from the managed dispatch.</summary>
    public static void Offer(ReadOnlySpan<byte> datagram)
    {
        if (installed is not { } take)
            return;

        Offered++;
        take(datagram);
    }

    /// <summary>
    /// Uninstalls, and only where this handle's own callback is still the installed one.
    /// </summary>
    /// <remarks>
    /// A second Install before the first is disposed replaces it, and the first handle then owns
    /// nothing. Clearing unconditionally would silence the tap that replaced it, which is a
    /// capture ending early with no line saying so.
    /// </remarks>
    private sealed class Handle(Action<ReadOnlySpan<byte>> mine) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(installed, mine))
                installed = null;
        }
    }
}
