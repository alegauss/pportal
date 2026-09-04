using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP32: the microphone was ported in four places and captured in none.
///
/// §PP32's remaining criterion asked whether this host captures a microphone or whether the line
/// says it will not. It was neither: the port had shipped the setting, the button, the ring rule and
/// the pad report, and nothing anywhere opened a capture device. That third answer turned the
/// criterion from a decision into work.
///
/// PP652 DID THE WORK, and the second test below is turned over rather than deleted. What the census
/// says now is that the port opens a device in exactly one place, which is a different claim from
/// the one it was written to make and still worth failing on.
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
    /// PP652: THE HOST OPENS A CAPTURE DEVICE NOW, and this assertion is turned over.
    ///
    /// It read "nothing in the host opens a capture device", and it was the right assertion for as
    /// long as the finding was an absence: a check on a gap has to be the thing that notices it
    /// filled. PP652 filled it - WasapiCapture opens the default communications endpoint and hands
    /// out whole units - so the check turns rather than being deleted, the way PP591 turned the
    /// harness's assertions over.
    ///
    /// Turned and not dropped, because the census still has something to say: it lists five capture
    /// APIs and the port now uses exactly one of them. A second appearing is a second way into the
    /// same device, and that is worth failing on.
    /// </summary>
    [Fact]
    public void TheHostOpensACaptureDeviceInExactlyOnePlace()
    {
        IReadOnlyList<string> capturing = MicrophoneSurface.FilesThatCapture();

        foreach (string one in capturing)
            output.WriteLine($"captures: {one}");

        string only = Assert.Single(capturing);

        Assert.EndsWith("WasapiCapture.cs", only, StringComparison.Ordinal);
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
