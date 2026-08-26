using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>An encoder and the message its failure log claims to have been encoding.</summary>
/// <param name="Function">The C function name.</param>
/// <param name="Claimed">
/// The phrase between "StreamConnection " and " protobuf encoding failed", which is the only thing
/// distinguishing these logs from one another.
/// </param>
public readonly record struct EncoderLog(string Function, string Claimed)
{
    /// <summary>
    /// Whether the phrase is the function's own, checked by DERIVATION rather than against a table.
    ///
    /// Every word of the claim has to appear in the function's name. A table of expected phrases would
    /// only restate what the file says and would be updated alongside a copy-paste; this cannot be,
    /// because the name is where the words have to come from.
    /// </summary>
    public bool NamesItsOwnMessage
    {
        get
        {
            string name = Function.ToLowerInvariant();

            return Claimed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => name.Contains(word.ToLowerInvariant(), StringComparison.Ordinal));
        }
    }

    /// <summary>Said the way a failure should read.</summary>
    public override string ToString() => $"{Function} logs \"{Claimed}\"";
}

/// <summary>
/// PP373: nine encoders in the stream connection, and two of them wore another's name.
///
///     stream_connection_enable_microphone   "controller connection protobuf encoding failed"
///     stream_connection_send_corrupt_frame  "heartbeat protobuf encoding failed"
///
/// Both were the previous function's line, kept when the function was copied from it. The failure is
/// real when it prints, and this log is the ONLY thing that distinguishes these paths: they return the
/// same error to callers that mostly pass it on, so what reaches a bug report is a log naming the
/// wrong message and a session that ended somewhere nobody can find.
///
/// The microphone one was the worse of the two, because "controller connection protobuf encoding
/// failed" already existed verbatim four lines up in a function that really does encode one. Two
/// identical sentences from two different functions means the log could not even narrow it down.
///
/// PP361 was this shape in a different register - a mic toggle logging the state it left rather than
/// the one it entered. There the words were inverted; here they belonged to another message entirely.
///
/// THE RULE IS DERIVED, NOT TABULATED. Every word of the claimed name has to appear in the function's
/// own name, so a tenth encoder copied from a ninth is caught without anyone remembering to add it to
/// a list.
/// </summary>
public static partial class EncoderLogIdentity
{
    /// <summary>Where the encoders live.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The sentence they all end with, which is what makes the phrase before it the identity.</summary>
    public const string Suffix = " protobuf encoding failed";

    [GeneratedRegex(
        @"^static ChiakiErrorCode (?<name>\w+)\(|^CHIAKI_EXPORT ChiakiErrorCode (?<name>\w+)\(",
        RegexOptions.Multiline)]
    private static partial Regex EncoderDefinition();

    [GeneratedRegex(
        @"""StreamConnection (?<claimed>[^""]+?) protobuf encoding failed""",
        RegexOptions.None)]
    private static partial Regex FailureLog();

    /// <summary>
    /// Every encoder in the file that logs an encoding failure, with what it claims to be encoding.
    ///
    /// Read out of each function's own body rather than by pairing the two regexes across the file:
    /// pairing by position is how a log lands in the neighbour of the function it belongs to and the
    /// reader agrees with it.
    /// </summary>
    public static IReadOnlyList<EncoderLog> EncodersIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var encoders = new List<EncoderLog>();

        // PP343's trap in a new shape. Every static function here is forward-declared, so the pattern
        // matches each of them twice - and CFunction correctly returns the DEFINITION's body both
        // times, which is not a wrong answer but is the same answer twice. Counted twice, every claim
        // in the file looked like a duplicate of itself. Names are unique in a translation unit, so
        // the first match of each is the whole of it.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match definition in EncoderDefinition().Matches(source))
        {
            string name = definition.Groups["name"].Value;
            if (!seen.Add(name))
                continue;

            string? body = CFunction.Body(source, definition.Value);
            if (body is null)
                continue;

            Match log = FailureLog().Match(body);
            if (!log.Success)
                continue;

            encoders.Add(new EncoderLog(name, log.Groups["claimed"].Value));
        }

        return encoders;
    }

    /// <summary>The ones whose log names a message that is not theirs.</summary>
    public static IReadOnlyList<EncoderLog> WearingAnothersName(IEnumerable<EncoderLog> encoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        return encoders.Where(e => !e.NamesItsOwnMessage).ToList();
    }

    /// <summary>
    /// And the claims that appear more than once, which is the sharper half of the same defect.
    ///
    /// Two functions logging the same sentence cannot be told apart at all, whether or not either
    /// name happens to fit.
    /// </summary>
    public static IReadOnlyList<string> ClaimsUsedTwice(IEnumerable<EncoderLog> encoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        return encoders
            .GroupBy(e => e.Claimed, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
    }
}
