using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D9;

namespace PresentPath;

/// <summary>
/// The two shapes PP43 exists to compare, behind one interface so the measured difference is the
/// presentation and nothing else.
///
/// Both are driven by the same D3D9Ex device and both do the same drawing: a 1080p offscreen
/// surface is filled with a per-frame colour and then copied to whatever the path presents from.
/// The source frame is synthesised on the GPU rather than uploaded from a CPU buffer because a
/// hardware-decoded frame does not cross the CPU boundary either - and a software decode would
/// add the same upload to both paths, so it cannot separate them.
/// </summary>
internal interface IPresenter : IDisposable
{
    /// <summary>Called once the hosting element exists. Creates the device and its surfaces.</summary>
    void Initialise(int width, int height);

    /// <summary>Draw one frame and present it. Returns the microseconds the present step took.</summary>
    double PresentOneFrame(int frameNumber);

    /// <summary>The element to put in the window.</summary>
    System.Windows.UIElement Element { get; }

    string Describe();
}

/// <summary>Shared device setup, so neither path can accidentally get a different one.</summary>
internal static class Device
{
    public static IDirect3D9Ex CreateD3D() => D3D9.Direct3DCreate9Ex();

    public static PresentParameters Params(IntPtr hwnd, int width, int height) => new()
    {
        BackBufferWidth = (uint)Math.Max(1, width),
        BackBufferHeight = (uint)Math.Max(1, height),
        BackBufferFormat = Format.X8R8G8B8,
        BackBufferCount = 1,
        SwapEffect = SwapEffect.Discard,
        Windowed = true,
        DeviceWindowHandle = hwnd,
        // Immediate, so what is measured is the cost of the path and not the wait for a vblank.
        // PP53 is the task about vblank waiting; conflating the two here would answer neither.
        PresentationInterval = PresentInterval.Immediate,
    };

    public static Vortice.Mathematics.Color FrameColour(int frameNumber)
    {
        // Varies every frame so nothing downstream can decide the surface is unchanged and skip
        // work - a present path that is fast because it presented nothing is not a result.
        byte r = (byte)(frameNumber * 7 & 0xff);
        byte g = (byte)(frameNumber * 3 & 0xff);
        byte b = (byte)(255 - (frameNumber * 5 & 0xff));
        return new Vortice.Mathematics.Color(r, g, b, (byte)255);
    }
}

/// <summary>
/// Path A: an airspace child window. A child HWND owned by a HwndHost, presented to directly by
/// D3D9Ex. Nothing WPF draws can appear over it, which is the structural cost the renderer task
/// weighs against the copy the other path pays.
/// </summary>
internal sealed class HwndPathPresenter : IPresenter
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    private sealed class ChildHost : HwndHost
    {
        public IntPtr Child { get; private set; }
        public event Action? Created;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            Child = CreateWindowExW(0, "static", null, WsChild | WsVisible,
                0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (Child == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
            Created?.Invoke();
            return new HandleRef(this, Child);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
            Child = IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName,
            int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }

    private readonly ChildHost host = new();
    private IDirect3D9Ex? d3d;
    private IDirect3DDevice9Ex? device;
    private IDirect3DSurface9? source;
    private IDirect3DSurface9? backBuffer;
    private int width, height;

    /// <summary>Whole surface, both sides: the copy is 1:1, so no scaling is being timed.</summary>
    private Vortice.Direct3D9.Rect FullRect => new(0, 0, width, height);

    public System.Windows.UIElement Element => host;

    public IntPtr ChildHandle => host.Child;

    public void Initialise(int w, int h)
    {
        width = w;
        height = h;
        if (host.Child == IntPtr.Zero)
            throw new InvalidOperationException("child HWND does not exist yet");

        d3d = Device.CreateD3D();
        device = d3d.CreateDeviceEx(0, DeviceType.Hardware, host.Child,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            Device.Params(host.Child, w, h));
        source = device.CreateOffscreenPlainSurface((uint)w, (uint)h, Format.X8R8G8B8, Pool.Default);
        backBuffer = device.GetBackBuffer(0, 0, BackBufferType.Mono);
    }

    public double PresentOneFrame(int frameNumber)
    {
        var dev = device ?? throw new InvalidOperationException("not initialised");

        // Drawing, outside the measured region: identical to the other path.
        dev.ColorFill(source!, Device.FrameColour(frameNumber));

        var sw = Stopwatch.StartNew();
        dev.StretchRect(source!, FullRect, backBuffer!, FullRect, TextureFilter.None);
        dev.PresentEx(Present.None);
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds;
    }

    public string Describe() => "HwndHost child window, D3D9Ex PresentEx to the child HWND (airspace)";

    public void Dispose()
    {
        backBuffer?.Dispose();
        source?.Dispose();
        device?.Dispose();
        d3d?.Dispose();
        host.Dispose();
    }

    public void WhenChildCreated(Action a) => host.Created += a;
}

/// <summary>
/// Path B: a shared surface. D3D9Ex renders into an offscreen render target that is handed to
/// WPF's D3DImage, so the frame composes properly with anything drawn above it - and pays a copy
/// per frame plus the compositor for that.
/// </summary>
internal sealed class D3DImagePathPresenter : IPresenter
{
    private readonly System.Windows.Controls.Image image = new();
    private readonly D3DImage d3dImage = new();
    private IDirect3D9Ex? d3d;
    private IDirect3DDevice9Ex? device;
    private IDirect3DSurface9? source;
    private IDirect3DSurface9? target;
    private int width, height;

    /// <summary>Whole surface, both sides: the copy is 1:1, so no scaling is being timed.</summary>
    private Vortice.Direct3D9.Rect FullRect => new(0, 0, width, height);

    public System.Windows.UIElement Element => image;

    public void Initialise(int w, int h)
    {
        width = w;
        height = h;
        d3d = Device.CreateD3D();
        // No HWND: this path never presents to a window of its own, which is the whole point.
        device = d3d.CreateDeviceEx(0, DeviceType.Hardware, IntPtr.Zero,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            Device.Params(IntPtr.Zero, 1, 1));
        source = device.CreateOffscreenPlainSurface((uint)w, (uint)h, Format.X8R8G8B8, Pool.Default);
        // Lockable false: a D3D9Ex render target is what D3DImage wants for the fast path.
        target = device.CreateRenderTarget((uint)w, (uint)h, Format.X8R8G8B8, MultisampleType.None, 0, false);

        image.Source = d3dImage;
        image.Stretch = System.Windows.Media.Stretch.Fill;

        d3dImage.Lock();
        d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, target.NativePointer);
        d3dImage.Unlock();
    }

    public double PresentOneFrame(int frameNumber)
    {
        var dev = device ?? throw new InvalidOperationException("not initialised");

        dev.ColorFill(source!, Device.FrameColour(frameNumber));

        var sw = Stopwatch.StartNew();
        d3dImage.Lock();
        dev.StretchRect(source!, FullRect, target!, FullRect, TextureFilter.None);
        d3dImage.AddDirtyRect(new Int32Rect(0, 0, width, height));
        d3dImage.Unlock();
        sw.Stop();
        return sw.Elapsed.TotalMicroseconds;
    }

    public string Describe() => "D3DImage shared surface, StretchRect into a D3D9Ex render target, composed by WPF";

    public void Dispose()
    {
        target?.Dispose();
        source?.Dispose();
        device?.Dispose();
        d3d?.Dispose();
    }
}
