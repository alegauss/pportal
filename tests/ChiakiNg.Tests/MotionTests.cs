using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP130: the pad's sensors, fused into the orientation the console is sent.
///
/// The filter itself is libchiaki's and is carried rather than ported, so what is checked here is
/// what the port does around it: the two conversions on the way in, and that the fusion is
/// actually running and reaching a controller state.
/// </summary>
public class MotionTests
{
    /// <summary>
    /// Acceleration is divided by gravity and angular velocity is not. Both sensors arrive
    /// through one SDL event type, so normalising both - or neither - is one line and no error.
    /// </summary>
    [Fact]
    public void OnlyAccelerationIsNormalisedByGravity()
    {
        Assert.Equal(9.80665f, MotionTracker.StandardGravity);
        Assert.Equal(1f, MotionTracker.ToGravities(9.80665f), 5);
        Assert.Equal(2f, MotionTracker.ToGravities(19.6133f), 4);
        Assert.Equal(0f, MotionTracker.ToGravities(0f));
    }

    /// <summary>
    /// The tracker counts microseconds and SDL reports milliseconds. Getting it wrong does not
    /// fail - the filter believes a thousand times more or less time passed than did, and the
    /// orientation lags or snaps.
    /// </summary>
    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(1u, 1000u)]
    [InlineData(16u, 16000u)]
    public void TheTimestampIsMicroseconds(uint ms, uint us)
        => Assert.Equal(us, MotionTracker.ToMicroseconds(ms));

    /// <summary>
    /// A fresh tracker is NOT the identity rotation, which is the assumption this test was
    /// written on and had to be corrected against the source.
    ///
    /// chiaki_orientation_init sets a 90 degree rotation about x - sin(pi/4), 0, 0, cos(pi/4) -
    /// with the comment "for Madgwick", the filter it feeds. A port that initialised to identity
    /// because that is what a fresh quaternion usually is would start the picture rotated a
    /// quarter turn and let the filter converge out of it, which looks like motion controls that
    /// settle rather than like a wrong constant.
    /// </summary>
    [Fact]
    public void AFreshTrackerIsAQuarterTurnAboutX()
    {
        using var motion = new MotionTracker();
        Orientation o = motion.Current;

        const float SinCosQuarterPi = 0.70710678f;
        Assert.Equal(SinCosQuarterPi, o.X, 5);
        Assert.Equal(0f, o.Y);
        Assert.Equal(0f, o.Z);
        Assert.Equal(SinCosQuarterPi, o.W, 5);

        Assert.Equal(0u, motion.Timestamp);
    }

    /// <summary>
    /// Samples move the tracker's clock, in the units it counts in. This is the assertion that
    /// would have caught a millisecond timestamp: the tracker would read 16 rather than 16000.
    /// </summary>
    [Fact]
    public void SamplesAdvanceTheTrackerInMicroseconds()
    {
        using var motion = new MotionTracker();

        motion.Accelerometer(0f, 9.80665f, 0f, sdlTimestampMs: 16);
        Assert.Equal(16000u, motion.Timestamp);

        motion.Gyroscope(0.1f, 0f, 0f, sdlTimestampMs: 32);
        Assert.Equal(32000u, motion.Timestamp);
    }

    /// <summary>
    /// Rotation actually happens: a gyroscope turning about one axis moves the orientation off
    /// identity. The filter's exact output is libchiaki's business - what this says is that the
    /// port is driving it rather than holding a quaternion nobody updates.
    /// </summary>
    [Fact]
    public void AGyroscopeSampleRotatesTheOrientation()
    {
        using var motion = new MotionTracker();

        motion.Accelerometer(0f, 9.80665f, 0f, 0);
        Orientation before = motion.Current;

        // A full second of steady rotation, in samples, so the filter has time to move.
        for (uint ms = 16; ms <= 1000; ms += 16)
            motion.Gyroscope(2f, 0f, 0f, ms);

        Orientation after = motion.Current;
        Assert.NotEqual(before, after);
    }

    /// <summary>And the orientation reaches a controller state, which is what is actually sent.</summary>
    [Fact]
    public void TheOrientationReachesAControllerState()
    {
        using var motion = new MotionTracker();
        using var state = new ChiakiControllerState();

        motion.Accelerometer(0f, 9.80665f, 0f, 0);
        for (uint ms = 16; ms <= 500; ms += 16)
            motion.Gyroscope(2f, 0f, 0f, ms);

        motion.ApplyTo(state);

        // Some component of the orientation is now non-zero in the state, which a tracker that
        // never applied would leave untouched.
        (float x, float y, float z, float w) = state.Orientation();
        Assert.True(x != 0f || y != 0f || z != 0f || w != 0f,
            $"orientation was {x},{y},{z},{w}");
    }

    /// <summary>The two conversions are still the Qt client's, and only one of them applies.</summary>
    [Fact]
    public void TheConversionsAreStillTheQtClients()
    {
        string? file = MotionSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(MotionSource.AccelIsDividedByGravity(text), "accel divided by gravity, all 3");
        Assert.True(MotionSource.GyroIsTakenRaw(text), "gyro taken raw, all 3");
        Assert.True(MotionSource.TimestampIsMicroseconds(text), "timestamp times 1000");
    }
}
