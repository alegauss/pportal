using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the two shapes a pad info message is, told apart by its length alone.</summary>
public enum PadInfoLayout
{
    /// <summary>Neither, which the C logs and reports nothing for.</summary>
    Unknown,

    /// <summary>0x19 bytes: the one carrying a feedback sequence number and a timestamp.</summary>
    Wide,

    /// <summary>0x11 bytes: the same values, eight bytes earlier and without the two diagnostics.</summary>
    Narrow,
}

/// <summary>
/// The light bar, as three bytes.
///
/// A struct rather than the array the C memcmps, because the whole of what this port does with the
/// colour is compare it: an array member would give <see cref="PadState"/> reference equality and a
/// state that says it changed when it did not is the defect this message exists to avoid.
/// </summary>
public readonly record struct PadLed(byte Red, byte Green, byte Blue)
{
    /// <summary>The three bytes where the layout puts them.</summary>
    public static PadLed From(ReadOnlySpan<byte> bytes)
        => bytes.Length < 3
            ? throw new ArgumentException("a light bar is three bytes", nameof(bytes))
            : new PadLed(bytes[0], bytes[1], bytes[2]);

    /// <summary>And back, for a caller that wants them as the wire has them.</summary>
    public byte[] ToBytes() => [Red, Green, Blue];
}

/// <summary>What the console last told this side about the pad.</summary>
/// <param name="PlayerIndex">Which player the pad is.</param>
/// <param name="Led">The light bar, red then green then blue.</param>
/// <param name="HapticIntensity">The DualSense haptic intensity, as the console's own enum.</param>
/// <param name="TriggerIntensity">And the adaptive triggers'.</param>
public readonly record struct PadState(
    byte PlayerIndex, PadLed Led, byte HapticIntensity, byte TriggerIntensity)
{
    /// <summary>What is held before a console has said anything, which is every field at zero.</summary>
    public static PadState Initial => new(0, new PadLed(0, 0, 0), 0, 0);
}

/// <summary>One thing the handler tells the rest of the port about.</summary>
public enum PadReportKind
{
    /// <summary>The console asking for motion control's origin to be taken as it is now.</summary>
    MotionReset,

    /// <summary>The haptic intensity moved.</summary>
    HapticIntensity,

    /// <summary>The adaptive triggers' intensity moved.</summary>
    TriggerIntensity,

    /// <summary>The light bar's colour moved.</summary>
    LedColor,

    /// <summary>The pad is a different player now.</summary>
    PlayerIndex,
}

/// <summary>What one pad info message produced.</summary>
/// <param name="Layout">Which shape it was, or Unknown where it was refused.</param>
/// <param name="Reports">What to tell the rest of the port, in the order the C sends them.</param>
/// <param name="State">What is held afterwards, unchanged where the message was refused.</param>
public readonly record struct PadInfoReading(
    PadInfoLayout Layout, IReadOnlyList<PadReportKind> Reports, PadState State);

/// <summary>
/// PP689, under PP295: the message the console drives the controller with.
///
/// The light bar's colour, which player the pad is, and the two DualSense intensities all arrive
/// here, in one of two layouts told apart by nothing but the message's length. Every field sits at a
/// different offset in each, and a length that is neither is refused with a log and no reports.
///
/// FOUR OF THE FIVE ARE COMPARISONS, not readings. The intensities, the player index and the light
/// bar are reported only where they DIFFER from what is held - so the state matters as much as the
/// parse, and a port without it would report five changes on every message and rewrite the light bar
/// as fast as the console sends. The fifth is the motion reset, which is not a comparison at all: it
/// fires whenever its byte is set, because it is the console asking rather than telling.
///
/// AND THE ORDER IS THE BEHAVIOUR. The C decides all five inside the switch and sends them after it,
/// in one order both layouts share: motion reset, haptic, trigger, light bar, player index. A port
/// that reported each as it parsed would send them in offset order instead - right about every field
/// and wrong about the sequence, which is exactly what PP295's first criterion is about.
///
/// The two diagnostics the wide layout carries are read and reported to nobody, as the C reads them:
/// a feedback sequence number and a timestamp, whose width PP374 corrected. They are exposed because
/// the timestamp is the number somebody reads when motion control drifts.
/// </summary>
public static class PadInfoMessage
{
    /// <summary>The wide layout's length, which is also how it is recognised.</summary>
    public const int WideSize = 0x19;

