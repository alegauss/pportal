using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP700: the D3DImage nobody ever filled.
///
/// <see cref="StreamOverlayViewModel.Video"/> is an ImageSource and StreamOverlayView binds it -
/// "the video is an ImageSource like any other and this is a Grid". PP10 wrote that, PP9 decided
/// it, and nothing ever assigned the property. This assigns it.
///
/// THREE RULES D3DImage HAS AND NOTHING ELSE IN WPF DOES:
///
///   it is a DispatcherObject, so every touch is on the thread that made it - and the renderer's
///   thread is not that thread. So the pump marshals;
///
///   the back buffer is set ONCE, inside a Lock, and the surface must come from a D3D9Ex device.
///   <see cref="SharedSurface"/> is that surface and PP135 already proved WPF takes it;
///
///   a frame does not appear because the texture changed. WPF redraws what it is told is dirty, so
///   AddDirtyRect after every render is what turns a written texture into a picture.
///
/// WHAT IT DOES NOT OWN is the surface or the presenter. Both outlive a redraw and belong to the
/// session; this holds the WPF end and nothing else, which is why disposing it leaves a running
/// stream running.
/// </summary>
public sealed class StreamPresentation
{
    private readonly D3DImage image = new();
    private readonly Dispatcher dispatcher;
    private readonly Int32Rect whole;
    private long shown;

    /// <summary>
    /// Build one on the calling thread, which must be the UI thread that will draw it.
    /// </summary>
    /// <param name="width">The picture's width, which is the dirty rectangle's too.</param>
    /// <param name="height">And its height.</param>
    public StreamPresentation(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        dispatcher = Dispatcher.CurrentDispatcher;
        whole = new Int32Rect(0, 0, width, height);
    }

    /// <summary>The ImageSource the view binds, which is the D3DImage itself.</summary>
    public ImageSource Source => image;

    /// <summary>How many times a frame was marked dirty, which is how many WPF was told about.</summary>
    public long Shown => Interlocked.Read(ref shown);

    /// <summary>Whether WPF reports a front buffer, which is what says a picture can appear.</summary>
    public bool Available => image.IsFrontBufferAvailable;

    /// <summary>
    /// Hand WPF the shared surface, once.
    /// </summary>
    /// <remarks>
    /// Inside a Lock, because SetBackBuffer is one of the calls that requires it. The surface is
    /// the share's - owned by it, released by it - and this borrows the pointer for as long as the
    /// image lives, which is the same rule <see cref="SurfacePresenter"/> states.
    /// </remarks>
    public bool Attach(SharedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        IntPtr back = surface.Surface;
        if (back == IntPtr.Zero)
            return false;

        image.Lock();
        try
        {
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back);
        }
        finally
        {
            image.Unlock();
        }

        return image.IsFrontBufferAvailable;
    }

    /// <summary>
    /// Render INSIDE the lock, which is the only synchronisation this surface has.
    /// </summary>
    /// <param name="render">Draws into the shared texture. Runs on the UI thread, under the lock.</param>
    /// <remarks>
    /// The share is the OLD kind of shared texture. chiaki_render.h records why - D3D9Ex cannot
    /// open a keyed-mutex resource - so there is no fence, no mutex and no primitive on the texture
    /// itself. D3DImage.Lock IS the primitive: WPF does not read the back buffer while it is held,
    /// so a render that happens outside it can be composed half-written.
    ///
    /// THE FIRST VERSION DREW OUTSIDE THE LOCK, and this moved inside it while chasing artefacts a
    /// person described as analogue television static. IT WAS NOT THE CAUSE - those stopped when
    /// idrOnFecFailure went true, so they were error propagation from a lost reference frame. The
    /// move stays because the reasoning above holds without them, and the wrong trail is recorded
    /// so the next reader does not take this for a fix to a decoding problem.
    ///
    /// So the draw moved inside it, and BLOCKING rather than posted. The decoded frame's planes are
    /// borrowed until the next pull, so a posted render would draw from a buffer the puller had
    /// already replaced - which is the same tearing arriving by a second route.
    ///
    /// It paces the pull to the UI thread, which is a real cost and the right one: PP700's own
    /// measurement puts a render at 0.27ms, and a frame torn in half is not cheaper for being
    /// early.
    /// </remarks>
    public bool Present(Func<bool> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        return dispatcher.Invoke(() =>
        {
            if (!image.IsFrontBufferAvailable)
                return false;

            image.Lock();
            try
            {
                if (!render())
                    return false;

                image.AddDirtyRect(whole);
            }
            finally
            {
                image.Unlock();
            }

            Interlocked.Increment(ref shown);
            return true;
        });
    }
}
