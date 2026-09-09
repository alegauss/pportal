using System.Globalization;
using System.Reflection;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// One managed call into a takion send, and the C call site it ports.
/// </summary>
/// <param name="Name">What to say when it disagrees.</param>
/// <param name="CFile">The vendored source holding the call.</param>
/// <param name="CCallText">The C call, verbatim, so a rewrite upstream is a red check.</param>
/// <param name="CFunction">The transport function, whose parameter NAMES the header supplies.</param>
/// <param name="ManagedFile">The port's file.</param>
/// <param name="ManagedCallText">The managed call, verbatim.</param>
/// <param name="ManagedMethod">The method on <see cref="ManagedTakion"/>, for its parameter names.</param>
/// <param name="Owner">
/// The managed type the call sits in, which is where an unqualified constant is looked up. A file
/// can name a constant of its own without spelling the class, and the reader has to follow that.
/// </param>
public sealed record PortedSend(
    string Name,
    string CFile,
    string CCallText,
    string CFunction,
    string ManagedFile,
    string ManagedCallText,
    string ManagedMethod,
    string Owner);

/// <summary>
/// PP831: the join from a managed send's arguments to the C call's, by parameter name.
///
/// THIS EXACT MISTAKE HAS BEEN MADE TWICE. PP778 found every stream message going out with the data
/// type in the chunk flags and the channel at zero; PP813 found senkusha doing the same six tasks
/// later, green the whole time. Nothing connected the two and nothing would have caught a third.
///
/// WHAT THE BUILDER TESTS CANNOT SEE is the shape. SenkushaMessages and StreamMessages both carry
/// the right number - one for a version request, eight for an echo command - and both are asserted
/// against the C field for field. The number is correct either way; only the ARGUMENT it is spent
/// in differs, and that is decided one layer down at the call.
///
/// SO THE CHECK IS ABOUT KIND AND NOT VALUE. The C's chokepoints both pass <c>(1, data_type)</c> - a
/// LITERAL for chunk_flags and a VARYING expression for the channel. An inversion swaps those two
/// kinds and nothing else, which is exactly what a reader can see and a builder cannot: a managed
/// call whose channel resolves to a constant is the bug, whatever the constant is.
///
/// THE BIG'S FRAGMENT LOOP IS DELIBERATELY NOT HERE, and the reason is structural rather than
/// tiredness. Its two C sites - <c>(0, 1, ...)</c> and <c>(1, 1, ...)</c> - collapse into ONE
/// managed site whose flags are a ternary, so there is no call-to-call correspondence to compare;
/// and its channel is the literal 1 in the C against <c>message.DataType</c> in the port, which is
/// 1 only because <see cref="ManagedStreamPhase"/> builds the BIG with OrdinaryData. Following that
/// is a call-graph question, and a check that guessed at it would be green and meaningless.
/// </summary>
public static class SendArgumentJoin
{
    /// <summary>Where the transport's parameter names are declared.</summary>
    public const string TakionHeader = @"lib\include\chiaki\takion.h";

    /// <summary>
    /// The two chokepoints, which is where both faults were.
    ///
    /// Each C call is spelled as the file spells it: a reordering upstream stops matching and the
    /// check goes red on the reading rather than on a comparison it can no longer make.
    /// </summary>
    public static IReadOnlyList<PortedSend> Chokepoints { get; } =
    [
        new(
            "senkusha's protobuf chokepoint",
            @"lib\src\senkusha.c",
            "chiaki_takion_send_message_data(&senkusha->takion, 1, data_type, buf, buf_size, seq_num_out)",
            "chiaki_takion_send_message_data",
            @"app\Protocol\ManagedSenkushaRunHost.cs",
            "takion.SendData(message.Channel, message.Body, SenkushaMessages.ChunkFlags)",
            nameof(ManagedTakion.SendData),
            nameof(ManagedSenkushaRunHost)),
        new(
            "the stream connection's chokepoint",
            @"lib\src\streamconnection.c",
            "chiaki_takion_send_message_data(&stream_connection->takion, 1, data_type, buf, buf_size, NULL)",
            "chiaki_takion_send_message_data",
            @"app\Protocol\TakionMessageSink.cs",
            "takion.SendData(message.DataType, message.Body, StreamChunkFlags)",
            nameof(ManagedTakion.SendData),
            nameof(TakionMessageSink)),
    ];

