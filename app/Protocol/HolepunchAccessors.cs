namespace ChiakiNg.Protocol;

/// <summary>One function a caller outside the holepunch file can reach.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ChecksNull">Whether it guards anything before dereferencing.</param>
/// <param name="StatesALength">Whether its documentation says how big a caller's buffer must be.</param>
public readonly record struct Accessor(string Name, bool ChecksNull, bool StatesALength);

/// <summary>
/// PP264: the public surface of the holepunch file.
///
/// ONE OF THEM COPIES THE WHOLE OF A FIELD INTO A BUFFER WHOSE SIZE IT NEVER STATES. The address
/// getter copies the session's field, sized from the SOURCE, into whatever the caller passed. Its
/// documentation names the parameter and says nothing about how large it has to be - while the same
/// header, two functions along, says of another buffer that sixteen bytes are needed. So the file
/// does state such a requirement when it has one in mind, and the one it omits is the copy three
/// times the size. <see cref="BytesWritten"/> against <see cref="StatedLength"/> is where that sits.
///
/// TWO NULL DISCIPLINES, SIX FUNCTIONS. Five check nothing at all, one of them dereferencing its
/// argument to release what it points at. The sixth checks the handle and then each output pointer
/// separately before writing through it. They are within thirty lines of each other - see
/// <see cref="All"/>.
///
/// The socket getter hands back a pointer INTO the session, so what a caller receives is the
/// session's own handle rather than a copy of it.
///
/// Checked and not a defect: the registration info copies four fields and the struct has four, so
/// nothing is returned uninitialised. What it does carry forward is whatever PP252 left in the local
/// address - including the case where the lookup failed and nothing was written.
/// </summary>
public static class HolepunchAccessors
{
    /// <summary>How many bytes the address getter writes, whatever the caller provided.</summary>
    public const int BytesWritten = PunchAccept.AddressLength;

    /// <summary>What its documentation says the caller must provide. Nothing.</summary>
    public static int? StatedLength => null;

    /// <summary>And what the other buffer in the same header does state.</summary>
    public const int StatedElsewhere = PortMapping.ExternalAddressBuffer;

    /// <summary>The public functions, in the order the file declares them.</summary>
    public static IReadOnlyList<Accessor> All { get; } =
    [
        new("chiaki_holepunch_free_device_list", ChecksNull: false, StatesALength: false),
        new("chiaki_get_regist_info", ChecksNull: false, StatesALength: false),
        new("chiaki_get_ps_selected_addr", ChecksNull: false, StatesALength: false),
        new("chiaki_get_ps_ctrl_port", ChecksNull: false, StatesALength: false),
        new("chiaki_get_holepunch_sock", ChecksNull: false, StatesALength: false),
        new("chiaki_holepunch_session_get_stun_allocation", ChecksNull: true, StatesALength: false),
    ];

    /// <summary>How many of them guard anything. One.</summary>
    public static int GuardCount => All.Count(a => a.ChecksNull);

    /// <summary>
    /// How far past the end a caller sized for one family is written.
    /// </summary>
    public static int OverrunFor(int callerBuffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(callerBuffer);
        return Math.Max(0, BytesWritten - callerBuffer);
    }

    /// <summary>The four fields the registration info carries.</summary>
    public static IReadOnlyList<string> RegistFields { get; } =
        ["data1", "data2", "custom_data1", "regist_local_ip"];

    /// <summary>How many the struct declares - the same four, which is why nothing is left unset.</summary>
    public const int RegistFieldsDeclared = 4;

    /// <summary>Which port types the socket getter answers for.</summary>
    public static IReadOnlyList<PunchPort> AnsweredTypes { get; } = [PunchPort.Control, PunchPort.Data];

    /// <summary>
    /// Whether the getter hands back the session's own handle rather than a copy.
    /// </summary>
    public const bool ReturnsTheSessionsOwnHandle = true;
}

