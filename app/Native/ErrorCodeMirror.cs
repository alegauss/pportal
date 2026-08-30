using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>
/// PP577: the managed error enum against the C one it mirrors, position by position.
///
/// <see cref="ChiakiError"/> is cast straight from integers the C returns - `(ChiakiError)err` at
/// half a dozen sites in ChiakiSession alone - so it is not a convenience spelling. It is a claim
/// that member N here is member N there.
///
/// AND BOTH ENUMS ARE IMPLICITLY VALUED. Only the first member of each says `= 0`; every other
/// value comes from its position. So a member INSERTED into the C's enum - not appended, inserted -
/// shifts everything after it, and every cast in this port silently starts meaning the neighbour.
/// Nothing would fail: the enum is still valid, the values still map, and a timeout starts reading
/// as an invalid response.
///
/// TWO MEMBERS WERE SPOT-CHECKED BEFORE THIS: the selftest asserts ErrorString(0) is "Success" and
/// that ParseAddr's sentence is the one libchiaki gives. Both hold whatever happens to the twenty
/// between them.
///
/// LETTERS, NOT CASING. CHIAKI_ERR_HTTP_NONOK is HttpNonOk here, which no mechanical rule derives -
/// Nonok is what splitting on underscores gives. So the comparison strips underscores and case and
/// compares what is left, which is the strongest join the two spellings actually support.
/// </summary>
public static class ErrorCodeMirror
{
    /// <summary>Where the C's enum lives.</summary>
    public const string RelativePath = @"lib\include\chiaki\common.h";

    /// <summary>common.h, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The prefix the C's members carry.</summary>
    public const string Prefix = "CHIAKI_ERR_";

    /// <summary>
    /// The C's members, in the order the header declares them - which is the order that IS their
    /// value.
    /// </summary>
    public static IReadOnlyList<string> MembersIn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var found = new List<string>();

        foreach (string line in header.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            int end = text.IndexOfAny([' ', ',', '=', '\t', '\r']);
            found.Add(end < 0 ? text : text[..end]);
        }

        return found;
    }

    /// <summary>The managed members, in their own declared order.</summary>
    public static IReadOnlyList<string> Managed { get; } = [.. Enum.GetNames<ChiakiError>()];

    /// <summary>Underscores and casing removed, which is all the two spellings share.</summary>
    public static string Normalise(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string bare = name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : name;
        return bare.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    /// <summary>
    /// Where the two disagree, as sentences naming the position.
    ///
    /// Position is the whole point: a name present in both but at a different index is the defect
    /// this exists for, and a set comparison would call that agreement.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        IReadOnlyList<string> c = MembersIn(header);
        var said = new List<string>();

        if (c.Count != Managed.Count)
            said.Add($"the C declares {c.Count} error codes and this enum has {Managed.Count}");

        for (int at = 0; at < Math.Min(c.Count, Managed.Count); at++)
        {
            if (Normalise(c[at]) != Normalise(Managed[at]))
                said.Add($"at {at} the C says {c[at]} and this enum says {Managed[at]}");
        }

        return said;
    }
}
