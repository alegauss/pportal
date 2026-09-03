using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP32: the microphone is ported in four places and captured in none.
///
/// §PP32's remaining criterion asked whether this host captures a microphone or whether the line
/// says it will not. Neither: the port has shipped the setting, the button, the ring rule and the
/// pad report, and nothing anywhere opens a capture device. That is a third answer, and it turns the
/// criterion from a decision into work.
/// </summary>
public class MicrophoneSurfaceTests(ITestOutputHelper output)
{
    /// <summary>
    /// All four places are still there, which is what makes the absence below a gap rather than a
    /// feature nobody wanted.
    ///
    /// Each is checked by the text that proves it does something about the microphone, not by the
    /// file existing. A file existing says nothing; a checkbox bound to start_mic_unmuted says the
    /// port intends the feature.
    /// </summary>
    [Fact]
    public void TheFourPlacesTheMicrophoneAlreadyExistsAreStillThere()
    {
        foreach (MicrophonePlace place in MicrophoneSurface.Places)
            output.WriteLine($"{place.Where}: {place.What}");

        IReadOnlyList<MicrophonePlace> gone = MicrophoneSurface.Missing();

        Assert.True(
            gone.Count == 0,
            "these places no longer say what this census claims, so the port's commitment to the "
                + "microphone has changed: " + string.Join(", ", gone.Select(g => g.Where)));
    }

    /// <summary>
    /// And nothing opens a capture device, which is the one piece the four assume.
    ///
    /// This is the assertion that turns red the day somebody starts the work, and that is the right
    /// way round: the finding is an absence, so the check has to be the thing that notices it
    /// filled. When it does, PP32's criterion is answered by code rather than by a census.
    /// </summary>
    [Fact]
    public void NothingInTheHostOpensACaptureDevice()
    {
        IReadOnlyList<string> capturing = MicrophoneSurface.FilesThatCapture();

        Assert.True(
            capturing.Count == 0,
            "the host now captures audio, so PP32's remaining criterion is answered and this census "
                + "should be replaced by whatever holds the capture: "
                + string.Join(", ", capturing));
    }

    /// <summary>
    /// The census names more than one place, which is what makes it a commitment rather than a
    /// leftover.
    ///
    /// One setting nobody removed would be a leftover from the Qt client. Four subsystems - a
    /// preference, a screen, an in-stream control and a pad report - is a feature the port carried
    /// forward on purpose, in four separate commits.
    /// </summary>
    [Fact]
    public void ItIsFourSubsystemsAndNotOneLeftover()
    {
        Assert.True(MicrophoneSurface.Places.Count >= 4);

        // Distinct files, so four entries are four places rather than four strings in one.
        Assert.Equal(
            MicrophoneSurface.Places.Count,
            MicrophoneSurface.Places.Select(p => p.Where).Distinct(StringComparer.Ordinal).Count());
    }
}
