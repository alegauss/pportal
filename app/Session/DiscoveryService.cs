using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP6: the discovery service - the socket, the search timer and the reply callback.
///
/// This was filed as needing a console on the network. It does not: it needs an address that
/// ANSWERS, and the service sends its search to whatever host it is pointed at rather than only to
/// a broadcast. Pointed at the loopback, a socket that replies is a console as far as it is
/// concerned - which is what makes the whole path exercisable with no hardware, and what turned
/// the remainder of PP6 from blocked into done.
///
/// The host list handed to <see cref="ConsolesChanged"/> is libchiaki's own array and is valid
/// only for the duration of the call. This copies each console out before returning, because a
/// list that outlived the callback would be eight pointers into a table the service is about to
/// rewrite - the same rule as a parsed reply, one level up.
/// </summary>
public sealed unsafe class DiscoveryService : IDisposable
{
    private readonly Action<IReadOnlyList<DiscoveredConsole>> onConsoles;
    private GCHandle _self;
    private IntPtr _handle;

    /// <param name="sendHost">Where the search goes. A broadcast address, or one console's.</param>
    /// <param name="pingMs">How often the search is repeated.</param>
    public DiscoveryService(
        string sendHost,
        Action<IReadOnlyList<DiscoveredConsole>> onConsoles,
        ulong pingMs = 1000,
        int hostsMax = 8,
        ChiakiLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(sendHost);
        ArgumentNullException.ThrowIfNull(onConsoles);
        this.onConsoles = onConsoles;

        _self = GCHandle.Alloc(this);
        _handle = ServiceCreate(log?.Handle ?? IntPtr.Zero, sendHost, pingMs, hostsMax,
            &Dispatch, GCHandle.ToIntPtr(_self));

        if (_handle == IntPtr.Zero)
        {
            _self.Free();
            throw new InvalidOperationException("chiaki_discovery_service_init failed.");
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            // Frees before the handle: fini joins the service thread, which may be inside the
            // callback, and what it would be calling into is about to stop existing.
            ServiceFree(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(IntPtr hosts, int count, IntPtr user)
    {
        try
        {
            if (user == IntPtr.Zero || GCHandle.FromIntPtr(user).Target is not DiscoveryService self)
                return;

            var consoles = new List<DiscoveredConsole>(count);
            for (int i = 0; i < count; i++)
            {
                consoles.Add(new DiscoveredConsole(
                    Address: Discovery.HostField(hosts, i, 0),
                    SystemVersion: Discovery.HostField(hosts, i, 1),
                    ProtocolVersion: Discovery.HostField(hosts, i, 2),
                    Name: Discovery.HostField(hosts, i, 3),
                    HostType: Discovery.HostField(hosts, i, 4),
                    Id: Discovery.HostField(hosts, i, 5),
                    RunningAppTitleId: Discovery.HostField(hosts, i, 6),
                    RunningAppName: Discovery.HostField(hosts, i, 7),
                    State: Discovery.HostState(hosts, i),
                    RequestPort: Discovery.HostRequestPort(hosts, i)));
            }

            self.onConsoles(consoles);
        }
        catch
        {
            // Nothing may escape into C, on a thread libchiaki owns.
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_service_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ServiceCreate(
        IntPtr log, [MarshalAs(UnmanagedType.LPUTF8Str)] string sendHost,
        ulong pingMs, int hostsMax,
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_discovery_service_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ServiceFree(IntPtr service);
}
