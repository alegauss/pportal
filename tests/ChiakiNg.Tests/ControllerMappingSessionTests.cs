using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP217: the whole mapping path, driven end to end without a window, without SDL and without a pad.
///
/// What these assert is the seam PP18 said needed a device: a click arms, an event on the SDL
/// thread becomes a token, the token crosses to the UI thread, the document takes the binding and
/// the grid shows it. The only thing left for the pad is whether a real DualSense produces those
/// events - which is one question, not a screen's worth of them.
/// </summary>
public class ControllerMappingSessionTests
{
    private const string Mapping = "0300aa,Pad,a:b0,b:b1,";

    /// <summary>What Cross is bound to before anything here touches it.</summary>
    private static readonly string[] Bound = ["b0"];

    /// <summary>Cross, whose row is the one these press.</summary>
    private const int Cross = 1 << 0;

    /// <summary>Moon, for the second binding.</summary>
    private const int Moon = 1 << 1;

    /// <summary>The marshal a test wants: run it here, now, so the assertion follows the call.</summary>
    private static void Inline(Action work) => work();

    private static ControllerMappingDocument Document()
        => ControllerMappingDocument.Parse(Mapping, "Pad")!;

    private static ControllerMappingSession Session(
        out ControllerMappingDocument document, Action<Action>? marshal = null)
    {
        document = Document();
        return new ControllerMappingSession(document, marshal ?? Inline);
    }

    private static SdlEvent ButtonDown(byte index)
        => new(Gamepads.EventType.JoyButtonDown, 0, 0, index, 0);

    private static SdlEvent AxisMotion(byte index)
        => new(Gamepads.EventType.JoyAxisMotion, 0, 0, index, 0);

    /// <summary>The screen opens already showing the document, rather than empty until told.</summary>
    [Fact]
    public void TheScreenOpensShowingTheDocument()
    {
        ControllerMappingSession session = Session(out ControllerMappingDocument document);

        Assert.Equal(document.ControllerType, session.Screen.ControllerType);
        Assert.NotEmpty(session.Screen.Rows);
    }

    /// <summary>Nothing is armed until a row is clicked, so a stray event binds nothing.</summary>
    [Fact]
    public void AnEventBeforeAnyClickBindsNothing()
    {
        ControllerMappingSession session = Session(out _);

        Assert.False(session.Armed);

        session.OnSdlEvent(ButtonDown(7));

        Assert.False(session.Screen.Altered);
        Assert.DoesNotContain(session.Screen.Requests, r => r.Action == MappingAction.UpdateButton);
    }

    /// <summary>Clicking a row's button arms the pad for exactly one press.</summary>
    [Fact]
    public void ClickingARowArmsThePad()
    {
        ControllerMappingSession session = Session(out _);

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);

