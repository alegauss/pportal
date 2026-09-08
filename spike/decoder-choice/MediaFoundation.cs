using System.Runtime.InteropServices;

namespace DecoderChoice;

/// <summary>
/// The Media Foundation surface both halves of this spike use.
///
/// PP650 enumerated and PP818 decodes, and the two need the same IMFAttributes and IMFActivate. It
/// lives here rather than being declared twice because a COM interface IS its vtable layout: two
/// copies that drift by one method do not fail to compile, they call the wrong function pointer -
/// so the thing that must not be duplicated is exactly this.
///
/// EVERY INTERFACE IS DECLARED FLAT, REPEATING IMFAttributes' THIRTY METHODS, and that is not a
/// style choice. C# interface inheritance on a [ComImport] interface does NOT compose the vtable
/// the way the header does: declared as `IMFActivate : IMFAttributes`, ActivateObject lands at slot
/// 3 rather than slot 33, so the first call into it returns MF_E_ATTRIBUTENOTFOUND - a plausible
/// failure from a method that was never reached. PP650's half of this spike was written flat for
/// the same reason and says so; PP818's was written with inheritance first and paid for it.
///
/// Each interface is then declared to the last method USED and no further, but never past a gap:
/// the slots before a wanted method are declared so its index is right, the ones after it are left
/// out. So IMFActivate stops at ActivateObject and IMFSample at AddBuffer.
/// </summary>
internal static class Mf
{
    /// <summary>MF_VERSION for Windows 7 and later, which is what MFStartup is given.</summary>
    public const int Version = 0x00020070;

    public const int MftEnumFlagSyncMft = 0x00000001;
    public const int MftEnumFlagAsyncMft = 0x00000002;
    public const int MftEnumFlagHardware = 0x00000004;

    public static readonly Guid CategoryVideoDecoder = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");
    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");

    /// <summary>MFVideoFormat_H264 - the FOURCC 'H264' in the media subtype namespace.</summary>
    public static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");

    /// <summary>And MFVideoFormat_HEVC, 'HEVC'.</summary>
    public static readonly Guid VideoFormatHevc = new("43564548-0000-0010-8000-00aa00389b71");

    /// <summary>MFVideoFormat_NV12, which is what a DXVA decoder hands back.</summary>
    public static readonly Guid VideoFormatNv12 = new("3231564e-0000-0010-8000-00aa00389b71");

    public static readonly Guid MtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MtAllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");

    public static readonly Guid FriendlyNameAttribute = new("314ffbae-5b41-4c95-9c19-4e7d586face3");
    public static readonly Guid TransformClsidAttribute = new("6821c42b-65a4-4e82-99bc-9a88205ecd0c");
    public static readonly Guid EnumHardwareUrlAttribute = new("2fb866ac-b078-4942-ab6c-003d05cda674");
    public static readonly Guid SaD3D11Aware = new("206b4fc8-fcf9-4c51-afe3-9764369e33a0");

    /// <summary>MF_LOW_LATENCY, which turns off the reordering a no-B-frame stream never needs.</summary>
    public static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");

    /// <summary>IID_IMFTransform, which is what an activate is asked to make.</summary>
    public static readonly Guid IidTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");

    /// <summary>IID_ID3D11Device, for the manager's ResetDevice.</summary>
    public static readonly Guid IidD3D11Device = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");

    public const int MessageSetD3DManager = 0x00000002;
    public const int MessageNotifyBeginStreaming = 0x10000000;
    public const int MessageNotifyStartOfStream = 0x10000002;
    public const int MessageNotifyEndOfStream = 0x10000001;
    public const int MessageCommandDrain = 0x00000001;

    public const int NeedMoreInput = unchecked((int)0xc00d6d72);
    public const int TransformStreamChange = unchecked((int)0xc00d6d61);
    public const int TransformTypeNotSet = unchecked((int)0xc00d6d3a);
    public const int OutputDataBufferFormatChange = 0x00000100;

    public const uint InterlaceModeProgressive = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RegisterTypeInfo
    {
        public Guid MajorType;
        public Guid Subtype;
    }

    /// <summary>MFT_OUTPUT_STREAM_INFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OutputStreamInfo
    {
        public int Flags;
        public int Size;
        public int Alignment;
    }

