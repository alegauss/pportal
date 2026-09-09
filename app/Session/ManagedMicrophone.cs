using System.Runtime.InteropServices;
using ChiakiNg.Native;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP825: the four stages with a device on one end and a takion on the other.
///
/// PP652 opened the capture, PP52's stage cleans, PP694 encodes and PP706 formats - and the only
/// place all four had ever run together was a test feeding synthetic units into a list. This is
/// that path with a microphone at the top and <see cref="ManagedTakion.SendMicrophone"/> at the
/// bottom, which is the half PP825 was left open for.
///
/// IT IS OFF UNLESS SOMETHING ASKS. A remote play client that opened the microphone because a
/// session started would be recording a room nobody said it could - so this is constructed by a
/// caller that wants it, and <see cref="ManagedStreamPhase"/> builds one only when told to. That
/// is a product rule rather than a porting one, and it is the reason the join is a flag and not a
/// line like PP824's.
///
/// THE CANCELLER IS OPTIONAL AND ITS ABSENCE IS NOT A FAILURE. VoiceCaptureDsp is a Windows
/// transform a machine may not have, and PP52's stage reports that rather than throwing. A unit
/// then goes to the encoder as it was captured: the console hears the room instead of a cleaned
/// room, which is worse than the canceller and much better than silence. Refusing to transmit
/// would be a vendor path whose absence is visible to the user.
///
/// THE FORMAT COMES OFF THE CAPTURE and not off the announcement. WasapiCapture asks the engine
/// for a rate and Windows converts, so the encoder is given what the capture actually produces -
/// asking the announcement would encode at 48000 a stream running at something else.
/// </summary>
public sealed class ManagedMicrophone : IDisposable
{
    private readonly CleanedMicrophone stage = new();
    private readonly ManagedOpusEncoder encoder = new();
    private readonly ManagedAudioSender sender;
    private readonly WasapiCapture capture;

    /// <summary>
    /// Where a formatted packet goes, and why it is copied rather than handed on.
    ///
    /// The sender hands out a ReadOnlySpan over its own buffer and SendMicrophone needs a mutable
    /// one - it encrypts and stamps in place, which is the C's own shape. Copying is the honest
    /// conversion: it is one memcpy per packet, at a third of the unit rate, and it leaves the
    /// sender's frame buffer untouched for the frame that follows.
    /// </summary>
    private readonly byte[] outgoing;

    private byte[] cleaned = [];
    private bool disposed;

    /// <param name="ps5">Whether the head carries its twentieth byte.</param>
    /// <param name="send">The takion's mic send, which the packet is handed to whole.</param>
    public ManagedMicrophone(bool ps5, Func<byte[], int, ChiakiError> send)
    {
        ArgumentNullException.ThrowIfNull(send);

        // Sized before the sender exists rather than off its PacketBytes, so the field the closure
        // below writes into is assigned first: the same two constants the sender derives its own
        // answer from, and the assertion in the tests holds the two together.
        outgoing = new byte[MicPacketHead.SizeFor(ps5) + ManagedAudioSender.FrameBufferBytes];

        sender = new ManagedAudioSender(ps5, packet =>
        {
            packet.CopyTo(outgoing);
            LastSend = send(outgoing, packet.Length);

            if (LastSend == ChiakiError.Success)
                Sent++;
        });

        capture = new WasapiCapture(Unit);
    }

    /// <summary>How many packets the takion accepted, which is what a live run has to show.</summary>
    public int Sent { get; private set; }

    /// <summary>How many the sender formatted, accepted or not.</summary>
    public int Formatted => sender.Sent;

    /// <summary>What the last send answered, or Success before there was one.</summary>
    public ChiakiError LastSend { get; private set; } = ChiakiError.Success;

    /// <summary>Whether the echo canceller is in the path, which a machine may not have.</summary>
    public bool Cleaning => stage.Running;

    /// <summary>How opening the device went.</summary>
    public CaptureResult Opened { get; private set; } = CaptureResult.NoDevice;

    /// <summary>
    /// Open the microphone and start the path.
    /// </summary>
    /// <param name="deviceId">An endpoint id, or null for the communications default.</param>
    /// <returns>Whether units are now flowing.</returns>
    public bool Start(string? deviceId = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        // Reported, not required: a machine with no transform still transmits.
        stage.Start();
        cleaned = new byte[Math.Max(stage.UnitBytes, 1) * 4];

        if (!encoder.Header(capture.Format.Rate, capture.Format.Channels))
            return false;

        Opened = capture.Start(deviceId);

        return Opened == CaptureResult.Running;
    }

    /// <summary>
    /// One captured unit, all the way down. Runs on the capture's own pump thread.
    /// </summary>
    private void Unit(ReadOnlySpan<byte> unit)
    {
        ReadOnlySpan<byte> pcm = unit;

        if (stage.Running)
        {
            CleanedUnit pass = stage.Clean(unit, [], cleaned);

            // The filter fills before it returns anything, and a short pass is that rather than an
            // error - PP52's stage counts them. Dropping the unit is what the C does with a frame
            // the canceller held.
            if (pass.Cleaned < stage.UnitBytes)
                return;

            pcm = cleaned.AsSpan(0, stage.UnitBytes);
        }

        if (encoder.Frame(MemoryMarshal.Cast<byte, short>(pcm), out ReadOnlySpan<byte> opus)
            != OpusFrameOutcome.Sent)
        {
            return;
        }

        // And the third one leaves: OpusData holds two frames back and sends on the third, which is
        // audiosender.c's own window and not a buffer this class gets to choose.
        sender.OpusData(opus);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // The capture first, so no unit reaches a stage that is going away.
        capture.Dispose();
        encoder.Dispose();
        stage.Dispose();
    }
}
