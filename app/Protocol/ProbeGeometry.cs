using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One constant that has to be derived rather than written down again.</summary>
/// <param name="File">The file it lives in, relative to app/Protocol.</param>
/// <param name="Name">The constant's name.</param>
public readonly record struct DerivedConstant(string File, string Name);

/// <summary>
/// PP454: the probe packet's geometry, and the rule that it is written down once.
///
/// Four classes model the eighty-eight byte candidate probe and its answer.
/// <see cref="PunchResponse"/> (PP236) read the offsets out of holepunch.c, <see cref="PunchProbe"/>
/// (PP243) derived from it and said why - "the two halves now assert against the same constants
/// rather than against two independently-read numbers that happen to agree" - and then
/// <see cref="CandidateRace"/> (PP197) and <see cref="NatProbe"/> (PP33) each read the same C again.
/// Three independent copies of thirteen numbers, all agreeing, none of them derived.
///
/// AGREEING IS THE PROBLEM, NOT THE COMFORT. Three copies that agree are indistinguishable from one
/// copy, right up to the commit that changes an offset in two of them. Nothing in the suite compared
/// the three, because each was asserted against the C separately and each was correct - so the
/// duplication was invisible to exactly the checks that would have to catch it.
///
/// So PP454 made <see cref="PunchResponse"/> the authority and the other two derive, keeping every
/// public name because the prose and the callers use them. What is left is this class: the rule
/// stated as something readable, so a FOURTH copy fails a test rather than passing three.
///
/// THE GUARD READS THIS PORT'S OWN SOURCE, which is unusual here and is the point. Every other drift
/// check in the tree reads the C to see whether the model still matches it. This one reads the model,
/// because the defect is not disagreement with the C - all three copies matched it - but the model
/// having said the same thing three times.
/// </summary>
public static class ProbeGeometry
{
    /// <summary>Where the geometry is written down. The only place a literal belongs.</summary>
    public const string AuthorityFile = "PunchResponse.cs";

    /// <summary>
    /// The two message types, which are this packet's signature.
    ///
    /// Distinctive enough to guard on: a `public const` initialised to either of these, outside the
    /// authority, is a fourth copy of the geometry and not a coincidence. The offsets are not guarded
    /// this way - 0x24 and 0x50 are also ctrl message types, in other files, and a check that reported
    /// those would be noise.
    /// </summary>
    public static IReadOnlyList<string> MessageTypeLiterals { get; } = ["0x06000000", "0x07000000"];

    /// <summary>Every constant PP454 made derived, and where.</summary>
    public static IReadOnlyList<DerivedConstant> Derived { get; } =
    [
        new("CandidateRace.cs", nameof(CandidateRace.RequestType)),
        new("CandidateRace.cs", nameof(CandidateRace.ResponseType)),
        new("CandidateRace.cs", nameof(CandidateRace.RequestIdOffset)),
        new("CandidateRace.cs", nameof(CandidateRace.RequestIdLength)),
        new("CandidateRace.cs", nameof(CandidateRace.MessageLength)),
        new("NatProbe.cs", nameof(NatProbe.Length)),
        new("NatProbe.cs", nameof(NatProbe.LocalHashedIdOffset)),
        new("NatProbe.cs", nameof(NatProbe.ConsoleHashedIdOffset)),
        new("NatProbe.cs", nameof(NatProbe.HashedIdLength)),
        new("NatProbe.cs", nameof(NatProbe.HashedIdSlot)),
        new("NatProbe.cs", nameof(NatProbe.LocalSidOffset)),
        new("NatProbe.cs", nameof(NatProbe.ConsoleSidOffset)),
        new("NatProbe.cs", nameof(NatProbe.RequestIdOffset)),
        new("NatProbe.cs", nameof(NatProbe.RequestIdLength)),
        new("NatProbe.cs", nameof(NatProbe.MaskedAddressOffset)),
        new("NatProbe.cs", nameof(NatProbe.MaskedPortOffset)),
        new("NatProbe.cs", nameof(NatProbe.MaskedAddressLength)),
        new("PunchProbe.cs", nameof(PunchProbe.Length)),
        new("PunchProbe.cs", nameof(PunchProbe.RequestType)),
        new("PunchProbe.cs", nameof(PunchProbe.RequestIdAt)),
        new("PunchProbe.cs", nameof(PunchProbe.RequestIdLength)),
    ];

    /// <summary>The directory the models live in, or null outside a checkout.</summary>
    public static string? LocateDirectory()
    {
        string? authority = SanitizerSource.LocateRelative(Path.Combine("app", "Protocol", AuthorityFile));
        return authority is null ? null : Path.GetDirectoryName(authority);
    }

    /// <summary>
    /// Whether one constant's declaration derives from the authority rather than naming a number.
    ///
    /// Reads the initialiser only: the text between `= ` and the `;`, so a doc comment mentioning
    /// PunchResponse above a literal does not pass.
    /// </summary>
    public static bool IsDerived(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        string? initialiser = InitialiserOf(source, name);

        return initialiser is not null
            && initialiser.Contains($"{nameof(PunchResponse)}.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every file under app/Protocol that declares a `public const` initialised to one of the two
    /// message types, other than the authority.
    ///
    /// Empty is the answer. A name in it is a fourth copy of the geometry.
    /// </summary>
    public static IReadOnlyList<string> FilesWithTheirOwnCopy()
    {
        if (LocateDirectory() is not { } directory)
            return [];

        var offenders = new List<string>();

        foreach (string path in Directory.EnumerateFiles(directory, "*.cs"))
        {
            string file = Path.GetFileName(path);
            if (string.Equals(file, AuthorityFile, StringComparison.Ordinal))
                continue;

            if (DeclaresAMessageTypeLiteral(File.ReadAllText(path)))
                offenders.Add(file);
        }

        offenders.Sort(StringComparer.Ordinal);
        return offenders;
    }

    /// <summary>Whether this text declares a constant holding one of the two message types.</summary>
    public static bool DeclaresAMessageTypeLiteral(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (string line in source.Split('\n'))
        {
            if (!line.Contains("public const", StringComparison.Ordinal))
                continue;

            foreach (string literal in MessageTypeLiterals)
            {
                if (line.Contains($"= {literal};", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static string? InitialiserOf(string source, string name)
    {
        foreach (string line in source.Split('\n'))
        {
            if (!line.Contains("public const", StringComparison.Ordinal))
                continue;

            // The name has to be the one being declared, not one mentioned in the initialiser: a
            // `= PunchResponse.EchoAt;` line would otherwise answer for EchoAt as well.
            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
                continue;

            string declaration = line[..equals];
            if (!declaration.Contains($" {name} ", StringComparison.Ordinal))
                continue;

            int end = line.IndexOf(';', equals);
            return end < 0 ? line[(equals + 1)..] : line[(equals + 1)..end];
        }

        return null;
    }
}