    /// <summary>And the narrow one's.</summary>
    public const int NarrowSize = 0x11;

    /// <summary>How many bytes the light bar takes: red, green, blue.</summary>
    public const int LedBytes = 3;

    /// <summary>Which shape a message of this length is.</summary>
    public static PadInfoLayout LayoutOf(int length) => length switch
    {
        WideSize => PadInfoLayout.Wide,
        NarrowSize => PadInfoLayout.Narrow,
        _ => PadInfoLayout.Unknown,
    };

    /// <summary>Where the player index sits in each layout.</summary>
    public static int PlayerIndexOffset(PadInfoLayout layout)
        => layout == PadInfoLayout.Wide ? 8 : 0;

    /// <summary>Where the light bar's three bytes start.</summary>
    public static int LedOffset(PadInfoLayout layout)
        => layout == PadInfoLayout.Wide ? 9 : 1;

    /// <summary>Where the motion reset's byte sits; non-zero is the ask.</summary>
    public static int MotionResetOffset(PadInfoLayout layout)
        => layout == PadInfoLayout.Wide ? 12 : 4;

    /// <summary>Where the haptic intensity sits.</summary>
    public static int HapticOffset(PadInfoLayout layout)
        => layout == PadInfoLayout.Wide ? 20 : 12;

    /// <summary>And the trigger intensity, always the byte after it.</summary>
    public static int TriggerOffset(PadInfoLayout layout) => HapticOffset(layout) + 1;

    /// <summary>
    /// The feedback packet this message answers, which only the wide layout carries.
    ///
    /// A diagnostic: the C logs it beside the motion reset and nothing acts on it.
    /// </summary>
    public static ushort? FeedbackSeqNum(ReadOnlySpan<byte> message)
        => LayoutOf(message.Length) == PadInfoLayout.Wide
            ? BinaryPrimitives.ReadUInt16BigEndian(message)
            : null;

    /// <summary>
    /// How long the stream had been running, in seconds - the other diagnostic, and the one PP374
    /// repaired.
    ///
    /// It is a four-byte field, and it was swapped as two: the read was truncated to its low half
    /// BEFORE anything was swapped, so what got logged advanced once per 65536 units for the whole
    /// session. Reproduced at its own width here, which is what makes the number the console's.
    /// </summary>
    public static uint? StreamSeconds(ReadOnlySpan<byte> message)
        => LayoutOf(message.Length) == PadInfoLayout.Wide
            ? BinaryPrimitives.ReadUInt32BigEndian(message[4..])
            : null;

    /// <summary>
    /// Reads one message against what is held, and says what to report.
    /// </summary>
    /// <param name="message">The pad info payload, whose length chooses the layout.</param>
    /// <param name="held">What the console last said. <see cref="PadState.Initial"/> to start.</param>
    public static PadInfoReading Read(ReadOnlySpan<byte> message, PadState held)
    {
        PadInfoLayout layout = LayoutOf(message.Length);

        // The C logs the length and returns, having decided nothing - so what is held survives a
        // message this side could not read, which is the safe half of an unknown layout.
        if (layout == PadInfoLayout.Unknown)
            return new PadInfoReading(layout, [], held);

        byte playerIndex = message[PlayerIndexOffset(layout)];
        byte haptic = message[HapticOffset(layout)];
        byte trigger = message[TriggerOffset(layout)];
        PadLed led = PadLed.From(message.Slice(LedOffset(layout), LedBytes));

        bool motionReset = message[MotionResetOffset(layout)] != 0;
        bool hapticChanged = haptic != held.HapticIntensity;
        bool triggerChanged = trigger != held.TriggerIntensity;
        bool ledChanged = led != held.Led;
        bool playerIndexChanged = playerIndex != held.PlayerIndex;

        var reports = new List<PadReportKind>(5);

        // The order the C sends them in, which is not the order it reads them.
        if (motionReset)
            reports.Add(PadReportKind.MotionReset);
        if (hapticChanged)
            reports.Add(PadReportKind.HapticIntensity);
        if (triggerChanged)
            reports.Add(PadReportKind.TriggerIntensity);
        if (ledChanged)
            reports.Add(PadReportKind.LedColor);
        if (playerIndexChanged)
            reports.Add(PadReportKind.PlayerIndex);

        return new PadInfoReading(
            layout, reports, new PadState(playerIndex, led, haptic, trigger));
    }

