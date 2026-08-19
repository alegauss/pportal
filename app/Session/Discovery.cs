using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>The two datagrams discovery sends. The values are libchiaki's enum order.</summary>
public enum DiscoveryCommand { Search = 0, Wakeup = 1 }

/// <summary>ChiakiDiscoveryHostState, which is what the console list shows beside a name.</summary>
public enum DiscoveryHostState { Unknown = 0, Ready = 1, Standby = 2 }

/// <summary>
/// One console as its reply described it, with every string already copied out of the datagram.
/// A null field is one the reply did not carry, which is most of them on a console in standby.
/// </summary>
public readonly record struct DiscoveredConsole(
    string? Address,
    string? SystemVersion,
    string? ProtocolVersion,
    string? Name,
    string? HostType,
    string? Id,
    string? RunningAppTitleId,
    string? RunningAppName,
    DiscoveryHostState State,
    ushort RequestPort);

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

    /// <summary>
    /// Parses one reply datagram into a console the list can show.
    ///
    /// The strings are copied out here and the native handle is freed before this returns, which
    /// is not tidiness: chiaki_http_response_parse works IN PLACE, so every field of a parsed host
    /// points into the datagram it came from. Holding the host past its buffer would hand a screen
    /// eight pointers that still read correctly until something reused the page.
    /// </summary>
    public static DiscoveredConsole? ParseReply(ReadOnlySpan<byte> reply, string fromAddress, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(fromAddress);

        byte[] buf = reply.ToArray();
        IntPtr handle = DiscoveryReplyParse(buf, buf.Length, fromAddress, out int err);
        error = (ChiakiError)err;
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            string? Field(int field) => Marshal.PtrToStringUTF8(DiscoveryReplyField(handle, field));

            return new DiscoveredConsole(
                Address: Field(0),
                SystemVersion: Field(1),
                ProtocolVersion: Field(2),
                Name: Field(3),
                HostType: Field(4),
                Id: Field(5),
                RunningAppTitleId: Field(6),
                RunningAppName: Field(7),
                State: (DiscoveryHostState)DiscoveryReplyState(handle),
                RequestPort: (ushort)DiscoveryReplyRequestPort(handle));
        }
        finally
        {
            DiscoveryReplyFree(handle);
        }
    }

    /// <summary>The eight string fields, in the order libchiaki declares them.</summary>
    internal static string? HostField(IntPtr hosts, int index, int field)
        => Marshal.PtrToStringUTF8(DiscoveryServiceHostField(hosts, index, field));

    internal static DiscoveryHostState HostState(IntPtr hosts, int index)
        => (DiscoveryHostState)DiscoveryServiceHostState(hosts, index);

    internal static ushort HostRequestPort(IntPtr hosts, int index)
        => (ushort)DiscoveryServiceHostRequestPort(hosts, index);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_service_host_field",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DiscoveryServiceHostField(IntPtr hosts, int index, int field);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_service_host_state",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryServiceHostState(IntPtr hosts, int index);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_service_host_request_port",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryServiceHostRequestPort(IntPtr hosts, int index);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_reply_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DiscoveryReplyParse(
        byte[] reply, int replyLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fromAddress, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_reply_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void DiscoveryReplyFree(IntPtr host);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_reply_state",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryReplyState(IntPtr host);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_reply_request_port",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int DiscoveryReplyRequestPort(IntPtr host);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_reply_field",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DiscoveryReplyField(IntPtr host, int field);

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
