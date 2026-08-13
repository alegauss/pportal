using System;

namespace VideoUpscale;

/// <summary>
/// The frame the upscaler is given, as NV12 - the format a hardware decoder hands over, which is
/// what RTX Video Super Resolution is built to receive.
///
/// It is synthesised rather than decoded, and that is a real limit on what this spike can answer.
/// No console is reachable from this machine, so there is no remote play frame to feed it. What a
/// synthetic pattern can still settle is the cost: VSR is a fixed convolutional network evaluated
/// per pixel, so its time depends on the input and output resolutions rather than on what the
/// picture contains. What it cannot settle is whether the picture is *better*, because a learned
/// upscaler's benefit on real encoded video does not follow from its behaviour on a chart.
///
/// The pattern is built to be hard in the ways an upscaler is judged on: near-horizontal edges
/// where staircasing shows, fine concentric rings that alias, hard text-like strokes, and a flat
/// gradient where ringing would be visible against nothing. It is deterministic - no RNG - so two
/// runs feed byte-identical input and any difference in the output is the processor's.
/// </summary>
internal static class Frame
{
    public const int Width = 1920;
    public const int Height = 1080;

    /// <summary>NV12: a full-size Y plane followed by a half-size interleaved UV plane.</summary>
    public static byte[] BuildNv12()
    {
        int ySize = Width * Height;
        int uvSize = Width * (Height / 2);
        var buf = new byte[ySize + uvSize];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                buf[y * Width + x] = Luma(x, y);
        }

        // Chroma: a slow horizontal sweep, so the frame is not grey and the colour path is
        // exercised, but nothing in it competes with the luma detail the upscaler is judged on.
        for (int y = 0; y < Height / 2; y++)
        {
            int row = ySize + y * Width;
            for (int x = 0; x < Width / 2; x++)
            {
                buf[row + x * 2] = (byte)(80 + 96 * x / (Width / 2));       // U
                buf[row + x * 2 + 1] = (byte)(200 - 96 * y / (Height / 2)); // V
            }
        }

        return buf;
    }

    private static byte Luma(int x, int y)
    {
        // Four quadrants, four ways to be hard.
        bool right = x >= Width / 2;
        bool bottom = y >= Height / 2;
        int lx = right ? x - Width / 2 : x;
        int ly = bottom ? y - Height / 2 : y;

        if (!right && !bottom)
        {
            // Near-horizontal edges: the classic staircase. One black-on-white wedge per 60 rows.
            int slope = ly / 3;
            return (byte)(((lx + slope) / 47) % 2 == 0 ? 235 : 16);
        }

        if (right && !bottom)
        {
            // Concentric rings, tightening outward until they alias against the sampling grid.
            int dx = lx - Width / 4, dy = ly - Height / 4;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double phase = r * r / 900.0;
            return (byte)(16 + 219 * (0.5 + 0.5 * Math.Cos(phase)));
        }

        if (!right && bottom)
        {
            // Hard strokes on a light ground, the shape of an on-screen HUD.
            bool ink = (lx % 23) < 4 || (ly % 31) < 3 || ((lx + ly) % 61) < 2;
            return (byte)(ink ? 16 : 210);
        }

        // A clean two-axis gradient: nothing here should acquire an edge, so ringing shows.
        return (byte)(16 + 219.0 * (lx / (double)(Width / 2) * 0.5 + ly / (double)(Height / 2) * 0.5));
    }
}
