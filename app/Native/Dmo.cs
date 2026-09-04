using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// PP710: the DirectX Media Object plumbing, once, for the two in-box transforms this port drives.
///
/// PP709 wrote all of this to reach the Voice Capture DSP and found the second thing that needs it
/// in the same breath: the canceller refuses 48000, so a rate bridge is owed, and Windows's own
/// resampler is a DMO too. Two copies of a COM vtable is the shape PP693 already had to make a rule
/// about - a missing method above one that is called sends the call to the wrong slot, and the
/// failure is a crash somewhere else entirely - so the surface is shared and the transforms are not.
///
/// EVERY METHOD CARRIES PreserveSig. Without it the CLR reads the declared int as a retval and every
/// HRESULT test compares against an uninitialised local. A DMO returns S_FALSE for "nothing yet" on
/// more than one method, so a non-zero success would be read as a failure and a failure as success.
/// </summary>
internal static class Dmo
{
    /// <summary>Ask whether a type would be accepted without accepting it.</summary>
    public const int SetTypeTestOnly = 0x00000001;

    /// <summary>The buffer begins a frame, which is what a fixed-size PCM chunk is.</summary>
    public const int InputSyncPoint = 0x00000001;

    /// <summary>VT_I4 and VT_BOOL, which are the two a transform's property store is set with.</summary>
    public const short VtI4 = 3;

    public const short VtBool = 11;

    private const short WaveFormatPcm = 1;

    private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MediaSubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FormatWaveFormatEx = new("05589f81-c356-11ce-bf01-00aa0055595a");

    /// <summary>
    /// A DMO_MEDIA_TYPE for 16-bit PCM at one rate, allocated whole.
    ///
    /// One block holding the type and the WAVEFORMATEX it points at, so freeing it is one call and
    /// there is no partial state to get wrong. MoInitMediaType would allocate the format for us and
    /// hand back memory MoFreeMediaType owns; owning both here is fewer rules.
    /// </summary>
    public static IntPtr MediaType(int rate, int channels = 1)
    {
        int typeSize = Marshal.SizeOf<DmoMediaType>();
        int formatSize = Marshal.SizeOf<WaveFormatEx>();

        IntPtr block = Marshal.AllocHGlobal(typeSize + formatSize);
        IntPtr format = block + typeSize;

        var wave = new WaveFormatEx
        {
            FormatTag = WaveFormatPcm,
            Channels = (short)channels,
            SamplesPerSec = rate,
            BitsPerSample = 16,
            BlockAlign = (short)(channels * 2),
            Size = 0,
        };
        wave.AvgBytesPerSec = wave.SamplesPerSec * wave.BlockAlign;

        Marshal.StructureToPtr(wave, format, false);

        var type = new DmoMediaType
        {
            MajorType = MediaTypeAudio,
            SubType = MediaSubTypePcm,
            FixedSizeSamples = 1,
            TemporalCompression = 0,
            SampleSize = wave.BlockAlign,
            FormatType = FormatWaveFormatEx,
            Unknown = IntPtr.Zero,
            FormatSize = formatSize,
            Format = format,
        };

        Marshal.StructureToPtr(type, block, false);
        return block;
    }

    /// <summary>Give the block back. One call, because <see cref="MediaType"/> made one.</summary>
    public static void FreeMediaType(IntPtr type) => Marshal.FreeHGlobal(type);

    /// <summary>Create a transform by class id, or null with the HRESULT it refused by.</summary>
    public static object? Create(Guid clsid, out int hresult)
    {
        hresult = 0;

