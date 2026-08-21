using System.Text.Json;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: how a candidate address was arrived at.
///
/// NOT a flags enum, unlike <see cref="PushNotificationType"/> and <see cref="SessionMessageAction"/>
/// beside it - these are 0, 1, 2, 3 and a candidate has exactly one. A port that made all three
/// masks for consistency would give this one values nothing compares against.
/// </summary>
public enum CandidateType
{
    /// <summary>Zero, and the FALLBACK - see <see cref="Candidate.TypeOf"/>.</summary>
    Static = 0,

    Local = 1,
    Stun = 2,
    Derived = 3,
}

/// <summary>One address the other end might be reachable at.</summary>
/// <param name="Type">How it was arrived at.</param>
/// <param name="Address">The address as this side knows it.</param>
/// <param name="MappedAddress">And as a NAT rewrote it, which is usually the one that works.</param>
/// <param name="Port">The port as this side knows it.</param>
/// <param name="MappedPort">And as a NAT rewrote it.</param>
public readonly record struct Candidate(
    CandidateType Type,
    string Address,
    string MappedAddress,
    ushort Port,
    ushort MappedPort);

/// <summary>
/// PP33: reading a candidate, where the parser and the writer do not agree.
///
/// The core WRITES four type names and READS three. LOCAL, STUN and DERIVED are recognised; every
/// other string - including the literal "STATIC" it writes itself - falls through to
/// <see cref="CandidateType.Static"/>.
///
/// So Static is the default rather than a word, and a port that built the obvious symmetric table
/// would be wrong twice: it would refuse a type PSN invents next, where the Qt client treats it as
/// static and carries on, and it would turn a round trip through its own writer into a parse
/// error for the one type the reader never names.
///
/// The type field's ABSENCE is treated differently again. A missing or non-string type makes the
/// whole candidate invalid, where the notification envelope beside it (PP190) reads a missing
/// dataType as Unknown and carries on. Two parsers, two files apart, two answers to the same
/// question - and both are reproduced, because the difference is what each caller was written
/// against.
///
/// EVERY FIELD IS MANDATORY, and that is the correction PP195 made to this file. All five - type,
/// addr, mappedAddr, port and mappedPort - jump to invalid_schema when missing or of the wrong
/// type, and there are no defaults anywhere in the reader. The lenient version this file shipped
/// with accepted candidates the Qt client refuses, which is the direction that does not announce
/// itself: an offer with half a candidate in it would connect here and fail there.
///
/// The mapped address is spelled <c>mappedAddr</c> - not addrMapped, which is what the C STRUCT
/// MEMBER is called (addr_mapped) and what this file first read. A wrong key is invisible to a
/// round trip through this port's own writer and to a test whose fixture was written beside it;
/// only reading the core's key back out finds it, which is why <see cref="MappedAddressField"/>
/// is now pinned by name.
/// </summary>
public static class CandidateReader
{
    /// <summary>The three the reader names. Static is deliberately not among them.</summary>
    public static IReadOnlyDictionary<string, CandidateType> Recognised { get; } =
        new Dictionary<string, CandidateType>(StringComparer.Ordinal)
        {
            ["LOCAL"] = CandidateType.Local,
            ["STUN"] = CandidateType.Stun,
            ["DERIVED"] = CandidateType.Derived,
        };

    /// <summary>The four the WRITER produces, which is one more than the reader names.</summary>
    public static IReadOnlyDictionary<CandidateType, string> Written { get; } =
        new Dictionary<CandidateType, string>
        {
            [CandidateType.Static] = "STATIC",
            [CandidateType.Local] = "LOCAL",
            [CandidateType.Stun] = "STUN",
            [CandidateType.Derived] = "DERIVED",
        };

    /// <summary>
    /// The type a word names, falling through to <see cref="CandidateType.Static"/> - which is what
    /// "STATIC" itself does, and what anything PSN invents next will do.
    /// </summary>
    public static CandidateType TypeOf(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return Recognised.TryGetValue(word, out CandidateType type) ? type : CandidateType.Static;
    }

    /// <summary>
    /// The mapped address's key. Spelled for the WIRE and not for the C struct member beside it,
    /// which is addr_mapped - see the class note.
    /// </summary>
    public const string MappedAddressField = "mappedAddr";

