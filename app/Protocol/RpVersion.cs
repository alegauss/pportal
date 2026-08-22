using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP293: the RP version a session negotiates with, both directions.
///
/// The first thing a session does is agree a protocol version with the console. The client sends a
/// version string, the console answers with one, and everything after that - the crypto, the takion
/// message shapes, whether a feature exists at all - follows from which <see cref="ChiakiTarget"/>
/// that string names. Get it wrong and nothing works, which is at least a symptom a person can see.
///
/// Where being wrong is quiet is the OTHER direction. Parsing an unrecognised string does not fail;
/// it answers Ps4Unknown or Ps5Unknown, and a session carries on with a target that is not the
/// console's. So the table is small, exhaustively testable, and worth having exactly once.
///
/// The names are a trap and RegistrationFlow already says so: CHIAKI_TARGET_PS4_8 is 800 and it is
/// the target for firmware BELOW 7.0, not for firmware 8. The strings here are protocol versions
/// and have nothing to do with the console's firmware number - "8.0" is the RP version PS4s below
/// 7.0 speak. Reproduced as the C spells it and not tidied into something that reads better.
/// </summary>
public static class RpVersion
{
    /// <summary>The version string a target speaks, or null where it has none.</summary>
    /// <remarks>
    /// Ps4Unknown and Ps5Unknown both answer null: an unknown target has no version to send, which
    /// is different from having an empty one.
    /// </remarks>
    public static string? StringFor(ChiakiTarget target) => target switch
    {
        ChiakiTarget.Ps4_8 => "8.0",
        ChiakiTarget.Ps4_9 => "9.0",
        ChiakiTarget.Ps4_10 => "10.0",
        ChiakiTarget.Ps5_1 => "1.0",
        _ => null,
    };

    /// <summary>
    /// The target a version string names, for a console already known to be a PS4 or a PS5.
    /// </summary>
    /// <param name="version">What the console answered with.</param>
    /// <param name="isPs5">
    /// Which family it is. NOT derivable from the string: "1.0" is a PS5 and would be an unknown
    /// PS4, and the two families number their versions independently.
    /// </param>
    /// <returns>
    /// The target, or the family's Unknown. Never null and never an exception - an unrecognised
    /// version is an ordinary answer from a console newer than this client.
    /// </returns>
    public static ChiakiTarget Parse(string? version, bool isPs5)
    {
        if (isPs5)
            return version == "1.0" ? ChiakiTarget.Ps5_1 : ChiakiTarget.Ps5Unknown;

        return version switch
        {
            "8.0" => ChiakiTarget.Ps4_8,
            "9.0" => ChiakiTarget.Ps4_9,
            "10.0" => ChiakiTarget.Ps4_10,
            _ => ChiakiTarget.Ps4Unknown,
        };
    }

    /// <summary>chiaki_target_is_ps5, which is a comparison rather than a set membership.</summary>
    /// <remarks>
    /// Greater-or-equal against Ps5Unknown, exactly as the C. That is what makes a PS5 target this
    /// client has never heard of still read as a PS5 - the numbering leaves room above 1000000 on
    /// purpose, and a port that listed the known PS5 values instead would call a future console a
    /// PS4.
    /// </remarks>
    public static bool IsPs5(ChiakiTarget target) => target >= ChiakiTarget.Ps5Unknown;

    /// <summary>chiaki_target_is_unknown: either family's Unknown, and nothing else.</summary>
    public static bool IsUnknown(ChiakiTarget target)
        => target is ChiakiTarget.Ps4Unknown or ChiakiTarget.Ps5Unknown;
}

/// <summary>
/// PP293: where session.c writes the version table, so the port can be held against it.
///
/// It is NOT <see cref="SessionSource"/>, which reads the Qt client's streamsession.cpp. Two files
/// with the same word in their name and nothing else in common.
/// </summary>
public static class SessionCoreSource
{
    /// <summary>The file, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the four version strings are still the four this port knows.</summary>
    public static bool TheVersionStringsAreStill(string core, params string[] versions)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(versions);

        foreach (string version in versions)
        {
            if (!core.Contains($"return \"{version}\";", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// And whether a PS5 is still decided by comparison rather than by a list of known values.
    /// </summary>
    public static bool Ps5IsStillDecidedByComparison(string commonHeader)
    {
        ArgumentNullException.ThrowIfNull(commonHeader);

        return commonHeader.Contains(
            "return target >= CHIAKI_TARGET_PS5_UNKNOWN;", StringComparison.Ordinal);
    }
}
