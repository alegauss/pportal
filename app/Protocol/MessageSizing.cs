namespace ChiakiNg.Protocol;

/// <summary>
/// PP250: how much room a session message is given, which is not measured from what goes in it.
///
/// PP191 and PP33 ported this writer's FORMAT. This is the other half: the sizing, the length handed
/// back, and the failures nobody reads.
///
/// THE BUFFER IS SIZED FROM THE WRONG FORMAT STRING. The allocation length is the ENVELOPE format's
/// size doubled plus the connection request; the text written into it comes from the MESSAGE format.
/// Two different constants for two different strings, and the arithmetic is safe only because the
/// one it happens to use is the larger. The two lengths are not transcribed here - they are measured
/// off the source by <see cref="MessageSizingSource.LengthOfEnvelopeFormat"/> and its sibling, so a
/// change to either format is caught rather than mirrored by hand.
///
/// AND THE LENGTH HANDED BACK IS WHAT SNPRINTF WOULD HAVE WRITTEN, not what it did. A message that
/// did not fit reports a length longer than itself. The caller then sizes the envelope's stack array
/// from that number - too large rather than too small, which is the safe direction, and still a
/// length that does not describe the string it arrives with. See <see cref="LengthOverstates"/>.
///
/// Neither serializer's result is read. An allocation failure leaves the payload null and the length
/// zero, and the caller hands that pointer to a percent-s. Nothing here allocates, so there is
/// nothing to reproduce - what ships is the naming, and the assertion that the call sites still
/// discard the code.
/// </summary>
public static class MessageSizing
{
    /// <summary>The connection request an ack carries, terminator included - three bytes.</summary>
    public const int AckConnectionRequestLength = 3;

    /// <summary>The multiplier the sizing applies to the format it took.</summary>
    public const int Doubled = 2;

    /// <summary>
    /// How large the buffer is made, from the length of the format it did NOT write.
    /// </summary>
    public static int BufferFor(int sizingFormatLength, int connectionRequestLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizingFormatLength);
        ArgumentOutOfRangeException.ThrowIfNegative(connectionRequestLength);

        return (sizingFormatLength * Doubled) + connectionRequestLength;
    }

    /// <summary>
    /// Whether the sizing covers the text, for the two format lengths and what is substituted in.
    ///
    /// The point is not the answer - it is that the answer depends on a relationship between two
    /// constants that nothing in the core compares.
    /// </summary>
    public static bool Fits(
        int sizingFormatLength, int writtenFormatLength, int connectionRequestLength, int substituted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(writtenFormatLength);
        ArgumentOutOfRangeException.ThrowIfNegative(substituted);

        return writtenFormatLength + substituted
            <= BufferFor(sizingFormatLength, connectionRequestLength);
    }

    /// <summary>
    /// The length the serializer reports: what would have been written, whether or not it was.
    /// </summary>
    public static int ReportedLength(int wouldHaveWritten, int buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wouldHaveWritten);
        ArgumentOutOfRangeException.ThrowIfNegative(buffer);

        // Taken from the call and passed straight out - the buffer is not consulted.
        return wouldHaveWritten;
    }

    /// <summary>And the length actually produced, which is what a reader would assume it meant.</summary>
    public static int ActualLength(int wouldHaveWritten, int buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wouldHaveWritten);
        ArgumentOutOfRangeException.ThrowIfNegative(buffer);

        // snprintf writes at most buffer - 1 characters, then terminates.
        return buffer == 0 ? 0 : Math.Min(wouldHaveWritten, buffer - 1);
    }

    /// <summary>Whether the reported length overstates the string it comes with.</summary>
    public static bool LengthOverstates(int wouldHaveWritten, int buffer)
        => ReportedLength(wouldHaveWritten, buffer) > ActualLength(wouldHaveWritten, buffer);

    /// <summary>
    /// The envelope's stack array, sized from the length the serializer reported - so a truncated
    /// payload makes the array LARGER than the text it will hold.
    /// </summary>
    public static int EnvelopeBufferFor(int sizingFormatLength, int reportedPayloadLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reportedPayloadLength);
        return BufferFor(sizingFormatLength, reportedPayloadLength);
    }

    /// <summary>
    /// What the caller holds when a serializer failed: no payload, and a length of zero.
    ///
    /// The failure is never read, so this is what reaches the envelope's percent-s.
    /// </summary>
    public static (string? Payload, int Length) AfterAFailure() => (null, 0);

    /// <summary>The core's own reason for not using a JSON library, quoted.</summary>
    public const string WhyNotAJsonLibrary =
        "Since the official remote play app doesn't send valid JSON half the time, "
        + "we can't use a proper JSON library to serialize the message.";
}

