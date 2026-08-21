using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP93: "one client should not hold three answers to one question", as an assertion.
///
/// The Qt client holds three pads: 1920x942 for a PS4, 1919x1079 for a PS5, and a third pair -
/// PS_TOUCHPAD_MAXX/MAXY, each axis's larger value - that is neither of them, which the dpad path
/// and the SDL path both scale by because that layer has no session to ask.
///
/// The port answers once, in <see cref="TouchpadExtents"/>. That is easy to claim and easy to lose:
/// the way the Qt client got to three was by someone typing a number in a second place, and every
/// path here that needs a pad already takes one as a parameter, so a copy would compile and pass
/// every other test.
///
/// So this scans the app's own sources - for 942 and 1919, and for nothing else. The other two
/// numbers in the three pads cannot be scanned for, and that limit is worth stating rather than
/// papering over:
///
///   1920 is a video width all over this project, from the render target to the custom-resolution
///   setting;
///
///   1079 is the maximum of the dpad-touch increment slider on the Controllers tab (PP164), an
///   unrelated setting measured in hundredths of a millimetre. This check found that on its first
///   run, which is the useful demonstration: a number that means a pad in one file means something
///   else in another, so the guard has to be narrower than a grep.
///
/// What that leaves uncovered is a copy of the QT MACROS' pair, 1920x1079, whose both numbers mean
/// other things here. That pair is the one nothing in this port should ever scale by, and it is
/// guarded by <see cref="TheOneFileHoldsAllThreeAnswers"/> naming it instead - as a fact about the
/// Qt client, not a value anything reaches for.
/// </summary>
public class TouchpadSingleAnswerTests
{
    /// <summary>
    /// The two numbers that mean a pad and nothing else here: a DualShock 4's height and a
    /// DualSense's width. Either real pad copied anywhere brings one of them with it.
    /// </summary>
    private static readonly string[] PadNumbers = ["942", "1919"];

    /// <summary>
    /// The one file allowed to hold them, and the one file whose whole job is to.
    /// </summary>
    private const string TheAnswer = "TouchpadExtents.cs";

    /// <summary>
    /// The host's selftest, which asserts the numbers rather than scaling by them. Excluded by
    /// name rather than by pattern, so adding a second exclusion is a decision somebody makes.
    /// </summary>
    private const string TheSelfTest = "SelfTest.cs";