    /// <summary>
    /// The order the five go out in, named once so a test asserts the sequence rather than a set.
    /// </summary>
    public static IReadOnlyList<PadReportKind> ReportOrder { get; } =
    [
        PadReportKind.MotionReset,
        PadReportKind.HapticIntensity,
        PadReportKind.TriggerIntensity,
        PadReportKind.LedColor,
        PadReportKind.PlayerIndex,
    ];
}

/// <summary>
/// PP689: the two layouts and the order, held against the handler they came from.
/// </summary>
public static class PadInfoMessageSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = StreamInfoMessageSource.RelativePath;

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The pad info handler's body, or null where it is gone.</summary>
    public static string? HandlerBody(string streamCore)
        => CFunction.Body(streamCore, "static void stream_connection_takion_data_pad_info(");

    /// <summary>Whether the two lengths the switch names are still these two.</summary>
    public static bool TheTwoLayoutsAreStillTheseLengths(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return handlerBody.Contains($"case 0x{PadInfoMessage.WideSize:x}:", StringComparison.Ordinal)
            && handlerBody.Contains($"case 0x{PadInfoMessage.NarrowSize:x}:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an unknown length still returns without reporting anything.
    ///
    /// A `break` there instead of a `return` would fall into the five sends with every flag false -
    /// which sends nothing today and would send everything the moment a flag was set above the
    /// switch. The `return` is what makes that unreachable rather than merely unlikely.
    /// </summary>
    public static bool AnUnknownLengthStillReturns(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int at = handlerBody.IndexOf("not equal to 0x19 or 0x11", StringComparison.Ordinal);
        if (at < 0)
            return false;

        return CCall.Happens(handlerBody[at..], "return;");
    }

    /// <summary>
    /// The order the five are sent in, read as the events the C raises after the switch.
    ///
    /// The sequence is the deliverable: both layouts share it, and it is decided where the parse is
    /// already over.
    ///
    /// PP744: PP719'S SWEEP, not a second copy of it. This was the same walk over the same literal,
    /// and when PP722 found that literal was anchored on a local's name - session.c calls two of its
    /// four raisers something other than event, and the sweep reported two in a file with four - the
    /// correction landed on one copy. This one was right for a reason that was not its own: every
    /// raiser in the handler it reads happens to be named event, exactly as the two files the sweep
    /// was written for were. The prefix on each name is the only difference, so it is stripped here
    /// rather than kept as a parallel loop that has already drifted once.
    /// </summary>
    public static IReadOnlyList<string> ReportOrderIn(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return
        [
            .. ManagedSessionEventsSource.EventsRaisedIn(handlerBody)
                .Select(one => one[ManagedSessionEventsSource.EventPrefix.Length..]),
        ];
    }

    /// <summary>
    /// Whether the timestamp is still read at its own width, which is PP374's repair.
    ///
    /// Four bytes swapped as four. As two, the read was truncated before the swap and the number
    /// advanced once per 65536 units - on the motion-reset path, which is where somebody looks when
    /// motion control drifts.
    /// </summary>
    public static bool TheTimestampIsStillReadAtItsOwnWidth(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return handlerBody.Contains(
            "ntohl(*(chiaki_unaligned_uint32_t *)(buf + 4))", StringComparison.Ordinal);
    }
}
