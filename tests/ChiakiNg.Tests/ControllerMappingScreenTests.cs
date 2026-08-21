using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP18: the mapping screen's call sequence, which is what a live pad would drive.
///
/// No device is involved and none is stood in for. What is asserted is the ORDER of the calls the
/// screen makes, because that order is where its two one-shot flags decide whether the input path
/// is left armed with nothing waiting for it.
/// </summary>
public class ControllerMappingScreenTests
{
    private static ControllerMappingViewModel Screen()
        => new() { ControllerType = "DualSense" };

    private static MappingAction[] Actions(ControllerMappingViewModel screen)
        => [.. screen.Requests.Select(r => r.Action)];

    /// <summary>Opening the capture arms the input path and nothing else.</summary>
    [Fact]
    public void OpeningTheCaptureArmsTheInputPath()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(buttonValue: 3, buttonIndex: 0, mappingIndex: 5);

        Assert.True(screen.CaptureOpen);
        Assert.Equal([MappingAction.SelectButton], Actions(screen));
    }

    /// <summary>
    /// Closing it without a press quits the button mapping - the ordinary cancel, and the case the
    /// one-shot flag exists to distinguish from the other one.
    /// </summary>
    [Fact]
    public void ClosingWithoutAPressQuitsTheButtonMapping()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(3, 0, 5);
        screen.CloseCapture();

        Assert.Equal([MappingAction.SelectButton, MappingAction.ButtonQuit], Actions(screen));
        Assert.False(screen.CaptureOpen);
    }

    /// <summary>
    /// A press writes the binding and does NOT quit - the close is suppressed for exactly this
    /// one, which is what the flag is for.
    /// </summary>
    [Fact]
    public void APressWritesTheBindingAndDoesNotQuit()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(buttonValue: 3, buttonIndex: 1, mappingIndex: 5);
        screen.ButtonSelected("a");

        Assert.Equal([MappingAction.SelectButton, MappingAction.UpdateButton], Actions(screen));

        MappingRequest update = screen.Requests[1];
        Assert.Equal(3, update.ButtonValue);
        Assert.Equal("a", update.PressedButton);
        Assert.Equal(1, update.ButtonIndex);

        Assert.True(screen.Altered);
        Assert.False(screen.CaptureOpen);
    }

    /// <summary>
    /// THE DEFECT THIS EXISTS FOR. The suppression is a ONE-SHOT: the capture after a successful
    /// one must quit again when cancelled. A port treating the flag as an ordinary boolean leaves
    /// the input path armed for every capture after the first, and nothing on screen says so.
    /// </summary>
    [Fact]
    public void TheSuppressionLastsForExactlyOneCapture()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(3, 0, 5);
        screen.ButtonSelected("a");

        // The second capture, cancelled: the quit must come back.
        screen.OpenCapture(4, 0, 6);
        screen.CloseCapture();

        Assert.Equal(
            [
                MappingAction.SelectButton,
                MappingAction.UpdateButton,
                MappingAction.SelectButton,
                MappingAction.ButtonQuit,
            ],
            Actions(screen));
    }

    /// <summary>And two presses in a row suppress two quits, one each.</summary>
    [Fact]
    public void TwoPressesSuppressTwoQuits()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(3, 0, 5);
        screen.ButtonSelected("a");
        screen.OpenCapture(4, 0, 6);
        screen.ButtonSelected("b");

        Assert.DoesNotContain(MappingAction.ButtonQuit, Actions(screen));
        Assert.Equal(2, screen.Requests.Count(r => r.Action == MappingAction.UpdateButton));
    }

    /// <summary>The focus goes back to the row and slot the capture was opened from.</summary>
    [Fact]
    public void TheFocusGoesBackToTheSlotItCameFrom()
    {
        ControllerMappingViewModel screen = Screen();

        screen.OpenCapture(buttonValue: 3, buttonIndex: 1, mappingIndex: 7);
        screen.CloseCapture();

        Assert.Equal((7, 1), screen.FocusOnClose);
    }

    /// <summary>Update applies and then quits, always both and always in that order.</summary>
    [Fact]
    public void UpdateAppliesThenQuits()
    {
        ControllerMappingViewModel screen = Screen();

        screen.Apply();

        Assert.Equal([MappingAction.Apply, MappingAction.Quit], Actions(screen));
    }

    /// <summary>And the Update button needs something to have been altered first.</summary>
    [Fact]
    public void UpdateNeedsAnAlteration()
    {
        ControllerMappingViewModel screen = Screen();
        Assert.False(screen.CanApply);

        screen.OpenCapture(3, 0, 5);
        screen.ButtonSelected("a");

        Assert.True(screen.CanApply);
    }

    /// <summary>
    /// Mapping stopping from the outside closes the screen; the screen stopping it does not close
    /// it a second time. That guard is easy to read past and is what keeps a close from recursing.
    /// </summary>
    [Fact]
    public void TheScreenDoesNotCloseItselfTwice()
    {
        ControllerMappingViewModel outside = Screen();
        outside.MappingStopped();
        Assert.True(outside.Closed);

        ControllerMappingViewModel itself = Screen();
        itself.Destroyed();
        itself.MappingStopped();

        Assert.False(itself.Closed);
        Assert.Equal([MappingAction.ButtonQuit, MappingAction.Quit], Actions(itself));
    }

    /// <summary>Going away when mapping already stopped asks for nothing.</summary>
    [Fact]
    public void GoingAwayAfterMappingStoppedAsksForNothing()
    {
        ControllerMappingViewModel screen = Screen();

        screen.MappingStopped();
        screen.Destroyed();

        Assert.Empty(screen.Requests);
    }

    /// <summary>The title and the prompt are the pad's own name, in two different places.</summary>
    [Fact]
    public void ThePadsNameIsTheTitleAndThePromptsMiddle()
    {
        var screen = new ControllerMappingViewModel { ControllerType = "Xbox Series Controller" };

        Assert.Equal("Xbox Series Controller", screen.Title);
        Assert.Equal(
            "Press any Xbox Series Controller button to map to DualSense controller button or click close",
            screen.CapturePrompt);
    }

    /// <summary>The sticks and triggers are behind an opt-in, off to begin with.</summary>
    [Fact]
    public void TheAnalogOptInStartsOff()
        => Assert.False(Screen().EnableAnalogStickMapping);

    /// <summary>Every rule above, still stated the same way in the screen it was read from.</summary>
    [Fact]
    public void TheMappingScreensRulesAreStillTheQtClients()
    {
        string? file = ControllerMappingScreenSource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(ControllerMappingScreenSource.TheQuitFlagIsStillAOneShot(qml), "a one-shot");
        Assert.True(ControllerMappingScreenSource.TheFocusFlagHasTheSameShape(qml), "and so is the other");
        Assert.True(ControllerMappingScreenSource.TheCloseGuardIsStillThere(qml), "the close guard");
        Assert.True(ControllerMappingScreenSource.UpdateStillAppliesThenQuits(qml), "apply then quit");
        Assert.True(ControllerMappingScreenSource.UpdateStillNeedsAnAlteration(qml), "altered only");
        Assert.True(ControllerMappingScreenSource.TheAnalogOptInIsStillOnThisScreen(qml), "the opt-in");
        Assert.True(ControllerMappingScreenSource.ARowStillCarriesTwoBindings(qml), "two bindings a row");
    }
}
