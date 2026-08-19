using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// PP9: the renderer's seam, which is a second DLL and not more of the first.
///
/// chiaki-shim.dll is loaded by every run of the selftest, on machines with no GPU and in CI.
/// Linking libplacebo into it would make a graphics driver a precondition for parsing a discovery
/// reply. So the renderer has its own DLL, its own ABI, and its own resolver entry - and a host
/// that never draws anything never loads it.
///
/// What this carries so far is a probe rather than a renderer. PP9 chose libplacebo on D3D11 from
/// the source alone: the shaders live above pl_gpu, and the only backend calls without a D3D11
/// counterpart are the ones handing frames to QtQuick, which the port does not have. That was a
/// decision taken without building anything, which is what this answers.
/// </summary>
public static class ChiakiRender
{
    /// <summary>The name the resolver in <see cref="ChiakiNative"/> maps to chiaki-render.dll.</summary>
    internal const string Library = "chiaki-render";

    /// <summary>Must equal CHIAKI_RENDER_ABI in shim/chiaki_render.h. Independent of the shim's.</summary>
    public const uint ExpectedAbi = 1;

    [DllImport(Library, EntryPoint = "chiaki_render_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint AbiVersion();

    /// <summary>
    /// Whether this libplacebo was built with the D3D11 backend, which PP9's decision rests on.
    ///
    /// A property of the copy that was linked, not of the project - so a toolchain that dropped
    /// it should say so here rather than at the first frame of a session.
    /// </summary>
    [DllImport(Library, EntryPoint = "chiaki_render_has_d3d11", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool HasD3d11();

    [DllImport(Library, EntryPoint = "chiaki_render_has_vulkan", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool HasVulkan();

    /// <summary>
    /// A libplacebo D3D11 device, or null.
    ///
    /// <paramref name="forceSoftware"/> selects the WARP adapter, which is what makes this
    /// answerable on a machine with no GPU: a CI runner still gets a real pl_gpu, so "the backend
    /// does not work" and "this box has no hardware" stop being the same answer.
    /// </summary>
    public static RenderDevice? CreateD3d11(bool forceSoftware = false)
    {
        IntPtr handle = D3d11Create(forceSoftware);
        return handle == IntPtr.Zero ? null : new RenderDevice(handle);
    }

    [DllImport(Library, EntryPoint = "chiaki_render_d3d11_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr D3d11Create([MarshalAs(UnmanagedType.I1)] bool forceSoftware);
}

/// <summary>One libplacebo D3D11 device, and the limits a renderer would be written against.</summary>
public sealed class RenderDevice : IDisposable
{
    private IntPtr _handle;

    internal RenderDevice(IntPtr handle) => _handle = handle;

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(RenderDevice));

    /// <summary>What libplacebo says it is, for a log line that names a version rather than a guess.</summary>
    public string Description => Marshal.PtrToStringUTF8(D3d11Description(Handle)) ?? "";

    /// <summary>
    /// The limits a stream is judged against. A maximum 2D texture below 3840 refuses a 4K
    /// stream, and finding that out at the first frame is finding it out from a user.
    /// </summary>
    public (int MaxTexture2D, int MaxBufferBytes) Limits()
    {
        if (!D3d11Limits(Handle, out int maxTexture, out int maxBuffer))
            throw new InvalidOperationException("chiaki_render_d3d11_limits failed.");

        return (maxTexture, maxBuffer);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        D3d11Destroy(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void D3d11Destroy(IntPtr d3d11);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_limits",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool D3d11Limits(IntPtr d3d11, out int maxTexture2D, out int maxBufferBytes);

    [DllImport(ChiakiRender.Library, EntryPoint = "chiaki_render_d3d11_description",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr D3d11Description(IntPtr d3d11);
}