/// <summary>
/// PP250: the sizing where the core writes it - including the two lengths themselves, measured
/// rather than transcribed.
/// </summary>
public static class MessageSizingSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>
    /// The length sizeof() gives for the envelope format - its bytes plus the terminator.
    /// </summary>
    public static int LengthOfEnvelopeFormat(string core)
        => FormatLength(core, "session_message_envelope_fmt");

    /// <summary>And for the message format, which is what is actually written.</summary>
    public static int LengthOfMessageFormat(string core)
        => FormatLength(core, "session_message_fmt");

    /// <summary>
    /// The length of one of these format strings, read out of the file.
    ///
    /// The fragments are adjacent C string literals with comments between them. Escapes are decoded
    /// so the count is bytes rather than source characters, which is what sizeof answers.
    /// </summary>
    private static int FormatLength(string core, string name)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(name);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int at = text.IndexOf($"static const char {name}[] =", StringComparison.Ordinal);
        if (at < 0)
            return -1;

        int end = text.IndexOf(";\n", at, StringComparison.Ordinal);
        if (end < 0)
            return -1;

        int bytes = 0;
        foreach (string line in text[at..end].Split('\n'))
        {
            // Comments carry quotes of their own in this file, so cut them first.
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            string code = comment >= 0 ? line[..comment] : line;

            foreach (string fragment in Fragments(code))
                bytes += Decoded(fragment);
        }

        // PRId64 is a macro, not part of the literal - it expands to a length specifier.
        if (text[at..end].Contains("PRId64", StringComparison.Ordinal))
            bytes += "lld".Length;

        // Plus the terminator sizeof counts.
        return bytes == 0 ? -1 : bytes + 1;
    }

    /// <summary>Every double-quoted run in one line of C.</summary>
    private static IEnumerable<string> Fragments(string code)
    {
        int at = 0;
        while (true)
        {
            int open = code.IndexOf('"', at);
            if (open < 0)
                yield break;

            int close = open + 1;
            while (close < code.Length && (code[close] != '"' || code[close - 1] == '\\'))
                close++;

            if (close >= code.Length)
                yield break;

            yield return code[(open + 1)..close];
            at = close + 1;
        }
    }

    /// <summary>How many bytes a C string fragment's source characters stand for.</summary>
    private static int Decoded(string fragment)
    {
        int bytes = 0;
        int at = 0;

        while (at < fragment.Length)
        {
            // Every escape in these formats is a two-character one.
            at += fragment[at] == '\\' && at + 1 < fragment.Length ? 2 : 1;
            bytes++;
        }

        return bytes;
    }

    /// <summary>
    /// Whether the buffer is still sized from the envelope format and written with the message one.
    /// </summary>
    public static bool TheSizingStillUsesTheOtherFormat(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                $"serialized_msg_len = sizeof(session_message_envelope_fmt) * {MessageSizing.Doubled} + connreq_len;",
                StringComparison.Ordinal)
            && text.Contains(
                "serialized_msg, serialized_msg_len, session_message_fmt,", StringComparison.Ordinal);
    }

    /// <summary>And how many serializers size it that way - both of them.</summary>
    public static int HowManySerializersSizeItThatWay(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Split(
            "sizeof(session_message_envelope_fmt) * 2 + connreq_len",
            StringSplitOptions.None).Length - 1;
    }

    /// <summary>Whether the reported length is still snprintf's return, unclamped.</summary>
    public static bool TheReportedLengthIsStillWhatWouldHaveBeenWritten(string core)
    {
        string body = Body(core);

        int wrote = body.IndexOf("CHIAKI_SSIZET_TYPE msg_len = snprintf(", StringComparison.Ordinal);
        if (wrote < 0)
            return false;

        // Past the end of the call itself, so its own assignment is not what this finds.
        int callEnds = body.IndexOf("connreq_json);", wrote, StringComparison.Ordinal);
        if (callEnds < 0)
            return false;

        int reports = body.IndexOf("*out_len = msg_len;", callEnds, StringComparison.Ordinal);
        if (reports < 0)
            return false;

        // Nothing between the call and the hand-off reduces it.
        return !body[callEnds..reports].Contains("msg_len =", StringComparison.Ordinal);
    }

    /// <summary>Whether the ack's connection request is still the literal, three bytes long.</summary>
    public static bool TheAckRequestIsStillThreeBytes(string core)
    {
        string body = Body(core);

        return body.Contains(
                $"char connreq_json[{MessageSizing.AckConnectionRequestLength}] = {{ '{{', '}}', '\\0' }};",
                StringComparison.Ordinal)
            && body.Contains("size_t connreq_len = sizeof(connreq_json);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the caller still discards both serializers' results, and still sizes a stack array
    /// from the length one of them reported.
    /// </summary>
    public static bool TheCallerStillDiscardsTheResult(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
            """
            if(short_msg)
                    short_message_serialize(session, message, &payload_str, &payload_len);
                else
                    session_message_serialize(session, message, &payload_str, &payload_len);
                char msg_buf[sizeof(session_message_envelope_fmt) * 2 + payload_len];
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>And whether the core still explains itself in those words.</summary>
    public static bool TheReasonIsStillStated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // The comment is wrapped across lines, so compare it with the wrapping taken out.
        string flat = string.Join(
            ' ',
            core.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(l => l.Trim().TrimStart('/').Trim()));

        return flat.Contains(MessageSizing.WhyNotAJsonLibrary, StringComparison.Ordinal);
    }

    /// <summary>short_message_serialize's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason eight earlier tasks each wrote down.
        int start = text.LastIndexOf(
            "static ChiakiErrorCode short_message_serialize(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic ", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
