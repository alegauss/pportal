using System.Buffers.Binary;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the three keyboard messages the console sent.</summary>
public enum KeyboardMessage
{
    /// <summary>KEYBOARD_OPEN: a field opened, carrying whatever is already in it.</summary>
    Open,

    /// <summary>KEYBOARD_TEXT_CHANGE_RES: the field is now this.</summary>
    TextChange,

    /// <summary>KEYBOARD_CLOSE_REMOTE: the console closed it. Carries nothing.</summary>
    RemoteClose,
}

/// <summary>Whether a handler raised its event, and where it stopped if it did not.</summary>
public enum KeyboardVerdict
{
    /// <summary>The event went to the screen.</summary>
    Raised,

    /// <summary>The payload was shorter than the header, so nothing was read.</summary>
    HeaderTooShort,

    /// <summary>
    /// The payload was not exactly the header plus the text it announced. PP357's check.
    /// </summary>
    TextLengthMismatch,
}

/// <summary>One arrival: what came, whether it was raised, and the text it carried.</summary>
/// <param name="Message">Which of the three.</param>
/// <param name="Verdict">Whether a screen heard about it.</param>
/// <param name="Text">
/// What the screen is handed. NULL in the C, so null here: see
/// <see cref="CtrlKeyboardArrivals.IsIndistinguishableFromAClose"/> for why that is worth a name
/// rather than a shrug.
/// </param>
public readonly record struct KeyboardArrival(
    KeyboardMessage Message, KeyboardVerdict Verdict, string? Text);

/// <summary>
/// PP409, under PP294: the three keyboard messages the console SENDS, and the empty text that
/// arrives as no text.
///
/// PP351 ported the four things a screen asks the control channel for. These are the three it hears
/// back, and nothing in this tree had written any of them down.
///
/// AN EMPTY TEXT ARRIVES AS NO TEXT. Both text-carrying handlers allocate only where the announced
/// length is non-zero - <c>msg-&gt;text_length &gt; 0 ? malloc(...) : NULL</c> - and then hand the
/// event whatever that produced. So a keyboard opening on an empty field reaches a screen as a NULL
/// <c>text_str</c>, which is the exact value KEYBOARD_CLOSE_REMOTE sends on purpose. A screen cannot
/// tell an opened keyboard with nothing in it from a keyboard that just closed, and those are
/// opposite instructions. Reproduced rather than fixed - PP294 does not own the event contract - but
/// named, because there is no consumer in this tree yet and the port is where a screen will be
/// written against it.
///
/// THE RESPONSE CARRIES TWO LENGTHS AND ONE IS READ. PP351 established that a text REQUEST writes
/// its byte length twice and that nothing in the tree says why. The response has the same pair,
/// <c>text_length1</c> at offset 8 and <c>text_length2</c> at offset 36, and the handler byte-swaps
/// and size-checks the first alone. Two fields that must agree with nothing comparing them is a
/// disagreement that reaches a screen as text. <see cref="TheTwoLengthsAgree"/> is the comparison
/// the C does not make; it is a reading and not a guard, so the arrival is raised either way.
///
/// THE SIZE GUARD IS PP357'S AND IS KEPT WHOLE. Each handler checks the header size, then checks the
/// payload is exactly the header plus the text it announced. That second check is the one PP357
/// turned from an assert - this tree builds Release with -DNDEBUG, so the assert was nothing in the
/// shipped binary - and it is what stands between a message announcing more text than it carries and
/// a memcpy out of the 512-byte receive buffer.
///
/// THE REMOTE CLOSE READS NOTHING AT ALL. It voids both payload arguments and sends its event, so
/// every payload length is accepted and none is looked at. Stated, because a port that added a guard
/// there would be stricter than the console it is talking to.
///
/// LATIN-1, AS ON THE SEND SIDE. The C memcpys bytes into a NUL-terminated buffer and hands out a
/// <c>const char *</c>; PP351 encodes the send side the same way. That keeps a round trip
/// byte-for-byte, which is what an oracle needs.
/// </summary>
public static class CtrlKeyboardArrivals
{
    /// <summary>sizeof(CtrlKeyboardOpenMessage): 0x1C unknown bytes then the length.</summary>
    public const int OpenHeaderSize = 32;

    /// <summary>Where the open message's only named field sits.</summary>
    public const int OpenTextLengthOffset = 28;

    /// <summary>sizeof(CtrlKeyboardTextResponseMessage).</summary>
    public const int TextResponseHeaderSize = 40;

    /// <summary>The response's counter, which the C reads no more than the unknowns.</summary>
    public const int ResponseCounterOffset = 0;

