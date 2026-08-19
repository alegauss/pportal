using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>The two datagrams discovery sends. The values are libchiaki's enum order.</summary>
public enum DiscoveryCommand { Search = 0, Wakeup = 1 }

/// <summary>ChiakiDiscoveryHostState, which is what the console list shows beside a name.</summary>
public enum DiscoveryHostState { Unknown = 0, Ready = 1, Standby = 2 }

/// <summary>ChiakiTarget, with the console's own numbers.</summary>
public enum ChiakiTarget
{
    Ps4Unknown = 0,
    Ps4_8 = 800,
    Ps4_9 = 900,
    Ps4_10 = 1000,
    Ps5Unknown = 1000000,
    Ps5_1 = 1000100,
}

/// <summary>
/// PP6: discovery's protocol half, which is the half a port gets wrong silently.
///
/// A console that does not answer looks exactly like a console that is switched off, so a byte out
/// of place in the search packet is a bug with no symptom except an empty list. The socket is the
/// easy half - .NET has UdpClient and libchiaki has its own service - and it is not what crosses
/// here. What crosses is the packet, the ports, and the two rules that decide what answered.
///
/// Nothing in this file writes a byte of the protocol itself. The packet is formatted by
/// libchiaki, the ports and versions are read out of its headers rather than copied into C#, and
/// the classification comes back through the same functions the Qt client asks.
/// </summary>
public static class Discovery
{
    /// <summary>987 for a PS4, 9302 for a PS5.</summary>
    public static int Port(bool ps5) => DiscoveryPort(ps5);

    /// <summary>
    /// The device-discovery-protocol-version a search carries. It is also the whole of what
    /// identifies a PS5 in a reply - not the host type, which is what it looks like it should be.
    /// </summary>
    public static string ProtocolVersion(bool ps5)
        => Marshal.PtrToStringUTF8(DiscoveryProtocolVersion(ps5)) ?? "";

    /// <summary>The local reply port range, inclusive.</summary>
    public static (int Min, int Max) LocalPortRange => (DiscoveryLocalPortMin(), DiscoveryLocalPortMax());

    /// <summary>
    /// The exact bytes of a search or a wake packet.
    ///
    /// <paramref name="userCredential"/> is only read for a wake, where it is the registration key
    /// reinterpreted as a hexadecimal number rather than as bytes.
    /// </summary>
    public static byte[] Packet(DiscoveryCommand command, bool ps5, ulong userCredential = 0)
    {
        string version = ProtocolVersion(ps5);

        // Asked twice: once for the length, once for the bytes. snprintf answers the length it
        // WANTED, so a first call with a small buffer is how the size is learned rather than
        // guessed - and a guess is what would silently truncate a wake packet's credential.
        var probe = new byte[1];
        int needed = DiscoveryPacketFmt((int)command, version, userCredential, probe, probe.Length);
        if (needed < 0)
            throw new ArgumentOutOfRangeException(nameof(command), command, "unknown discovery command.");

        var buf = new byte[needed + 1];
        int written = DiscoveryPacketFmt((int)command, version, userCredential, buf, buf.Length);
        if (written != needed)
            throw new InvalidOperationException($"discovery packet length changed between calls: {needed} then {written}.");

        return buf[..needed];
    }

    /// <summary>The packet as text, which is what it is - the protocol is line-based HTTP-ish.</summary>
    public static string PacketText(DiscoveryCommand command, bool ps5, ulong userCredential = 0)
        => Encoding.UTF8.GetString(Packet(command, ps5, userCredential));

    /// <summary>
    /// Whether a reply came from a PS5, decided by the protocol version it announced.
    /// </summary>
    public static bool IsPs5(string? deviceDiscoveryProtocolVersion)
        => DiscoveryIsPs5(deviceDiscoveryProtocolVersion);

    /// <summary>
    /// The target a reply resolves to, by libchiaki's own ladder. The PS5 rungs are tested first
    /// and both require the PS5 protocol version, so a reply announcing a PS4 protocol with a PS5
    /// system version lands on Ps4_10 - which is the ladder's own answer and not a fallback.
    /// </summary>
    public static ChiakiTarget Target(string? systemVersion, string? deviceDiscoveryProtocolVersion)
        => (ChiakiTarget)DiscoveryTarget(systemVersion, deviceDiscoveryProtocolVersion);

    /// <summary>The word shown beside a console: "ready", "standby" or "unknown".</summary>
    public static string? HostStateString(DiscoveryHostState state)
        => Marshal.PtrToStringUTF8(DiscoveryHostStateString((int)state));

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_port",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryPort([MarshalAs(UnmanagedType.I1)] bool ps5);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_protocol_version",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DiscoveryProtocolVersion([MarshalAs(UnmanagedType.I1)] bool ps5);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_local_port_min",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryLocalPortMin();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_local_port_max",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryLocalPortMax();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_packet_fmt",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryPacketFmt(
        int cmd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string protocolVersion,
        ulong userCredential,
        byte[] buf,
        int bufSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_is_ps5",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DiscoveryIsPs5(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceDiscoveryProtocolVersion);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_target",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryTarget(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? systemVersion,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceDiscoveryProtocolVersion);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_host_state_string",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DiscoveryHostStateString(int state);
}
