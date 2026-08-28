using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP511, under PP27: the wire between the tap's fifth channel and PP510's capture.
///
/// PP510 settled what a timing run keeps and left it with nothing to fill it from. The C now emits
/// every arriving datagram on <see cref="ChiakiMessageTap.TakionChannel"/>, above the MAC gate, with
/// the base type as its type and a head truncated at the emit. This installs a tap and turns those
/// into <see cref="TakionTimingCapture"/> entries.
///
/// THE CLOCK IS INJECTED AND MONOTONIC. The tap carries no timestamp - it never needed one, because
/// the four message channels are ordered by the recording's own offsets - so the reading is taken
/// here, on the receive thread, at the moment the callback fires. A test passes its own clock; a
/// session passes the same monotonic source the C uses, so the two sides of PP27's comparison are
/// measured on one timebase.
///
/// IT TAKES ONLY ITS OWN CHANNEL. A tap sees all five, and the other four are framed messages that
/// PP326's recorder is for. Filtering here rather than installing a second tap is not a preference:
/// <see cref="ChiakiMessageTap.Install"/> replaces whatever was installed, so two taps is one tap
/// and a silence.
/// </summary>
public sealed class TakionDatagramTap : IDisposable
{
    private readonly TakionTimingCapture capture;
    private readonly Func<long> clock;
    private readonly ChiakiMessageTap tap;
    private bool disposed;

    /// <summary>
    /// Installs a tap that fills <paramref name="capture"/>.
    /// </summary>
    /// <param name="capture">Where arrivals go. Its bounds decide when this stops taking.</param>
    /// <param name="clock">Monotonic microseconds. Only differences are used.</param>
    public TakionDatagramTap(TakionTimingCapture capture, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(clock);

        this.capture = capture;
        this.clock = clock;

        tap = ChiakiMessageTap.Install(Take);
    }

    /// <summary>How many messages on the other four channels were passed over.</summary>
    public int OtherChannels { get; private set; }

    private void Take(TappedMessage message)
    {
        if (!string.Equals(message.Channel, ChiakiMessageTap.TakionChannel, StringComparison.Ordinal))
        {
            OtherChannels++;
            return;
        }

        // The reading is taken when the datagram surfaces, not when it is offered: the capture's
        // bounds may refuse it, and a refused arrival still happened.
        capture.Offer(message.Payload, clock());
    }

    /// <summary>Uninstalls the tap. The capture keeps what it has.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        tap.Dispose();
    }
}

/// <summary>
/// PP511: the C's own emit site, so "above the MAC gate" is asserted rather than remembered.
/// </summary>
public static class TakionDatagramTapSource
{
    /// <summary>messagetap.h, where the channel is named.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\messagetap.h";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? Locate() => TakionPostpone.Locate();

    /// <summary>messagetap.h, or null outside a checkout.</summary>
    public static string? LocateHeader()
        => ChiakiNg.Session.SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The dispatch, where the emit is.</summary>
    public static string? HandleBody(string takionSource)
        => ChiakiNg.Session.CFunction.Body(takionSource, "static void takion_handle_packet");

    /// <summary>
    /// Whether the emit still sits ABOVE the MAC gate.
    ///
    /// The whole placement claim. Below it, a packet rejected for its MAC would never be captured -
    /// and those are the arrivals a timing run is most interested in, because they are the ones a
    /// managed transport might handle differently.
    /// </summary>
    public static bool TheEmitIsAboveTheMacGate(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        int emit = handleBody.IndexOf("chiaki_message_tap_emit(", StringComparison.Ordinal);
        int gate = handleBody.IndexOf(
            "if(takion_handle_packet_mac(takion, base_type, buf, buf_size)", StringComparison.Ordinal);

        return emit >= 0 && gate > emit;
    }

    /// <summary>
    /// Whether the emit is still behind the tap's active check.
    ///
    /// Without it, the argument setup runs on every datagram for a callback that is null - on the
    /// one path PP44 budgeted at zero allocations and PP485 rented a buffer for.
    /// </summary>
    public static bool TheEmitIsGuardedByTheActiveCheck(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        int guard = handleBody.IndexOf("if(chiaki_message_tap_active())", StringComparison.Ordinal);
        int emit = handleBody.IndexOf("chiaki_message_tap_emit(", StringComparison.Ordinal);

        return guard >= 0 && emit > guard;
    }

    /// <summary>
    /// Whether the head is still truncated at the emit rather than left to a consumer.
    ///
    /// Truncating here is what makes it true of EVERY consumer. A tap handing over the whole
    /// datagram would move a frame of somebody's screen through a callback.
    /// </summary>
    public static bool TheHeadIsTruncatedAtTheEmit(string handleBody)
    {
        ArgumentNullException.ThrowIfNull(handleBody);

        return handleBody.Contains("CHIAKI_MESSAGE_TAP_TAKION_HEAD", StringComparison.Ordinal)
            && handleBody.Contains("base_type,", StringComparison.Ordinal);
    }

    /// <summary>The channel name the header declares.</summary>
    public static string? ChannelIn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        const string define = "#define CHIAKI_MESSAGE_TAP_CHANNEL_TAKION \"";

        int at = header.IndexOf(define, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + define.Length;
        int close = header.IndexOf('"', from);

        return close < 0 ? null : header[from..close];
    }

    /// <summary>The head length the header declares.</summary>
    public static long? HeadBytesIn(string header)
        => ChiakiNg.Session.CDefine.Value(header, "CHIAKI_MESSAGE_TAP_TAKION_HEAD");
}