    /// <summary>text_length1 - the one the handler swaps and checks.</summary>
    public const int ResponseFirstLengthOffset = 8;

    /// <summary>text_length2 - the one nothing reads.</summary>
    public const int ResponseSecondLengthOffset = 36;

    /// <summary>What the open handler does with one payload.</summary>
    public static KeyboardArrival ReceiveOpen(ReadOnlySpan<byte> payload)
        => TextCarrying(KeyboardMessage.Open, payload, OpenHeaderSize, OpenTextLengthOffset);

    /// <summary>And the text-change handler, which differs only in its header and offset.</summary>
    public static KeyboardArrival ReceiveTextChange(ReadOnlySpan<byte> payload)
        => TextCarrying(
            KeyboardMessage.TextChange, payload, TextResponseHeaderSize, ResponseFirstLengthOffset);

    /// <summary>The remote close, which looks at nothing and always raises.</summary>
    public static KeyboardArrival ReceiveRemoteClose(ReadOnlySpan<byte> payload)
    {
        _ = payload.Length;
        return new KeyboardArrival(KeyboardMessage.RemoteClose, KeyboardVerdict.Raised, null);
    }

    /// <summary>
    /// The announced text length, as the handler byte-swaps it. Only meaningful past the header
    /// check.
    /// </summary>
    public static uint AnnouncedLength(ReadOnlySpan<byte> payload, int lengthOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lengthOffset);

        return payload.Length < lengthOffset + sizeof(uint)
            ? 0
            : BinaryPrimitives.ReadUInt32BigEndian(payload[lengthOffset..]);
    }

    /// <summary>
    /// Whether a text response's two length fields say the same thing.
    ///
    /// The comparison the C never makes. A reading rather than a guard: the handler raises its event
    /// whatever this answers, so this reports a disagreement and does not refuse one.
    /// </summary>
    public static bool TheTwoLengthsAgree(ReadOnlySpan<byte> payload)
        => payload.Length >= TextResponseHeaderSize
            && AnnouncedLength(payload, ResponseFirstLengthOffset)
                == AnnouncedLength(payload, ResponseSecondLengthOffset);

    /// <summary>
    /// Whether this arrival reached the screen as the same thing a close reaches it as.
    ///
    /// True for a raised open or text change carrying no text. That is the conflation: the screen
    /// holds a null text and the message that means "closed" also holds a null text, so the two are
    /// one value apart from the type - which the C event does carry, and which is the only reason
    /// this is a trap rather than a bug today.
    /// </summary>
    public static bool IsIndistinguishableFromAClose(KeyboardArrival arrival)
        => arrival.Verdict == KeyboardVerdict.Raised
            && arrival.Text is null
            && arrival.Message != KeyboardMessage.RemoteClose;

    private static KeyboardArrival TextCarrying(
        KeyboardMessage message, ReadOnlySpan<byte> payload, int headerSize, int lengthOffset)
    {
        if (payload.Length < headerSize)
            return new KeyboardArrival(message, KeyboardVerdict.HeaderTooShort, null);

        uint announced = AnnouncedLength(payload, lengthOffset);

        // PP357's check, whole: exactly the header plus what it announced, or nothing is read.
        if (payload.Length != headerSize + (long)announced)
            return new KeyboardArrival(message, KeyboardVerdict.TextLengthMismatch, null);

        // And the allocation the C only makes for a non-empty text. Zero yields null, not "".
        string? text = announced == 0
            ? null
            : Encoding.Latin1.GetString(payload[headerSize..]);

        return new KeyboardArrival(message, KeyboardVerdict.Raised, text);
    }
}

/// <summary>PP409: the three handlers' rules, still stated the same way in the core.</summary>
public static class CtrlKeyboardArrivalsSource
{
    /// <summary>Where all three live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The open handler's body, or null where the signature has moved.</summary>
    public static string? OpenBody(string core) => BodyOf(core, "keyboard_open");

    /// <summary>The text-change handler's body.</summary>
    public static string? TextChangeBody(string core) => BodyOf(core, "keyboard_text_change");

    /// <summary>The remote-close handler's body.</summary>
    public static string? RemoteCloseBody(string core) => BodyOf(core, "keyboard_close");

    /// <summary>
    /// Whether both header structs are still the fields this port gives offsets to.
    ///
    /// The unknown blocks are what make the headers 32 and 40 bytes rather than 4 and 16, so their
    /// sizes are as load-bearing as the fields with names.
    /// </summary>
    public static bool TheHeaderStructsAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? open = Declaration(core, "ctrl_keyboard_open_t");
        string? response = Declaration(core, "ctrl_keyboard_text_response_t");
        if (open is null || response is null)
            return false;

