using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: json-c's accessors in managed code, answering the way json-c answers.
///
/// The second half of "two dependencies that simply leave". The HTTP parser was the easy one -
/// libchiaki's own code, one function, transcribable. json-c is not that: it is a third-party
/// library whose accessors are LENIENT in ways System.Text.Json is strict, and holepunch.c leans on
/// exactly those. 24 object_get_ex, 20 get_string, 8 get_int and 7 json_pointer_get, and the naive
/// translation of every one of them is wrong in a different way.
///
/// Every rule below was MEASURED against json-c through <see cref="NativeJson"/> rather than read
/// off its header, and the tests re-measure them. What was found:
///
///   get_string is not string-typed. On an int it returns "42", on a double the ORIGINAL TEXT, on a
///   bool "true"/"false", on an object json-c's own spaced serialisation. Only null and an absent
///   key give NULL. GetString() throws for every one of those, so the naive port turns a working
///   read of a numeric field into an exception - and holepunch reads fields it does not type-check.
///
///   get_int parses strings, and parses them the way JavaScript does: "42" is 42 and "42px" is
///   ALSO 42, while "abc" and "" are 0. That is the same leniency PP140 found in the settings
///   fields, arrived at from the other end of the tree.
///
///   get_int saturates instead of wrapping. 99999999999 reads as int.MaxValue, not as a truncated
///   low word - so a port using unchecked casts would produce a small wrong number where json-c
///   produces a large wrong one, and only one of those looks wrong.
///
///   get_boolean calls any NON-EMPTY string true. The string "false" is true. So is "0".
///
///   object_get_ex is case-sensitive, and where a document repeats a key it answers with the LAST
///   one. System.Text.Json's GetProperty answers with the first, which is the one difference here
///   that a test using well-formed input would never show.
///
/// The tokener is deliberately NOT reproduced - see <see cref="Parse"/>.
/// </summary>
public static class JsonC
{
    /// <summary>
    /// Parses the way json-c's callers see it: the first value in the text, or null.
    ///
    /// Two of json-c's behaviours are reproduced because a caller cannot work around them:
    ///
    ///   trailing data is IGNORED. `{"a":1} junk` parses to the object, and `{"a":1}{"b":2}` to the
    ///   first of the two. JsonDocument.Parse refuses both, so the value is read with a reader that
    ///   stops after one - which is what json_tokener_parse does.
    ///
    ///   a bare `null` document is REFUSED. json_tokener_parse returns NULL for it, which its
    ///   callers cannot tell apart from a parse error, so neither can this.
    ///
    /// What is NOT reproduced is json-c's lexer being lenient about the JSON itself: it accepts
    /// single-quoted strings, trailing commas, NaN, Infinity, and reads `0x1f` as 0. Those are
    /// accepted inputs here would refuse, and reproducing them would mean writing a JSON parser to
    /// be bug-compatible with one - for inputs Sony's endpoints do not send. The divergence is
    /// asserted rather than hidden, so a translation of holepunch.c can see the list.
    /// </summary>
    public static JsonDocument? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            // NOT lenient, and that is a decision rather than an omission - see
            // JsonCTests.JsonCsLexerIsLenientWhereThisIsNot. json-c's tokener accepts a trailing
            // comma and reads 0x1f as 0; matching it means writing a parser to be bug-compatible
            // with one, for inputs Sony's endpoints do not send.
            //
            // PP33's differential re-found the trailing comma and this comment is why it stays:
            // the line is drawn at the LEXER. Accessor semantics are matched exactly, because
            // those decide what a document the two clients both accepted MEANS.
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
            JsonDocument document = JsonDocument.ParseValue(ref reader);

            // json_tokener_parse cannot return a null node, so its callers never see one.
            if (document.RootElement.ValueKind == JsonValueKind.Null)
            {
                document.Dispose();
                return null;
            }

