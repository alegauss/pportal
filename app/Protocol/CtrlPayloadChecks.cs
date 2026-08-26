using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP352, under PP294: no handler reads a payload byte without first looking at the size.
///
/// ctrl.c has eleven received-message handlers and most of them check. The session id refuses under
/// two bytes, the login message warns and returns under one, the heartbeat and the stream switch
/// warn about a payload they did not want. The two display handlers did neither: they read
/// payload[0] - and payload[1] - and never mentioned payload_size at all.
///
/// NOT OUT OF BOUNDS, AND THE HONEST DESCRIPTION MATTERS. payload points eight bytes into a
/// 512-byte buffer that always has room, so a zero-length message read a byte that was inside the
/// buffer and was not part of the message: whatever the previous message left, or whatever was
/// never written. The read is safe and the behaviour is not.
///
/// What followed was a state machine driven by leftovers. cant_displaya and cant_displayb decide
/// whether the client tells its display sink the stream cannot be shown, and the recorded DISPLAYB
/// payload is 01-ff - the pair that CLEARS the flag. So a short message read as anything else takes
/// the branch that raises it, and the user's stream stops for content that is not playing.
///
/// The check below is a shape and not a list: a handler is required to mention payload_size before
/// it indexes payload. That is what notices the twelfth handler written without one.
/// </summary>
public static partial class CtrlPayloadChecks
{
    /// <summary>Where the handlers live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every received-message handler that indexes its payload without mentioning its size.
    ///
    /// A handler is found by its declaration shape, and its body is read through
    /// <see cref="CFunction"/> so a forward declaration cannot be mistaken for one - the trap
    /// PP343 gave a single reader for.
    /// </summary>
    /// <returns>The name of each unchecked handler, so a failure names what it found.</returns>
    public static IReadOnlyList<string> HandlersThatIndexWithoutChecking(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match handler in HandlerName().Matches(source))
        {
            string name = handler.Groups["name"].Value;
            if (found.Contains(name))
                continue;

            string? body = CFunction.Body(source, name);
            if (body is null)
                continue;

            // Indexing at all, and never naming the size that bounds it.
            bool indexes = body.Contains("payload[", StringComparison.Ordinal);
            bool checks = body.Contains("payload_size", StringComparison.Ordinal);

            if (indexes && !checks)
                found.Add(name);
        }

        return found;
    }

    // The handlers, by the shape ctrl.c declares them in. The prototypes at the top of the file
    // match too, which is harmless: the body reader resolves each name to its definition.
    [GeneratedRegex(@"\b(?<name>ctrl_message_received_\w+)\s*\(\s*ChiakiCtrl")]
    private static partial Regex HandlerName();
}
