using System;

namespace FrameGeneration;

/// <summary>
/// The sequence the interpolator is given: N NV12 frames of one pattern translating by a fixed
/// number of pixels per frame.
///
/// Motion is the whole point, and it is why this cannot reuse video-upscale's Frame. An upscaler
/// is judged on one still image and a frame interpolator cannot be judged on any number of copies
/// of one - handed the same picture twice, a driver that interpolates and a driver that repeats
/// produce byte-identical output, and the check that tells them apart would pass on both. So the
/// pattern moves, by <see cref="ShiftPerFrame"/> pixels horizontally per frame, and the frame the
/// processor generates between two of them is expected to sit between them.
///
/// It is synthesised rather than decoded, and that is the same limit video-upscale recorded one
/// task over: a rigid horizontal pan is the easiest motion a block-matcher will ever see. What a
/// synthetic pan can settle is the COST - the work is a fixed per-pixel search whose time follows
/// resolution - and the HOLD, which is read from the driver's own caps and does not depend on the
/// picture at all. What it cannot settle is whether a real 30fps title interpolates without
/// artefacts, because a pan is exactly the case that never breaks.
///
/// Deterministic - no RNG - so two runs feed byte-identical input and any difference in the output
/// is the processor's.
/// </summary>
internal static class Frames
{
    public const int Width = 1920;
    public const int Height = 1080;

    /// <summary>
    /// Pixels of horizontal translation between consecutive frames. 32 at 30fps is 960 px/s, a
    /// brisk camera pan rather than a pathological one - fast enough that a repeat is obvious and
    /// slow enough that a block search is not being asked for something unreasonable.
    /// </summary>
    public const int ShiftPerFrame = 32;

    /// <summary>NV12: a full-size Y plane followed by a half-size interleaved UV plane.</summary>
    public static byte[] BuildNv12(int frameIndex)
    {
        int shift = frameIndex * ShiftPerFrame;
        int ySize = Width * Height;
        int uvSize = Width * (Height / 2);
        var buf = new byte[ySize + uvSize];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                buf[y * Width + x] = Luma(Wrap(x + shift), y);
        }

        // Chroma moves with the luma. A colour plane that stands still while the picture pans is
        // a frame no decoder ever produces, and it would let a processor that only touches luma
        // look like one that interpolates the frame.
        for (int y = 0; y < Height / 2; y++)
        {
            int row = ySize + y * Width;
            for (int x = 0; x < Width / 2; x++)
            {
                int sx = Wrap(2 * x + shift) / 2;
                buf[row + x * 2] = (byte)(80 + 96 * sx / (Width / 2));       // U
                buf[row + x * 2 + 1] = (byte)(200 - 96 * y / (Height / 2));  // V
            }
        }

        return buf;
    }

    /// <summary>
    /// Horizontal wrap, so every frame in the sequence is full and none of them carries a band of
    /// undefined pixels at one edge. A wrap seam is one vertical discontinuity travelling across
    /// the frame; the comparison below ignores it by construction, because it compares whole
    /// frames against each other rather than against a predicted picture.
    /// </summary>
    private static int Wrap(int x) => ((x % Width) + Width) % Width;

    private static byte Luma(int x, int y)
    {
        // Three bands, three ways for a repeat to give itself away.
        int band = y * 3 / Height;

        if (band == 0)
        {
            // Hard vertical edges at an irregular pitch. An edge that has not moved is the single
            // most visible evidence that a generated frame is a copy.
            int p = x % 211;
            return (byte)(p < 37 || (p > 96 && p < 118) ? 235 : 16);
        }

        if (band == 1)
        {
            // A disc with a soft rim, which is where a block-matcher's seams show if it has any:
            // a rigid shape crossing block boundaries either arrives whole or arrives torn.
            int dx = x - Width / 2, dy = y - Height / 2;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double t = Math.Clamp((180.0 - r) / 24.0, 0.0, 1.0);
            return (byte)(16 + 219 * t);
        }

        // A fine diagonal grating, near the sampling limit. It aliases under any resampling, so a
        // frame built by averaging two of them looks nothing like either.
        return (byte)(((x + y) / 3) % 2 == 0 ? 200 : 40);
    }
}