        Assert.True(session.Armed);
        Assert.True(session.Screen.CaptureOpen);
    }

    /// <summary>
    /// The whole path, in one test: click, press, and the binding is in the document and on the
    /// grid. This is what PP18 was waiting for a device to prove.
    /// </summary>
    [Fact]
    public void APressBecomesABindingTheGridShows()
    {
        ControllerMappingSession session = Session(out ControllerMappingDocument document);

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);
        session.OnSdlEvent(ButtonDown(7));

        Assert.Equal("b7", document.Physical("a").FirstOrDefault());
        Assert.Contains(session.Screen.Rows, row => row.Value == Cross && row.First == "b7");
        Assert.True(session.Screen.Altered);
    }

    /// <summary>And it disarms itself, so the release and everything after it bind nothing.</summary>
    [Fact]
    public void OnePressIsOneBinding()
    {
        ControllerMappingSession session = Session(out ControllerMappingDocument document);

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);
        session.OnSdlEvent(ButtonDown(7));

        Assert.False(session.Armed);

        session.OnSdlEvent(ButtonDown(9));

        Assert.Equal("b7", document.Physical("a").FirstOrDefault());
    }

    /// <summary>The capture closes with the press, and closing it does not quit a second time.</summary>
    [Fact]
    public void APressClosesTheCaptureWithoutQuittingIt()
    {
        ControllerMappingSession session = Session(out _);

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);
        session.OnSdlEvent(ButtonDown(7));

        Assert.False(session.Screen.CaptureOpen);
        Assert.DoesNotContain(session.Screen.Requests, r => r.Action == MappingAction.ButtonQuit);
    }

    /// <summary>Dismissing without a press quits the button mapping and disarms the pad.</summary>
    [Fact]
    public void DismissingWithoutAPressDisarms()
    {
        ControllerMappingSession session = Session(out _);

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);
        session.CloseCapture();

        Assert.False(session.Armed);
        Assert.Contains(session.Screen.Requests, r => r.Action == MappingAction.ButtonQuit);
    }

    /// <summary>
    /// The analog opt-in reaches the arm, and it is read when the capture OPENS. A stick moving
    /// while the box is off binds nothing.
    /// </summary>
    [Fact]
    public void TheAnalogOptInReachesTheArm()
    {
        ControllerMappingSession off = Session(out ControllerMappingDocument first);
        off.OpenCapture(Cross, 0, 0);
        off.OnSdlEvent(AxisMotion(1));

        // Still waiting, and the row still carries what the string came with and nothing else.
        Assert.True(off.Armed);
        Assert.Equal(Bound, first.Physical("a"));

        ControllerMappingSession on = Session(out ControllerMappingDocument second);
        on.Screen.EnableAnalogStickMapping = true;
        on.OpenCapture(Cross, 0, 0);
        on.OnSdlEvent(AxisMotion(1));

        Assert.False(on.Armed);
        Assert.Equal("a1", second.Physical("a").FirstOrDefault());
    }

    /// <summary>A second binding on the same row lands in the second slot and the grid draws it.</summary>
    [Fact]
    public void ASecondBindingFillsTheSecondSlot()
    {
        ControllerMappingSession session = Session(out ControllerMappingDocument document);

        session.OpenCapture(Moon, buttonIndex: 1, mappingIndex: 1);
        session.OnSdlEvent(ButtonDown(4));

        Assert.Contains("b4", document.Physical("b"));
        Assert.Contains(session.Screen.Rows, row => row.Value == Moon && row.HasSecond);
    }

    /// <summary>Update serialises the document and then leaves, always both and in that order.</summary>
    [Fact]
    public void UpdateSerialisesThenFinishes()
    {
        ControllerMappingSession session = Session(out ControllerMappingDocument document);

        session.OpenCapture(Cross, 0, 0);
        session.OnSdlEvent(ButtonDown(7));
        session.Apply();

        Assert.Equal(document.Serialise(), session.Applied);
        Assert.Contains("b7", session.Applied);
        Assert.True(session.Finished);
    }

    /// <summary>Nothing is applied until Update is pressed, however much has been bound.</summary>
    [Fact]
    public void NothingIsAppliedUntilUpdate()
    {
        ControllerMappingSession session = Session(out _);

        session.OpenCapture(Cross, 0, 0);
        session.OnSdlEvent(ButtonDown(7));

        Assert.Null(session.Applied);
        Assert.False(session.Finished);
    }

    /// <summary>
    /// The token crosses threads through the marshal and NOT around it. Nothing touches the screen
    /// until the marshalled work runs, which is what the dispatcher would be doing in the
    /// application.
    /// </summary>
    [Fact]
    public void ThePressReachesTheScreenOnlyThroughTheMarshal()
    {
        var queued = new List<Action>();
        ControllerMappingDocument document = Document();
        var session = new ControllerMappingSession(document, queued.Add);

        session.OpenCapture(Cross, 0, 0);
        session.OnSdlEvent(ButtonDown(7));

        // Taken - the arm is cleared on the SDL thread - but nothing has reached the screen.
        Assert.False(session.Armed);
        Assert.False(session.Screen.Altered);
        Assert.Equal(Bound, document.Physical("a"));
        Assert.Single(queued);

        queued[0]();

        Assert.True(session.Screen.Altered);
        Assert.Equal("b7", document.Physical("a").FirstOrDefault());
    }

    /// <summary>Going away while mapping runs quits both levels, as PP172 recorded.</summary>
    [Fact]
    public void GoingAwayFinishes()
    {
        ControllerMappingSession session = Session(out _);

        session.Destroyed();

        Assert.True(session.Finished);
    }

    /// <summary>
    /// Why the gate exists: the Qt client polls SDL on the GUI thread, so the race this session
    /// serialises is the port's own and has no original to be faithful to.
    /// </summary>
    [Fact]
    public void TheRaceIsThePortsAlone()
    {
        string? file = MappingSessionSource.Locate();
        if (file is null)
            return;

        string source = File.ReadAllText(file);

        Assert.True(MappingSessionSource.ThePollIsStillOnAGuiTimer(source), "a QTimer on the GUI thread");
        Assert.True(
            MappingSessionSource.TheIntervalIsStillTheOnePP8Copied(source),
            "and PP8's number is still its number");
    }
}
