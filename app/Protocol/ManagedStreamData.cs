namespace ChiakiNg.Protocol;

/// <summary>What one data message came to.</summary>
public enum StreamDataResult
{
    /// <summary>At least one session event was raised from it.</summary>
    Raised,

    /// <summary>The handler's own guard refused it - too short, or a length it does not know.</summary>
    Refused,

    /// <summary>Read, and nothing had changed. A pad info repeating what is held is the case.</summary>
    NothingToSay,

    /// <summary>A protobuf, whose meaning is the state's question and not this layer's.</summary>
    ToProtobuf,

    /// <summary>A data type none of the four. The C's default arm, which breaks.</summary>
    Dropped,
}

/// <summary>What one delivery decided, so a caller reads an answer rather than a side effect.</summary>
/// <param name="Kind">Which of PP366's second-layer kinds it was.</param>
/// <param name="Events">How many session events the message decided on.</param>
/// <param name="Result">What became of it.</param>
public readonly record struct StreamDataOutcome(TakionData Kind, int Events, StreamDataResult Result);

/// <summary>
/// PP721, under PP707: stream_connection_takion_data - the layer that had no caller either side.
///
/// PP366 modelled the second layer of dispatch and returns an enum; PP689 reads a pad info message
/// and returns five reports in the C's order; PP719 built the seam those reports become events on.
/// Between them was nothing. This is the join: a takion data message in, the C's switch, the parse,
/// and the events out.
///
/// THE PAD STATE LIVES HERE, which is why this is an object and not a function. Four of the pad
/// info five are COMPARISONS against what the console last said, so the answer to one message
/// depends on every message before it - and a port holding that state in the parser, or not at all,
/// would report five changes on every message and rewrite the light bar as fast as the console
/// sends. The C keeps it on the stream connection, which is what this stands in for.
///
/// AN UNKNOWN DATA TYPE IS DROPPED SILENTLY, as the C's default arm does. Not an error and not a
/// log: the four are the four, and the switch has nothing to say about a fifth.
///
/// THE PROTOBUF ARM STOPS HERE. Which handler a protobuf reaches is decided by the STATE, which is
/// PP366's third layer and the run's business - so this reports it and hands it no further.
/// </summary>
/// <param name="events">Where the events go. Its own sink may be absent, which is the C's case too.</param>
public sealed class ManagedStreamData(ManagedSessionEvents events)
{
    private readonly ManagedSessionEvents events =
        events ?? throw new ArgumentNullException(nameof(events));

    /// <summary>What the console last said about the pad, which four of the five compare against.</summary>
    public PadState Pad { get; private set; } = PadState.Initial;

    /// <summary>How many events every delivery so far has decided on.</summary>
    public int Decided { get; private set; }

    /// <summary>How many messages were refused by a handler's own guard.</summary>
    public int Refused { get; private set; }

    /// <summary>
    /// The C's data types as PP366's second layer names them.
    ///
    /// Two enums rather than one because they are two questions: the wire's byte and the kind the
    /// dispatch switches on. The C has the same pair and this is the same mapping.
    /// </summary>
    public static TakionData KindOf(TakionDataType type) => type switch
    {
        TakionDataType.Protobuf => TakionData.Protobuf,
        TakionDataType.Rumble => TakionData.Rumble,
        TakionDataType.PadInfo => TakionData.PadInfo,
        TakionDataType.TriggerEffects => TakionData.TriggerEffects,
        _ => TakionData.Other,
    };

    /// <summary>
    /// One data message, past its nine-byte header - which is what PP493's drain hands on.
    /// </summary>
    public StreamDataOutcome Deliver(TakionDataType type, ReadOnlySpan<byte> body)
    {
        TakionData kind = KindOf(type);

        if (!StreamDispatch.IsHandled(kind))
            return new StreamDataOutcome(kind, 0, StreamDataResult.Dropped);

        return kind switch
        {
            TakionData.Protobuf => new StreamDataOutcome(kind, 0, StreamDataResult.ToProtobuf),
            TakionData.Rumble => One(kind, ManagedSessionEvents.Rumble(body)),
            TakionData.TriggerEffects => One(kind, ManagedSessionEvents.TriggerEffects(body)),
            _ => PadInfo(body),
        };
    }