            return document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// json_object_object_get_ex. Case-sensitive, and the LAST of repeated keys.
    ///
    /// The last, not the first. json-c's tokener adds each pair to a hash table that replaces on
    /// collision; GetProperty stops at the first match instead, and a document with a repeated key
    /// is the only input that tells the two apart.
    /// </summary>
    public static JsonElement? Get(JsonElement? node, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (node is not { ValueKind: JsonValueKind.Object } obj)
            return null;

        JsonElement? found = null;
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.Ordinal))
                found = property.Value;
        }

        return AsJsonC(found);
    }

    /// <summary>
    /// PP33: a JSON null answered the way json-c answers it - as NOTHING.
    ///
    /// json-c represents a null VALUE as a C null pointer, the same pointer a missing key returns.
    /// So <c>json_object_object_get(o, "k")</c> cannot tell <c>{"k":null}</c> from <c>{}</c>, and
    /// every caller that checks "was the key there" is really checking "was there a value".
    ///
    /// That is not a nicety. A reply carrying <c>"error":null</c> means no error to the console's
    /// own client and means an error field to a port that kept the distinction - and the port would
    /// be reading a field it should have skipped. Found by running both over the same document,
    /// which is the only way this shows: each library is self-consistent.
    /// </summary>
    private static JsonElement? AsJsonC(JsonElement? node)
        => node is { ValueKind: JsonValueKind.Null } ? null : node;

    /// <summary>json_object_array_length, or -1 where the node is not an array.</summary>
    /// <summary>
    /// PP33: json_object_get_type, which is the accessor the other five are chosen with.
    ///
    /// json-c has no separate kind for true and false - both are <c>json_type_boolean</c> - and no
    /// kind for a number that happens to be whole: an integer and a double are
    /// <c>json_type_int</c> and <c>json_type_double</c>, decided by how the text was WRITTEN and
    /// not by the value. System.Text.Json splits the first and merges the second, so neither
    /// mapping is the identity and this is where they are reconciled.
    /// </summary>
    public static JsonCType TypeOf(JsonElement? node)
    {
        if (node is not JsonElement element)
            return JsonCType.Null;

        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonCType.Object,
            JsonValueKind.Array => JsonCType.Array,
            JsonValueKind.String => JsonCType.String,

            // Both, because json-c has one kind for the pair and the distinction is the value.
            JsonValueKind.True or JsonValueKind.False => JsonCType.Boolean,

            // A number's kind follows its TEXT: 1 is an int and 1.0 is a double, in json-c as in
            // the document. TryGetInt64 answers about the value, so the raw text decides instead.
            JsonValueKind.Number => NumberKind(element),

            _ => JsonCType.Null,
        };
    }

    private static JsonCType NumberKind(JsonElement element)
    {
        string raw = element.GetRawText();

        return raw.Contains('.', StringComparison.Ordinal)
            || raw.Contains('e', StringComparison.OrdinalIgnoreCase)
                ? JsonCType.Double
                : JsonCType.Int;
    }

    public static int ArrayLength(JsonElement? node)
        => node is { ValueKind: JsonValueKind.Array } array ? array.GetArrayLength() : -1;

    /// <summary>json_object_array_get_idx. Null past the end rather than an exception.</summary>
    public static JsonElement? ArrayAt(JsonElement? node, int index)
    {
        if (node is not { ValueKind: JsonValueKind.Array } array || index < 0)
            return null;

        // AsJsonC for the same reason Get has it: a null ELEMENT of an array is a null pointer to
        // json-c, indistinguishable from an index past the end.
        return index >= array.GetArrayLength() ? null : AsJsonC(array[index]);
    }

    /// <summary>
    /// json_object_get_string, which answers for every type except null.
    ///
    /// A double gives back the text it was parsed from rather than a reformatting of its value -
    /// json-c keeps the source token - which is why 9.99 reads as "9.99" and not "9.9900000000000002".
    /// </summary>
    public static string? String(JsonElement? node)
    {
        if (node is not { } value)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => Serialise(value),
        };
    }

    /// <summary>json_object_get_int, saturating and string-parsing.</summary>
    public static int Int(JsonElement? node)
    {
        long wide = Int64(node);
        return wide > int.MaxValue ? int.MaxValue
            : wide < int.MinValue ? int.MinValue
            : (int)wide;
    }

    /// <summary>json_object_get_int64. The same leniency, at the width json-c stores.</summary>
    public static long Int64(JsonElement? node)
    {
        if (node is not { } value)
            return 0;

        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return 1;

            case JsonValueKind.False:
            case JsonValueKind.Null:
                return 0;

            case JsonValueKind.Number:
                if (value.TryGetInt64(out long exact))
                    return exact;

                // A double truncates toward zero, and one outside the range saturates.
                double d = value.GetDouble();
                return d >= long.MaxValue ? long.MaxValue
                    : d <= long.MinValue ? long.MinValue
                    : (long)d;

            case JsonValueKind.String:
                return LeadingInt64(value.GetString());

            default:
                return 0;
        }
    }

    /// <summary>
    /// json_object_get_boolean, whose rule for strings is emptiness and not content.
    ///
    /// So "false" is true, and "0" is true. Naming that here rather than at the call sites, because
    /// a caller reading a flag out of a string field has no reason to suspect it.
    /// </summary>
    public static bool Bool(JsonElement? node)
    {
        if (node is not { } value)
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => (value.GetString() ?? "").Length > 0,
            JsonValueKind.Number => Int64(value) != 0 || NonZeroDouble(value),
            _ => false,
        };
    }

    /// <summary>
    /// json_pointer_get, RFC 6901, including the three parts of it a hand-rolled split gets wrong.
    ///
    /// An empty path is the whole document. `~1` is a literal `/` in a key and `~0` a literal `~`,
    /// unescaped in that order - the other order turns `~01` into `/` instead of `~1`. And a path
    /// of "/" addresses the EMPTY-STRING key rather than the root, which is the one every
    /// implementation gets wrong once.
    ///
    /// A path with no leading slash misses, an index past the end misses, and `-` misses: json-c
    /// only gives `-` a meaning when setting.
    /// </summary>
    public static JsonElement? Pointer(JsonElement? root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (root is not { } node)
            return null;
        if (path.Length == 0)
            return node;
        if (path[0] != '/')
            return null;

        JsonElement? current = node;
        foreach (string raw in path[1..].Split('/'))
        {
            if (current is not { } here)
                return null;

            // ~1 before ~0. Reversed, "~01" would unescape to "/" rather than to "~1".
            string token = raw.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (here.ValueKind == JsonValueKind.Array)
            {
                if (!IsArrayIndex(token)
                    || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    return null;
                }

                current = ArrayAt(here, index);
            }
            else
            {
                current = Get(here, token);
            }
        }

        return current;
    }

    /// <summary>
    /// RFC 6901's array index: "0", or a digit string with no leading zero.
    ///
    /// So "/a/b/00" misses where "/a/b/0" resolves, which json-c enforces and a plain int.TryParse
    /// does not - it reads "00" as 0 and returns the first element for a path the spec rejects.
    /// </summary>
    private static bool IsArrayIndex(string token)
    {
        if (token.Length == 0 || !token.All(char.IsAsciiDigit))
            return false;

        return token.Length == 1 || token[0] != '0';
    }

    /// <summary>
    /// json-c's own serialisation, which is what get_string returns for an object or an array.
    ///
    /// Spaced - `{ "a": 1 }`, not `{"a":1}` - because JSON_C_TO_STRING_SPACED is json-c's default,
    /// and with `/` escaped as `\/`, which is legal and which System.Text.Json does not do.
    /// </summary>
    public static string Serialise(JsonElement element)
    {
        var text = new StringBuilder();
        Write(element, text);
        return text.ToString();
    }

    private static void Write(JsonElement element, StringBuilder text)
    {
        switch (element.ValueKind)
        {
            // An empty one is "{ }" and not "{  }": json-c writes the opening brace, then a space
            // before each entry, then one space before the closer - so with no entries the two
            // spaces collapse to the single one it emits either way.
            case JsonValueKind.Object:
                text.Append('{');
                bool firstProperty = true;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    text.Append(firstProperty ? " " : ", ");
                    firstProperty = false;
                    WriteString(property.Name, text);
                    text.Append(": ");
                    Write(property.Value, text);
                }

                text.Append(" }");
                break;

            case JsonValueKind.Array:
                text.Append('[');
                bool firstItem = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    text.Append(firstItem ? " " : ", ");
                    firstItem = false;
                    Write(item, text);
                }

                text.Append(" ]");
                break;

            case JsonValueKind.String:
                WriteString(element.GetString() ?? "", text);
                break;

            case JsonValueKind.True:
                text.Append("true");
                break;

            case JsonValueKind.False:
                text.Append("false");
                break;

            case JsonValueKind.Null:
                text.Append("null");
                break;

            default:
                text.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteString(string value, StringBuilder text)
    {
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '/': text.Append("\\/"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    else
                        text.Append(c);
                    break;
            }
        }

        text.Append('"');
    }

    /// <summary>
    /// The leading-integer parse json-c uses for a string, saturating at the ends.
    ///
    /// The same shape as PP140's NumericSettingField.ParseInt, and deliberately not shared with it:
    /// that one is a settings field's rule and this one is a JSON library's, and the two agreeing
    /// today is not a reason for one to change when the other does.
    /// </summary>
    private static long LeadingInt64(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int at = 0;
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;

        bool negative = at < text.Length && text[at] == '-';
        if (at < text.Length && (text[at] == '-' || text[at] == '+'))
            at++;

        int digits = 0;
        ulong magnitude = 0;
        bool saturated = false;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            digits++;
            if (!saturated)
            {
                ulong next = (magnitude * 10) + (ulong)(text[at] - '0');
                if (next < magnitude || next > long.MaxValue)
                    saturated = true;
                else
                    magnitude = next;
            }

            at++;
        }

        if (digits == 0)
            return 0;
        if (saturated)
            return negative ? long.MinValue : long.MaxValue;

        return negative ? -(long)magnitude : (long)magnitude;
    }

    private static bool NonZeroDouble(JsonElement element)
        => element.TryGetDouble(out double d) && d != 0;
}