/// <summary>
/// PP264: the surface where the core writes it.
/// </summary>
public static class HolepunchAccessorsSource
{
    /// <summary>The implementation file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>The header that documents it. Named, so PP278's sweep can see it.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\remote\holepunch.h";

    /// <summary>And the header that documents it.</summary>
    public static string? LocateHeader()
        => ChiakiNg.Session.SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>Whether the address getter still copies the source's whole field.</summary>
    public static bool TheGetterStillCopiesTheWholeField(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            "memcpy(ps_ip, session->ps_ip, sizeof(session->ps_ip));", StringComparison.Ordinal);
    }

    /// <summary>
    /// THE CONTRAST. Whether the header still omits a length here and states one two functions
    /// along.
    /// </summary>
    public static bool TheHeaderStillOmitsTheLength(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        string text = header.Replace("\r\n", "\n", StringComparison.Ordinal);

        int documents = text.IndexOf(
            "@param ps_ip The char array to store the selected PlayStation IP", StringComparison.Ordinal);
        if (documents < 0)
            return false;

        int declares = text.IndexOf(
            "chiaki_get_ps_selected_addr(ChiakiHolepunchSession session, char *ps_ip);",
            documents, StringComparison.Ordinal);

        // Nothing between the description and the declaration says how big it has to be.
        return declares > documents
            && !text[documents..declares].Contains("bytes", StringComparison.Ordinal);
    }

    /// <summary>And whether the other buffer's requirement is still stated.</summary>
    public static bool TheOtherLengthIsStillStated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            $"needs to be at least {HolepunchAccessors.StatedElsewhere} bytes long",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// How many of the public functions still guard anything before dereferencing.
    /// </summary>
    public static int HowManyStillGuard(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string[] lines = core.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        int guarding = 0;
        foreach (Accessor accessor in HolepunchAccessors.All)
        {
            int at = Array.FindLastIndex(
                lines, l => l.StartsWith("CHIAKI_EXPORT", StringComparison.Ordinal)
                    && l.Contains($" {accessor.Name}(", StringComparison.Ordinal));

            if (at < 0)
                continue;

            // The first six lines of the body, which is where a guard would be.
            for (int line = at + 1; line < Math.Min(at + 7, lines.Length); line++)
            {
                if (!lines[line].Contains("if (!", StringComparison.Ordinal)
                    && !lines[line].Contains("if(!", StringComparison.Ordinal))
                {
                    continue;
                }

                guarding++;
                break;
            }
        }

        return guarding;
    }

    /// <summary>Whether the releaser still dereferences its argument unguarded.</summary>
    public static bool TheReleaserStillDereferencesUnguarded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            """
            chiaki_holepunch_free_device_list(ChiakiHolepunchDeviceInfo** devices)
            {
                free(*devices);
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>Whether the socket getter still returns the session's own handle.</summary>
    public static bool TheSocketGetterStillReturnsTheSessionsOwn(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("return &session->ctrl_sock;", StringComparison.Ordinal)
            && text.Contains("return &session->data_sock;", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the registration info still copies every field the struct declares.
    /// </summary>
    public static bool TheRegistInfoStillCopiesEveryField(string core, string header)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(header);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (string field in HolepunchAccessors.RegistFields)
        {
            if (!text.Contains($"memcpy(regist_info.{field},", StringComparison.Ordinal))
                return false;
        }

        // And the struct declares no more than those.
        string declaration = header.Replace("\r\n", "\n", StringComparison.Ordinal);

        int opens = declaration.IndexOf(
            "typedef struct holepunch_regist_info_t", StringComparison.Ordinal);
        int closes = declaration.IndexOf(
            "} ChiakiHolepunchRegistInfo;", opens < 0 ? 0 : opens, StringComparison.Ordinal);

        if (opens < 0 || closes < 0)
            return false;

        int declared = declaration[opens..closes].Split(';', StringSplitOptions.None).Length - 1;

        return declared == HolepunchAccessors.RegistFieldsDeclared;
    }
}
