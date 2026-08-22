using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP29: discovery's pure half in managed code, where PP6 only wrapped it.
///
/// ChiakiNg.Session.Discovery reaches every one of these through the shim and says so - "nothing in
/// this file writes a byte of the protocol itself". That was the right call when a reply parser on
/// this side of the seam was the one piece of discovery a console would have to be present to
/// disprove. It is not the right call now: the shim IS the oracle, so the parser can be written
/// here and held against the C on the same bytes, which is what every test beside this file does.
///
/// The classification ladder tests the PS5 rungs first and both need the PS5 protocol version
/// ----------------------------------------------------------------------------------------------
/// A console announcing the PS5 protocol with a system version below 8050000 does not fall to a PS5
/// default - it drops through to the PS4 rungs and comes out CHIAKI_TARGET_PS4_10. That is the
/// ladder's own answer rather than a fallback, and a port that special-cased "is a PS5" ahead of
/// the version would disagree on exactly the consoles running early firmware.
///
/// Four ways of reading a number, in four places
/// ---------------------------------------------
/// host-request-port is strtoul base 0 - octal when zero-padded, so "0987" is 519. system-version
/// is atoi, which is base ten always and saturates rather than wrapping. PP29's RP-KeyType is
/// strtoul base 0 and PP293's RP-Application-Reason is base 16 unconditionally. One shared numeric
/// helper across the four would be wrong about three of them.
///
/// What is NOT here
/// ----------------
/// The socket, the threads and the broadcast. Those are chiaki_discovery_init and the two thread
/// functions, and they are the half PP27 owns the shape of.
/// </summary>
public static class DiscoveryProtocol
{
    /// <summary>Where a console listens for a search.</summary>
    public const int Ps4Port = 987;

    /// <summary>And the PS5's, which is not adjacent to it.</summary>
    public const int Ps5Port = 9302;

    /// <summary>The local port range a reply is received on, inclusive at both ends.</summary>
    public const int LocalPortMin = 9303;

    /// <summary>...</summary>
    public const int LocalPortMax = 9319;

    /// <summary>The version string a PS4 announces, and the whole of what identifies it.</summary>
    public const string Ps4ProtocolVersion = "00020020";

    /// <summary>The PS5's - compared exactly, so a console is a PS5 or it is not.</summary>
    public const string Ps5ProtocolVersion = "00030010";

    /// <summary>The protocol version a family announces.</summary>
    public static string ProtocolVersion(bool ps5) => ps5 ? Ps5ProtocolVersion : Ps4ProtocolVersion;

    /// <summary>The port a family listens on.</summary>
    public static int Port(bool ps5) => ps5 ? Ps5Port : Ps4Port;

    /// <summary>
    /// Whether a reply came from a PS5, which is one string comparison and nothing else.
    ///
    /// Not the host-type, which is what it looks like it should be, and not the system version.
    /// </summary>
    public static bool IsPs5(string? deviceDiscoveryProtocolVersion)
        => deviceDiscoveryProtocolVersion is not null
            && string.Equals(deviceDiscoveryProtocolVersion, Ps5ProtocolVersion, StringComparison.Ordinal);

