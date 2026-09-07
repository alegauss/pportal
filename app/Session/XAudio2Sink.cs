using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP816: decoded PCM straight to the engine, with nothing between them.
///
/// <see cref="AudioRingSink"/> is the other implementation of this seam and the shape it replaces:
/// the decoder writes into <see cref="AudioRing"/>, <see cref="AudioPlayback"/>'s thread drains the
/// ring into <see cref="WasapiRender"/>, and that pump asks the engine how much room it has and
/// clamps a unit to fit. Three objects, two threads and a piece of arithmetic - and PP810 is one
/// line of the arithmetic going wrong, with both of its measured fixes moving the loss instead of
/// removing it.
///
/// THIS IS THE SAME PATH WITH THE MIDDLE TAKEN OUT. A decoded frame is handed to the engine whole,
/// on the thread that decoded it, and the engine plays it after whatever it already holds. There is
/// no ring to overflow, no drain to fall behind and no room to get wrong.
///
/// THE FORMAT ARRIVES BEFORE THE AUDIO DOES, which is why the device is opened here rather than in
/// a constructor. A STREAMINFO says what the stream is and the source voice is built for exactly
/// that; a render opened earlier would be opened for a guess. Same reason <see cref="AudioRingSink"/>
/// sizes its ring here and not before.
///
/// AND A MACHINE WITH NO SPEAKERS STILL RUNS. <see cref="Opened"/> carries what the device said and
/// the frames are counted either way, so a session on a machine that cannot play sound reports what
/// it decoded rather than failing - which is PP708's rule and <see cref="WasapiRender"/>'s.
/// </summary>
public sealed class XAudio2Sink : IPcmSink, IDisposable
{
    private XAudio2Render? render;
    private bool disposed;

    /// <summary>The engine, or null before a STREAMINFO said what to open it for.</summary>
    public XAudio2Render? Render => render;

    /// <summary>What opening the device answered, or null before one was opened.</summary>
    public RenderResult? Opened { get; private set; }

    /// <summary>What the stream announced, or null before it did.</summary>
    public (byte Channels, uint Rate)? Announced { get; private set; }

    /// <summary>How many decoded frames were handed to the engine, whole.</summary>
    public int Submitted { get; private set; }

    /// <summary>
    /// And how many were dropped because there was no engine to hand them to.
    ///
    /// A machine with no speakers, or a device that refused. Counted rather than silent, because a
    /// run that decodes and plays nothing should say which of the two it is.
    /// </summary>
    public int Unplayable { get; private set; }

    /// <summary>
    /// Open the device ahead of the stream, for the format every stream so far has announced.
    /// </summary>
    /// <remarks>
    /// OPENING IS NOT WORK FOR THE RECEIVE THREAD. <see cref="Settings"/> is reached from the
    /// takion's own receive thread, inside the STREAMINFO handler, and creating an XAudio2 engine
    /// and a mastering voice is a device open - it talks to the audio service and starts threads.
    /// Two live runs with the open on that thread ended with the run completing after eleven
    /// arrivals, where the same build with the device unavailable ran a whole session.
    ///
    /// So the root calls this when it builds the phase, on its own thread and before any datagram
    /// arrives, and <see cref="Settings"/> keeps what is already open when the announcement matches.
    /// </remarks>
    /// <param name="announced">What to open for, defaulting to 48 kHz stereo.</param>
    public void Prepare(MicrophoneAnnouncement? announced = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        MicrophoneAnnouncement wanted = announced ?? new MicrophoneAnnouncement(2, 16, 48000, 480);

        render?.Dispose();
        render = new XAudio2Render(wanted);
        Opened = render.Open();

        Announced = ((byte)wanted.Channels, (uint)wanted.Rate);
    }

    /// <inheritdoc/>
    public void Settings(byte channels, uint rate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        // ALREADY OPEN FOR THIS, which is the ordinary case and the one that must not touch the
        // device: every stream this port has seen announces 48 kHz stereo, and Prepare opened for it
        // before the receive thread existed.
        if (render is { Running: true } open
            && open.Format.Channels == channels
            && open.Format.Rate == rate)
        {
            Announced = (channels, rate);
            return;
        }

        Announced = (channels, rate);

        // A stream that announced something else rebuilds the voice, as it rebuilds the decoder:
        // rate and channels are what a source voice IS. This is the path that pays the open on the
        // receive thread, and it is the rare one rather than every session's.
        render?.Dispose();

        // A frame is a hundredth of a second, which is what the header's frame size has been on
        // every stream this port has seen and what AudioRingSink already derives the same way.
        render = new XAudio2Render(
            new MicrophoneAnnouncement(channels, 16, (int)rate, (int)rate / 100));

        Opened = render.Open();
    }

    /// <inheritdoc/>
    public void Pcm(ReadOnlySpan<short> pcm, int samplesPerChannel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (pcm.IsEmpty)
            return;

        if (render is not { Running: true })
        {
            Unplayable++;
            return;
        }

        // The whole frame, and there is no answer to read: the engine does not take part of it.
        render.Write(MemoryMarshal.AsBytes(pcm));
        Submitted++;
    }

    /// <summary>Stops the voice and lets the engine go.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        render?.Dispose();
        render = null;
    }
}