    /// <summary>
    /// Which managed parameter carries which C one.
    ///
    /// Declared rather than positional, because the port reorders on purpose: the C takes the flags
    /// before the channel and the managed method takes the channel first, so that the argument a
    /// caller varies comes first and the one it almost never changes has a default. That reordering
    /// is the whole reason this file exists - it is also what makes an inversion invisible.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Correspondence { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chunk_flags"] = "chunkFlags",
            ["channel"] = "channel",
            ["buf"] = "payload",
        };

    /// <summary>One of the files, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// The arguments of the first call to <paramref name="callee"/> in <paramref name="text"/>.
    ///
    /// Split at depth-zero commas, so a nested call or a cast keeps its own arguments together.
    /// Empty where the call is not there, which a caller reports as a reading that failed rather
    /// than as a comparison that passed.
    /// </summary>
    public static IReadOnlyList<string> ArgumentsOf(string text, string callee)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(callee);

        int at = text.IndexOf(callee + "(", StringComparison.Ordinal);
        if (at < 0)
            return [];

        int open = at + callee.Length;
        var depth = 0;
        var argument = new System.Text.StringBuilder();
        var found = new List<string>();

        for (int one = open; one < text.Length; one++)
        {
            char c = text[one];

            if (c is '(' or '[')
            {
                depth++;
                if (depth == 1)
                    continue;
            }
            else if (c is ')' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    found.Add(argument.ToString().Trim());
                    return found;
                }
            }
            else if (c == ',' && depth == 1)
            {
                found.Add(argument.ToString().Trim());
                argument.Clear();
                continue;
            }

            argument.Append(c);
        }

        return [];
    }

    /// <summary>
    /// The C function's parameter names, in order, off its prototype in the header.
    ///
    /// Read rather than written down: the names are the join's keys, so a header that renamed one
    /// should break the correspondence loudly instead of matching a key nothing carries any more.
    /// </summary>
    public static IReadOnlyList<string> CParametersOf(string header, string function)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrEmpty(function);

        // The prototype, not a call: it is the one occurrence preceded by the export macro.
        int at = header.IndexOf("CHIAKI_EXPORT", StringComparison.Ordinal);

        while (at >= 0)
        {
            int end = header.IndexOf(';', at);
            if (end < 0)
                break;

            string line = header[at..end];

            if (line.Contains(function + "(", StringComparison.Ordinal))
            {
                return
                [
                    .. ArgumentsOf(line, function)
                        .Select(one => one.Split([' ', '*'], StringSplitOptions.RemoveEmptyEntries)[^1]),
                ];
            }

            at = header.IndexOf("CHIAKI_EXPORT", end, StringComparison.Ordinal);
        }

        return [];
    }

    /// <summary>The managed method's parameter names, in order.</summary>
    public static IReadOnlyList<string> ManagedParametersOf(string method)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        MethodInfo? found = typeof(ManagedTakion).GetMethod(method);

        return found is null ? [] : [.. found.GetParameters().Select(p => p.Name ?? string.Empty)];
    }

    /// <summary>
    /// What an argument resolves to as a number, or null where it varies.
    ///
    /// THE NULL IS THE ANSWER THIS CHECK IS ABOUT. A literal and a named constant are the same
    /// thing to a console; an expression reaching into a message is not, and the whole of both
    /// faults was one appearing where the other belonged.
    /// </summary>
    /// <param name="expression">The argument, as the source spells it.</param>
    /// <param name="owner">The type an unqualified name is looked up on.</param>
    public static int? ConstantOf(string expression, string owner)
    {
        ArgumentNullException.ThrowIfNull(expression);

        string text = expression.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return hex;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain))
            return plain;

        // A member access names its own type; a bare name is the calling file's.
        int dot = text.LastIndexOf('.');
        string typeName = dot < 0 ? owner : text[..dot];
        string member = dot < 0 ? text : text[(dot + 1)..];

        // Anything carrying an index, a call or an operator is varying by construction.
        if (member.Length == 0 || !member.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return null;

        FieldInfo? field = typeof(SendArgumentJoin).Assembly.GetTypes()
            .Where(t => t.Name.Equals(typeName, StringComparison.Ordinal))
            .Select(t => t.GetField(member, BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(f => f is { IsLiteral: true });

        return field?.GetRawConstantValue() is { } value
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// What one port's two calls say about a parameter: the C's kind against the managed's.
    /// </summary>
    /// <param name="send">The port.</param>
    /// <param name="cSource">The vendored file's text.</param>
    /// <param name="header">takion.h's text.</param>
    /// <param name="managedSource">The port's file text.</param>
    public static IReadOnlyList<string> DisagreementsIn(
        PortedSend send, string cSource, string header, string managedSource)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(cSource);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(managedSource);

        var wrong = new List<string>();

        IReadOnlyList<string> cNames = CParametersOf(header, send.CFunction);
        IReadOnlyList<string> cArgs = ArgumentsOf(send.CCallText, send.CFunction);
        IReadOnlyList<string> managedNames = ManagedParametersOf(send.ManagedMethod);

        string managedCallee = send.ManagedCallText[..send.ManagedCallText.IndexOf('(', StringComparison.Ordinal)];
        IReadOnlyList<string> managedArgs = ArgumentsOf(send.ManagedCallText, managedCallee);

        // The reading has to have worked before a comparison means anything, and each of these is a
        // rename upstream rather than an inversion here - so they are reported by their own name.
        if (cNames.Count == 0 || cNames.Count != cArgs.Count)
            wrong.Add($"{send.Name}: the C prototype and its call do not line up");

        if (managedNames.Count == 0 || managedNames.Count != managedArgs.Count)
            wrong.Add($"{send.Name}: the managed signature and its call do not line up");

        if (!CCall.Happens(cSource, send.CCallText))
            wrong.Add($"{send.Name}: the C no longer contains the call this ports");

        if (!managedSource.Contains(send.ManagedCallText, StringComparison.Ordinal))
            wrong.Add($"{send.Name}: the port no longer contains the managed call");

        if (wrong.Count > 0)
            return wrong;

        foreach ((string cName, string managedName) in Correspondence)
        {
            int cAt = cNames.ToList().IndexOf(cName);
            int managedAt = managedNames.ToList().IndexOf(managedName);

            if (cAt < 0 || managedAt < 0)
            {
                wrong.Add($"{send.Name}: {cName}/{managedName} is not a parameter of both");
                continue;
            }

            int? inC = ConstantOf(cArgs[cAt], send.Owner);
            int? inManaged = ConstantOf(managedArgs[managedAt], send.Owner);

            if (inC.HasValue != inManaged.HasValue)
            {
                wrong.Add(
                    $"{send.Name}: the C passes {(inC.HasValue ? "a constant" : "a varying value")} "
                        + $"for {cName} and the port passes "
                        + $"{(inManaged.HasValue ? "a constant" : "a varying value")} "
                        + $"for {managedName} - {cArgs[cAt]} against {managedArgs[managedAt]}");
                continue;
            }

            if (inC.HasValue && inC != inManaged)
            {
                wrong.Add(
                    $"{send.Name}: the C's {cName} is {inC} where {managedName} carries {inManaged}");
            }
        }

        return wrong;
    }
}