    /// <summary>
    /// The exact text of a search or a wake datagram.
    ///
    /// Line endings are bare newlines rather than CRLF, despite the HTTP/1.1 on the first line, and
    /// every packet ends with one. chiaki_discovery_send then transmits len + 1 bytes, so the
    /// terminating NUL travels with it - see <see cref="OnTheWire"/>.
    /// </summary>
    public static string PacketText(DiscoveryCommand command, bool ps5, ulong userCredential = 0)
    {
        string version = ProtocolVersion(ps5);

        return command switch
        {
            DiscoveryCommand.Search =>
                $"SRCH * HTTP/1.1\ndevice-discovery-protocol-version:{version}\n",

            DiscoveryCommand.Wakeup =>
                "WAKEUP * HTTP/1.1\n"
                + "client-type:vr\n"
                + "auth-type:R\n"
                + "model:w\n"
                + "app-type:r\n"
                + $"user-credential:{userCredential}\n"
                + $"device-discovery-protocol-version:{version}\n",

            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "unknown discovery command."),
        };
    }

    /// <summary>The packet's bytes, which is the text and no terminator.</summary>
    public static byte[] Packet(DiscoveryCommand command, bool ps5, ulong userCredential = 0)
        => Encoding.UTF8.GetBytes(PacketText(command, ps5, userCredential));

    /// <summary>
    /// What chiaki_discovery_send actually puts in the datagram: the packet AND its NUL.
    ///
    /// sendto is handed len + 1, so a console receives a trailing zero byte that the formatter
    /// never counted. A port sending len bytes would send a datagram one byte short of the one
    /// consoles have always been answering.
    /// </summary>
    public static byte[] OnTheWire(DiscoveryCommand command, bool ps5, ulong userCredential = 0)
        => [.. Packet(command, ps5, userCredential), 0];

    /// <summary>
    /// The target a reply resolves to, by the C's ladder in the C's order.
    /// </summary>
    public static ChiakiTarget TargetFor(string? systemVersion, string? deviceDiscoveryProtocolVersion)
    {
        int version = Atoi(systemVersion);
        bool isPs5 = IsPs5(deviceDiscoveryProtocolVersion);

        if (version >= 8050001 && isPs5)
            return ChiakiTarget.Ps5_1;

        if (version >= 8050000 && isPs5)
            return ChiakiTarget.Ps5Unknown;

        // ...and a PS5 that reached neither rung arrives HERE, not at a PS5 default.
        if (version >= 8000000)
            return ChiakiTarget.Ps4_10;

        if (version >= 7000000)
            return ChiakiTarget.Ps4_9;

        if (version > 0)
            return ChiakiTarget.Ps4_8;

        return ChiakiTarget.Ps4Unknown;
    }

    /// <summary>The word the console list shows beside a name.</summary>
    public static string StateString(DiscoveryHostState state) => state switch
    {
        DiscoveryHostState.Ready => "ready",
        DiscoveryHostState.Standby => "standby",
        _ => "unknown",
    };

    /// <summary>
    /// One reply datagram, read the way chiaki_discovery_srch_response_parse reads it.
    /// </summary>
    /// <returns>The console, or null where the datagram is not a parseable HTTP response.</returns>
    public static DiscoveredConsole? ParseReply(ReadOnlySpan<byte> reply, string fromAddress)
    {
        ArgumentNullException.ThrowIfNull(fromAddress);

        (int Code, IReadOnlyList<HttpHeader> Headers)? parsed =
            HttpResponse.Parse(Encoding.UTF8.GetString(reply));

        if (parsed is null)
            return null;

        // 620 is not an HTTP status code. It is what a console in standby answers with, and any
        // other code at all is "unknown" rather than a refusal - which is why a reply from
        // something that is not a console still produces a row.
        DiscoveryHostState state = parsed.Value.Code switch
        {
            200 => DiscoveryHostState.Ready,
            620 => DiscoveryHostState.Standby,
            _ => DiscoveryHostState.Unknown,
        };

        string? systemVersion = null, protocolVersion = null, name = null;
        string? hostType = null, id = null, titleId = null, appName = null;
        ushort requestPort = 0;

        foreach (HttpHeader header in parsed.Value.Headers)
        {
            // Ordinal, because the C is strcmp - PP296's note, a third time.
            switch (header.Key)
            {
                case "system-version": systemVersion = header.Value; break;
                case "device-discovery-protocol-version": protocolVersion = header.Value; break;
                case "host-name": name = header.Value; break;
                case "host-type": hostType = header.Value; break;
                case "host-id": id = header.Value; break;
                case "running-app-titleid": titleId = header.Value; break;
                case "running-app-name": appName = header.Value; break;

                // Base 0 and a truncating cast, both the C's - see the class note.
                case "host-request-port":
                    requestPort = unchecked((ushort)RegistResponse.ParseAutoBase(header.Value));
                    break;
            }
        }

        return new DiscoveredConsole(
            fromAddress, systemVersion, protocolVersion, name, hostType, id,
            titleId, appName, state, requestPort);
    }

    /// <summary>
    /// C's atoi: base ten always, leading space and sign allowed, stops at the first non-digit.
    ///
    /// It saturates rather than wrapping, which matters because a console reporting a system
    /// version longer than an int would otherwise classify as something arbitrary rather than as
    /// the newest thing the ladder knows.
    /// </summary>
    public static int Atoi(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        int i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i]))
            i++;

        bool negate = false;
        if (i < value.Length && (value[i] == '-' || value[i] == '+'))
            negate = value[i++] == '-';

        long result = 0;
        for (; i < value.Length && value[i] is >= '0' and <= '9'; i++)
        {
            result = (result * 10) + (value[i] - '0');
            if (result > (long)int.MaxValue + 1)
            {
                result = (long)int.MaxValue + 1;
                break;
            }
        }

        long signed = negate ? -result : result;
        return signed > int.MaxValue ? int.MaxValue : signed < int.MinValue ? int.MinValue : (int)signed;
    }

    /// <summary>PP29: whether the ladder still tests its PS5 rungs before the PS4 ones.</summary>
    public static bool ThePs5RungsAreStillFirst(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int ps5 = core.IndexOf("version >= 8050000 && is_ps5", StringComparison.Ordinal);
        int ps4 = core.IndexOf("version >= 8000000", StringComparison.Ordinal);

        return ps5 >= 0 && ps4 > ps5;
    }

    /// <summary>
    /// PP299: whether the classifier still guards system_version before handing it to atoi.
    ///
    /// This one asks the opposite question of the others here, because PP299 changed the C rather
    /// than reproducing it. A reply with no system-version header leaves the field null, and the
    /// ladder used to read it as an address - reachable by anything on the LAN answering on 987 or
    /// 9302, while the client was only looking for consoles. The guard going missing again is a
    /// remotely triggerable crash, not a divergence, so it is worth a check of its own.
    /// </summary>
    public static bool TheVersionIsStillGuardedBeforeAtoi(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "int version = host->system_version ? atoi(host->system_version) : 0;",
            StringComparison.Ordinal);
    }

    /// <summary>And whether the request port still lets its text choose a base.</summary>
    public static bool TheRequestPortStillAutoDetectsItsBase(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "response->host_request_port = (uint16_t)strtoul(header->value, NULL, 0);",
            StringComparison.Ordinal);
    }

    /// <summary>And whether the NUL still travels with the datagram.</summary>
    public static bool TheTerminatorIsStillSent(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("buf, (size_t)len + 1, 0, addr, addr_size", StringComparison.Ordinal);
    }
}
