using System.Net.Sockets;
using System.Security.Cryptography;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP337, continuing PP293: what chiaki_session_init decides before anything reaches the network.
///
/// Four decisions are made from the connect info alone, and each is the kind that is invisible
/// until it is wrong: the device id the console is told, which address family the hostname is
/// resolved on, which target the session negotiates, and what "video disabled" actually streams.
///
/// THE DEVICE ID IS MOSTLY NOT RANDOM. It is 32 bytes: a fixed ten-byte prefix, sixteen bytes from
/// the crypto random source, and six zero bytes. A port that generated 32 random bytes would build
/// something the console has no reason to accept, and would do it without an error - the prefix is
/// not a checksum and nothing validates it locally.
///
/// IPv6 IS DECIDED BY A COLON IN THE HOSTNAME. The family is pinned rather than left unspecified -
/// "make hostname use ipv4 for now" is the comment - so a name that resolves to both is resolved as
/// v4 unless it was written with a colon in it, which is to say unless it was a literal address.
///
/// DISABLING VIDEO STREAMS 360p. It does not stop the video: the preset is replaced with the
/// smallest one, at fps zero. A port that read the flag as "send nothing" would produce a session
/// the console ends, because the stream is still negotiated either way.
/// </summary>
public static class SessionIdentity
{
    /// <summary>CHIAKI_RP_DID_SIZE, and the whole of what is sent.</summary>
    public const int DeviceIdSize = 32;

    /// <summary>The ten bytes every device id starts with.</summary>
    public static ReadOnlySpan<byte> DeviceIdPrefix =>
        [0x00, 0x18, 0x00, 0x00, 0x00, 0x07, 0x00, 0x40, 0x00, 0x80];

    /// <summary>The six it ends with, which are zero and are written rather than left.</summary>
    public static ReadOnlySpan<byte> DeviceIdSuffix => [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>How many bytes in the middle are actually random.</summary>
    public static int DeviceIdRandomLength =>
        DeviceIdSize - DeviceIdPrefix.Length - DeviceIdSuffix.Length;

    /// <summary>
    /// A device id, built the way session_init builds one.
    /// </summary>
    /// <param name="random">
    /// Where the middle comes from. session.c uses chiaki_random_bytes_crypt, so the default here is
    /// the crypto source and not Random - a device id from a predictable generator is one another
    /// client on the same network can produce.
    /// </param>
    public static byte[] NewDeviceId(Func<int, byte[]>? random = null)
    {
        var did = new byte[DeviceIdSize];

        DeviceIdPrefix.CopyTo(did);

        byte[] middle = random?.Invoke(DeviceIdRandomLength)
            ?? RandomNumberGenerator.GetBytes(DeviceIdRandomLength);

        if (middle.Length != DeviceIdRandomLength)
        {
            throw new ArgumentException(
                $"the middle is {DeviceIdRandomLength} bytes, not {middle.Length}.", nameof(random));
        }

        middle.CopyTo(did, DeviceIdPrefix.Length);
        DeviceIdSuffix.CopyTo(did.AsSpan(DeviceIdSize - DeviceIdSuffix.Length));

        return did;
    }

    /// <summary>
    /// The family a hostname is resolved on: v6 where it contains a colon, v4 otherwise.
    ///
    /// Pinned rather than unspecified, which is what makes this a decision rather than a default.
    /// </summary>
    public static AddressFamily FamilyFor(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.Contains(':', StringComparison.Ordinal)
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
    }

    /// <summary>The target a session starts on, which is decided by the family and nothing else.</summary>
    public static ChiakiTarget TargetFor(bool ps5)
        => ps5 ? ChiakiTarget.Ps5_1 : ChiakiTarget.Ps4_10;

    /// <summary>
    /// What a session with video disabled actually asks for: the smallest preset, at fps zero.
    /// </summary>
    public static (ChiakiVideoResolution Resolution, ChiakiVideoFps Fps) DisabledVideoPreset { get; }
        = (ChiakiVideoResolution.P360, 0);
}

/// <summary>
/// PP337: the four decisions held against session_init, none of which PP297's capture can judge -
/// a recording shows the request that went out, not the constant that was copied into it.
/// </summary>
public static class SessionIdentitySource
{
    /// <summary>Where the init lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Where the size is defined.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\session.h";

    /// <summary>That header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>Whether the device id is still 32 bytes.</summary>
    public static bool TheDeviceIdIsStill(string header, int size)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Contains($"#define CHIAKI_RP_DID_SIZE {size}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the prefix and suffix are still the bytes copied here.
    ///
    /// Written as session.c writes them, because that is the form a change would be made in - and
    /// a prefix that drifted would produce a device id refused by the console with no local error.
    /// </summary>
    public static bool TheFixedBytesAreStill(string core, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> suffix)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains($"did_prefix[] = {{ {Spelled(prefix)} }}", StringComparison.Ordinal)
            && core.Contains($"did_suffix[] = {{ {Spelled(suffix)} }}", StringComparison.Ordinal);
    }

    /// <summary>Whether the middle still comes from the crypto random source.</summary>
    public static bool TheMiddleIsStillCryptoRandom(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "chiaki_random_bytes_crypt(session->connect_info.did + sizeof(did_prefix)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a colon in the hostname still chooses IPv6.</summary>
    public static bool AColonStillChoosesIpv6(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int probe = core.IndexOf("char *ipv6 = strchr(connect_info->host, ':');", StringComparison.Ordinal);
        if (probe < 0)
            return false;

        int v6 = core.IndexOf("hints.ai_family = AF_INET6;", probe, StringComparison.Ordinal);
        int v4 = core.IndexOf("hints.ai_family = AF_INET;", probe, StringComparison.Ordinal);

        return v6 > probe && v4 > v6;
    }

    /// <summary>Whether disabling video still asks for 360p rather than for nothing.</summary>
    public static bool DisabledVideoIsStill360p(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("CHIAKI_VIDEO_RESOLUTION_PRESET_360p, 0", StringComparison.Ordinal);
    }

    private static string Spelled(ReadOnlySpan<byte> bytes)
    {
        var parts = new List<string>(bytes.Length);
        foreach (byte b in bytes)
            parts.Add($"0x{b:x2}");

        return string.Join(", ", parts);
    }
}