    /// <summary>
    /// One candidate, or null when the JSON is not one.
    ///
    /// EVERY field is required. A missing or wrongly-typed one invalidates the whole candidate
    /// rather than defaulting - the fallback above is for a type that is PRESENT and unrecognised,
    /// and the two are not the same thing.
    ///
    /// Both ports must be json_type_int, so a port sent as "9295" or as 9295.0 invalidates the
    /// candidate rather than being coerced - the same rule natType is held to (PP194).
    /// </summary>
    public static Candidate? Read(JsonElement? json)
    {
        JsonElement? typeField = JsonC.Get(json, "type");
        if (typeField is not { ValueKind: JsonValueKind.String })
            return null;

        JsonElement? address = JsonC.Get(json, "addr");
        if (address is not { ValueKind: JsonValueKind.String })
            return null;

        JsonElement? mapped = JsonC.Get(json, MappedAddressField);
        if (mapped is not { ValueKind: JsonValueKind.String })
            return null;

        JsonElement? port = JsonC.Get(json, "port");
        JsonElement? mappedPort = JsonC.Get(json, "mappedPort");
        if (JsonC.TypeOf(port) != JsonCType.Int || JsonC.TypeOf(mappedPort) != JsonCType.Int)
            return null;

        return new Candidate(
            TypeOf(typeField.Value.GetString() ?? ""),
            address.Value.GetString() ?? "",
            mapped.Value.GetString() ?? "",
            (ushort)JsonC.Int(port),
            (ushort)JsonC.Int(mappedPort));
    }
}

/// <summary>
/// PP33: the candidate's rules where the Qt core states them.
/// </summary>
public static class CandidateSource
{
    /// <summary>Where candidates are read and written.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the reader still names three and falls through to static.</summary>
    public static bool TheReaderStillNamesThree(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string word in CandidateReader.Recognised.Keys)
        {
            if (!core.Contains($"strcmp(type_str, \"{word}\") == 0", StringComparison.Ordinal))
                return false;
        }

        // And never compares against the fourth, which is what makes it the fallback.
        return !core.Contains("strcmp(type_str, \"STATIC\") == 0", StringComparison.Ordinal)
            && core.Contains("candidate.type = CANDIDATE_TYPE_STATIC;", StringComparison.Ordinal);
    }

    /// <summary>Whether the writer still produces all four.</summary>
    public static bool TheWriterStillProducesFour(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string word in CandidateReader.Written.Values)
        {
            if (!core.Contains($"strcpy(candidate_type, \"{word}\")", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether the types are still plain values rather than a mask.</summary>
    public static bool TheTypesAreStillNotFlags(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("CANDIDATE_TYPE_STATIC = 0", StringComparison.Ordinal)
            && core.Contains("CANDIDATE_TYPE_DERIVED = 3", StringComparison.Ordinal);
    }

    /// <summary>Whether a missing type still invalidates the candidate rather than defaulting.</summary>
    public static bool AMissingTypeIsStillInvalid(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("Coudln't parse type field from candidate json.", StringComparison.Ordinal)
            && core.Contains("goto invalid_schema;", StringComparison.Ordinal);
    }

    /// <summary>
    /// The five reads a candidate is made of, each with the type its guard demands - PP195.
    /// </summary>
    public static IReadOnlyList<(string Key, string Type)> Fields { get; } =
    [
        ("type", "json_type_string"),
        ("addr", "json_type_string"),
        (CandidateReader.MappedAddressField, "json_type_string"),
        ("port", "json_type_int"),
        ("mappedPort", "json_type_int"),
    ];

    /// <summary>
    /// Whether every field is still read under a guard of its declared type - which is also what
    /// pins the mapped address's KEY, the one a round trip through this port could not catch.
    /// </summary>
    public static bool EveryFieldIsStillGuarded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string key, string type) in Fields)
        {
            int at = core.IndexOf(
                $"json_object_object_get_ex(candidate_json, \"{key}\", &jobj);", StringComparison.Ordinal);
            if (at < 0)
                return false;

            // The guard is what stands between this read and the NEXT one - bounded that way rather
            // than by a character count, so a guard growing a line does not quietly stop being read
            // and a neighbour's guard is never mistaken for this one's.
            int next = core.IndexOf("json_object_object_get_ex(", at + 1, StringComparison.Ordinal);
            string guard = next < 0 ? core[at..] : core[at..next];
            if (!guard.Contains($"!json_object_is_type(jobj, {type})", StringComparison.Ordinal)
                || !guard.Contains("goto invalid_schema;", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether one unreadable candidate still invalidates the whole MESSAGE. The loop's exit is the
    /// same invalid_schema the fields jump to, so there is no per-candidate recovery to port.
    /// </summary>
    public static bool OneBadCandidateStillFailsTheMessage(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("*out = msg;", StringComparison.Ordinal)
            && core.Contains(
                "session_message_parse: Unexpected JSON schema for holepunch session message.",
                StringComparison.Ordinal);
    }
}
