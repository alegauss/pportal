using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP367: no caller of the gkcrypt cipher discards what it answered.
///
/// chiaki_gkcrypt_decrypt returns a ChiakiErrorCode and has two ways to fail - the key stream
/// allocation, and get_key_stream. On either it returns BEFORE the xor, so the buffer it was handed
/// is untouched: still ciphertext.
///
/// The AV route discarded it. So a failed decrypt handed ciphertext to the video receiver, which
/// parsed it as a frame - and what that looks like from outside is not an error but a frame that
/// decodes into noise, a frame processor reporting corruption, and an IDR requested for a packet
/// that arrived intact and was mangled locally. Under memory pressure, which is exactly when the
/// allocation fails, a stream that degrades and blames the network.
///
/// THE CHECK IS OVER EVERY CALL, not the one that was wrong. The two in takion.c already assigned
/// and tested their result, so the discard was the odd one out - which is the shape a second one
/// would take.
/// </summary>
public static partial class GkCryptResults
{
    /// <summary>The files that use the cipher.</summary>
    public static IReadOnlyList<string> Callers { get; } =
        [@"lib\src\streamconnection.c", @"lib\src\takion.c"];

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Every call to the cipher whose result is discarded.
    ///
    /// A discard is a call that opens a statement: nothing to its left on the line but whitespace.
    /// A call assigned to something, returned, or tested is not.
    /// </summary>
    /// <returns>The call text of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> DiscardedResults(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match call in DiscardedCall().Matches(source))
            found.Add(call.Value.Trim());

        return found;
    }

    // A cipher call at the start of a line - nothing but whitespace to its left, so whatever it
    // returns goes nowhere. Multiline rather than a lookbehind for a newline: the first line of a
    // file, or of a fragment a test hands over, is a line start too. The definition in gkcrypt.c
    // begins with its return type and does not match.
    [GeneratedRegex(@"^[ \t]*chiaki_gkcrypt_(?:decrypt|encrypt)\s*\(", RegexOptions.Multiline)]
    private static partial Regex DiscardedCall();
}
