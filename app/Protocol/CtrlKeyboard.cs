using System.Buffers.Binary;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP351, under PP294: the four things a screen can ask the control channel for, and the bytes that
/// tell two of them apart.
///
/// All four are the send queue with a payload built in front of it. They are small, and they are the
/// whole of what the keyboard and the power button do.
///
/// ACCEPT AND REJECT ARE THE SAME MESSAGE. Both are KEYBOARD_CLOSE_REQ with a four-byte payload,
/// and the only difference is the last byte: zero accepts what the user typed, one rejects it.
/// Nothing in the C names either constant, and swapping them sends the console the opposite of what
/// was pressed with nothing anywhere to catch it - which is why they are named here.
///
/// THE TEXT REQUEST WRITES ITS LENGTH TWICE. The header is 36 bytes: a counter, the length, 24
/// bytes that are zeroed and never written, and the length AGAIN. Both are the byte length of the
/// text, and nothing in the tree says why there are two - so a port writing one and leaving the
/// other zero is sending a message the console reads differently, and would find out at the far
/// end rather than here.
///
/// THE COUNTER IS PRE-INCREMENTED, so the first text a session sends carries one and not zero. It
/// is also read and written outside any lock, from whatever thread the screen runs on: two edits
/// racing would produce two messages with the same number, and that number is how the console
/// orders them. Reproduced rather than fixed - the lock is a change to the C's threading and PP294
/// does not own that.
/// </summary>
public static class CtrlKeyboard
{
    /// <summary>sizeof(CtrlKeyboardTextRequestMessage): the text starts after this.</summary>
    public const int TextRequestHeaderSize = 36;

    /// <summary>Where the counter sits.</summary>
    public const int CounterOffset = 0;

    /// <summary>Where the first copy of the length sits.</summary>
    public const int FirstLengthOffset = 4;

    /// <summary>Where the second copy sits, 24 zeroed bytes after the first.</summary>
    public const int SecondLengthOffset = 32;

    /// <summary>The payload that accepts what the user typed.</summary>
    public static ReadOnlySpan<byte> Accept => [0x00, 0x00, 0x00, 0x00];

    /// <summary>And the one that rejects it - the same message, one byte apart.</summary>
    public static ReadOnlySpan<byte> Reject => [0x00, 0x00, 0x00, 0x01];

    /// <summary>
    /// A keyboard text request, laid out as chiaki_ctrl_keyboard_set_text lays one out.
    /// </summary>
    /// <param name="text">What the user typed. Its BYTE length is what both fields carry.</param>
    /// <param name="counter">
    /// Already incremented by the caller, because the C pre-increments: the first request of a
    /// session carries one.
    /// </param>
    public static byte[] TextRequest(string text, uint counter)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Latin-1 rather than UTF-8: the C takes strlen of whatever it was handed and copies bytes,
        // so what goes on the wire is the caller's encoding and its length in bytes.
        byte[] bytes = Encoding.Latin1.GetBytes(text);

        // Zeroed whole, which is what leaves the 24 unknown bytes zero.
        var payload = new byte[TextRequestHeaderSize + bytes.Length];

        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(CounterOffset), counter);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(FirstLengthOffset), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(SecondLengthOffset), (uint)bytes.Length);

        bytes.CopyTo(payload, TextRequestHeaderSize);

        return payload;
    }

    /// <summary>The text a request carries, read back out of one.</summary>
    public static string TextIn(ReadOnlySpan<byte> payload)
        => payload.Length <= TextRequestHeaderSize
            ? ""
            : Encoding.Latin1.GetString(payload[TextRequestHeaderSize..]);

    /// <summary>What the two length fields of a request say. They should agree.</summary>
    public static (uint First, uint Second) LengthsIn(ReadOnlySpan<byte> payload)
        => (BinaryPrimitives.ReadUInt32BigEndian(payload[FirstLengthOffset..]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[SecondLengthOffset..]));

    /// <summary>The counter a request carries.</summary>
    public static uint CounterIn(ReadOnlySpan<byte> payload)
        => BinaryPrimitives.ReadUInt32BigEndian(payload[CounterOffset..]);

    /// <summary>Each of the four asks, as the type and payload it queues.</summary>
    public static QueuedCtrlMessage GotoBed() => new((ushort)CtrlMessage.GotoBed, []);

    /// <summary>Accepting what was typed.</summary>
    public static QueuedCtrlMessage AcceptText()
        => new((ushort)CtrlMessage.KeyboardCloseReq, Accept.ToArray());

    /// <summary>And rejecting it - note the type is identical.</summary>
    public static QueuedCtrlMessage RejectText()
        => new((ushort)CtrlMessage.KeyboardCloseReq, Reject.ToArray());

    /// <summary>Setting the text, at a counter the caller has already advanced.</summary>
    public static QueuedCtrlMessage SetText(string text, uint counter)
        => new((ushort)CtrlMessage.KeyboardTextChangeReq, TextRequest(text, counter));
}

/// <summary>
/// PP351: the four asks held against ctrl.c, since none of them is in PP297's capture - that session
/// opened no keyboard and nobody pressed the power button.
/// </summary>
public static class CtrlKeyboardSource
{
    /// <summary>Where they live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the request header is still the five fields this reproduces, in order.
    ///
    /// The two unknown blocks are what make the header 36 bytes rather than 12, so their sizes are
    /// as load-bearing as the fields with names.
    /// </summary>
    public static bool TheRequestHeaderIsStill(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf("ctrl_keyboard_text_request_t", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = source.IndexOf('}', at);
        if (end < 0)
            return false;

        string declared = source[at..end];

        return declared.Contains("uint32_t counter;", StringComparison.Ordinal)
            && declared.Contains("uint32_t text_length1;", StringComparison.Ordinal)
            && declared.Contains("uint8_t unk1[0x8];", StringComparison.Ordinal)
            && declared.Contains("uint8_t unk2[0x10];", StringComparison.Ordinal)
            && declared.Contains("uint32_t text_length2;", StringComparison.Ordinal);
    }

    /// <summary>Whether both length fields are still written, and with the same value.</summary>
    public static bool BothLengthsAreStillWritten(string setTextBody)
    {
        ArgumentNullException.ThrowIfNull(setTextBody);

        return setTextBody.Contains("msg->text_length1 = htonl(length);", StringComparison.Ordinal)
            && setTextBody.Contains("msg->text_length2 = htonl(length);", StringComparison.Ordinal);
    }

    /// <summary>Whether the counter is still pre-incremented, so the first request carries one.</summary>
    public static bool TheCounterIsStillPreIncremented(string setTextBody)
    {
        ArgumentNullException.ThrowIfNull(setTextBody);

        return setTextBody.Contains(
            "htonl(++ctrl->keyboard_text_counter)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether accept and reject still differ only in their last byte, on one message type.
    ///
    /// Read as the two payloads rather than as their names: a port that swapped them would send the
    /// opposite of what the user pressed, and the names would still look right.
    /// </summary>
    public static bool AcceptAndRejectStillDifferByOneByte(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains("accept[4] = { 0x00, 0x00, 0x00, 0x00 }", StringComparison.Ordinal)
            && source.Contains("reject[4] = { 0x00, 0x00, 0x00, 0x01 }", StringComparison.Ordinal)
            && CountOf(source, "CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REQ") >= 2;
    }

    private static int CountOf(string haystack, string needle)
    {
        var found = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
