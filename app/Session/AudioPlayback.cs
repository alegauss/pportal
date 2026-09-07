using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP797: the ring's far side - what moves decoded PCM out of it and into the speakers.
///
/// PP708 opened the render endpoint and PP751 filled <see cref="AudioRing"/>, and nothing joined
/// them: the ring's only caller outside a test was the selftest, and a decoded frame reached it and
/// stopped. Both halves were built and neither could hear the other.
///
/// TWO PUMPS AND THEY ARE NOT THE SAME ONE. <see cref="WasapiRender"/> already runs a thread on the
/// engine's cadence, moving units into IAudioRenderClient and writing silence where there are none.
/// What was missing is the one above it: something that reads the ring on its own cadence and hands
/// units down. A single pump cannot do both, because the engine's asks for room and the ring's asks
/// for how far behind the sink is.
///
/// THE POLICY IS THE Qt CLIENT'S AND THE NUMBERS ARE ALREADY NAMED. streamsession.cpp's output
/// drain fills the sink to <see cref="AudioRing.DrainTargetFor"/> and clears it outright past
/// <see cref="AudioRing.ClearThresholdFor"/>, and PP5 wrote both so the port could not pick
/// different ones by accident. This is their first caller.
///
/// AND THE UNITS STAY SMALL. A drain that handed the engine one large buffer would meet
/// <see cref="WasapiRender"/>'s own arithmetic - a unit longer than the room left is played as far
/// as it fits - so each pass writes at most one buffer's worth, which is the size the ring was
/// built around.
/// </summary>
public sealed class AudioPlayback : IDisposable
{
    private readonly WasapiRender render;
    private readonly bool ownsRender;
    private readonly CancellationTokenSource stopping = new();

    private Thread? pump;
    private bool disposed;

    /// <param name="render">
    /// The endpoint, or null for one of this object's own. Taken so a test can drive the drain
    /// against a render that was never started - what a pass DOES is the same either way, and a
    /// machine with no speakers still has to run the session.
    /// </param>
    public AudioPlayback(WasapiRender? render = null)
    {
        ownsRender = render is null;
        this.render = render ?? new WasapiRender();
    }

    /// <summary>Where the opus decoder writes, which is the ring this drains.</summary>
    public AudioRingSink Sink { get; } = new();

    /// <summary>The endpoint, for a caller reading its health rather than its bytes.</summary>
    public WasapiRender Render => render;

    /// <summary>How many units this has handed the renderer.</summary>
    public int Drained { get; private set; }

    /// <summary>And how many times it threw a sink away for being more than three buffers behind.</summary>
    public int Cleared { get; private set; }

    /// <summary>Whether the drain thread is up.</summary>
    public bool Running => pump is not null;

    /// <summary>
    /// Open the endpoint and start draining into it.
    ///
    /// The thread goes up whatever the device answered, because the drain is what keeps the RING
    /// from filling: a machine with no speakers that stopped draining would leave the decoder
    /// dropping every frame it produced and reporting an overflow that is about the missing device.
    /// </summary>
    public RenderResult Start(string? deviceId = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        RenderResult opened = render.Start(deviceId);

        if (pump is null)
        {
            pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "audio ring drain",
            };

            pump.Start();
        }

        return opened;
    }

    /// <summary>
    /// One drain pass, which is what the thread does on a cadence.
    ///
    /// Public so the join can be asserted without a device and without a thread: what a pass moves
    /// is the whole of this class's behaviour, and a test driving it directly is reading the same
    /// code a live session runs.
    /// </summary>
    /// <returns>How many units this pass handed the renderer.</returns>
    public int Pass()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Sink.Ring is not { } ring)
            return 0;

        // The Qt client's audio_buffer_size, which is what the ring was sized from: capacity is
        // eight of them, so the buffer is the capacity over eight rather than a second constant.
        int buffer = ring.Capacity / 8;
        if (buffer < 1)
            return 0;

        if (render.QueuedBytes > AudioRing.ClearThresholdFor(buffer))
        {
            render.Clear();
            Cleared++;
        }

        var moved = 0;

        while (render.QueuedBytes < AudioRing.DrainTargetFor(buffer) && ring.Fill > 0)
        {
            byte[] unit = ring.Read(buffer);

            if (unit.Length == 0)
                break;

            render.Write(unit);
            moved++;
        }

        Drained += moved;

        return moved;
    }

    private void Pump()
    {
        while (!stopping.IsCancellationRequested)
        {
            Pass();

            // The renderer's own cadence, borrowed rather than chosen: the two pumps are looking at
            // the same queue from either side, and a drain slower than the engine's drain would let
            // it run dry between passes.
            stopping.Token.WaitHandle.WaitOne(WasapiRender.PollInterval);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        stopping.Cancel();

        // Bounded, and for the reason PP768 gives one seam over: a shutdown is not worth hanging
        // over, and the thread is waiting on a token it has just been handed.
        pump?.Join(TimeSpan.FromSeconds(2));
        pump = null;

        if (ownsRender)
            render.Dispose();

        stopping.Dispose();
    }
}
