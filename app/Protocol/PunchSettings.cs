namespace ChiakiNg.Protocol;

/// <summary>One thing about the punch a caller might want to change.</summary>
public enum PunchSetting
{
    /// <summary>How many ports to guess.</summary>
    GuessCount,

    /// <summary>How many sockets to open to catch them.</summary>
    SocketCount,

    /// <summary>Whether to force guessing when the measurement says not to.</summary>
    ForceGuessing,

    /// <summary>How far the NAT moves its ports. Readable only.</summary>
    AllocationIncrement,

    /// <summary>And whether it moves them at random. Readable only.</summary>
    RandomAllocation,
}

/// <summary>What a call to change a setting did.</summary>
public enum SettingOutcome
{
    /// <summary>It took.</summary>
    Applied,

    /// <summary>It did not, and nothing said so.</summary>
    RefusedSilently,
}

/// <summary>
/// PP265: the settings a caller can change, and the two it can only watch.
///
/// A REFUSED VALUE LOOKS EXACTLY LIKE AN ACCEPTED ONE. Both counted settings take an int and keep it
/// only if it is positive. A caller passing zero or a negative leaves the previous value standing,
/// and the function returns nothing, logs nothing, and changes nothing a later call could read back
/// - so the setting a caller believes it made is the default it never replaced. <see cref="Apply"/>
/// answers with both the outcome and the value, because the core answers with neither.
///
/// AND THE TWO THAT MATTER MOST HAVE NO SETTER. The allocation increment and the random-allocation
/// flag are exposed through a getter and nothing else. They are written by the STUN test, and
/// written again by PP253's diagnosis when it decides the measurement cannot be used. A caller can
/// therefore observe that the code overruled itself and has no way to say otherwise - see
/// <see cref="IsSettable"/> against <see cref="IsReadable"/>.
///
/// The third knob is a bool and takes anything, having nothing to refuse.
/// </summary>
public static class PunchSettings
{
    /// <summary>How many ports are guessed unless a caller says otherwise.</summary>
    public const int DefaultGuessCount = PortGuessing.RandomAllocationGuesses;

    /// <summary>And how many sockets are opened to catch them.</summary>
    public const int DefaultSocketCount = PortGuessing.RandomAllocationSocks;

    /// <summary>Whether guessing is forced unless a caller says otherwise.</summary>
    public const bool DefaultForceGuessing = false;

    /// <summary>The increment a fresh session carries - PP259's sentinel.</summary>
    public const int DefaultAllocationIncrement = StunLookup.NotMeasured;

    /// <summary>And the flag.</summary>
    public const bool DefaultRandomAllocation = false;

    /// <summary>Whether a caller can change this setting.</summary>
    public static bool IsSettable(PunchSetting setting)
        => setting is PunchSetting.GuessCount or PunchSetting.SocketCount or PunchSetting.ForceGuessing;

    /// <summary>Whether a caller can read it back.</summary>
    public static bool IsReadable(PunchSetting setting)
        => setting is PunchSetting.AllocationIncrement or PunchSetting.RandomAllocation;

    /// <summary>Every setting the code writes to itself and a caller cannot.</summary>
    public static IReadOnlyList<PunchSetting> WrittenOnlyByTheCode { get; } =
        [.. Enum.GetValues<PunchSetting>().Where(s => !IsSettable(s))];

    /// <summary>
    /// What a call to change a counted setting does.
    /// </summary>
    /// <param name="current">What the session holds now.</param>
    /// <param name="asked">What the caller passed.</param>
    /// <returns>The outcome, and what the session holds afterwards.</returns>
    public static (SettingOutcome Outcome, int Value) Apply(int current, int asked)
        => asked > 0
            ? (SettingOutcome.Applied, asked)
            : (SettingOutcome.RefusedSilently, current);

    /// <summary>Whether the caller is told which of the two happened. It is not.</summary>
    public const bool TheOutcomeIsReported = false;

    /// <summary>The default a counted setting starts at.</summary>
    public static int DefaultFor(PunchSetting setting) => setting switch
    {
        PunchSetting.GuessCount => DefaultGuessCount,
        PunchSetting.SocketCount => DefaultSocketCount,
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "not a counted setting"),
    };
}

/// <summary>
/// PP265: the settings where the core writes them.
/// </summary>
public static class PunchSettingsSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>And the header, which is where the absence of a setter is visible.</summary>
    public static string? LocateHeader() => HolepunchAccessorsSource.LocateHeader();

    /// <summary>
    /// THE FINDING. Whether both counted setters still keep a value only if positive, and say
    /// nothing either way.
    /// </summary>
    public static bool BothSettersStillRefuseSilently(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (string field in new[] { "port_guessing_count", "port_guessing_socks" })
        {
            if (!text.Contains(
                $"    if(count > 0)\n        session->{field} = count;\n}}", StringComparison.Ordinal))
            {
                return false;
            }
        }

        // Both return void, so there is nothing for a caller to read.
        return text.Contains(
                "CHIAKI_EXPORT void chiaki_holepunch_session_set_port_guessing_ports(", StringComparison.Ordinal)
            && text.Contains(
                "CHIAKI_EXPORT void chiaki_holepunch_session_set_port_guessing_socks(", StringComparison.Ordinal);
    }

    /// <summary>And whether the bool one still takes anything.</summary>
    public static bool TheFlagStillTakesAnything(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            """
            chiaki_holepunch_session_force_port_guessing(Session *session, bool enabled)
            {
                session->force_port_guessing = enabled;
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>Whether the defaults are still those, set where the session is created.</summary>
    public static bool TheDefaultsAreStillThose(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                "session->port_guessing_count = RANDOM_ALLOCATION_GUESSES_NUMBER;", StringComparison.Ordinal)
            && text.Contains(
                "session->port_guessing_socks = RANDOM_ALLOCATION_SOCKS_NUMBER;", StringComparison.Ordinal)
            && text.Contains(
                $"session->stun_allocation_increment = {PunchSettings.DefaultAllocationIncrement};",
                StringComparison.Ordinal)
            && text.Contains("session->stun_random_allocation = false;", StringComparison.Ordinal)
            && text.Contains("session->force_port_guessing = false;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two the code writes to itself still have a getter and no setter.
    /// </summary>
    public static bool TheReadOnlyPairIsStillReadOnly(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        string text = header.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                "chiaki_holepunch_session_get_stun_allocation(", StringComparison.Ordinal)
            && !text.Contains("set_stun_allocation", StringComparison.Ordinal)
            && !text.Contains("set_random_allocation", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many places read each setting, so the reach is counted rather than described.
    /// </summary>
    public static int ReadsOf(string core, string field)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(field);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split($"session->{field}", StringSplitOptions.None).Length - 1;
    }
}