    private static string? AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "app");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "SelfTest.cs")))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Whether a line is code rather than a comment. Crude on purpose: a line whose first
    /// non-blank characters are a slash pair is prose, and prose about the three pads is what
    /// several of these files are largely made of.
    /// </summary>
    private static bool IsCode(string line)
    {
        string trimmed = line.TrimStart();
        return !trimmed.StartsWith("//", StringComparison.Ordinal)
            && !trimmed.StartsWith('*');
    }

    /// <summary>Whether a file is one this check looks at.</summary>
    private static bool Scanned(string path)
    {
        // obj/ carries generated copies of everything, including the answer itself.
        if (path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            return false;
        }

        return Path.GetFileName(path) is not (TheAnswer or TheSelfTest);
    }

    /// <summary>Every code line in one file that carries a pad's number, with where it is.</summary>
    private static IEnumerable<string> PadLinesIn(string path)
        => File.ReadLines(path)
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(l => IsCode(l.Line) && PadNumbers.Any(n => l.Line.Contains(n, StringComparison.Ordinal)))
            .Select(l => $"{Path.GetFileName(path)}:{l.Number}: {l.Line.Trim()}");

    [Fact]
    public void OnlyOneFileInTheAppHoldsAPadsSize()
    {
        string? app = AppRoot();
        if (app is null)
            return;

        string[] offenders = [.. Directory
            .EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(Scanned)
            .SelectMany(PadLinesIn)];

        Assert.True(
            offenders.Length == 0,
            "a second answer to how big the touchpad is:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// And the one file that does hold them holds all three answers - the two real pads and the
    /// Qt macros' impossible one - so the finding is stated where the numbers are.
    /// </summary>
    [Fact]
    public void TheOneFileHoldsAllThreeAnswers()
    {
        Assert.Equal(new TouchpadExtents(1920, 942), TouchpadExtents.Ps4);
        Assert.Equal(new TouchpadExtents(1919, 1079), TouchpadExtents.Ps5);
        Assert.Equal(new TouchpadExtents(1920, 1079), TouchpadExtents.QtMacros);

        // The third is neither pad, which is the whole of why it is named rather than used.
        Assert.NotEqual(TouchpadExtents.Ps4, TouchpadExtents.QtMacros);
        Assert.NotEqual(TouchpadExtents.Ps5, TouchpadExtents.QtMacros);
    }

    /// <summary>
    /// The third pair is outward of BOTH real pads on the axis it differs on, which is why nobody
    /// has reported it: the gesture overshoots and still works. An inward error would stop a finger
    /// short of an edge the pad has, and a user would notice that.
    /// </summary>
    [Fact]
    public void TheThirdPairErrsOutwardOfBothPads()
    {
        Assert.True(TouchpadExtents.QtMacros.IsOutwardOf(TouchpadExtents.Ps4));
        Assert.True(TouchpadExtents.QtMacros.IsOutwardOf(TouchpadExtents.Ps5));

        // And neither real pad is outward of the other - they differ in both directions, which is
        // exactly why no single pair can serve both.
        Assert.False(TouchpadExtents.Ps4.IsOutwardOf(TouchpadExtents.Ps5));
        Assert.False(TouchpadExtents.Ps5.IsOutwardOf(TouchpadExtents.Ps4));
    }

    /// <summary>
    /// Every path that scales into pad units takes the pad as a parameter. Asserted through the
    /// two callers rather than by reading the signatures: the mouse path and the normalised path
    /// must disagree about the same point when told different consoles, or one of them is holding
    /// a pad of its own.
    /// </summary>
    [Fact]
    public void TheTwoPathsAnswerDifferentlyForDifferentConsoles()
    {
        (ushort ps4X, ushort ps4Y) = InputTranslation.MouseToTouchpad(1280, 720, 1280, 720, ps5: false);
        (ushort ps5X, ushort ps5Y) = InputTranslation.MouseToTouchpad(1280, 720, 1280, 720, ps5: true);

        Assert.Equal((ushort)1920, ps4X);
        Assert.Equal((ushort)942, ps4Y);
        Assert.Equal((ushort)1919, ps5X);
        Assert.Equal((ushort)1079, ps5Y);

        Assert.Equal(((ushort)1920, (ushort)942), InputTranslation.NormalizedToTouchpad(1f, 1f, false));
        Assert.Equal(((ushort)1919, (ushort)1079), InputTranslation.NormalizedToTouchpad(1f, 1f, true));
    }

    /// <summary>
    /// And the Qt client still holds its third answer, asserted as STILL TRUE. The port diverges
    /// here, and a divergence nobody re-reads is indistinguishable from a mistake.
    /// </summary>
    [Fact]
    public void TheQtClientStillHoldsThreeAnswers()
    {
        string? header = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.ControllerHeaderRelativePath);
        string? session = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.StreamSessionRelativePath);
        string? controller = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.ControllerCppRelativePath);

        if (header is null || session is null || controller is null)
            return;

        Assert.Equal(
            TouchpadExtents.QtMacros,
            TouchpadExtentsSource.MacroPair(File.ReadAllText(header)));

        (TouchpadExtents Ps4, TouchpadExtents Ps5)? pairs =
            TouchpadExtentsSource.PerConsolePairs(File.ReadAllText(session));

        Assert.NotNull(pairs);
        Assert.Equal(TouchpadExtents.Ps4, pairs.Value.Ps4);
        Assert.Equal(TouchpadExtents.Ps5, pairs.Value.Ps5);

        Assert.True(
            TouchpadExtentsSource.SdlPathStillUsesTheMacros(File.ReadAllText(controller)),
            "the SDL path still scales by the pad that is neither console's");
    }
}