        return open.Contains("uint8_t unk[0x1C];", StringComparison.Ordinal)
            && open.Contains("uint32_t text_length;", StringComparison.Ordinal)
            && response.Contains("uint32_t counter;", StringComparison.Ordinal)
            && response.Contains("uint32_t unk;", StringComparison.Ordinal)
            && response.Contains("uint32_t text_length1;", StringComparison.Ordinal)
            && response.Contains("uint32_t unk2;", StringComparison.Ordinal)
            && response.Contains("uint8_t unk3[0x10];", StringComparison.Ordinal)
            && response.Contains("uint32_t unk4;", StringComparison.Ordinal)
            && response.Contains("uint32_t text_length2;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether PP357's exact-size check still stands in both text-carrying handlers, after the
    /// header check rather than instead of it.
    /// </summary>
    public static bool TheExactSizeCheckStillStands(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return ChecksHeaderThenExactSize(OpenBody(core), "text_length")
            && ChecksHeaderThenExactSize(TextChangeBody(core), "text_length1");
    }

    /// <summary>
    /// Whether an empty text still allocates nothing, which is what makes the event carry NULL.
    ///
    /// This is the finding, read out of the C rather than asserted about the port alone: the
    /// conditional allocation and the event field it feeds, in that order.
    /// </summary>
    public static bool AnEmptyTextStillCarriesNothing(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CarriesTheAllocationStraightThrough(OpenBody(core), "text_length")
            && CarriesTheAllocationStraightThrough(TextChangeBody(core), "text_length1");
    }

    /// <summary>
    /// Whether the response's second length is still read by nothing.
    ///
    /// Declared in the struct - <see cref="TheHeaderStructsAreStillThese"/> holds that - and absent
    /// from the handler that parses one. PP400's rule applies: comments are stripped before an
    /// absence is claimed, since the comment naming the field would otherwise satisfy the search.
    /// </summary>
    public static bool TheSecondLengthIsStillNeverRead(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = TextChangeBody(core);
        if (body is null)
            return false;

        return !CCall.Code(body).Contains("text_length2", StringComparison.Ordinal);
    }

    /// <summary>Whether the remote close still voids its payload rather than reading it.</summary>
    public static bool TheRemoteCloseStillReadsNothing(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = RemoteCloseBody(core);
        if (body is null)
            return false;

        // Compacted, so the two casts are found however they are laid out - and asked of the body
        // alone, which is what makes "reads nothing" a claim about this handler.
        string code = CCall.Compact(CCall.Code(body));

        return code.Contains("(void)payload;", StringComparison.Ordinal)
            && code.Contains("(void)payload_size;", StringComparison.Ordinal)
            && !code.Contains("if(", StringComparison.Ordinal);
    }

    /// <summary>Whether the three still raise the three events this port names them by.</summary>
    public static bool TheEventsAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return Raises(OpenBody(core), "CHIAKI_EVENT_KEYBOARD_OPEN")
            && Raises(TextChangeBody(core), "CHIAKI_EVENT_KEYBOARD_TEXT_CHANGE")
            && Raises(RemoteCloseBody(core), "CHIAKI_EVENT_KEYBOARD_REMOTE_CLOSE");
    }

    private static string? BodyOf(string core, string which)
    {
        ArgumentNullException.ThrowIfNull(core);

        // PP359: the whole signature. A prefix that stops at the name matches a longer one, and
        // keyboard_close is a prefix of nothing here only by luck of the ordering.
        return CFunction.Body(
            core,
            $"static void ctrl_message_received_{which}"
                + "(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)");
    }

    private static string? Declaration(string core, string name)
    {
        int at = core.IndexOf(name, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int end = core.IndexOf('}', at);
        return end < 0 ? null : core[at..end];
    }

    private static bool ChecksHeaderThenExactSize(string? body, string lengthField)
    {
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int header = CCall.Mark(code, "if(payload_size < sizeof(");
        int exact = CCall.Mark(code, $"if(payload_size != sizeof(");

        return header >= 0
            && exact > header
            && code.Contains($"+ msg->{lengthField})", StringComparison.Ordinal);
    }

    private static bool CarriesTheAllocationStraightThrough(string? body, string lengthField)
    {
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int allocation = CCall.Mark(code, $"msg->{lengthField} > 0 ? malloc(");
        int handover = CCall.Mark(code, "keyboard_event.keyboard.text_str = (const char *)buffer;");

        return allocation >= 0 && handover > allocation;
    }

    private static bool Raises(string? body, string eventType)
        => body is not null
            && CCall.Code(body).Contains(
                $"keyboard_event.type = {eventType};", StringComparison.Ordinal);
}
