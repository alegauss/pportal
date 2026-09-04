namespace ChiakiNg.Session;

/// <summary>
/// PP652: the accumulator between a capture device's packets and the console's units.
///
/// A WASAPI capture client hands back whatever a packet holds - the engine's period, not the
/// console's frame. streamconnection.c announces 480 frames to a unit, which at one 16-bit channel
/// is 960 bytes, and the encoder downstream wants exactly that. So something has to hold a
/// remainder across calls, and this is it.
///
/// WHY THIS IS A TYPE AND NOT THREE LINES IN THE CAPTURE LOOP. The loop cannot be tested here: it
/// needs a device, a thread, and a person making noise. This can, exhaustively - a packet smaller
/// than a unit, larger, an exact multiple, a run of odd sizes summing to whole units - and every
/// one of those is a place a hand-written index goes wrong quietly, emitting a unit with the tail
/// of one packet and the head of the next in the wrong order.
///
/// WHAT IT DOES NOT DO. Convert. PP652's spike established that initialising with
/// AUTOCONVERTPCM succeeds on every capture device here, so what arrives is already one channel of
/// 16-bit PCM at 48000 Hz - the downmix and the resample are Windows's, and a converter written
/// here would be a second one.
/// </summary>
public sealed class MicrophoneUnits
{
    private readonly byte[] held;
    private int filled;

    /// <summary>A unit's size, from the format the C announces.</summary>
    public int UnitBytes { get; }

    /// <summary>How many whole units have been handed out.</summary>
    public long Emitted { get; private set; }

    /// <summary>Bytes held back, waiting for the rest of a unit. Always below <see cref="UnitBytes"/>.</summary>
    public int Pending => filled;

    /// <summary>An accumulator for the announced format.</summary>
    public MicrophoneUnits()
        : this(MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced))
    {
    }

    /// <summary>An accumulator for a unit of a given size, which is what makes the shape testable.</summary>
    public MicrophoneUnits(int unitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(unitBytes, 1);

        UnitBytes = unitBytes;
        held = new byte[unitBytes];
    }

    /// <summary>
    /// Take a capture packet and hand every whole unit in it to <paramref name="unit"/>.
    ///
    /// The span handed to the callback is valid only for that call: the buffer is reused, and a
    /// caller that needs the bytes to outlive the callback copies them. That is the same contract
    /// <see cref="Protocol.VideoReceiver"/> states for its packets, and for the same reason - the
    /// alternative is an allocation per unit at a hundred units a second.
    /// </summary>
    public void Take(ReadOnlySpan<byte> packet, Action<ReadOnlySpan<byte>> unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // The held remainder first, because a unit that spans two packets starts in the older one.
        if (filled > 0)
        {
            int wanted = Math.Min(UnitBytes - filled, packet.Length);
            packet[..wanted].CopyTo(held.AsSpan(filled));
            filled += wanted;
            packet = packet[wanted..];

            if (filled < UnitBytes)
                return;

            filled = 0;
            Emitted++;
            unit(held);
        }

        // Then whole units straight out of the packet, with no copy at all.
        while (packet.Length >= UnitBytes)
        {
            Emitted++;
            unit(packet[..UnitBytes]);
            packet = packet[UnitBytes..];
        }

        if (packet.IsEmpty)
            return;

        packet.CopyTo(held);
        filled = packet.Length;
    }

    /// <summary>
    /// Drop what is held, which is what a stop does.
    ///
    /// The remainder is NOT padded out and emitted. A partial unit is not ten milliseconds of
    /// audio, and sending one would put the encoder a fraction of a frame out of step for the rest
    /// of the session - which is the kind of drift that sounds like nothing and never recovers.
    /// </summary>
    public void Reset() => filled = 0;
}
