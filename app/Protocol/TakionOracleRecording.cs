using System.Text.Json;
using System.Text.Json.Serialization;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One element the queue held or let go of.</summary>
/// <param name="SeqNum">Its sequence number.</param>
/// <param name="Payload">The index the seam carries in place of an object.</param>
public readonly record struct QueueSlot(ulong SeqNum, long Payload);

/// <summary>What reorderqueue.c did with one script.</summary>
/// <param name="Name">The script, by <see cref="QueueScript.Name"/>.</param>
/// <param name="Size">chiaki_reorder_queue_size, which the window's exponent decides.</param>
/// <param name="Counts">
/// The count after EVERY operation, which is what catches a divergence that re-converges: two queues
/// can differ for twenty steps and agree again at the end, and a comparison of the end would call
/// that agreement.
/// </param>
/// <param name="Pulls">What each pull answered, in order. Null where the window's head had not arrived.</param>
/// <param name="Drops">The whole drop log, in order and with payloads.</param>
/// <param name="Window">
/// The final window, peeked offset by offset - including one PAST the end, which is the offset both
/// implementations have to refuse the same way.
/// </param>
public sealed record QueueAnswer(
    string Name,
    int Size,
    IReadOnlyList<ulong> Counts,
    IReadOnlyList<QueueSlot?> Pulls,
    IReadOnlyList<QueueSlot> Drops,
    IReadOnlyList<QueueSlot?> Window);

/// <summary>What takionsendbuffer.c did with one script.</summary>
/// <param name="Name">The script, by <see cref="BufferScript.Name"/>.</param>
/// <param name="Errors">
/// What the C returned for each step. A push's error is the interesting one - overflow is not a
/// fault, it is the buffer saying the console is behind - and an ack's is recorded too, so a
/// recording taken from a buffer that started refusing acks is a finding rather than a silence.
/// </param>
/// <param name="Counts">How many packets were still outstanding after each step.</param>
public sealed record SendBufferAnswer(
    string Name,
    IReadOnlyList<ChiakiError> Errors,
    IReadOnlyList<int> Counts);

/// <summary>What chiaki_takion_packet_mac did to one packet, with no cipher.</summary>
/// <param name="BaseType">The packet's type, which chooses the layout.</param>
/// <param name="Size">Its length.</param>
/// <param name="Error">What the C returned.</param>
/// <param name="MacBefore">The four bytes that were in the MAC field, or null where it refused.</param>
/// <param name="After">
/// The whole packet as the call left it, in hex.
///
/// The whole thing rather than the changed span, because "wrote the field" and "wrote nothing else"
/// are one claim: a gate that zeroed the right four bytes and one more would pass a comparison of
/// the field alone.
/// </param>
public readonly record struct MacAnswer(
    int BaseType,
    int Size,
    ChiakiError Error,
    string? MacBefore,
    string After);

/// <summary>What takion.c's v9 arm made of one datagram.</summary>
/// <param name="Lead">The type byte.</param>
/// <param name="Size">The datagram's length.</param>
/// <param name="Error">What the C returned.</param>
/// <param name="Packet">The header, or null where the parse refused.</param>
public readonly record struct AvV9Answer(byte Lead, int Size, ChiakiError Error, AvPacket? Packet);

/// <summary>What takion.c's v7 arm made of the same datagram, which is a different reading.</summary>
/// <param name="Lead">The type byte.</param>
/// <param name="Size">The datagram's length.</param>
/// <param name="Error">What the C returned.</param>
/// <param name="Header">The header, or null where the parse refused.</param>
public readonly record struct AvV7Answer(byte Lead, int Size, ChiakiError Error, V7AvHeader? Header);

/// <summary>Everything takion's oracles were asked, and everything they said.</summary>
/// <param name="Recorded">When the run happened, so a reader can tell how old the second opinion is.</param>
/// <param name="Queues">reorderqueue.c, script by script.</param>
/// <param name="SendBuffers">takionsendbuffer.c, script by script.</param>
/// <param name="Mac">takion.c's MAC gate, over every base type and length.</param>
/// <param name="V9">takion.c's v9 AV parse.</param>
/// <param name="V7">takion.c's v7 AV parse, over the same bytes.</param>
public sealed record TakionOracleRecording(
    string Recorded,
    IReadOnlyList<QueueAnswer> Queues,
    IReadOnlyList<SendBufferAnswer> SendBuffers,
    IReadOnlyList<MacAnswer> Mac,
    IReadOnlyList<AvV9Answer> V9,
    IReadOnlyList<AvV7Answer> V7)
{
    /// <summary>
    /// PP785: the second opinion, written down so takion.c can leave without taking it.
    ///
    /// PP780 measured what PP27's end state costs and the answer was not a list of call sites: the
    /// shim holds eighteen of the twenty-four symbols the deletion leaves undefined, twice what lib's
    /// own four files hold between them, and every one of those wrappers exists so a managed
    /// replacement can be held against the C. So the deletion removes what proves the port right at
    /// the same moment it removes the thing being replaced.
    ///
    /// PP33 ANSWERED THIS FOR json-c AND THE ANSWER TRANSFERS. Neither of the two obvious moves is
    /// good: keeping the C for a comparison nobody runs by default is what PP623's order already
    /// decided against, and deleting it and losing the comparison spends a reference implementation
    /// to save three files. Recording what it said costs neither - the C's answers do not change,
    /// because nothing in this tree develops takion.c and the inputs are ones this port chose.
    ///
    /// WHAT KEEPS IT FROM BECOMING A FOSSIL is the same guard json-c's has. A build that still HAS
    /// the oracle re-derives the recording and compares, so a recording that drifted fails on the one
    /// configuration that can tell - and until PP27's deletion lands, every build is that
    /// configuration.
    /// </summary>
    public const string RelativePath = @"tests\oracles\takion.json";

    /// <summary>The recording, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Indented and with the errors spelled out, because this file is read by people too.</summary>
    public static JsonSerializerOptions Format { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The recording as it sits in the tree, or null where it is absent or unreadable.</summary>
    public static TakionOracleRecording? Read()
    {
        if (Locate() is not { } path)
            return null;

        try
        {
            return JsonSerializer.Deserialize<TakionOracleRecording>(File.ReadAllText(path), Format);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>The queue script of that name, or null where the recording does not hold it.</summary>
    public QueueAnswer? Queue(string name)
        => Queues.FirstOrDefault(one => one.Name == name);

    /// <summary>The send buffer script of that name, or null where the recording does not hold it.</summary>
    public SendBufferAnswer? SendBuffer(string name)
        => SendBuffers.FirstOrDefault(one => one.Name == name);

    /// <summary>The MAC gate's answer for one packet, or null where the recording does not hold it.</summary>
    public MacAnswer? MacRow(int baseType, int size)
        => Mac.FirstOrDefault(one => one.BaseType == baseType && one.Size == size) is { After: not null } found
            ? found
            : null;

    /// <summary>The v9 parse's answer for one datagram, or null where the recording does not hold it.</summary>
    public AvV9Answer? V9Row(byte lead, int size)
        => V9.Any(one => one.Lead == lead && one.Size == size)
            ? V9.First(one => one.Lead == lead && one.Size == size)
            : null;

    /// <summary>The v7 parse's answer for the same datagram, or null where it is not held.</summary>
    public AvV7Answer? V7Row(byte lead, int size)
        => V7.Any(one => one.Lead == lead && one.Size == size)
            ? V7.First(one => one.Lead == lead && one.Size == size)
            : null;
}
