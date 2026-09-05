using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP730: proto2's `required`, checked where nanopb checks it.
///
/// PP25's pair is one .proto through two generators, and they do not agree about this word. A
/// TakionMessage of type BANG carrying an empty bang payload is four bytes - 08 01 1A 00 -
/// and Google.Protobuf writes it, parses it, and reports every required field absent.
/// DecodeWithNanopb refuses the same bytes, which is what the console's own decoder does.
///
/// SO THE MANAGED PARSER IS THE LENIENT ONE, and every reader built on it accepts messages the C
/// would have thrown out. PP729 was where that mattered first: it read those four bytes as a bang
/// and answered state_failed, where the C logs a decode failure and leaves both flags alone. The
/// two endings meet today only because PP365 proved state_failed is watched by nobody.
///
/// READ FROM THE DESCRIPTOR, NOT FROM A LIST. Which fields are required is in the .proto, so a
/// field made required upstream is covered here without anybody remembering to add it - which is
/// PP279's finding about hand-kept lists, and the reason this is nine lines of reflection rather
/// than five names.
///
/// A SUB-MESSAGE THAT IS ABSENT IS NOT CHECKED, which is nanopb's rule too: an optional payload
/// nobody sent has no required fields to be missing. Only what actually arrived is judged.
/// </summary>
public static class RequiredFields
{
    /// <summary>
    /// Every required field this message, or a message under it, arrived without.
    /// </summary>
    /// <returns>Their full names, in field-number order, deepest last. Empty is nanopb's yes.</returns>
    public static IReadOnlyList<string> MissingIn(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var missing = new List<string>();
        Walk(message, missing);

        return missing;
    }

    /// <summary>Whether nanopb would accept it, which is the question with one answer.</summary>
    public static bool AllPresentIn(IMessage message) => MissingIn(message).Count == 0;

    private static void Walk(IMessage message, List<string> missing)
    {
        foreach (FieldDescriptor field in message.Descriptor.Fields.InFieldNumberOrder())
        {
            // A repeated field is never required and has no presence to ask about.
            if (field.IsRepeated)
                continue;

            bool present = field.Accessor.HasValue(message);

            if (field.IsRequired && !present)
            {
                missing.Add(field.FullName);
                continue;
            }

            if (present && field.Accessor.GetValue(message) is IMessage nested)
                Walk(nested, missing);
        }
    }
}
