using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What a message arriving where a disconnect might be turned out to be.</summary>
/// <param name="Disconnected">
/// Whether the remote hung up - the C's <c>remote_disconnected</c>, which the session thread waits
/// on. False on every refusal, which is the whole risk: a client not told is a client waiting.
/// </param>
/// <param name="Reason">
/// The reason the console gave, or null where there was none to read. Empty is a real answer: the
/// field is required, so a console can send one of no characters and the C keeps it.
/// </param>
/// <param name="Undecodable">
/// Whether the message failed to decode, which is what a reason past its bound does. Distinguished
/// from "not a disconnect" because the C logs them differently and only one is the console's fault.
/// </param>
public readonly record struct DisconnectReading(bool Disconnected, string? Reason, bool Undecodable);

/// <summary>
/// PP687, under PP295: the disconnect the console sends, read - and the bound that makes a long
/// reason no disconnect at all.
///
/// PP686 made this message reachable: a DISCONNECT arriving where a streaminfo was expected is
/// routed rather than dropped. Nothing read what it carried, so the port could tell that the console
/// hung up and not why - and could not tell one case from another at all.
///
/// THE REASON IS BOUNDED AT <see cref="ReasonBound"/> AND THE BOUND REFUSES. The C gives nanopb a
/// 256-byte array and 255 as the maximum, through chiaki_pb_decode_buf, whose first act is to return
/// false when the field is longer. That failure is not local to the field: it fails the enclosing
/// pb_decode, so the handler logs a failed decode and returns having set nothing. A console
/// disconnecting with a reason past the bound leaves this side never told it disconnected.
///
/// WHICH IS NOT A TRUNCATION, and that is the distinction worth writing down. A port that clipped
/// the reason at 255 and carried on would be more useful and would disagree with the console's own
/// client about whether the session ended - so it is reproduced, and the case that names it is the
/// one that would fail a tidier port.
///
/// The C also keeps the reason with strdup and logs where that fails, which PP371 recorded because
/// the session thread then reads a null twice. There is no such path here: a managed string either
/// exists or the reading says it did not.
/// </summary>
public static class DisconnectMessage
{
    /// <summary>
    /// The longest reason the C can read: its array is 256 and the maximum it declares is one less,
    /// leaving room for the terminator it writes after the decode.
    /// </summary>
    public const int ReasonBound = 255;

    /// <summary>The array the C declares, which is the bound plus that terminator.</summary>
    public const int ReasonBufferSize = 256;

    /// <summary>Reads one message as the disconnect handler reads it.</summary>
    /// <param name="message">The whole TakionMessage, as the data handler receives it.</param>
    public static DisconnectReading Read(ReadOnlySpan<byte> message)
    {
        Tkproto.TakionMessage decoded;

        try
        {
            decoded = Tkproto.TakionMessage.Parser.ParseFrom(message.ToArray());
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            return new DisconnectReading(false, null, Undecodable: true);
        }

        // The reason is read through a bounded buffer, so a longer one fails the decode itself.
        // protoc's parser has no such bound, which is why it is applied here.
        string? reason = decoded.DisconnectPayload?.Reason;

        if (reason is not null && System.Text.Encoding.UTF8.GetByteCount(reason) > ReasonBound)
            return new DisconnectReading(false, null, Undecodable: true);

        // The C sets remote_disconnected whatever the type turned out to be - the handler is only
        // ever called for a message already read as a DISCONNECT, and it does not check again. What
        // reaches it is the idle handler's switch and the streaminfo handler's routing.
        return new DisconnectReading(Disconnected: true, reason ?? string.Empty, Undecodable: false);
    }

    /// <summary>
    /// Whether a message is one the disconnect handler would be called for at all.
    ///
    /// Kept apart from <see cref="Read"/> because the C keeps them apart: the type is tested by the
    /// caller and the handler assumes it. A reader that tested the type itself would be a different
    /// function, and would answer differently for a message the C routes here regardless.
    /// </summary>
    public static bool IsDisconnect(ReadOnlySpan<byte> message)
    {
        try
        {
            return Tkproto.TakionMessage.Parser.ParseFrom(message.ToArray()).Type
                == Tkproto.TakionMessage.Types.PayloadType.Disconnect;
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            return false;
        }
    }
}

/// <summary>
/// PP687: the bound, read out of the C rather than restated - and the helper that makes it a
/// refusal.
/// </summary>
public static class DisconnectMessageSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = StreamInfoMessageSource.RelativePath;

    /// <summary>Where the bounded read lives, which is the finding.</summary>
    public const string DecodeHelperRelativePath = @"lib\src\pb_utils.h";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>The disconnect handler's body, or null where it is gone.</summary>
    public static string? HandlerBody(string streamCore)
        => CFunction.Body(streamCore, "static void stream_connection_takion_data_handle_disconnect(");

    /// <summary>
    /// Whether the bounded read still REFUSES a field past its maximum rather than truncating it.
    ///
    /// The whole of PP687 in three lines of a header: the size is zeroed and false is returned, and
    /// false from a decode callback fails the message. A helper that clipped instead would make
    /// every caller's bound a truncation, and two readers here would be wrong the other way.
    /// </summary>
    public static bool TheBoundedReadStillRefuses(string helper)
    {
        ArgumentNullException.ThrowIfNull(helper);

        string text = helper.ReplaceLineEndings("\n");

        int test = text.IndexOf("stream->bytes_left > buf->max_size", StringComparison.Ordinal);
        if (test < 0)
            return false;

        // What it does about it, within its own branch: zero the size and refuse.
        int refuses = text.IndexOf("return false;", test, StringComparison.Ordinal);
        int reads = text.IndexOf("pb_read(stream", test, StringComparison.Ordinal);

        return refuses > test && (reads < 0 || refuses < reads);
    }

    /// <summary>
    /// Whether the reason is still bounded by one less than its array, which is what leaves room for
    /// the terminator written after the decode.
    /// </summary>
    public static bool TheReasonIsStillBoundedBelowItsArray(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return handlerBody.Contains($"char reason[{DisconnectMessage.ReasonBufferSize}];", StringComparison.Ordinal)
            && handlerBody.Contains("decode_buf.max_size = sizeof(reason) - 1;", StringComparison.Ordinal)
            && handlerBody.Contains("reason[decode_buf.size] = '\\0';", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a failed decode still returns before the flag is set.
    ///
    /// The order is the behaviour: past it, remote_disconnected is written and the session thread is
    /// woken. A port that set the flag first would tell the client a session ended on a message it
    /// could not read.
    /// </summary>
    public static bool AFailedDecodeStillReturnsBeforeTheFlag(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int failed = handlerBody.IndexOf("failed to decode data protobuf", StringComparison.Ordinal);
        int flag = handlerBody.IndexOf("remote_disconnected = true", StringComparison.Ordinal);
        if (failed < 0 || flag < 0 || failed > flag)
            return false;

        return CCall.Happens(handlerBody[failed..flag], "return;");
    }
}
