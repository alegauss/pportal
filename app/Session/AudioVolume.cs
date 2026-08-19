namespace ChiakiNg.Session;

/// <summary>
/// PP5: the volume step of PushAudioFrame, which is SDL_MixAudioFormat into a zeroed buffer.
///
/// Mixing into silence is scaling, so what SDL is being asked for here is one multiply per sample
/// - but the shape of the call carries two decisions that a rewrite would drop.
///
/// The first is that volume zero does not produce silence, it produces NOTHING: the frame returns
/// early and never reaches the ring. A port that scaled by zero instead would keep feeding the
/// sink, and a muted stream would hold its audio latency instead of letting the queue drain.
///
/// The second is that the scaling is skipped entirely at <see cref="MaxVolume"/> and above. At
/// exactly 128 the arithmetic would be a no-op anyway; above it, it would be a boost, and the
/// branch is what makes 128 a ceiling rather than a midpoint.
/// </summary>
public static class AudioVolume
{
    /// <summary>SDL_MIX_MAXVOLUME: the volume at which a sample passes through unchanged.</summary>
    public const int MaxVolume = 128;

    /// <summary>
    /// Whether the frame should be dropped rather than scaled. True only at zero, and it means
    /// dropped - not queued as silence.
    /// </summary>
    public static bool ShouldDrop(int volume) => volume == 0;

    /// <summary>
    /// One frame at one volume, as SDL mixes it into a zeroed destination: the sample times the
    /// volume over 128, saturated to a signed 16-bit sample.
    ///
    /// The saturation cannot fire while the volume is within range - that is what makes 128 the
    /// maximum - and is kept because the clamp is SDL's and its absence would only be visible if
    /// somebody widened the setting.
    /// </summary>
    public static void Apply(ReadOnlySpan<short> source, Span<short> destination, int volume)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("destination is shorter than source.", nameof(destination));

        if (volume >= MaxVolume)
        {
            source.CopyTo(destination);
            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            int scaled = source[i] * volume / MaxVolume;
            destination[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
        }
    }
}
