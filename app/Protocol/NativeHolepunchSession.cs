using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What a recorded exchange supplies, which is what the five value-returning asks read.</summary>
/// <param name="PsIp">The address the console was reached at.</param>
/// <param name="ClientLocalIp">The local address the registration info carries.</param>
/// <param name="CtrlPort">The control port.</param>
/// <param name="Data1">Sixteen bytes.</param>
/// <param name="Data2">Sixteen bytes.</param>
/// <param name="CustomData1">Sixteen bytes.</param>
public readonly record struct RecordedHolepunch(
    string PsIp, string ClientLocalIp, ushort CtrlPort,
    byte[] Data1, byte[] Data2, byte[] CustomData1);

/// <summary>The registration info, copied out rather than pointed at. PP478.</summary>
/// <param name="Data1">Sixteen bytes.</param>
/// <param name="Data2">Sixteen bytes.</param>
/// <param name="CustomData1">Sixteen bytes.</param>
/// <param name="LocalIp">The local address.</param>
public readonly record struct HolepunchRegistInfo(
    byte[] Data1, byte[] Data2, byte[] CustomData1, string LocalIp);

/// <summary>
/// PP481: <see cref="IHolepunchSession"/> over the real C.
///
/// PP429 wrote down the nine call sites, PP479 gave them an interface and PP480 joined the two.
/// This is the implementation that was missing, and what was stopping it was not the code: every
/// one of the nine takes a session handle, and a handle came only from PSN credentials, a network
/// and a console answering - so the wrappers could be written and never run, and an assertion over
/// them would have tested that a P/Invoke declaration exists.
///
/// SIX OF THE SEVEN RUN AGAINST THE REAL C. The five value-returning ones read fields a recorded
/// exchange carries, so a session from the real init with those fields stamped on answers them.
/// <see cref="CreateOffer"/> was expected to be the sixth that could not, and measuring said
/// otherwise: over a recorded session it returned success, building its offer from state the
/// session already holds. That is a return code and not a verdict on the offer.
///
/// <see cref="PunchHole"/> is the one that needs a console. It is wrapped and reachable so a live
/// run needs no further task, and nothing in the suite calls it: a test that did would be testing
/// the network, in a suite that bounds its runs precisely because nothing here has a timeout.
///
/// THE SESSION IS A REAL ONE. chiaki_holepunch_session_init allocates, sets the defaults and
/// creates the pipes and mutexes, and touches nothing remote - so <see cref="Fini"/> here is the
/// C's own teardown over the C's own object, rather than a fabricated struct that resembles one.
/// </summary>
public sealed class NativeHolepunchSession : IHolepunchSession, IDisposable
{
    /// <summary>
    /// INET6_ADDRSTRLEN, which is what both address getters write - asked, never assumed.
    ///
    /// It is 46 in every reference that quotes it and 65 on Windows, which reserves room for a
    /// scope id. A constant of 46 here is not a truncation: the C memcpys its full width out
    /// unconditionally, so the wrappers refuse with CHIAKI_ERR_BUF_TOO_SMALL rather than let it
    /// write past the end - which is how the difference was found, and is the guard working.
    /// </summary>
    public static int AddressSize { get; } = AddressSizeNative();

    /// <summary>The three recorded byte fields, each sixteen long.</summary>
    public const int DataSize = 16;

    private IntPtr session;

    private NativeHolepunchSession(IntPtr session) => this.session = session;

    /// <summary>How many times <see cref="Fini"/> ran, which is what the two teardown paths share.</summary>
    public int FinisCalled { get; private set; }

    /// <summary>Whether the handle is still open.</summary>
    public bool IsOpen => session != IntPtr.Zero;

    /// <summary>
    /// A session built the way a live one is built, then stamped with a recording.
    ///
    /// The token is what the C hashes into an OAuth header and is never sent by anything this
    /// reaches, so a recording's placeholder is honest here: nothing below this call goes near a
    /// network until <see cref="CreateOffer"/> or <see cref="PunchHole"/> is asked for.
    /// </summary>
    public static NativeHolepunchSession FromRecording(RecordedHolepunch recorded, string token = "replay")
    {
        ArgumentNullException.ThrowIfNull(recorded.Data1);
        ArgumentNullException.ThrowIfNull(recorded.Data2);
        ArgumentNullException.ThrowIfNull(recorded.CustomData1);

        IntPtr handle = SessionInit(token);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_holepunch_session_init returned null.");

        var built = new NativeHolepunchSession(handle);
        SetRecorded(
            handle, recorded.PsIp ?? string.Empty, recorded.ClientLocalIp ?? string.Empty,
            recorded.CtrlPort, Sized(recorded.Data1), Sized(recorded.Data2), Sized(recorded.CustomData1));

        return built;

        static byte[] Sized(byte[] value)
        {
            if (value.Length == DataSize)
                return value;

            var padded = new byte[DataSize];
            value.AsSpan(0, Math.Min(value.Length, DataSize)).CopyTo(padded);
            return padded;
        }
    }

