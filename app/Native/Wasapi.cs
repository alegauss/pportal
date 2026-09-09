using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>
/// PP708: the WASAPI surface, once, for the two directions this port opens.
///
/// PP652 wrote all of it to open a microphone and PP698 reused it for a loopback reference, and
/// both are the same object graph: an enumerator, an endpoint, an audio client, and a service off
/// it. Playing sound is the third caller and it differs in one interface - IAudioRenderClient
/// instead of IAudioCaptureClient - so a second copy would be a second copy of everything else.
///
/// PP693's rule is why that matters more than tidiness. A COM interface is an offset table: a
/// method missing above one that is called sends the call to the wrong slot, and the failure is a
/// crash somewhere else. One copy has one chance to be wrong, and the port already has the checks
/// that hold it - every method here carries PreserveSig, and ComSignatures sweeps the tree for one
/// that does not.
/// </summary>
internal static class Wasapi
{
    /// <summary>CLSCTX_ALL, which is what an endpoint is activated with.</summary>
    public const int ClsCtxAll = 23;

    /// <summary>STGM_READ, for the property store a friendly name comes out of.</summary>
    public const int StgmRead = 0;

    /// <summary>WAVE_FORMAT_PCM.</summary>
    public const short WaveFormatPcm = 1;

    /// <summary>DEVICE_STATE_ACTIVE: plugged in and enabled.</summary>
    public const int DeviceStateActive = 1;

    /// <summary>AUDCLNT_BUFFERFLAGS_SILENT, which says the buffer pointer is not to be read.</summary>
    public const int BufferFlagsSilent = 2;

    public static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid CaptureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    /// <summary>IAudioRenderClient, which is the one interface the playing side adds.</summary>
    public static readonly Guid RenderClientIid = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    /// <summary>
    /// PP840: IAudioSessionControl, which GetService hands back and a QI turns into the 2.
    ///
    /// Asked for by the BASE iid because that is the one the client's service table knows; the RCW
    /// answers the derived interface by QueryInterface, which is what a cast does here.
    /// </summary>
    public static readonly Guid AudioSessionControlIid = new("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");

