using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP442, under PP294: the C arrays CtrlExchangeParticipant.Payloads was copied out of.
///
/// That table says where it came from - "they live here, read off ctrl.c and named" - and every entry
/// carries the C declaration in a comment above it. Nothing read ctrl.c to check any of it was still
/// true. All seven were correct when this was written, and that was established by hand.
///
/// THE SIBLING IS WHY THIS IS A GAP AND NOT A CHOICE. SenkushaExchangeParticipant.Payloads has a test
/// holding its bytes against PP297's recording. The ctrl participant had neither that nor a source
/// check, and no *Source class existed in the file.
///
/// FOUR OF THE SEVEN HAVE NO RECORDING TO BE WRONG AGAINST. PP441 counted which types a console was
/// watched exchanging; EnableDualSenseFeatures, KeyboardEnable, KeyboardEnableToggle and the
/// sixteen-byte 0x11 are not among them. For those four this is the only oracle that can exist.
///
/// ZERO-FILLED TO THE DECLARED SIZE, which is PP383's finding made mechanical.
/// <c>const uint8_t connect[0x10]</c> carries FIFTEEN initialisers, so its sixteenth byte is an
/// implicit zero that the managed copy spells out. Comparing initialiser counts would report a
/// disagreement that is not one; comparing prefixes would miss a value that changed. The declared
/// size is read from the subscript and the array is compared whole.
/// </summary>
public static partial class CtrlPayloadSource
{
    /// <summary>The file these were copied from.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The C array each payload was copied from, by the name ctrl.c gives it.
    ///
    /// Two entries are deliberately absent. HeartbeatRep is <c>ctrl_message_send(..., NULL, 0)</c>
    /// and has no array; KeyboardEnableToggle is <c>uint8_t enable = 1</c>, a scalar - and there are
    /// TWO things called enable in this file, the scalar at 1081 and <c>const uint8_t enable[3]</c>
    /// at 1067, which is why the subscript is part of what is matched.
    /// </summary>
    public static IReadOnlyDictionary<ushort, string> ArrayFor { get; } =
        new Dictionary<ushort, string>
        {
            [(ushort)CtrlMessage.MicToggle] = "toggle",
            [(ushort)CtrlMessage.DisplayDevices] = "display",
            [(ushort)CtrlMessage.EnableDualSenseFeatures] = "enable",
            [(ushort)CtrlMessage.KeyboardEnable] = "signature",
            [0x11] = "connect",
        };

    /// <summary>
    /// The bytes a named <c>uint8_t</c> array is declared with, zero-filled to its declared size, or
    /// null where ctrl.c declares no such array.
    ///
    /// Comments stripped first: this file quotes its own declarations in prose, and PP400's rule is
    /// that a claim about what a file declares reads the code.
    ///
    /// The FIRST declaration wins where a name is reused. ctrl.c has two arrays called connect - a
    /// two-byte one at 901 and the sixteen-byte one at 1074 - so a caller that cares which gets it
    /// by size, not by hoping.
    /// </summary>
    public static byte[]? Declared(string ctrlSource, string arrayName, int declaredSize)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);
        ArgumentException.ThrowIfNullOrEmpty(arrayName);

        if (declaredSize <= 0)
            return null;

        string code = CCall.Code(ctrlSource);

        foreach (Match found in ArrayRegex().Matches(code))
        {
            if (!string.Equals(found.Groups["name"].Value, arrayName, StringComparison.Ordinal))
                continue;

            if (Size(found.Groups["size"].Value) != declaredSize)
                continue;

            byte[] bytes = new byte[declaredSize];
            int index = 0;

            foreach (Match number in ByteRegex().Matches(found.Groups["init"].Value))
            {
                // More initialisers than the subscript allows is not a thing C compiles, so it is a
                // reader fault rather than a source fault - reported as no answer.
                if (index >= declaredSize)
                    return null;

                bytes[index++] = Parse(number.Value);
            }

            // Fewer is legal and is the 0x11 case: the rest are implicit zeroes, already there.
            return bytes;
        }

        return null;
    }

    /// <summary>
    /// Every payload whose managed bytes and ctrl.c's array no longer agree, as sentences.
    ///
    /// A payload whose array cannot be found at all is reported too, and separately: an array that
    /// was renamed or removed upstream is a different thing from one whose contents moved.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        var apart = new List<string>();

        foreach ((ushort type, string name) in ArrayFor)
        {
            if (!CtrlExchangeParticipant.Payloads.TryGetValue(type, out byte[]? managed))
            {
                apart.Add($"0x{type:x} has a C array named here and no entry in Payloads");
                continue;
            }

            byte[]? declared = Declared(ctrlSource, name, managed.Length);

            if (declared is null)
            {
                apart.Add(
                    $"0x{type:x}: ctrl.c declares no uint8_t {name}[{managed.Length}] any more");
                continue;
            }

            if (!declared.AsSpan().SequenceEqual(managed))
            {
                apart.Add($"0x{type:x}: ctrl.c's {name} is {Render(declared)} and Payloads has "
                    + Render(managed));
            }
        }

        return apart;
    }

    /// <summary>Hex bytes, dash-separated, the way the corpus renders a payload.</summary>
    public static string Render(IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return string.Join("-", bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static int Size(string subscript)
        => subscript.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(subscript[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(subscript, CultureInfo.InvariantCulture);

    private static byte Parse(string number)
        => number.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.Parse(number[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.Parse(number, CultureInfo.InvariantCulture);

    // (const) uint8_t name[0x10] = { ... };
    [GeneratedRegex(
        @"(?:const\s+)?uint8_t\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\[\s*(?<size>0[xX][0-9a-fA-F]+|[0-9]+)\s*\]\s*=\s*\{(?<init>[^}]*)\}")]
    private static partial Regex ArrayRegex();

    [GeneratedRegex(@"0[xX][0-9a-fA-F]+|[0-9]+")]
    private static partial Regex ByteRegex();
}