    /// <summary>
    /// The socket for a channel, as the address of the session's own field. Never null - PP461 -
    /// and never dereferenced here: the flow passes it on, it does not read it.
    /// </summary>
    public object GetSocket(HolepunchPortType type)
    {
        ThrowIfClosed();
        IntPtr sock = GetSock(session, (int)type);
        return sock == IntPtr.Zero
            ? throw new InvalidOperationException($"chiaki_get_holepunch_sock({type}) returned null.")
            : sock;
    }

    /// <summary>The registration info, as a value. See <see cref="HolepunchRegistInfo"/>.</summary>
    public object GetRegistInfo()
    {
        ThrowIfClosed();

        var data1 = new byte[DataSize];
        var data2 = new byte[DataSize];
        var custom = new byte[DataSize];
        var localIp = new byte[AddressSize];

        var err = (ChiakiError)GetRegistInfoNative(session, data1, data2, custom, localIp, localIp.Length);
        if (err != ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_get_regist_info failed: {err}.");

        return new HolepunchRegistInfo(data1, data2, custom, Cstring(localIp));
    }

    /// <summary>The address the console was reached at.</summary>
    public string GetSelectedAddress()
    {
        ThrowIfClosed();

        var buf = new byte[AddressSize];
        var err = (ChiakiError)GetSelectedAddr(session, buf, buf.Length);
        return err != ChiakiError.Success
            ? throw new InvalidOperationException($"chiaki_get_ps_selected_addr failed: {err}.")
            : Cstring(buf);
    }

    /// <summary>The port the control channel connects to.</summary>
    public ushort GetCtrlPort()
    {
        ThrowIfClosed();
        return (ushort)GetCtrlPortNative(session);
    }

    /// <summary>
    /// An offer for the data connection. THIS ONE TALKS TO PSN, and a recorded session has nothing
    /// standing in for that - it is reachable so that a live run needs no further task, and a test
    /// calling it would be testing the network.
    /// </summary>
    public ChiakiError CreateOffer()
    {
        ThrowIfClosed();
        return (ChiakiError)CreateOfferNative(session);
    }

    /// <summary>A hole punched for a channel. Talks to the console; see <see cref="CreateOffer"/>.</summary>
    public ChiakiError PunchHole(HolepunchPortType type)
    {
        ThrowIfClosed();
        return (ChiakiError)PunchHoleNative(session, (int)type);
    }

    /// <summary>
    /// The session released, and the handle cleared so a second call is not a second free.
    ///
    /// Reached from two teardown paths, which is why <see cref="FinisCalled"/> counts rather than
    /// flagging: the flow is entitled to release once, and a test wants to see which.
    /// </summary>
    public void Fini()
    {
        if (session == IntPtr.Zero)
            return;

        IntPtr releasing = session;
        session = IntPtr.Zero;
        FinisCalled++;
        SessionFini(releasing);
    }

    /// <summary>Releases it if the flow did not, so a dropped session is not a leaked one.</summary>
    public void Dispose() => Fini();

    private void ThrowIfClosed()
    {
        if (session == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(NativeHolepunchSession));
    }

    /// <summary>The C writes a NUL-terminated address into a fixed buffer; this is the string in it.</summary>
    private static string Cstring(byte[] buf)
    {
        int end = Array.IndexOf(buf, (byte)0);
        return Encoding.ASCII.GetString(buf, 0, end < 0 ? buf.Length : end);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_address_size",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int AddressSizeNative();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_session_init",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr SessionInit(string token);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_session_set_recorded",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void SetRecorded(
        IntPtr session, string psIp, string clientLocalIp, ushort ctrlPort,
        byte[] data1, byte[] data2, byte[] customData1);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_get_sock",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetSock(IntPtr session, int portType);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_get_regist_info",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetRegistInfoNative(
        IntPtr session, byte[] data1, byte[] data2, byte[] customData1, byte[] localIp, int localIpSize);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_get_selected_addr",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetSelectedAddr(IntPtr session, byte[] buf, int size);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_get_ctrl_port",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetCtrlPortNative(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_create_offer",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int CreateOfferNative(IntPtr session);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_punch_hole",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int PunchHoleNative(IntPtr session, int portType);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_holepunch_session_fini",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void SessionFini(IntPtr session);
}
