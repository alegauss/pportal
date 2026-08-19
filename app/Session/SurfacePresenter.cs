using System.Windows.Interop;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP135: the shared surface handed to WPF, which is the last link of PP9's chain.
///
/// PP131 built the device, PP132 the hop to D3D9Ex, PP133 the wrap that renders, PP134 the render
/// call. All four are libplacebo and Direct3D saying yes. This is WPF saying yes, and it is a
/// separate question: D3DImage.SetBackBuffer VALIDATES what it is given, and refuses a surface
/// that did not come from a D3D9Ex device or that is not a render target it can compose.
///
/// So the whole design rests on a check nothing in the graphics stack can answer. If WPF refuses
/// the surface, the shared texture, the wrap and the render are all correct and the picture is
/// still black - and the failure arrives at the first frame of the first session.
///
/// STA and a Dispatcher, no Window. D3DImage is a DispatcherObject, so it needs a thread with a
/// dispatcher to be created on at all; it does not need to be shown, which is what makes this
/// answerable without a display.
/// </summary>
public static class SurfacePresenter
{
    /// <summary>What happened when WPF was offered the surface.</summary>
    public enum Result
    {
        /// <summary>WPF took it and reports a front buffer, which is what a frame needs.</summary>
        Available,
        /// <summary>WPF took it and reports no front buffer - accepted, but nothing would show.</summary>
        NoFrontBuffer,
        /// <summary>SetBackBuffer threw. The message says which of its rules the surface broke.</summary>
        Refused,
    }

    /// <summary>
    /// Offers an IDirect3DSurface9 to a D3DImage on an STA thread, and reports what WPF did.
    ///
    /// Bounded, because the failure this guards against historically has not been an exception -
    /// PP117 is a whole task about a graphics call that did not return - and a suite that hangs
    /// reports nothing at all.
    /// </summary>
    public static Result Offer(IntPtr surface, TimeSpan timeout, out string detail)
    {
        Result result = Result.Refused;
        string message = "the STA thread did not answer";

        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                var image = new D3DImage();
                image.Lock();
                try
                {
                    // The surface, as it came off the D3D9Ex device. WPF checks the device it was
                    // created on and the usage it was created with, and throws rather than
                    // returning false - which is why this is in a try at all.
                    image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface);
                    result = image.IsFrontBufferAvailable ? Result.Available : Result.NoFrontBuffer;
                    message = $"front buffer {(image.IsFrontBufferAvailable ? "available" : "absent")}"
                        + $", {image.PixelWidth}x{image.PixelHeight}";
                }
                finally
                {
                    image.Unlock();
                }
            }
            catch (Exception ex)
            {
                result = Result.Refused;
                message = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                done.Set();
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(timeout))
        {
            detail = "SetBackBuffer did not return within " + timeout;
            return Result.Refused;
        }

        detail = message;
        return result;
    }

    /// <summary>
    /// The whole chain in one call: a device, a shared surface, a render into it, and WPF taking
    /// it - so that a break anywhere is one failure with a name rather than four green checks and
    /// a black window.
    /// </summary>
    public static Result OfferSharedSurface(out string detail)
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11(forceSoftware: false);
        if (device is null)
        {
            detail = "no hardware adapter";
            return Result.Refused;
        }

        using SharedSurface? shared = SharedSurface.Create(device, 1920, 1080, out ShareStage stage);
        if (shared is null)
        {
            detail = "the share failed at " + stage;
            return Result.Refused;
        }

        shared.Render(device);
        return Offer(shared.Surface, TimeSpan.FromSeconds(10), out detail);
    }
}