        Type? type = Type.GetTypeFromCLSID(clsid);
        if (type is null)
            return null;

        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException)
        {
            hresult = error.HResult;
            return null;
        }
    }

    /// <summary>Set a VT_BOOL property, where VARIANT_TRUE is -1 rather than a bit.</summary>
    public static bool SetBool(IPropertyStore store, PropertyKey key, bool value)
    {
        ArgumentNullException.ThrowIfNull(store);

        var variant = new PropVariant { Type = VtBool, Value = value ? new IntPtr(-1) : IntPtr.Zero };
        PropertyKey local = key;

        return store.SetValue(ref local, ref variant) == 0;
    }

    /// <summary>Set a VT_I4 property.</summary>
    public static bool SetInt(IPropertyStore store, PropertyKey key, int value)
    {
        ArgumentNullException.ThrowIfNull(store);

        var variant = new PropVariant { Type = VtI4, Value = new IntPtr(value) };
        PropertyKey local = key;

        return store.SetValue(ref local, ref variant) == 0;
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
    public struct DmoMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;
        public int TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OutputDataBuffer
    {
        [MarshalAs(UnmanagedType.Interface)]
        public IMediaBuffer Buffer;

        public int Status;
        public long Timestamp;
        public long TimeLength;
    }

    /// <summary>
    /// A block of unmanaged memory a transform reads from and writes into.
    ///
    /// Managed, because a DMO wants an interface and the buffer's lifetime is the call's. The bytes
    /// themselves are unmanaged: the transform is handed the pointer and writes through it, which a
    /// pinned array would also allow and a moving one would not.
    /// </summary>
    public sealed class Buffer(int capacity) : IMediaBuffer, IDisposable
    {
        private readonly IntPtr data = Marshal.AllocHGlobal(Math.Max(capacity, 1));
        private int length;
        private bool freed;

        /// <summary>Put bytes in and say how many, which is what a full buffer is.</summary>
        public void Fill(ReadOnlySpan<byte> bytes)
        {
            unsafe
            {
                bytes.CopyTo(new Span<byte>((void*)data, capacity));
            }

            length = bytes.Length;
        }

        /// <summary>Take whatever the transform left, up to the caller's room.</summary>
        public int Read(Span<byte> into)
        {
            int taken = Math.Min(length, into.Length);

            unsafe
            {
                new ReadOnlySpan<byte>((void*)data, taken).CopyTo(into);
            }

            return taken;
        }

        /// <summary>Say it holds nothing, which is what a buffer handed out again must.</summary>
        public void Empty() => length = 0;

        public int SetLength(int cbLength)
        {
            if (cbLength > capacity)
                return unchecked((int)0x80070057);

            length = cbLength;
            return 0;
        }

        public int GetMaxLength(out int cbMaxLength)
        {
            cbMaxLength = capacity;
            return 0;
        }

        public int GetBufferAndLength(out IntPtr ppBuffer, out int pcbLength)
        {
            ppBuffer = data;
            pcbLength = length;
            return 0;
        }

        public void Dispose()
        {
            if (freed)
                return;

            freed = true;
            Marshal.FreeHGlobal(data);
        }
    }

    [ComImport]
    [Guid("59eff8b9-938c-4a26-82f2-95cb84cdc837")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaBuffer
    {
        [PreserveSig]
        int SetLength(int cbLength);

        [PreserveSig]
        int GetMaxLength(out int cbMaxLength);

        [PreserveSig]
        int GetBufferAndLength(out IntPtr ppBuffer, out int pcbLength);
    }

    /// <summary>
    /// IMediaObject, whole.
    ///
    /// Every method in vtable order and none omitted, which is not tidiness: a COM interface is an
    /// offset table, so a missing method above one that is called sends the call to the wrong slot.
    /// </summary>
    [ComImport]
    [Guid("d8ad0f58-5494-4102-97c5-ec798e59bcf4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaObject
    {
        [PreserveSig]
        int GetStreamCount(out int inputs, out int outputs);

        [PreserveSig]
        int GetInputStreamInfo(int stream, out int flags);

        [PreserveSig]
        int GetOutputStreamInfo(int stream, out int flags);

        [PreserveSig]
        int GetInputType(int stream, int typeIndex, IntPtr type);

        [PreserveSig]
        int GetOutputType(int stream, int typeIndex, IntPtr type);

        [PreserveSig]
        int SetInputType(int stream, IntPtr type, int flags);

        [PreserveSig]
        int SetOutputType(int stream, IntPtr type, int flags);

        [PreserveSig]
        int GetInputCurrentType(int stream, IntPtr type);

        [PreserveSig]
        int GetOutputCurrentType(int stream, IntPtr type);

        [PreserveSig]
        int GetInputSizeInfo(int stream, out int size, out int maxLookahead, out int alignment);

        [PreserveSig]
        int GetOutputSizeInfo(int stream, out int size, out int alignment);

        [PreserveSig]
        int GetInputMaxLatency(int stream, out long latency);

        [PreserveSig]
        int SetInputMaxLatency(int stream, long latency);

        [PreserveSig]
        int Flush();

        [PreserveSig]
        int Discontinuity(int stream);

        [PreserveSig]
        int AllocateStreamingResources();

        [PreserveSig]
        int FreeStreamingResources();

        [PreserveSig]
        int GetInputStatus(int stream, out int flags);

        [PreserveSig]
        int ProcessInput(
            int stream,
            [MarshalAs(UnmanagedType.Interface)] IMediaBuffer buffer,
            int flags,
            long timestamp,
            long timeLength);

        [PreserveSig]
        int ProcessOutput(
            int flags,
            int outputBufferCount,
            [In, Out, MarshalAs(UnmanagedType.LPArray)] OutputDataBuffer[] buffers,
            out int status);

        [PreserveSig]
        int Lock(int locked);
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

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);
    }
}