    /// <summary>
    /// PP840: IAudioSessionManager2, which is how the RENDER session is reached.
    ///
    /// The capture's own session refuses the ducking preference with AUDCLNT_E_WRONG_ENDPOINT_TYPE
    /// - the experience it governs is a render one, so the call belongs on a render session. This
    /// manager hands over the process's session on an endpoint without owning a stream on it.
    /// </summary>
    public static readonly Guid AudioSessionManagerIid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private static PropertyKey friendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    /// <summary>The device enumerator, or null where COM will not make one.</summary>
    public static IMMDeviceEnumerator? Enumerator(out int hresult)
    {
        hresult = 0;

        try
        {
            return (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid)!)!;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException)
        {
            hresult = error.HResult;
            return null;
        }
    }

    /// <summary>An endpoint's friendly name, or empty where the store will not answer.</summary>
    public static string NameOf(IMMDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.OpenPropertyStore(StgmRead, out IPropertyStore? store) != 0 || store is null)
            return string.Empty;

        try
        {
            if (store.GetValue(ref friendlyName, out PropVariant value) != 0)
                return string.Empty;

            string name = value.AsString() ?? string.Empty;
            PropVariantClear(ref value);
            return name;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>
    /// A WAVEFORMATEX for one announcement, allocated unmanaged for a call that takes a pointer.
    ///
    /// The caller frees it. Both directions ask the engine for a format rather than taking the mix,
    /// which is what AUTOCONVERTPCM makes possible and what PP652 measured as necessary: no device
    /// here takes the announced format in shared mode without a converter in front.
    /// </summary>
    public static IntPtr Format(MicrophoneAnnouncement announced)
    {
        var wanted = new WaveFormatEx
        {
            FormatTag = WaveFormatPcm,
            Channels = (short)announced.Channels,
            SamplesPerSec = announced.Rate,
            BitsPerSample = (short)announced.Bits,
            BlockAlign = (short)(announced.Channels * announced.Bits / 8),
            Size = 0,
        };
        wanted.AvgBytesPerSec = wanted.SamplesPerSec * wanted.BlockAlign;

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(wanted, buffer, false);
        return buffer;
    }

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);

    public enum EDataFlow
    {
        Render = 0,
        Capture = 1,
    }

    public enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    public enum AudioClientShareMode
    {
        Shared = 0,
        Exclusive = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public short Type;
        public short Reserved1;
        public short Reserved2;
        public short Reserved3;
        public IntPtr Value;
        public IntPtr Value2;

        /// <summary>VT_LPWSTR is 31, and a friendly name is one.</summary>
        public readonly string? AsString() => Type == 31 ? Marshal.PtrToStringUni(Value) : null;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            int context,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore? store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetAt(int index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        [PreserveSig]
        int Initialize(
            AudioClientShareMode mode, int flags, long duration, long period, IntPtr format, IntPtr session);

        [PreserveSig]
        int GetBufferSize(out int frames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out int frames);

        [PreserveSig]
        int IsFormatSupported(AudioClientShareMode mode, IntPtr format, out IntPtr closest);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr handle);

        [PreserveSig]
        int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object? service);
    }

    /// <summary>
    /// PP840: the session manager of an endpoint, for the one method that names a process's session.
    ///
    /// Two slots inherited from IAudioSessionManager and five of its own; only the first is called,
    /// and the rest are declared because a COM interface is its vtable order.
    /// </summary>
    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        /// <summary>
        /// The session for this process on this endpoint, created if it does not exist yet.
        /// </summary>
        /// <param name="sessionGuid">Null for the process's default session.</param>
        /// <param name="streamFlags">Zero.</param>
        /// <param name="control">An IAudioSessionControl, which a cast turns into the 2.</param>
        [PreserveSig]
        int GetAudioSessionControl(
            IntPtr sessionGuid, int streamFlags, [MarshalAs(UnmanagedType.IUnknown)] out object? control);

        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr volume);

        [PreserveSig] int GetSessionEnumerator(out IntPtr sessions);

        [PreserveSig] int RegisterSessionNotification(IntPtr notification);

        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);

        [PreserveSig] int RegisterDuckNotification(IntPtr sessionId, IntPtr notification);

        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    /// <summary>
    /// PP840: the session behind a capture, for the one method that turns its ducking off.
    ///
    /// EVERY SLOT IS DECLARED AND ALL BUT ONE ARE UNUSED, which is not clutter. A COM interface is
    /// its vtable order: skipping a method this port never calls would put SetDuckingPreference on
    /// whichever function came after it, and the failure would be a wrong call that returns a
    /// plausible HRESULT rather than a compiler error. The nine inherited from
    /// IAudioSessionControl come first, in its own order, then this interface's five.
    ///
    /// The unused ones take IntPtr where the real signature takes an interface or a string: the
    /// marshalling of a method nothing calls never runs, and a pointer keeps each declaration to
    /// one slot without pulling in three more interfaces to describe.
    /// </summary>
    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);

        [PreserveSig] int GetDisplayName(out IntPtr name);

        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);

        [PreserveSig] int GetIconPath(out IntPtr path);

        [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);

        [PreserveSig] int GetGroupingParam(out Guid grouping);

        [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr eventContext);

        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notifications);

        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notifications);

        [PreserveSig] int GetSessionIdentifier(out IntPtr id);

        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);

        [PreserveSig] int GetProcessId(out uint processId);

        [PreserveSig] int IsSystemSoundsSession();

        /// <summary>
        /// TRUE says this application does not want the system to duck other streams for it.
        ///
        /// Which is exactly the port's case: the only other stream that matters is the console's
        /// own audio, playing in the same window the microphone is for.
        /// </summary>
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(
            out IntPtr buffer,
            out int frames,
            out int flags,
            out long devicePosition,
            out long counterPosition);

        [PreserveSig]
        int ReleaseBuffer(int frames);

        [PreserveSig]
        int GetNextPacketSize(out int frames);
    }

    /// <summary>
    /// PP708: the playing side's service, and the whole of what it adds.
    ///
    /// Two methods, and the pair is a lease rather than a call: GetBuffer hands back a pointer into
    /// the engine's own ring and ReleaseBuffer says how much of it was filled. A caller that took a
    /// buffer and did not release it stalls the engine, which is the opposite failure from the
    /// capture side's - there a missed release drops audio, here it stops it.
    /// </summary>
    [ComImport]
    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioRenderClient
    {
        [PreserveSig]
        int GetBuffer(int framesRequested, out IntPtr buffer);

        [PreserveSig]
        int ReleaseBuffer(int framesWritten, int flags);
    }
}