    /// <summary>
    /// MFT_OUTPUT_DATA_BUFFER.
    /// </summary>
    /// <remarks>
    /// pSample is an [in, out]: the caller provides it where the transform does NOT allocate its own
    /// output samples, and the transform overwrites it where it does. A D3D11 decoder allocates, so
    /// this goes in null and comes back holding a sample that has to be released.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct OutputDataBuffer
    {
        public uint StreamId;
        public IntPtr Sample;
        public int Status;
        public IntPtr Events;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFTEnumEx(
        Guid category, int flags, ref RegisterTypeInfo input, IntPtr output,
        out IntPtr activate, out uint count);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateDXGIDeviceManager(out uint token, out IMFDXGIDeviceManager manager);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoTaskMemFree(IntPtr memory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, int flags,
        IntPtr featureLevels, int featureLevelCount, int sdkVersion,
        out IntPtr device, out int featureLevel, IntPtr context);

    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint capacity, out uint size);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    /// <summary>IMFActivate: IMFAttributes' thirty, then ActivateObject at slot 33.</summary>
    [ComImport]
    [Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFActivate
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint capacity, out uint size);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);

        [PreserveSig]
        int ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? value);

        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    /// <summary>
    /// IMFMediaType: IMFAttributes' thirty and nothing after them.
    /// </summary>
    /// <remarks>
    /// The five methods a media type adds - GetMajorType and the rest - are not declared, because
    /// none is called: a type is built and read entirely through attributes. Stopping here is safe
    /// where stopping mid-vtable would not be, since nothing below the cut is ever reached.
    /// </remarks>
    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint capacity, out uint size);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    [ComImport]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    /// <summary>IMFSample: IMFAttributes' thirty, then ten more as far as AddBuffer.</summary>
    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig]
        int GetString(
            ref Guid key,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] value,
            uint capacity,
            out uint length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint capacity, out uint size);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);

        [PreserveSig] int GetSampleFlags(out uint flags);
        [PreserveSig] int SetSampleFlags(uint flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out uint count);
        [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("eb533d5d-2db6-40f8-97a9-494692014f07")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFDXGIDeviceManager
    {
        [PreserveSig] int CloseDeviceHandle(IntPtr handle);
        [PreserveSig] int GetVideoService(IntPtr handle, ref Guid riid, out IntPtr service);
        [PreserveSig] int LockDevice(IntPtr handle, ref Guid riid, out IntPtr device, bool block);
        [PreserveSig] int OpenDeviceHandle(out IntPtr handle);
        [PreserveSig] int ResetDevice(IntPtr device, uint token);
        [PreserveSig] int TestDevice(IntPtr handle);
        [PreserveSig] int UnlockDevice(IntPtr handle, bool saveState);
    }

    /// <summary>
    /// IMFTransform, to ProcessOutput - which is the last of the twenty-three.
    /// </summary>
    /// <remarks>
    /// Declared whole because the decode needs the last method in the vtable, so there is nothing
    /// left to leave out. The order is the header's and not a convenient one: SetInputType and
    /// SetOutputType sit at 13 and 14, AFTER the four Get*AvailableType and Delete/Add slots, and a
    /// declaration that omitted those would put SetOutputType where GetInputAvailableType is.
    /// </remarks>
    [ComImport]
    [Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFTransform
    {
        [PreserveSig]
        int GetStreamLimits(
            out uint inputMinimum, out uint inputMaximum,
            out uint outputMinimum, out uint outputMaximum);

        [PreserveSig] int GetStreamCount(out uint inputs, out uint outputs);

        [PreserveSig]
        int GetStreamIDs(
            uint inputCapacity, [Out] uint[] inputs,
            uint outputCapacity, [Out] uint[] outputs);

        [PreserveSig] int GetInputStreamInfo(uint id, IntPtr info);
        [PreserveSig] int GetOutputStreamInfo(uint id, out OutputStreamInfo info);
        [PreserveSig] int GetAttributes(out IMFAttributes? attributes);
        [PreserveSig] int GetInputStreamAttributes(uint id, out IMFAttributes? attributes);
        [PreserveSig] int GetOutputStreamAttributes(uint id, out IMFAttributes? attributes);
        [PreserveSig] int DeleteInputStream(uint id);
        [PreserveSig] int AddInputStreams(uint count, [In] uint[] ids);
        [PreserveSig] int GetInputAvailableType(uint id, uint index, out IMFMediaType? type);
        [PreserveSig] int GetOutputAvailableType(uint id, uint index, out IMFMediaType? type);
        [PreserveSig] int SetInputType(uint id, IMFMediaType? type, int flags);
        [PreserveSig] int SetOutputType(uint id, IMFMediaType? type, int flags);
        [PreserveSig] int GetInputCurrentType(uint id, out IMFMediaType? type);
        [PreserveSig] int GetOutputCurrentType(uint id, out IMFMediaType? type);
        [PreserveSig] int GetInputStatus(uint id, out uint flags);
        [PreserveSig] int GetOutputStatus(out uint flags);
        [PreserveSig] int SetOutputBounds(long lower, long upper);
        [PreserveSig] int ProcessEvent(uint id, IntPtr theEvent);
        [PreserveSig] int ProcessMessage(int message, IntPtr param);
        [PreserveSig] int ProcessInput(uint id, IMFSample sample, int flags);

        [PreserveSig]
        int ProcessOutput(
            int flags, uint count, ref OutputDataBuffer buffers, out int status);
    }
}
