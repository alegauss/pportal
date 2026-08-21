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
    /// One candidate, or null when the JSON is not one.
    ///
    /// A missing or non-string type invalidates the whole candidate rather than defaulting - the
    /// fallback above is for a type that is PRESENT and unrecognised, and the two are not the same
    /// thing.
    /// </summary>
    public static Candidate? Read(JsonElement? json)
    {
        JsonElement? typeField = JsonC.Get(json, "type");
        if (typeField is not { ValueKind: JsonValueKind.String })
            return null;

        JsonElement? address = JsonC.Get(json, "addr");
        if (address is not { ValueKind: JsonValueKind.String })
            return null;

        return new Candidate(
            TypeOf(typeField.Value.GetString() ?? ""),
            address.Value.GetString() ?? "",
            JsonC.String(JsonC.Get(json, "addrMapped")) ?? "",
            (ushort)JsonC.Int(JsonC.Get(json, "port")),
            (ushort)JsonC.Int(JsonC.Get(json, "mappedPort")));
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
}
