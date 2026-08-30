using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>One managed enum and the C enum it mirrors.</summary>
/// <param name="Managed">The managed type, whose declared order is the claim.</param>
/// <param name="Prefix">What the C's members are spelled with.</param>
/// <param name="HeaderRelativePath">The header declaring them.</param>
public readonly record struct NativeEnumMirror(Type Managed, string Prefix, string HeaderRelativePath);

/// <summary>
/// PP578: every managed enum cast from a C value, against the C enum it mirrors.
///
/// PP577 held ChiakiError this way and stopped there. Two more are cast from native values on the
/// next two lines of the same file - <c>(ChiakiEventType)type</c> and
/// <c>(ChiakiQuitReason)quitReason</c> at ChiakiSession.cs:443-444 - and neither was held.
///
/// ALL THREE C ENUMS ARE IMPLICITLY VALUED, and two of them do not even say `= 0` on the first
/// member. Every value is its position, so a member inserted upstream shifts everything after it and
/// each cast silently starts meaning the neighbour: a quit reason reads as the next reason, an event
/// as the next event. Nothing throws, because the value is still a member.
///
/// ONE LIST, NOT THREE. PP577's check was error-specific and generalising it by copying would have
/// been the defect PP551 and PP576 each cost a task - a second list agreeing until one is edited. So
/// the mirrors are a list and the comparison is written once; ChiakiError is a row in it.
///
/// LETTERS, NOT CASING. CHIAKI_ERR_HTTP_NONOK is HttpNonOk, which no mechanical split derives. The
/// comparison strips underscores and case, which is the strongest join the two spellings support.
/// </summary>
public static class NativeEnumMirrors
{
    /// <summary>The three, and the only three: every managed enum a C value is cast into.</summary>
    public static IReadOnlyList<NativeEnumMirror> All { get; } =
    [
        new(typeof(ChiakiError), "CHIAKI_ERR_", @"lib\include\chiaki\common.h"),
        new(typeof(ChiakiEventType), "CHIAKI_EVENT_", @"lib\include\chiaki\session.h"),
        new(typeof(ChiakiQuitReason), "CHIAKI_QUIT_REASON_", @"lib\include\chiaki\session.h"),
    ];

    /// <summary>A mirror's header, or null outside a checkout.</summary>
    public static string? Locate(NativeEnumMirror mirror)
        => SanitizerSource.LocateRelative(mirror.HeaderRelativePath);

    /// <summary>
    /// The C's members, in the order the header declares them - which is the order that IS their
    /// value.
    /// </summary>
    public static IReadOnlyList<string> MembersIn(string header, string prefix)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var found = new List<string>();

        foreach (string line in header.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            int end = text.IndexOfAny([' ', ',', '=', '\t', '\r']);
            found.Add(end < 0 ? text : text[..end]);
        }

        return found;
    }

    /// <summary>Underscores and casing removed, which is all the two spellings share.</summary>
    public static string Normalise(string name, string prefix)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(prefix);

        string bare = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
        return bare.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    /// <summary>
    /// Where one mirror's two sides disagree, as sentences naming the position.
    ///
    /// Position is the whole point: a name present on both sides at a different index is the defect
    /// this exists for, and a set comparison would call that agreement.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(NativeEnumMirror mirror, string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        IReadOnlyList<string> c = MembersIn(header, mirror.Prefix);
        IReadOnlyList<string> managed = [.. Enum.GetNames(mirror.Managed)];
        var said = new List<string>();

        if (c.Count != managed.Count)
            said.Add($"{mirror.Managed.Name}: the C declares {c.Count} and this enum has {managed.Count}");

        for (int at = 0; at < Math.Min(c.Count, managed.Count); at++)
        {
            if (Normalise(c[at], mirror.Prefix) != Normalise(managed[at], mirror.Prefix))
                said.Add($"{mirror.Managed.Name}: at {at} the C says {c[at]} and this enum says {managed[at]}");
        }

        return said;
    }
}