    private StreamDataOutcome One(TakionData kind, SessionEvent? raised)
    {
        if (raised is null)
        {
            Refused++;
            return new StreamDataOutcome(kind, 0, StreamDataResult.Refused);
        }

        events.Send(raised.Value);
        Decided++;

        return new StreamDataOutcome(kind, 1, StreamDataResult.Raised);
    }

    private StreamDataOutcome PadInfo(ReadOnlySpan<byte> body)
    {
        PadInfoReading reading = PadInfoMessage.Read(body, Pad);

        if (reading.Layout == PadInfoLayout.Unknown)
        {
            Refused++;
            return new StreamDataOutcome(TakionData.PadInfo, 0, StreamDataResult.Refused);
        }

        // The state AFTER, held for the next message. A length the C refuses leaves it alone, which
        // is what PadInfoMessage returns above rather than something this has to remember.
        Pad = reading.State;

        events.SendPadInfo(reading);
        Decided += reading.Reports.Count;

        return new StreamDataOutcome(
            TakionData.PadInfo,
            reading.Reports.Count,
            reading.Reports.Count == 0 ? StreamDataResult.NothingToSay : StreamDataResult.Raised);
    }
}

/// <summary>
/// PP721: the switch this reproduces, held against streamconnection.c.
/// </summary>
public static class ManagedStreamDataSource
{
    /// <summary>Where the callback lives.</summary>
    public const string RelativePath = StreamDispatchSource.RelativePath;

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => Session.SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The callback's body, or null where it is gone.</summary>
    public static string? SwitchBody(string source)
        => Session.CFunction.Body(
            source, "static void stream_connection_takion_data(ChiakiStreamConnection *stream_connection, ChiakiTakionMessageDataType");

    /// <summary>
    /// The handler each data type reaches, keyed by the C's constant.
    ///
    /// Read as the pairing rather than as a list of names, because the failure this guards is two
    /// arms swapped: a rumble routed to the pad info handler is a length that parses and a light
    /// bar written from a motor strength.
    /// </summary>
    public static IReadOnlyDictionary<string, string> HandlersIn(string switchBody)
    {
        ArgumentNullException.ThrowIfNull(switchBody);

        const string prefix = "case CHIAKI_TAKION_MESSAGE_DATA_TYPE_";
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int at = switchBody.IndexOf(prefix, StringComparison.Ordinal);
             at >= 0;
             at = switchBody.IndexOf(prefix, at + prefix.Length, StringComparison.Ordinal))
        {
            int from = at + prefix.Length;
            int colon = switchBody.IndexOf(':', from);
            if (colon < 0)
                break;

            int call = switchBody.IndexOf("stream_connection_takion_data_", colon, StringComparison.Ordinal);
            if (call < 0)
                break;

            int open = switchBody.IndexOf('(', call);
            if (open < 0)
                break;

            found[switchBody[from..colon].Trim()] = switchBody[call..open].Trim();
        }

        return found;
    }

    /// <summary>
    /// Whether an unrecognised data type is still dropped without a word.
    ///
    /// A `default: break;` and nothing else. The C logs plenty elsewhere and deliberately not here,
    /// so a port that warned would be reporting on traffic the console sends and the client ignores.
    /// </summary>
    public static bool AnUnknownTypeIsStillDroppedSilently(string switchBody)
    {
        ArgumentNullException.ThrowIfNull(switchBody);

        int arm = switchBody.IndexOf("default:", StringComparison.Ordinal);
        if (arm < 0)
            return false;

        string tail = switchBody[arm..];
        int brace = tail.IndexOf('}');

        return brace > 0
            && tail[..brace].Contains("break;", StringComparison.Ordinal)
            && !tail[..brace].Contains("CHIAKI_LOG", StringComparison.Ordinal);
    }
}
